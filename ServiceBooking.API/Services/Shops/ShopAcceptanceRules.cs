using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services.Shops;

/// <summary>The acceptance state a screen prints. Everything here is text the server composed.</summary>
public sealed record ShopAcceptanceState(
    ShopAcceptanceMode Mode, DateTime? PausedUntilUtc, string StatusText, DateTime? ChangedAtUtc, string? ChangedByName, string? ChangedText);

/// <summary>
/// ARCHITECTURE_CYCLE24.md §449.3 — pause and the "stop" switch as pure functions. There is NO background task that ends a pause:
/// <c>PausedUntilUtc ≤ now</c> simply reads as "no pause", so the resume is instant and survives any restart.
/// </summary>
public static class ShopAcceptanceRules
{
    public const string AcceptingText = "Принимаем заказы";
    public const string StoppedText = "Не принимаем, пока не включите";
    public const string PauseDurationRequired = "Укажите длительность паузы";

    public static bool IsPaused(ShopSettings settings, DateTime nowUtc) =>
        !settings.OrdersStopped && settings.PausedUntilUtc is { } until && until > nowUtc;

    public static ShopAcceptanceMode Mode(ShopSettings settings, DateTime nowUtc) =>
        settings.OrdersStopped ? ShopAcceptanceMode.Stopped : IsPaused(settings, nowUtc) ? ShopAcceptanceMode.Paused : ShopAcceptanceMode.Accepting;

    public static ShopAcceptanceState State(ShopSettings settings, DateTime nowUtc, TimeZoneInfo zone)
    {
        var mode = Mode(settings, nowUtc);
        var until = mode == ShopAcceptanceMode.Paused ? settings.PausedUntilUtc : null;
        var status = mode switch
        {
            ShopAcceptanceMode.Paused => $"Пауза до {PausedUntilText(until!.Value, nowUtc, zone)}",
            ShopAcceptanceMode.Stopped => StoppedText,
            _ => AcceptingText
        };
        return new ShopAcceptanceState(mode, until, status, settings.AcceptanceChangedAtUtc, settings.AcceptanceChangedByName,
            ChangedText(settings.AcceptanceChangedAtUtc, settings.AcceptanceChangedByName, zone));
    }

    /// <summary>"13:30", or "3 окт 9:00" when the pause ends on another day of the shop.</summary>
    public static string PausedUntilText(DateTime pausedUntilUtc, DateTime nowUtc, TimeZoneInfo zone)
    {
        var until = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(pausedUntilUtc), zone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(nowUtc), zone);
        var clock = ShopTimeTexts.Clock(until.Hour * 60 + until.Minute);
        return DateOnly.FromDateTime(until) == DateOnly.FromDateTime(now) ? clock : $"{ShopTimeTexts.DayMonth(DateOnly.FromDateTime(until))} {clock}";
    }

    /// <summary>P1 — "Изменено: Анна, 12:58": neutral, no gender is guessed.</summary>
    public static string? ChangedText(DateTime? changedAtUtc, string? changedByName, TimeZoneInfo zone)
    {
        if (changedAtUtc is not { } at) return null;
        var local = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(at), zone);
        var clock = ShopTimeTexts.Clock(local.Hour * 60 + local.Minute);
        return string.IsNullOrWhiteSpace(changedByName) ? $"Изменено: {clock}" : $"Изменено: {changedByName}, {clock}";
    }

    /// <summary>The values to store for a request. <c>pause</c> is required exactly for <see cref="ShopAcceptanceMode.Paused"/>.</summary>
    public static (bool Stopped, DateTime? PausedUntilUtc)? Apply(
        ShopAcceptanceMode mode, PauseDuration? pause, DateTime nowUtc, PickupSchedule schedule)
    {
        switch (mode)
        {
            case ShopAcceptanceMode.Accepting:
                return (false, null);
            case ShopAcceptanceMode.Stopped:
                return (true, null);
            default:
                if (pause is null) return null;
                var until = pause.Value switch
                {
                    PauseDuration.Minutes15 => nowUtc.AddMinutes(15),
                    PauseDuration.Minutes30 => nowUtc.AddMinutes(30),
                    PauseDuration.Hour1 => nowUtc.AddHours(1),
                    _ => schedule.PauseEndOfDay(nowUtc)
                };
                return (false, until);
        }
    }

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
