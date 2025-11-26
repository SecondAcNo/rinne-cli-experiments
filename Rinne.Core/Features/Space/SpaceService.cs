using Rinne.Core.Common;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Rinne.Core.Features.Space;

public sealed class SpaceService
{
    private readonly RinnePaths _paths;

    private StringComparer NameComparer =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public SpaceService(RinnePaths paths)
    {
        _paths = paths;
    }

    public IEnumerable<string> List()
    {
        if (!Directory.Exists(_paths.SpacesRoot))
            yield break;

        foreach (var dir in Directory.EnumerateDirectories(_paths.SpacesRoot))
        {
            var name = Path.GetFileName(dir)!;
            if (SpaceNameRules.NameRegex.IsMatch(name))
                yield return name;
        }
    }

    public string GetCurrentSpaceFromPointer()
    {
        var p = _paths.SnapshotsCurrent;
        if (!File.Exists(p))
            throw new InvalidOperationException($"current file not found: {p}");

        var s = File.ReadAllText(p).Trim();
        if (string.IsNullOrWhiteSpace(s))
            throw new InvalidOperationException($"current file is empty: {p}");

        return s;
    }

    public (string Name, string Source) ResolveEffectiveSpace(string? cliArg = null)
    {
        if (!string.IsNullOrWhiteSpace(cliArg)) return (cliArg!, "cli");

        var env = Environment.GetEnvironmentVariable("RINNE_SPACE");
        if (!string.IsNullOrWhiteSpace(env)) return (env!, "env");

        var ptr = ReadCurrentPointerOrNull();
        if (!string.IsNullOrWhiteSpace(ptr)) return (ptr!, "current-file");

        var cfgName = ReadConfigCurrentOrNull();
        if (!string.IsNullOrWhiteSpace(cfgName)) return (cfgName!, "config");

        return ("main", "default");
    }

    public string GetCurrent() => ResolveEffectiveSpace().Name;

    public void Create(string name, CancellationToken ct = default)
    {
        EnsureRepoInitializedOrThrow();
        ValidateNameOrThrow(name);
        ct.ThrowIfCancellationRequested();

        if (List().Any(s => NameComparer.Equals(s, name)))
            throw new InvalidOperationException($"space '{name}' already exists.");

        Directory.CreateDirectory(_paths.SnapshotsSpace(name));
    }

    public void Use(string name, CancellationToken ct = default)
    {
        EnsureRepoInitializedOrThrow();
        ValidateNameOrThrow(name);
        ct.ThrowIfCancellationRequested();

        var real = FindExistingNameOrNull(name)
            ?? throw new InvalidOperationException($"space '{name}' does not exist.");

        WriteCurrentPointer(real);
        WriteConfigCurrent(real);
    }

    public void Rename(string oldName, string newName, CancellationToken ct = default)
    {
        EnsureRepoInitializedOrThrow();
        ValidateNameOrThrow(oldName);
        ValidateNameOrThrow(newName);
        ct.ThrowIfCancellationRequested();

        var realOld = FindExistingNameOrNull(oldName)
            ?? throw new InvalidOperationException($"space '{oldName}' does not exist.");

        if (List().Any(s => NameComparer.Equals(s, newName)))
            throw new InvalidOperationException($"space '{newName}' already exists.");

        var src = _paths.SnapshotsSpace(realOld);
        var dst = _paths.SnapshotsSpace(newName);
        Directory.Move(src, dst);

        var cur = ReadCurrentPointerOrNull();
        if (cur is not null && NameComparer.Equals(cur, realOld))
            WriteCurrentPointer(newName);

        var cfg = ReadConfigCurrentOrNull();
        if (cfg is not null && NameComparer.Equals(cfg, realOld))
            WriteConfigCurrent(newName);
    }

    public void Delete(string name, CancellationToken ct = default)
    {
        EnsureRepoInitializedOrThrow();
        ValidateNameOrThrow(name);
        ct.ThrowIfCancellationRequested();

        var real = FindExistingNameOrNull(name)
            ?? throw new InvalidOperationException($"space '{name}' does not exist.");

        var current = ReadCurrentPointerOrNull();
        if (current is not null && NameComparer.Equals(current, real))
            throw new InvalidOperationException(
                $"space '{real}' is current (cannot delete). Switch to another space first.");

        var cfg = ReadConfigCurrentOrNull();
        if (cfg is not null && NameComparer.Equals(cfg, real))
            throw new InvalidOperationException(
                $"space '{real}' is current in config (cannot delete). Switch to another space first.");

        var dir = _paths.SnapshotsSpace(real);
        if (!Directory.Exists(dir))
            throw new InvalidOperationException($"space directory not found: {dir}");

        if (Directory.EnumerateFileSystemEntries(dir).Any())
            throw new InvalidOperationException($"space '{real}' is not empty (cannot delete).");

        Directory.Delete(dir, recursive: false);
    }

    private void EnsureRepoInitializedOrThrow()
    {
        if (!Directory.Exists(_paths.RinneRoot) ||
            !Directory.Exists(_paths.SnapshotsRoot) ||
            !Directory.Exists(_paths.SpacesRoot) ||
            !Directory.Exists(_paths.ConfigDir))
        {
            throw new InvalidOperationException($".rinne is missing or not initialized. Run `rinne init` first: {_paths.RinneRoot}");
        }
    }

    private void ValidateNameOrThrow(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !SpaceNameRules.NameRegex.IsMatch(name))
            throw new ArgumentException("Invalid space name. Must start with [a-z], may contain '-' and '_', max 64 chars. e.g., 'main', 'work_a', 'exp-01'.");
    }

    private string? FindExistingNameOrNull(string name)
        => List().FirstOrDefault(s => NameComparer.Equals(s, name));

    private string? ReadCurrentPointerOrNull()
    {
        var p = _paths.SnapshotsCurrent;
        if (!File.Exists(p)) return null;
        var text = File.ReadAllText(p).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private void WriteCurrentPointer(string name)
    {
        EnsureRepoInitializedOrThrow();
        var p = _paths.SnapshotsCurrent;
        var dir = Path.GetDirectoryName(p)!;
        if (!Directory.Exists(dir))
            throw new InvalidOperationException($"snapshots dir missing: {dir}. Run `rinne init`.");

        var tmp = p + ".tmp";
        File.WriteAllText(tmp, name + Environment.NewLine);
        File.Move(tmp, p, true);
    }

    private string? ReadConfigCurrentOrNull()
    {
        var p = _paths.ConfigJson;
        if (!File.Exists(p)) return null;
        try
        {
            using var fs = File.OpenRead(p);
            using var doc = System.Text.Json.JsonDocument.Parse(fs);
            if (doc.RootElement.TryGetProperty("CurrentSpace", out var prop))
            {
                var val = prop.GetString();
                return string.IsNullOrWhiteSpace(val) ? null : val;
            }
        }
        catch { }
        return null;
    }

    private void WriteConfigCurrent(string name)
    {
        EnsureRepoInitializedOrThrow();
        var p = _paths.ConfigJson;
        var dir = Path.GetDirectoryName(p)!;
        if (!Directory.Exists(dir))
            throw new InvalidOperationException($"config dir missing: {dir}. Run `rinne init`.");

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(p))
        {
            try
            {
                var json = File.ReadAllText(p);
                var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
                if (parsed is not null) foreach (var kv in parsed) dict[kv.Key] = kv.Value;
            }
            catch { }
        }
        dict["CurrentSpace"] = name;

        var tmp = p + ".tmp";
        var text = System.Text.Json.JsonSerializer.Serialize(dict, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(tmp, text);
        File.Move(tmp, p, true);
    }
}
