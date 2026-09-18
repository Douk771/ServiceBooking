using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;
using PlatformSettingKeys = ServiceBooking.API.Services.Notifications.PlatformSettings;

namespace ServiceBooking.API.Services;

/// <summary>
/// Writes the two cycle-4 <see cref="PlatformSetting"/> keys (ARCHITECTURE_CYCLE4.md §23.3), journaling
/// every change (US-57 pp. 5–6). Reads go through the other backend developer's
/// <see cref="Notifications.PlatformSettings"/> (scoped, 60s <c>IMemoryCache</c> layer) — this class only
/// adds the write/journal half admin endpoints need, which that reader intentionally doesn't have, and
/// reuses its key constants (<see cref="PlatformSettingKeys.ChannelPricePerMonthKey"/>,
/// <see cref="PlatformSettingKeys.ChannelIdleDaysKey"/>) so the two files can never disagree about which
/// string is which setting. Kept outside <c>Services/Notifications/</c> (this cycle's file-ownership
/// split between two backend developers) purely because it's this developer's admin-endpoint code, not
/// because the read/write split is architecturally meaningful on its own.
/// </summary>
public static class PlatformSettingsWriter
{
    public const string PriceKey = PlatformSettingKeys.ChannelPricePerMonthKey;
    public const string IdleDaysKey = PlatformSettingKeys.ChannelIdleDaysKey;

    /// <summary>Writes one key, journaling the change — caller supplies the old value it already has
    /// (from <see cref="Notifications.PlatformSettings"/>) so this doesn't need a second round trip.
    /// Does NOT call <c>SaveChangesAsync</c> — same "caller's transaction commits it" convention as
    /// <see cref="NotificationScheduler"/>.</summary>
    public static async Task WriteAsync(
        AppDbContext db, string key, string? oldValue, string newValue, string changedByUserId, string? comment = null)
    {
        var existing = db.PlatformSettings.Local.FirstOrDefault(s => s.Key == key) ??
            await db.PlatformSettings.FindAsync(key);

        if (existing is null)
        {
            db.PlatformSettings.Add(new PlatformSetting
            {
                Key = key, Value = newValue, UpdatedAt = DateTime.UtcNow, UpdatedByUserId = changedByUserId,
            });
        }
        else
        {
            existing.Value = newValue;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedByUserId = changedByUserId;
        }

        db.PlatformSettingChangeLogs.Add(new PlatformSettingChangeLog
        {
            Id = Guid.NewGuid(),
            Key = key,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedByUserId = changedByUserId,
            Comment = comment,
        });
    }
}
