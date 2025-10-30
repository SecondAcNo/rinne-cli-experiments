using System.Text;

namespace Rinne.Cli.Utility
{
    /// <summary>
    /// Console 出力をコンソールとファイルの両方へ複写する TextWriter。
    /// 各行には UTC 日時を自動で付与します。
    /// </summary>
    public sealed class DualWriter : TextWriter
    {
        private readonly TextWriter _console;
        private readonly StreamWriter _file;

        public DualWriter(string logFilePath)
        {
            if (string.IsNullOrWhiteSpace(logFilePath))
                throw new ArgumentException("logFilePath is required.", nameof(logFilePath));

            _console = Console.Out;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logFilePath))!);

            var fs = new FileStream(
                logFilePath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.ReadWrite); // ← 共有

            fs.Seek(0, SeekOrigin.End); // append 相当

            _file = new StreamWriter(fs, Encoding.UTF8)
            {
                AutoFlush = true
            };
        }

        public override Encoding Encoding => Encoding.UTF8;

        private static string Prefix()
            => $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ";

        public override void WriteLine(string? value)
        {
            var line = value ?? string.Empty;
            _console.WriteLine(line);
            _file.WriteLine(Prefix() + line);
        }

        public override void Write(string? value)
        {
            _console.Write(value);
            _file.Write(value);
        }

        public override void WriteLine()
        {
            _console.WriteLine();
            _file.WriteLine();
        }

        public override void Flush()
        {
            _console.Flush();
            _file.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _file.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
