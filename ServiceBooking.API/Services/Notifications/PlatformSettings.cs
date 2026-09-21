using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// Typed reader over <see cref="Core.Entities.PlatformSetting"/> (ARCHITECTURE_CYCLE4.md §23.3) — a
/// thin, scoped wrapper with a 60-second <see cref="IMemoryCache"/> layer, so a background task polling
/// every 15 minutes and an admin screen hit on every page load don't each turn into a query against a
/// two-row table on every call.
/// </summary>
public sealed class PlatformSettings(AppDbContext db, IMemoryCache cache)
{
    public const string ChannelPricePerMonthKey = "notifications.channel.price-per-month";
    public const string ChannelIdleDaysKey = "notifications.channel.idle-days";
    // T5-B12 (ARCHITECTURE_CYCLE5.md §51.2, US-70 п. 1) — comma-separated, edited by SuperAdmin without a
    // release (PlatformSettingsWriter already exists, its own change-log too). PlatformSetting.Value was
    // widened to 2000 chars specifically because this list didn't fit in the old 200 (§44.7).
    public const string AdMarkersKey = "notifications.template.ad-markers";

    /// <summary>US-62 p.6, §30.3 default — used whenever the key is absent, which is the ordinary case
    /// (the key exists only once a superadmin has ever changed it away from the default).</summary>
    public const int DefaultChannelIdleDays = 3;

    /// <summary>The starting dictionary (ARCHITECTURE_CYCLE5.md §51.2's own example list) — used only
    /// when a superadmin has never set the key, exactly like <see cref="DefaultChannelIdleDays"/> above.</summary>
    public static readonly IReadOnlyList<string> DefaultAdMarkers =
        ["скидк", "акци", "промо", "дарим", "подар", "спецпредлож", "%", "бесплатн", "приводи", "успей", "только до"];

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    /// <summary>Monthly price of the notification-channel option, or <see langword="null"/> when the key
    /// is absent — absence means the option is not offered yet (US-57 p.6), never "price 0".</summary>
    public async Task<decimal?> GetChannelPricePerMonthAsync(CancellationToken ct = default)
    {
        var raw = await GetRawAsync(ChannelPricePerMonthKey, ct);
        return raw is not null && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var price)
            ? price
            : null;
    }

    /// <summary>How many days a channel may sit idle before <see cref="ServiceBooking.API.Services.Scheduling.Tasks.ChannelHealthTask"/> deletes its
    /// provider instance (§30.3) — <see cref="DefaultChannelIdleDays"/> when the key is absent or not a
    /// valid non-negative integer.</summary>
    public async Task<int> GetChannelIdleDaysAsync(CancellationToken ct = default)
    {
        var raw = await GetRawAsync(ChannelIdleDaysKey, ct);
        return raw is not null && int.TryParse(raw, out var days) && days >= 0 ? days : DefaultChannelIdleDays;
    }

    /// <summary>Split on commas, trimmed, empty entries dropped — <see cref="DefaultAdMarkers"/> when the
    /// key is absent.</summary>
    public async Task<IReadOnlyList<string>> GetAdMarkersAsync(CancellationToken ct = default)
    {
        var raw = await GetRawAsync(AdMarkersKey, ct);
        if (raw is null) return DefaultAdMarkers;

        var markers = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return markers.Length > 0 ? markers : DefaultAdMarkers;
    }

    private async Task<string?> GetRawAsync(string key, CancellationToken ct)
    {
        var cacheKey = $"platform-setting:{key}";
        if (cache.TryGetValue(cacheKey, out string? cached)) return cached;

        var value = await db.PlatformSettings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        cache.Set(cacheKey, value, CacheDuration);
        return value;
    }

    /// <summary>Reviewer note: the admin PUT endpoint (<see cref="PlatformSettingsWriter"/>) wrote a new
    /// value straight to the database without ever calling this — the 60s cache above kept serving the
    /// OLD price/idle-days for up to a minute after a superadmin saved a change, with no way to tell
    /// (from the API response, which reads straight from the DB write) that the read side hadn't caught
    /// up yet. Called once per changed key, right after <see cref="PlatformSettingsWriter.WriteAsync"/>.</summary>
    public void InvalidateCache(string key) => cache.Remove($"platform-setting:{key}");
}
