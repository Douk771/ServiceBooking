using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// Typed reader over <see cref="Core.Entities.PlatformSetting"/> (ARCHITECTURE_CYCLE4.md §23.3) — a
/// thin, scoped wrapper with a 60-second <see cref="IMemoryCache"/> layer, so a background task polling
/// every 15 minutes and an admin screen hit on every page load don't each turn into a query against a
/// two-row table on every call.
/// </summary>
public sealed class PlatformSettings(AppDbContext db, IMemoryCache cache, IConfiguration configuration)
{
    // Cycle 40 (ARCHITECTURE_CYCLE40.md §40.7.1, §40.12). The two availability switches and the global messaging switch are
    // changed by a superadmin without a release; the configuration only supplies the default for an ABSENT key
    // (Notifications:OptionAvailability:WhatsApp = false, :Max = true; no key for the global switch = on).
    public const string OptionWhatsAppOpenKey = "notifications.option.whatsapp.open";
    public const string OptionMaxOpenKey = "notifications.option.max.open";
    public const string CustomerMessagingEnabledKey = "notifications.customer-messaging.enabled";

    public const string ChannelPricePerMonthKey = "notifications.channel.price-per-month";
    public const string ChannelIdleDaysKey = "notifications.channel.idle-days";
    // T5-B12 (ARCHITECTURE_CYCLE5.md §51.2, US-70 п. 1) — comma-separated, edited by SuperAdmin without a
    // release (PlatformSettingsWriter already exists, its own change-log too). PlatformSetting.Value was
    // widened to 2000 chars specifically because this list didn't fit in the old 200 (§44.7).
    public const string AdMarkersKey = "notifications.template.ad-markers";

    // Cycle 18 (ARCHITECTURE_CYCLE18.md §339.1). Act ONLY on new trials — an already-granted trial
    // reads its own snapshot (BillingAccount.Trial*/TrialGrant), never these live values (Д19).
    public const string TrialDurationDaysKey = "trial.duration-days";
    public const string TrialMailingWindowDaysKey = "trial.mailing-window-days";
    public const string TrialWarningThresholdsDaysKey = "trial.warning-thresholds-days";
    public const int DefaultTrialDurationDays = 14;
    public const int DefaultTrialMailingWindowDays = 7;

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

    /// <summary>Platform-setting key of a transport's availability switch (§40.7.1).</summary>
    public static string OptionOpenKey(NotificationTransport transport) => transport switch
    {
        NotificationTransport.WhatsApp => OptionWhatsAppOpenKey,
        NotificationTransport.Max => OptionMaxOpenKey,
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null),
    };

    /// <summary>Configuration default of a transport's availability (§40.7.1): WhatsApp closed, MAX open — applies only
    /// while the platform-setting key is absent or holds something other than <c>true</c>/<c>false</c>.</summary>
    public bool OptionOpenDefault(NotificationTransport transport) => transport switch
    {
        NotificationTransport.WhatsApp => configuration.GetValue("Notifications:OptionAvailability:WhatsApp", false),
        NotificationTransport.Max => configuration.GetValue("Notifications:OptionAvailability:Max", true),
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null),
    };

    /// <summary><c>open(X)</c> of §40.7.1: the platform setting, else the configuration default. Open does NOT mean
    /// "can be sold" (price, active flag and the published offer are checked by <c>ChannelOptionAvailability.Sellable</c>)
    /// and never switches off what has already been paid for.</summary>
    public async Task<bool> IsOptionOpenAsync(NotificationTransport transport, CancellationToken ct = default) =>
        Funding.ChannelOptionAvailability.IsOpen(await GetRawAsync(OptionOpenKey(transport), ct), OptionOpenDefault(transport));

    /// <summary>The global switch of customer messaging (§40.12): an absent key (or garbage) means ON.</summary>
    public async Task<bool> IsCustomerMessagingEnabledAsync(CancellationToken ct = default) =>
        !string.Equals((await GetRawAsync(CustomerMessagingEnabledKey, ct))?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>Cycle 18 (§339.1). A corrupt/missing duration is treated as a hard refusal
    /// (<c>TrialNotOffered</c>) by the caller, NOT silently defaulted — an activation must never grant
    /// an unclear number of days. Returns null on a missing/invalid value so the caller can tell the
    /// two cases apart from a merely-absent-but-fine default.</summary>
    public async Task<int?> GetTrialDurationDaysAsync(CancellationToken ct = default)
    {
        var raw = await GetRawAsync(TrialDurationDaysKey, ct);
        if (raw is null) return DefaultTrialDurationDays;
        return int.TryParse(raw, out var days) && days is >= 1 and <= 365 ? days : null;
    }

    public async Task<int?> GetTrialMailingWindowDaysAsync(CancellationToken ct = default)
    {
        var raw = await GetRawAsync(TrialMailingWindowDaysKey, ct);
        if (raw is null) return DefaultTrialMailingWindowDays;
        return int.TryParse(raw, out var days) && days is >= 1 and <= 365 ? days : null;
    }

    /// <summary>Degrades to <see cref="ServiceBooking.API.Services.Billing.TrialWindow.DefaultWarningThresholds"/>
    /// on a corrupt value — a display-only degradation (§339.1: "для thresholds — деградация показа"),
    /// unlike the duration/window getters above which fail closed.</summary>
    public async Task<IReadOnlyList<int>> GetTrialWarningThresholdsDaysAsync(CancellationToken ct = default)
    {
        var raw = await GetRawAsync(TrialWarningThresholdsDaysKey, ct);
        return ServiceBooking.API.Services.Billing.TrialWindow.ParseThresholds(raw);
    }
}
