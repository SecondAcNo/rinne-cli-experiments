using System.Globalization;

namespace Rinne.Cli.Commands;

internal static class CliArgs
{
    public static string NeedValue(string[] args, ref int i, string opt)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"missing value for {opt}");
        return args[++i];
    }

    public static int ParseNonNegativeInt(string s, string optName)
    {
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) || v < 0)
            throw new ArgumentException($"{optName} must be a non-negative integer.");
        return v;
    }
}
