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

    /// <summary>US-62 p.6, §30.3 default — used whenever the key is absent, which is the ordinary case
    /// (the key exists only once a superadmin has ever changed it away from the default).</summary>
    public const int DefaultChannelIdleDays = 3;

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
}
