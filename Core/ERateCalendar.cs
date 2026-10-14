namespace ERateLookup.Core;

/// <summary>
/// E-rate funding year N runs from July 1 of calendar year N through June 30 of year N+1.
/// The date is taken from the injected clock's offset, not converted to another zone.
/// Filing-window pages describe a known year; they do not decide which year is current.
/// </summary>
public static class ERateCalendar
{
    public static int CurrentFundingYear(DateTimeOffset instant)
    {
        var date = instant.Date;
        return date.Month >= 7 ? date.Year : date.Year - 1;
    }

    public static int NextFundingYear(DateTimeOffset instant) => CurrentFundingYear(instant) + 1;
}
