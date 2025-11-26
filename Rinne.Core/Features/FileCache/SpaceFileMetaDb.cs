using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Rinne.Core.Features.FileCache;

public readonly struct SpaceFileMeta
{
    public long Size { get; }
    public long MtimeTicks { get; }
    public string FileHashHex { get; }
    public IReadOnlyList<string> ChunkHashes { get; }

    public SpaceFileMeta(long size, long mtimeTicks, string fileHashHex, IReadOnlyList<string> chunkHashes)
    {
        Size = size;
        MtimeTicks = mtimeTicks;
        FileHashHex = fileHashHex;
        ChunkHashes = chunkHashes;
    }
}

public sealed class SpaceFileMetaDb : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private bool _disposed;

    private sealed class StagedMeta
    {
        public long Size;
        public long MtimeTicks;
        public string FileHashHex = string.Empty;
        public List<string> ChunkHashesRef = new();
        public long UpdatedAtTicks;
    }

    private readonly Dictionary<string, StagedMeta> _staged = new(StringComparer.Ordinal);

    private SpaceFileMetaDb(string dbPath, SqliteConnection connection)
    {
        _dbPath = dbPath;
        _connection = connection;
    }

    public static SpaceFileMetaDb Open(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        var conn = new SqliteConnection(cs);
        conn.Open();

        var db = new SpaceFileMetaDb(dbPath, conn);
        db.EnsureSchema();
        return db;
    }

    private void EnsureSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS filemeta (
                path TEXT PRIMARY KEY,
                size INTEGER NOT NULL,
                mtime_ticks INTEGER NOT NULL,
                file_hash TEXT NOT NULL,
                chunk_hashes TEXT NOT NULL,
                updated_at_ticks INTEGER NOT NULL
            );";
        cmd.ExecuteNonQuery();
    }

    public SpaceFileMeta? TryGet(string relativePath)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT size, mtime_ticks, file_hash, chunk_hashes
            FROM filemeta
            WHERE path = $path
            LIMIT 1;";
        cmd.Parameters.AddWithValue("$path", relativePath);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var size = reader.GetInt64(0);
        var mtime = reader.GetInt64(1);
        var fileHash = reader.GetString(2);
        var chunkJson = reader.GetString(3);

        List<string>? chunks;
        try
        {
            chunks = JsonSerializer.Deserialize<List<string>>(chunkJson) ?? new List<string>();
        }
        catch
        {
            chunks = new List<string>();
        }

        return new SpaceFileMeta(size, mtime, fileHash, chunks);
    }

    public void StageForUpdate(string relativePath, long size, long mtimeTicks, string fileHashHex,
                               List<string> chunkHashesRef, long nowTicks)
    {
        if (relativePath is null) throw new ArgumentNullException(nameof(relativePath));
        if (chunkHashesRef is null) throw new ArgumentNullException(nameof(chunkHashesRef));

        _staged[relativePath] = new StagedMeta
        {
            Size = size,
            MtimeTicks = mtimeTicks,
            FileHashHex = fileHashHex ?? string.Empty,
            ChunkHashesRef = chunkHashesRef,
            UpdatedAtTicks = nowTicks
        };
    }

    public void SetStagedChunkHash(string relativePath, int index, string chunkHash)
    {
        if (!_staged.TryGetValue(relativePath, out var meta))
        {
            return;
        }

        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        var list = meta.ChunkHashesRef;
        while (list.Count <= index)
        {
            list.Add(string.Empty);
        }

        list[index] = chunkHash ?? string.Empty;
    }

    public void CommitStagedUpdates()
    {
        if (_staged.Count == 0)
            return;

        using var tx = _connection.BeginTransaction();
        try
        {
            foreach (var (path, meta) in _staged)
            {
                var chunksJson = JsonSerializer.Serialize(meta.ChunkHashesRef);

                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO filemeta (path, size, mtime_ticks, file_hash, chunk_hashes, updated_at_ticks)
                    VALUES ($path, $size, $mtime, $hash, $chunks, $updated)
                    ON CONFLICT(path) DO UPDATE SET
                        size = excluded.size,
                        mtime_ticks = excluded.mtime_ticks,
                        file_hash = excluded.file_hash,
                        chunk_hashes = excluded.chunk_hashes,
                        updated_at_ticks = excluded.updated_at_ticks;";

                cmd.Parameters.AddWithValue("$path", path);
                cmd.Parameters.AddWithValue("$size", meta.Size);
                cmd.Parameters.AddWithValue("$mtime", meta.MtimeTicks);
                cmd.Parameters.AddWithValue("$hash", meta.FileHashHex);
                cmd.Parameters.AddWithValue("$chunks", chunksJson);
                cmd.Parameters.AddWithValue("$updated", meta.UpdatedAtTicks);

                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            _staged.Clear();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            if (_staged.Count > 0)
            {
                try
                {
                    CommitStagedUpdates();
                }
                catch
                {
                }
            }
        }
        finally
        {
            _connection.Dispose();
            _disposed = true;
        }
    }
}
