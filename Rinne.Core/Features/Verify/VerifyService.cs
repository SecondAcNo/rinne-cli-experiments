using Rinne.Core.Common;
using Rinne.Core.Features.Cas.Pipes;
using Rinne.Core.Features.Meta;
using System.Text.Json;

namespace Rinne.Core.Features.Verify;

public enum MissingPayloadPolicy
{
    Error,
    Skip,
    Hydrate,
    TempHydrate
}

public sealed class VerifyService
{
    private readonly RinnePaths _paths;
    private readonly MetaService _meta;

    public VerifyService(RinnePaths paths, MetaService meta)
    {
        _paths = paths;
        _meta = meta;
    }

    public sealed record ItemResult(
        string Id,
        string Status,
        string Message);

    public sealed record Summary(
        int Total,
        int Ok,
        int Mismatch,
        int MetaMissing,
        int PayloadMissingError,
        int Skipped,
        int Hydrated,
        int TempHydrated,
        int HydrateFail,
        int TempHydrateFail,
        int OtherErrors,
        IReadOnlyList<ItemResult> Details);

    public async Task<Summary> RunAsync(
        string space,
        IEnumerable<string>? ids,
        MissingPayloadPolicy policy,
        int workers,
        CancellationToken ct)
    {
        var targets = (ids is null || !ids.Any())
            ? EnumerateSpaceIds(space)
            : ids.ToArray();

        var details = new List<ItemResult>();
        int ok = 0, mismatch = 0, metaMissing = 0, payloadErr = 0, skipped = 0,
            hydrated = 0, tempHydrated = 0, hydFail = 0, tempHydFail = 0, otherErr = 0;

        foreach (var id in targets)
        {
            ct.ThrowIfCancellationRequested();

            var idDir = SnapshotIdDir(space, id);
            var metaPath = Path.Combine(idDir, "meta.json");
            var payloadDir = _paths.SnapshotPayload(space, id);
            var manifest = _paths.StoreManifest(id);

            try
            {
                if (!File.Exists(metaPath))
                {
                    details.Add(new(id, "META-NOT-FOUND", $"meta.json not found: {metaPath}"));
                    //Console.Error.WriteLine($"[{id}] meta not found");
                    metaMissing++;
                    continue;
                }

                var meta = await ReadMetaAsync(metaPath, ct);

                if (Directory.Exists(payloadDir))
                {
                    var computed = _meta.ComputeMeta(payloadDir, ct);
                    if (Equal(computed.SnapshotHash, meta.SnapshotHash)
                        && computed.FileCount == meta.FileCount
                        && computed.TotalBytes == meta.TotalBytes)
                    {
                        details.Add(new(id, "OK", "verified"));
                        //Console.WriteLine($"[{id}] OK");
                        ok++;
                    }
                    else
                    {
                        details.Add(new(id, "MISMATCH",
                            $"hash/count/bytes mismatch (expected={meta.SnapshotHash}/{meta.FileCount}/{meta.TotalBytes}, actual={computed.SnapshotHash}/{computed.FileCount}/{computed.TotalBytes})"));
                        //Console.Error.WriteLine($"[{id}] MISMATCH");
                        mismatch++;
                    }
                    continue;
                }

                if (!File.Exists(manifest))
                {
                    details.Add(new(id, "PAYLOAD-MISSING",
                        $"payload not found and manifest not found: {payloadDir} , {manifest}"));
                    //Console.Error.WriteLine($"[{id}] payload/manifest both missing");
                    payloadErr++;
                    continue;
                }

                switch (policy)
                {
                    case MissingPayloadPolicy.Error:
                        details.Add(new(id, "PAYLOAD-MISSING", "payload missing"));
                        //Console.Error.WriteLine($"[{id}] payload missing (policy=Error)");
                        payloadErr++;
                        break;

                    case MissingPayloadPolicy.Skip:
                        details.Add(new(id, "SKIP-MISSING", "skipped (payload missing)"));
                        //Console.WriteLine($"[{id}] skip (payload missing)");
                        skipped++;
                        break;

                    case MissingPayloadPolicy.Hydrate:
                        {
                            //Console.WriteLine($"[{id}] hydrate -> {payloadDir}");
                            if (!await HydrateIntoPayloadAsync(id, manifest, payloadDir, workers, ct))
                            {
                                details.Add(new(id, "HYDRATE-FAIL", "failed to hydrate"));
                                //Console.Error.WriteLine($"[{id}] hydrate FAIL");
                                hydFail++;
                                break;
                            }

                            var computed = _meta.ComputeMeta(payloadDir, ct);
                            var meta2 = await ReadMetaAsync(metaPath, ct);

                            if (Equal(computed.SnapshotHash, meta2.SnapshotHash)
                                && computed.FileCount == meta2.FileCount
                                && computed.TotalBytes == meta2.TotalBytes)
                            {
                                details.Add(new(id, "OK", "hydrated + verified"));
                                //Console.WriteLine($"[{id}] OK (hydrated)");
                                hydrated++;
                            }
                            else
                            {
                                details.Add(new(id, "MISMATCH",
                                    $"after hydrate mismatch (expected={meta2.SnapshotHash}/{meta2.FileCount}/{meta2.TotalBytes}, actual={computed.SnapshotHash}/{computed.FileCount}/{computed.TotalBytes})"));
                                //Console.Error.WriteLine($"[{id}] MISMATCH (after hydrate)");
                                mismatch++;
                            }

                            break;
                        }

                    case MissingPayloadPolicy.TempHydrate:
                        {
                            //Console.WriteLine($"[{id}] temp-hydrate");
                            if (!await TempHydrateVerifyAsync(space, id, manifest, workers, metaPath, ct))
                            {
                                details.Add(new(id, "TEMP-HYDRATE-FAIL", "failed to temp-hydrate"));
                                //Console.Error.WriteLine($"[{id}] temp-hydrate FAIL");
                                tempHydFail++;
                                break;
                            }

                            details.Add(new(id, "OK", "temp-hydrated + verified"));
                            //Console.WriteLine($"[{id}] OK (temp-hydrated)");
                            tempHydrated++;
                            break;
                        }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                details.Add(new(id, "ERROR", ex.Message));
                //Console.Error.WriteLine($"[{id}] ERROR: {ex.Message}");
                otherErr++;
            }
        }

        return new Summary(
            Total: targets.Count(),
            Ok: ok,
            Mismatch: mismatch,
            MetaMissing: metaMissing,
            PayloadMissingError: payloadErr,
            Skipped: skipped,
            Hydrated: hydrated,
            TempHydrated: tempHydrated,
            HydrateFail: hydFail,
            TempHydrateFail: tempHydFail,
            OtherErrors: otherErr,
            Details: details);
    }

    private async Task<bool> HydrateIntoPayloadAsync(
        string id,
        string manifestPath,
        string payloadDir,
        int workers,
        CancellationToken ct)
    {
        var idDir = Path.GetDirectoryName(payloadDir)!;
        var tmp = Path.Combine(idDir, ".hydrate_tmp");

        TryDeleteDirectory(tmp);
        Directory.CreateDirectory(tmp);

        try
        {
            await RestoreDirectoryPipe.RunAsync(
                manifestPath: manifestPath,
                storeDir: _paths.StoreRoot,
                outputDir: tmp,
                workers: workers,
                ct: ct);

#if NET8_0_OR_GREATER
            Directory.Move(tmp, payloadDir);
#else
            if (Directory.Exists(payloadDir)) throw new IOException($"target exists: {payloadDir}");
            Directory.Move(tmp, payloadDir);
#endif
            return true;
        }
        catch
        {
            TryDeleteDirectory(tmp);
            return false;
        }
    }

    private async Task<bool> TempHydrateVerifyAsync(
        string space,
        string id,
        string manifestPath,
        int workers,
        string metaPath,
        CancellationToken ct)
    {
        var idDir = SnapshotIdDir(space, id);
        var tmpRoot = Path.Combine(idDir, ".verify_tmp");
        var tmpPayload = Path.Combine(tmpRoot, "snapshots");

        TryDeleteDirectory(tmpRoot);
        Directory.CreateDirectory(tmpPayload);

        try
        {
            await RestoreDirectoryPipe.RunAsync(
                manifestPath: manifestPath,
                storeDir: _paths.StoreRoot,
                outputDir: tmpPayload,
                workers: workers,
                ct: ct);

            var computed = _meta.ComputeMeta(tmpPayload, ct);
            var meta = await ReadMetaAsync(metaPath, ct);

            return Equal(computed.SnapshotHash, meta.SnapshotHash)
                && computed.FileCount == meta.FileCount
                && computed.TotalBytes == meta.TotalBytes;
        }
        finally
        {
            TryDeleteDirectory(tmpRoot);
        }
    }

    private static bool Equal(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private string SnapshotIdDir(string space, string id) =>
        Path.Combine(_paths.SnapshotsRoot, "space", space, id);

    private string[] EnumerateSpaceIds(string space)
    {
        var spaceDir = Path.Combine(_paths.SnapshotsRoot, "space", space);
        if (!Directory.Exists(spaceDir)) return Array.Empty<string>();
        return Directory.EnumerateDirectories(spaceDir)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToArray()!;
    }

    private static async Task<SnapshotMeta> ReadMetaAsync(string metaPath, CancellationToken ct)
    {
        await using var fs = File.OpenRead(metaPath);
        var meta = await JsonSerializer.DeserializeAsync<SnapshotMeta>(fs, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }, ct);
        if (meta is null) throw new InvalidDataException($"invalid meta.json: {metaPath}");
        if (meta.Version != 1) throw new NotSupportedException($"unsupported meta version: {meta.Version}");
        if (!string.Equals(meta.HashAlgorithm, "sha256", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"unsupported hashAlg: {meta.HashAlgorithm}");
        return meta;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }
}
