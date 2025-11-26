using System.Text.Json;

namespace Rinne.Core.Feature.BuildTree;

public sealed class ManifestTreeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly char[] PathSeparators = { '/', '\\' };

    public FsNode BuildFromFile(string manifestJsonPath)
    {
        if (manifestJsonPath is null) throw new ArgumentNullException(nameof(manifestJsonPath));
        if (!File.Exists(manifestJsonPath))
            throw new FileNotFoundException("manifest json not found", manifestJsonPath);

        using var stream = File.OpenRead(manifestJsonPath);
        var manifest = JsonSerializer.Deserialize<Cas2Manifest>(stream, JsonOptions)
                       ?? throw new InvalidOperationException("failed to deserialize manifest");

        return BuildFromManifest(manifest);
    }

    public FsNode BuildFromManifest(Cas2Manifest manifest)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));

        var root = new FsNode(
            name: manifest.Root,
            relativePath: string.Empty,
            isDirectory: true,
            fileEntry: null
        );

        if (manifest.Files is not null)
        {
            foreach (var file in manifest.Files)
            {
                if (string.IsNullOrWhiteSpace(file.RelativePath))
                    continue;

                AddFileNode(root, file);
            }
        }

        if (manifest.Dirs is not null)
        {
            foreach (var dirPath in manifest.Dirs)
            {
                if (string.IsNullOrWhiteSpace(dirPath))
                    continue;

                EnsureDirectory(root, dirPath);
            }
        }

        SortRecursive(root);
        return root;
    }

    private static void AddFileNode(FsNode root, Cas2FileEntry file)
    {
        var parts = SplitPath(file.RelativePath);
        if (parts.Length == 0) return;

        var current = root;
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var isLast = i == parts.Length - 1;

            if (isLast)
            {
                var relativePath = CombineRelativePath(current.RelativePath, part);
                var node = new FsNode(
                    name: part,
                    relativePath: relativePath,
                    isDirectory: false,
                    fileEntry: file
                );
                current.Children.Add(node);
            }
            else
            {
                current = EnsureChildDirectory(current, part);
            }
        }
    }

    private static void EnsureDirectory(FsNode root, string dirPath)
    {
        var parts = SplitPath(dirPath);
        if (parts.Length == 0) return;

        var current = root;
        foreach (var part in parts)
        {
            current = EnsureChildDirectory(current, part);
        }
    }

    private static FsNode EnsureChildDirectory(FsNode parent, string name)
    {
        var existing = parent.Children.FirstOrDefault(x => x.IsDirectory && x.Name == name);
        if (existing is not null) return existing;

        var relativePath = CombineRelativePath(parent.RelativePath, name);
        var node = new FsNode(
            name: name,
            relativePath: relativePath,
            isDirectory: true,
            fileEntry: null
        );
        parent.Children.Add(node);
        return node;
    }

    private static string[] SplitPath(string path) =>
        path.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);

    private static string CombineRelativePath(string parentRelativePath, string name)
    {
        if (string.IsNullOrEmpty(parentRelativePath))
            return name;

        return parentRelativePath + "/" + name;
    }

    private static void SortRecursive(FsNode node)
    {
        if (node.Children.Count == 0) return;

        node.Children.Sort((a, b) =>
        {
            if (a.IsDirectory && !b.IsDirectory) return -1;
            if (!a.IsDirectory && b.IsDirectory) return 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        foreach (var child in node.Children)
        {
            SortRecursive(child);
        }
    }
}

public sealed class FsNode
{
    public string Name { get; }
    public string RelativePath { get; }
    public bool IsDirectory { get; }
    public Cas2FileEntry? FileEntry { get; }
    public List<FsNode> Children { get; } = new();

    public FsNode(string name, string relativePath, bool isDirectory, Cas2FileEntry? fileEntry)
    {
        Name = name;
        RelativePath = relativePath;
        IsDirectory = isDirectory;
        FileEntry = fileEntry;
    }
}

public sealed class Cas2Manifest
{
    public string Version { get; init; } = string.Empty;
    public string Root { get; init; } = string.Empty;
    public string? OriginalSha256 { get; init; }
    public long TotalBytes { get; init; }
    public int FileCount { get; init; }
    public List<Cas2FileEntry> Files { get; init; } = new();
    public List<string>? Dirs { get; init; }
}

public sealed class Cas2FileEntry
{
    public string RelativePath { get; init; } = string.Empty;
    public long Bytes { get; init; }
    public List<string> ChunkHashes { get; init; } = new();
}
