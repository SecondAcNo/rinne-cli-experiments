using System.Text;

namespace Rinne.Core.Features.Notes;

public sealed class NoteService
{
    public const string DefaultFileName = "note.md";

    public sealed record WriteOptions(
        string? Text = null,
        string? FromFile = null,
        string FileName = DefaultFileName,
        bool Overwrite = true,
        bool EnsureUtf8Bom = true,
        bool UseCrLf = true);

    public bool Ensure(string snapshotRoot, string fileName = DefaultFileName, bool ensureUtf8Bom = true, bool useCrLf = true)
    {
        if (string.IsNullOrWhiteSpace(snapshotRoot))
            throw new ArgumentException("snapshotRoot is required");

        Directory.CreateDirectory(snapshotRoot);

        var path = Path.Combine(snapshotRoot, fileName);
        if (File.Exists(path)) return false;

        var enc = ensureUtf8Bom
            ? new UTF8Encoding(true)
            : new UTF8Encoding(false);

        File.WriteAllText(path, string.Empty, enc);
        return true;
    }

    public string? Write(string snapshotRoot, WriteOptions opt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(snapshotRoot))
            throw new ArgumentException("snapshotRoot is required");

        string? content = null;

        if (!string.IsNullOrEmpty(opt.FromFile))
        {
            if (!File.Exists(opt.FromFile))
                throw new FileNotFoundException("content file not found", opt.FromFile);
            content = File.ReadAllText(opt.FromFile);
        }
        else if (opt.Text is not null)
        {
            content = opt.Text;
        }

        if (content is null)
            return null;

        if (opt.UseCrLf)
            content = content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

        Directory.CreateDirectory(snapshotRoot);

        var path = Path.Combine(snapshotRoot, opt.FileName);
        if (!opt.Overwrite && File.Exists(path))
            throw new IOException($"note already exists: {path}");

        var enc = opt.EnsureUtf8Bom
            ? new UTF8Encoding(true)
            : new UTF8Encoding(false);

        File.WriteAllText(path, content, enc);
        return path;
    }

    public void Clear(string snapshotRoot, string fileName = DefaultFileName, bool ensureUtf8Bom = true)
    {
        if (string.IsNullOrWhiteSpace(snapshotRoot))
            throw new ArgumentException("snapshotRoot is required");

        Directory.CreateDirectory(snapshotRoot);
        var path = Path.Combine(snapshotRoot, fileName);

        var enc = ensureUtf8Bom
            ? new UTF8Encoding(true)
            : new UTF8Encoding(false);

        File.WriteAllText(path, string.Empty, enc);
    }

    public bool Exists(string snapshotRoot, string fileName = DefaultFileName)
        => File.Exists(Path.Combine(snapshotRoot, fileName));

    public string? Read(string snapshotRoot, string fileName = DefaultFileName)
    {
        var path = Path.Combine(snapshotRoot, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void DeleteIfExists(string snapshotRoot, string fileName = DefaultFileName)
    {
        var path = Path.Combine(snapshotRoot, fileName);
        if (File.Exists(path)) File.Delete(path);
    }
}
