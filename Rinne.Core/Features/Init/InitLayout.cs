using Rinne.Core.Common;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace Rinne.Core.Features.Init;

public static class InitLayout
{
    public static InitLayoutResult Ensure(string sourceRoot, InitLayoutOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
            throw new ArgumentException("sourceRoot is required.", nameof(sourceRoot));
        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Source root not found: {sourceRoot}");

        options ??= new InitLayoutOptions();
        var p = new RinnePaths(sourceRoot);

        if (Directory.Exists(p.RinneRoot))
            throw new InvalidOperationException($".rinne already exists: {p.RinneRoot}");

        var createdDirs = new List<string>();
        var createdFiles = new List<string>();
        var warnings = new List<string>();

        void MkDir(string dir)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    createdDirs.Add(Rel(dir));
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"mkdir failed: {Rel(dir)} :: {ex.Message}");
            }
        }

        MkDir(p.RinneRoot);
        MkDir(p.ConfigDir);
        MkDir(p.SnapshotsSpace(options.Space));
        MkDir(p.StoreRoot);
        MkDir(p.StoreManifests);
        MkDir(p.StoreMeta);
        MkDir(p.StoreTmp);
        MkDir(p.TempDir);

        try
        {
            var versionPath = Path.Combine(p.ConfigDir, "version.txt");
            if (!File.Exists(versionPath))
            {
                var version = GetAssemblyVersion();
                WriteUtf8NoBom(versionPath, version + Environment.NewLine);
                createdFiles.Add(Rel(versionPath));
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"version.txt create failed: {ex.Message}");
        }

        if (options.CreateDefaultConfigIfMissing)
        {
            var ignorePath = p.RinneIgnoreJson;
            if (!File.Exists(ignorePath))
            {
                try
                {
                    var json = BuildDefaultIgnoreJson(options);
                    WriteUtf8NoBom(ignorePath, json);
                    createdFiles.Add(Rel(ignorePath));
                }
                catch (Exception ex) { warnings.Add($"rinneignore.json create failed: {ex.Message}"); }
            }
        }

        try
        {
            var cur = p.SnapshotsCurrent;
            Directory.CreateDirectory(Path.GetDirectoryName(cur)!);
            var tmp = cur + ".tmp";
            WriteUtf8NoBom(tmp, options.Space + Environment.NewLine);
            File.Move(tmp, cur, overwrite: true);
            createdFiles.Add(Rel(cur));
        }
        catch (Exception ex)
        {
            warnings.Add($"current create failed: {ex.Message}");
        }

        return new InitLayoutResult(Rel(p.RinneRoot), createdDirs, createdFiles, warnings);

        static void WriteUtf8NoBom(string path, string content)
        {
            var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(path, content, enc);
        }

        static string GetAssemblyVersion()
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var ver = asm.GetName().Version?.ToString();
            return info ?? ver ?? "0.0.0";
        }

        string Rel(string abs)
        {
            try { return Path.GetRelativePath(sourceRoot, abs).Replace('\\', '/'); }
            catch { return abs; }
        }
    }

    private static string BuildDefaultIgnoreJson(InitLayoutOptions opt)
    {
        var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".rinne/**", ".git/**", "bin/**", "obj/**" };
        var excludeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "*.tmp", "*.log", "*.user" };
        var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "cache/", "temp/" };

        if (opt.AddExclude != null) foreach (var s in opt.AddExclude) AddTrimmed(exclude, s);
        if (opt.AddExcludeFiles != null) foreach (var s in opt.AddExcludeFiles) AddTrimmed(excludeFiles, s);
        if (opt.AddExcludeDirs != null) foreach (var s in opt.AddExcludeDirs) AddTrimmed(excludeDirs, s);

        var payload = new
        {
            exclude = exclude.ToArray(),
            excludeFiles = excludeFiles.ToArray(),
            excludeDirs = excludeDirs.ToArray()
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        static void AddTrimmed(HashSet<string> set, string? s)
        { if (!string.IsNullOrWhiteSpace(s)) set.Add(s.Trim()); }
    }
}
