namespace Rinne.Core.Common;

public static class Clock
{
    public static Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;
}
