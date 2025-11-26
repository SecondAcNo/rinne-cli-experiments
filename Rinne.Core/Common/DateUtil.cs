using System.Globalization;

namespace Rinne.Core.Common
{
    public static class DateUtil
    {
        public static DateTimeOffset ParseLocalDateAsUtcMidnight(string s)
        {
            var d = DateTime.ParseExact(s, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None);
            var localMidnight = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Local);
            return new DateTimeOffset(localMidnight).ToUniversalTime();
        }

        public static bool TryParseLocalDateAsUtcMidnight(string s, out DateTimeOffset dto)
        {
            if (DateTime.TryParseExact(s, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                var localMidnight = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Local);
                dto = new DateTimeOffset(localMidnight).ToUniversalTime();
                return true;
            }
            dto = default;
            return false;
        }
    }
}
