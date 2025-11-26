using System.Text.Json;

namespace Rinne.Core.Config;

public sealed class ExcludeConfig
{
    public List<string>? Exclude { get; init; }
    public List<string>? ExcludeFiles { get; init; }
    public List<string>? ExcludeDirs { get; init; }

    public static ExcludeConfig Load(string configPath)
    {
        try
        {
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var cfg = JsonSerializer.Deserialize<ExcludeConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
                if (cfg is not null) return cfg;
            }
        }
        catch
        {
        }
        return new ExcludeConfig();
    }

    public ExcludeConfig WithDefaults()
    {
        static IEnumerable<string> Norm(IEnumerable<string>? xs)
            => (xs ?? Enumerable.Empty<string>())
                .Select(s => s.Trim())
                .Where(s => s.Length > 0);

        var common = new HashSet<string>(Norm(Exclude), StringComparer.OrdinalIgnoreCase)
        {
            ".rinne/**", ".git/**", "bin/**", "obj/**"
        };
        var files = new HashSet<string>(Norm(ExcludeFiles), StringComparer.OrdinalIgnoreCase)
        {
            "*.tmp", "*.log", "*.user"
        };
        var dirs = new HashSet<string>(Norm(ExcludeDirs), StringComparer.OrdinalIgnoreCase);

        return new ExcludeConfig
        {
            Exclude = common.ToList(),
            ExcludeFiles = files.ToList(),
            ExcludeDirs = dirs.ToList()
        };
    }
}
