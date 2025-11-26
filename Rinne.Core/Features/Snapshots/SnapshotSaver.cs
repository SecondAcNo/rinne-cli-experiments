using Rinne.Core.Common;
using Rinne.Core.Config;
using Rinne.Core.Features.Meta;
using Rinne.Core.Features.Notes;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Rinne.Core.Features.Snapshots
{
    public static class SnapshotSaver
    {
        public enum HashMode { Full, None }

        public static SnapshotResult Save(SnapshotOptions opt, HashMode hashMode = HashMode.Full)
        {
            if (!Directory.Exists(opt.SourceRoot))
                throw new DirectoryNotFoundException($"source directory not found: {opt.SourceRoot}");

            var paths = new RinnePaths(opt.SourceRoot);

            if (!Directory.Exists(paths.RinneRoot))
                throw new InvalidOperationException($".rinne is missing. Run `rinne init` first: {paths.RinneRoot}");
            if (!Directory.Exists(paths.SnapshotsSpace(opt.Space)))
                throw new InvalidOperationException($"space '{opt.Space}' does not exist.");

            CleanupIncompleteSnapshots(paths, opt.Space);

            var snapshotId = SnapshotId.CreateUtc();
            var targetDir = paths.Snapshot(opt.Space, snapshotId);
            var payloadDir = Path.Combine(targetDir, "snapshots");

            var cfg = ExcludeConfig.Load(paths.RinneIgnoreJson).WithDefaults();
            var ignorer = new Ignorer(cfg);

            var plan = CopyPlanner.PlanAll(opt.SourceRoot, ignorer);

            var errors = new ConcurrentBag<string>();

            try
            {
                Directory.CreateDirectory(targetDir);
                Directory.CreateDirectory(payloadDir);

                foreach (var rel in plan.Dirs)
                {
                    if (rel == ".") continue;
                    var dstDir = Path.Combine(payloadDir, rel.Replace('\\', '/'));
                    Directory.CreateDirectory(dstDir);
                }

                Parallel.ForEach(
                    plan.Files,
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                    item =>
                    {
                        try
                        {
                            var dst = Path.Combine(payloadDir, item.RelativePath);
                            var dstParent = Path.GetDirectoryName(dst);
                            if (!string.IsNullOrEmpty(dstParent))
                                Directory.CreateDirectory(dstParent);
                            File.Copy(item.FullPath, dst, overwrite: false);
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{item.RelativePath} :: {ex.Message}");
                        }
                    });

                var errorList = errors.ToList();
                if (errorList.Count > 0)
                {
                    SilentDelete(targetDir, recursive: true);
                    throw new IOException($"one or more files failed to copy. errors={errorList.Count}");
                }

                var triples = Directory.EnumerateFiles(payloadDir, "*", SearchOption.AllDirectories)
                    .Select(full => (FullPath: full, RelativePath: NormalizeRelativePath(payloadDir, full), Length: new FileInfo(full).Length));

                string hashAlg;
                string hashHex;
                long fileCount;
                long totalBytes;

                if (hashMode == HashMode.Full)
                {
                    var res = SnapshotHash.Compute(
                        SnapshotHash.ItemsFromPlan(triples, excludeMetaJson: false, excludeRinneDir: true));

                    hashAlg = "sha256";
                    hashHex = res.HashHex;
                    fileCount = res.FileCount;
                    totalBytes = res.TotalBytes;
                }
                else
                {
                    hashAlg = "skip";
                    hashHex = "SKIP";
                    fileCount = triples.LongCount();
                    totalBytes = triples.Sum(t => t.Length);
                }

                var meta = new SnapshotMeta(1, hashAlg, hashHex, fileCount, totalBytes);
                var metaPath = Path.Combine(targetDir, "meta.json");
                File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

                return new SnapshotResult(targetDir, fileCount, totalBytes, 0, errorList);
            }
            catch
            {
                SilentDelete(targetDir, recursive: true);
                throw;
            }
        }

        private static void CleanupIncompleteSnapshots(RinnePaths paths, string space)
        {
            var spaceDir = paths.SnapshotsSpace(space);
            if (!Directory.Exists(spaceDir))
                return;

            foreach (var dir in Directory.EnumerateDirectories(spaceDir))
            {
                try
                {
                    var metaPath = Path.Combine(dir, "meta.json");
                    var notePath = Path.Combine(dir, NoteService.DefaultFileName);

                    var isOrphan =
                        !File.Exists(metaPath) ||
                        !File.Exists(notePath);

                    if (isOrphan)
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                }
                catch
                {
                }
            }
        }

        private static void SilentDelete(string dir, bool recursive)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive);
            }
            catch
            {
            }
        }

        private static string NormalizeRelativePath(string root, string fullPath)
        {
            return Path.GetRelativePath(root, fullPath)
                       .Replace(Path.DirectorySeparatorChar, '/')
                       .Replace(Path.AltDirectorySeparatorChar, '/');
        }
    }
}
