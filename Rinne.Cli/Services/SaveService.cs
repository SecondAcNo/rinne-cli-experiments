using System.IO.Compression;
using System.Text.RegularExpressions;
using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;
using Rinne.Cli.Utility;

namespace Rinne.Cli.Services
{
    /// <summary>
    /// <see cref="ISaveService"/> の既定実装。
    /// </summary>
    public sealed class SaveService : ISaveService
    {
        private readonly IArchiveService _archiveService;
        private readonly IMetaService _metaService;
        private const string ForceExclude = ".rinne";

        public SaveService(IArchiveService archiveService, IMetaService metaService)
        {
            _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
            _metaService = metaService ?? throw new ArgumentNullException(nameof(metaService));
        }

        /// <inheritdoc/>
        public async Task<SaveResult> SaveAsync(string repoRoot, string? space, string? message, CancellationToken cancellationToken = default)
        {
            return await SaveAsync(repoRoot, repoRoot, space, message, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<SaveResult> SaveAsync(string repoRoot, string targetRoot, string? space, string? message, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(repoRoot))
                throw new ArgumentException("Repository root is required.", nameof(repoRoot));
            if (string.IsNullOrWhiteSpace(targetRoot))
                throw new ArgumentException("Target root is required.", nameof(targetRoot));

            var layout = new RepositoryLayout(repoRoot);

            if (!Directory.Exists(layout.RinneDir))
                throw new InvalidOperationException("先に init コマンドで初期化してください。(.rinne が見つかりません)");

            // スペース解決
            var resolvedSpace = layout.ResolveSpace(space);

            // スペースの物理構造を保証
            layout.EnsureSpaceStructure(resolvedSpace);
            var spaceDataDir = layout.GetSpaceDataDir(resolvedSpace);

            // 排他ロック
            using var fileLock = LockFile.Acquire(layout.RinneDir, "save", TimeSpan.FromMinutes(10));

            // 採番と ID 生成
            var seq = SequenceUtility.GetNextSequence(spaceDataDir);
            var id = $"{seq:D8}_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            var zipPath = Path.Combine(spaceDataDir, id + ".zip");

            // 除外リスト作成
            var exclude = IgnoreUtility.LoadIgnoreList(layout.IgnorePath);
            IgnoreUtility.EnsureForceExclude(exclude, ForceExclude);

            var options = new ArchiveZipOptions
            {
                ExcludeGlobs = exclude.ToArray(),
                IncludeHidden = false,
                Overwrite = false,
                CompressionLevel = CompressionLevel.NoCompression
            };

            // 指定フォルダをZIP化
            var createdZipPath = await _archiveService.CreateZipAsync(targetRoot, zipPath, options, cancellationToken);

            // メタ出力
            var metaOutput = await _metaService.WriteAsync(new MetaWriteInput
            {
                RepoRoot = layout.RepoRoot,
                Space = resolvedSpace,
                Seq = seq,
                ZipAbsolutePath = createdZipPath,
                Message = message ?? string.Empty,
                IgnoreSourceFileName = Path.GetFileName(layout.IgnorePath)
            }, cancellationToken);

            return new SaveResult
            {
                Id = id,
                Space = resolvedSpace,
                ZipPath = createdZipPath,
                MetaPath = metaOutput.MetaPath,
                Sequence = seq
            };
        }
    }
}
