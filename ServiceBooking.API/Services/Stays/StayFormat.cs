using System.Globalization;

namespace ServiceBooking.API.Services.Stays;

/// <summary>Wire formats of the vertical: times of day are "HH:mm".</summary>
public static class StayFormat
{
    public static string Time(TimeOnly t) => t.ToString("HH':'mm", CultureInfo.InvariantCulture);

    public static string? Time(TimeOnly? t) => t is null ? null : Time(t.Value);

    public static bool TryParseTime(string? raw, out TimeOnly time) =>
        TimeOnly.TryParseExact((raw ?? string.Empty).Trim(), "HH':'mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>A time on a 30-minute grid.</summary>
    public static bool IsHalfHour(TimeOnly t) => t.Minute % 30 == 0 && t.Second == 0;

    public static string Date(DateOnly d) => d.ToString("dd'.'MM'.'yyyy", CultureInfo.InvariantCulture);
}
