using System.Globalization;

namespace ERateLookup.Core;

public static class UsacTimestamps
{
    public static bool TryParse(string? raw, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
            return true;
        if (DateTime.TryParse(raw, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dateTime))
        {
            value = new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
            return true;
        }

        return false;
    }

    public static string CalendarDate(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        return TryParse(raw, out var parsed)
            ? parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : raw;
    }
}
