using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Rinne.Core.Features.FileCache;

public sealed class SpaceFileMetaDbParallel : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private bool _disposed;

    private sealed class StagedMeta
    {
        public long Size;
        public long MtimeTicks;
        public string FileHashHex = string.Empty;
        public string SnapshotFileHashHex = string.Empty;
        public List<string> ChunkHashesRef = new();
        public long UpdatedAtTicks;
    }

    private readonly Dictionary<string, StagedMeta> _staged = new(StringComparer.Ordinal);
    private readonly object _stagedLock = new();

    private SpaceFileMetaDbParallel(string dbPath, string connectionString)
    {
        _dbPath = dbPath;
        _connectionString = connectionString;
    }

    public static SpaceFileMetaDbParallel Open(string dbPath)
    {
        if (dbPath is null) throw new ArgumentNullException(nameof(dbPath));

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

        using (var conn = new SqliteConnection(cs))
        {
            conn.Open();
            EnsureSchema(conn);
        }

        return new SpaceFileMetaDbParallel(dbPath, cs);
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS filemeta (
                    path TEXT PRIMARY KEY,
                    size INTEGER NOT NULL,
                    mtime_ticks INTEGER NOT NULL,
                    file_hash TEXT NOT NULL,
                    chunk_hashes TEXT NOT NULL,
                    updated_at_ticks INTEGER NOT NULL,
                    snapshot_file_hash TEXT NOT NULL DEFAULT ''
                );";
            cmd.ExecuteNonQuery();
        }

        try
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = @"
                ALTER TABLE filemeta
                ADD COLUMN snapshot_file_hash TEXT NOT NULL DEFAULT '';";
            alter.ExecuteNonQuery();
        }
        catch
        {
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SpaceFileMetaDbParallel));
    }

    public SpaceFileMeta? TryGet(string relativePath)
    {
        if (relativePath is null) throw new ArgumentNullException(nameof(relativePath));
        ThrowIfDisposed();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
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

    public void StageForUpdate(
        string relativePath,
        long size,
        long mtimeTicks,
        string fileHashHex,
        List<string> chunkHashesRef,
        long nowTicks,
        string? snapshotFileHashHex = null)
    {
        if (relativePath is null) throw new ArgumentNullException(nameof(relativePath));
        if (chunkHashesRef is null) throw new ArgumentNullException(nameof(chunkHashesRef));
        ThrowIfDisposed();

        lock (_stagedLock)
        {
            _staged[relativePath] = new StagedMeta
            {
                Size = size,
                MtimeTicks = mtimeTicks,
                FileHashHex = fileHashHex ?? string.Empty,
                SnapshotFileHashHex = snapshotFileHashHex ?? string.Empty,
                ChunkHashesRef = chunkHashesRef,
                UpdatedAtTicks = nowTicks
            };
        }
    }

    public void SetStagedChunkHash(string relativePath, int index, string chunkHash)
    {
        if (relativePath is null) throw new ArgumentNullException(nameof(relativePath));
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        ThrowIfDisposed();

        lock (_stagedLock)
        {
            if (!_staged.TryGetValue(relativePath, out var meta))
            {
                return;
            }

            var list = meta.ChunkHashesRef;
            while (list.Count <= index)
            {
                list.Add(string.Empty);
            }

            list[index] = chunkHash ?? string.Empty;
        }
    }

    public void CommitStagedUpdates()
    {
        ThrowIfDisposed();

        Dictionary<string, StagedMeta> snapshot;
        lock (_stagedLock)
        {
            if (_staged.Count == 0)
                return;

            snapshot = new Dictionary<string, StagedMeta>(_staged, StringComparer.Ordinal);
            _staged.Clear();
        }

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var tx = conn.BeginTransaction();
        try
        {
            foreach (var (path, meta) in snapshot)
            {
                var chunksJson = JsonSerializer.Serialize(meta.ChunkHashesRef);

                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO filemeta (path, size, mtime_ticks, file_hash, chunk_hashes, updated_at_ticks, snapshot_file_hash)
                    VALUES ($path, $size, $mtime, $hash, $chunks, $updated, $snapshot_hash)
                    ON CONFLICT(path) DO UPDATE SET
                        size = excluded.size,
                        mtime_ticks = excluded.mtime_ticks,
                        file_hash = excluded.file_hash,
                        chunk_hashes = excluded.chunk_hashes,
                        updated_at_ticks = excluded.updated_at_ticks,
                        snapshot_file_hash = excluded.snapshot_file_hash;";

                cmd.Parameters.AddWithValue("$path", path);
                cmd.Parameters.AddWithValue("$size", meta.Size);
                cmd.Parameters.AddWithValue("$mtime", meta.MtimeTicks);
                cmd.Parameters.AddWithValue("$hash", meta.FileHashHex);
                cmd.Parameters.AddWithValue("$chunks", chunksJson);
                cmd.Parameters.AddWithValue("$updated", meta.UpdatedAtTicks);
                cmd.Parameters.AddWithValue("$snapshot_hash", meta.SnapshotFileHashHex);

                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            try
            {
                tx.Rollback();
            }
            catch
            {
            }

            throw;
        }
    }

    public int GarbageCollect(IEnumerable<string> aliveRelativePaths, long minUpdatedAtTicks)
    {
        if (aliveRelativePaths is null) throw new ArgumentNullException(nameof(aliveRelativePaths));
        ThrowIfDisposed();

        var alive = new HashSet<string>(aliveRelativePaths, StringComparer.Ordinal);

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var tx = conn.BeginTransaction();
        try
        {
            var toDelete = new List<string>();

            using (var select = conn.CreateCommand())
            {
                select.Transaction = tx;
                select.CommandText = @"
                    SELECT path
                    FROM filemeta
                    WHERE updated_at_ticks < $cutoff;";
                select.Parameters.AddWithValue("$cutoff", minUpdatedAtTicks);

                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    var path = reader.GetString(0);
                    if (!alive.Contains(path))
                    {
                        toDelete.Add(path);
                    }
                }
            }

            if (toDelete.Count == 0)
            {
                tx.Commit();
                return 0;
            }

            int deleted = 0;
            using (var delete = conn.CreateCommand())
            {
                delete.Transaction = tx;
                delete.CommandText = @"DELETE FROM filemeta WHERE path = $path;";
                var pPath = delete.CreateParameter();
                pPath.ParameterName = "$path";
                delete.Parameters.Add(pPath);

                foreach (var path in toDelete)
                {
                    pPath.Value = path;
                    deleted += delete.ExecuteNonQuery();
                }
            }

            tx.Commit();
            return deleted;
        }
        catch
        {
            try
            {
                tx.Rollback();
            }
            catch
            {
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            try
            {
                CommitStagedUpdates();
            }
            catch
            {
            }
        }
        finally
        {
            _disposed = true;
        }
    }
}
