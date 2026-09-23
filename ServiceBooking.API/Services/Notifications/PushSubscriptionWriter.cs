using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Notifications.WebPush;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// Create/upsert/delete for <see cref="PushSubscription"/> rows (ARCHITECTURE_CYCLE9.md §105.4, §105.5,
/// US-123). Every write goes through this one class so the advisory-locked "count, then maybe evict"
/// sequence (§105.4's potolok — "не блокирующая ошибка, а тихая замена") is guaranteed, not re-implemented
/// per call site.
/// </summary>
public sealed class PushSubscriptionWriter(AppDbContext db, IOptions<WebPushOptions> webPushOptions, IOptions<NotificationOptions> notificationOptions)
{
    /// <summary>
    /// §105.5: new endpoint → created; same endpoint + same caller → updated in place, not duplicated;
    /// same endpoint + a DIFFERENT caller → reassigned to the caller (the "shared salon computer"
    /// scenario — a 409 here would leave a previous owner receiving a new coworker's client names).
    /// Runs inside its own transaction with <c>AdvisoryLock.AcquireAsync(db, "push-subscriptions:{userId}")</c>
    /// (§105.4) so two tabs subscribing at the same instant can't both sneak past the potolok.
    /// </summary>
    public async Task<(PushSubscription Subscription, bool Created)> UpsertAsync(
        string userId, string endpoint, string p256dh, string auth, string? deviceLabel, CancellationToken ct)
    {
        var encryptionKey = notificationOptions.Value.EncryptionKey ?? string.Empty;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"push-subscriptions:{userId}");

        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        var created = existing is null;
        var reassigned = existing is not null && !string.Equals(existing.UserId, userId, StringComparison.Ordinal);

        var row = existing ?? new PushSubscription { Id = Guid.NewGuid(), Endpoint = endpoint };
        if (created) db.PushSubscriptions.Add(row);

        row.UserId = userId;
        // §105.5 rubeж 1: on a genuine ownership handoff the metadata is entirely the new owner's — a
        // "created" timestamp from the PREVIOUS owner's subscribe would be misleading on the devices
        // list. A same-owner re-subscribe (browser re-registered its own, unchanged endpoint) keeps its
        // original CreatedAtUtc — nothing actually changed hands.
        if (created || reassigned) row.CreatedAtUtc = DateTime.UtcNow;
        row.DeviceLabel = string.IsNullOrWhiteSpace(deviceLabel) ? "Неизвестное устройство" : Truncate(deviceLabel, 100);

        var aad = $"push-subscription:{row.Id}";
        row.P256dhCiphertext = SecretProtector.Encrypt(p256dh, encryptionKey, aad);
        row.AuthCiphertext = SecretProtector.Encrypt(auth, encryptionKey, aad);
        row.KeyId = SecretProtector.ComputeKeyId(SecretProtector.DecodeKey(encryptionKey));

        await db.SaveChangesAsync(ct);

        await EvictOverflowAsync(userId, keepRowId: row.Id, ct);

        await transaction.CommitAsync(ct);
        return (row, created);
    }

    public Task<List<PushSubscription>> ListAsync(string userId, CancellationToken ct) =>
        db.PushSubscriptions.AsNoTracking().Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAtUtc).ToListAsync(ct);

    /// <summary>§105.5: chosen by id, scoped to the caller's own <c>UserId</c> — a chosen id that
    /// belongs to someone else simply isn't found (404, never 403: "существование чужой строки не
    /// подтверждается").</summary>
    public async Task<bool> DeleteByIdAsync(string userId, Guid id, CancellationToken ct)
    {
        var row = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, ct);
        if (row is null) return false;
        db.PushSubscriptions.Remove(row);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>§105.5 rubeж 2: idempotent by design — logout must succeed even on a second call with no
    /// matching row (already deleted, or belongs to someone else after a rubeж-1 reassignment on a
    /// shared device).</summary>
    public async Task DeleteByEndpointAsync(string userId, string endpoint, CancellationToken ct)
    {
        var row = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint && s.UserId == userId, ct);
        if (row is null) return;
        db.PushSubscriptions.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>§105.4: "при одиннадцатой самая старая по CreatedAtUtc удаляется" — silent, never an
    /// error surfaced to the caller. <paramref name="keepRowId"/> is excluded from eviction candidates:
    /// the row this very call just wrote must never be the one removed, even in the edge case where a
    /// long-time subscriber's very first device happens to be their oldest by timestamp.</summary>
    private async Task EvictOverflowAsync(string userId, Guid keepRowId, CancellationToken ct)
    {
        var max = webPushOptions.Value.MaxSubscriptionsPerUser;
        var all = await db.PushSubscriptions.Where(s => s.UserId == userId).ToListAsync(ct);
        var overflow = all.Count - max;
        if (overflow <= 0) return;

        var toEvict = all.Where(s => s.Id != keepRowId).OrderBy(s => s.CreatedAtUtc).Take(overflow).ToList();
        db.PushSubscriptions.RemoveRange(toEvict);
        await db.SaveChangesAsync(ct);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
