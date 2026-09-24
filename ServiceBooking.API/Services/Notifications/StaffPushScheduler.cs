using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// Queues <see cref="StaffPushNotification"/> rows on <c>StaffBookingCreated</c> (ARCHITECTURE_CYCLE9.md
/// §105.6, US-116). Called from <c>BookingsController.Create</c>, right next to the existing
/// <see cref="NotificationScheduler.OnBookingCreatedAsync"/> call, in the SAME transaction — this class
/// never calls <c>SaveChangesAsync</c> itself, exactly like <see cref="NotificationScheduler"/> (see that
/// class's own doc comment for why).
///
/// Cancellation, reschedule and reminder are deliberately NOT wired here (§105.6: "Отмена, перенос и
/// напоминание в push не шлются") — only the create path exists this cycle.
/// </summary>
public sealed class StaffPushScheduler(AppDbContext db)
{
    /// <param name="booking">The just-created booking (not yet saved — same convention as
    /// <see cref="NotificationScheduler.OnBookingCreatedAsync"/>).</param>
    /// <param name="serviceNames">Visit's service names, in visit order.</param>
    /// <param name="creatorUserId">The AUTHENTICATED caller's own id (never <c>booking.ClientId</c>) —
    /// §105.6 p.2: "запись создал сам мастер" is judged against who is actually making this HTTP
    /// request, not who the booking is nominally for.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task OnBookingCreatedAsync(
        Booking booking, IReadOnlyList<string> serviceNames, string? creatorUserId, CancellationToken ct)
    {
        var settings = await db.CompanyNotificationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CompanyId == booking.CompanyId, ct);
        var staffPushEnabled = settings?.StaffPushEnabled ?? new CompanyNotificationSettings().StaffPushEnabled;
        // §105.6 p.1: disabled setting -> zero rows, not "queued then skipped" (a disabled feature must
        // not grow a journal of its own).
        if (!staffPushEnabled) return;

        // §105.6 p.2: the master recording their own new booking gets nothing.
        if (creatorUserId is not null && string.Equals(creatorUserId, booking.MasterId, StringComparison.Ordinal))
            return;

        var subscriptions = await db.PushSubscriptions.AsNoTracking()
            .Where(s => s.UserId == booking.MasterId).ToListAsync(ct);
        // §105.6 p.3: not subscribed on any device -> nobody to tell, nothing to queue.
        if (subscriptions.Count == 0) return;

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == booking.CompanyId, ct);
        if (company is null) return;

        var visitStartUtc = NotificationTiming.ComputeVisitStartUtc(booking.Date, booking.StartTime, company.TimeZoneId);
        var clientName = await ResolveClientNameAsync(booking, ct);
        var payload = BuildPayload(serviceNames, booking.Date, booking.StartTime, clientName, booking.Id);
        var nowUtc = DateTime.UtcNow;
        // §105.8 (Q17): min(CreatedAt + 1h, visit start) — a row surviving past this is never sent at all.
        var expiresAtUtc = nowUtc.AddHours(1) < visitStartUtc ? nowUtc.AddHours(1) : visitStartUtc;

        // §105.6 p.4: one row per subscribed device, keyed so a retry/duplicate call collides into the
        // same row instead of duplicating (the DB's unique index on IdempotencyKey is the actual
        // guarantee; this AddRange simply relies on it the same way NotificationScheduler's QueueAsync does).
        foreach (var subscription in subscriptions)
        {
            db.StaffPushNotifications.Add(new StaffPushNotification
            {
                Id = Guid.NewGuid(),
                UserId = booking.MasterId,
                CompanyId = booking.CompanyId,
                BookingId = booking.Id,
                SubscriptionId = subscription.Id,
                Type = NotificationType.StaffBookingCreated,
                Payload = payload,
                Status = NotificationStatus.Pending,
                ExpiresAtUtc = expiresAtUtc,
                CreatedAt = nowUtc,
                IdempotencyKey = BuildIdempotencyKey(booking.Id, booking.MasterId, subscription.Id),
            });
        }
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE15.md §257.6/§287.5 — queues a StaffBookingRescheduled push row for the
    /// booking's master. Called ONLY when the caller who moved the booking has ClientOwner authority
    /// (BookingsController.Reschedule) — staff rescheduling their own booking never reaches this method,
    /// the same rule <see cref="OnBookingCreatedAsync"/> already applies via its creatorUserId check.
    /// </summary>
    /// <param name="booking">The just-rescheduled booking (already updated to its NEW date/time — same
    /// convention as <see cref="NotificationScheduler.OnBookingRescheduledAsync"/>, called right before
    /// this in the same transaction).</param>
    /// <param name="serviceNames">Visit's service names, in visit order.</param>
    /// <param name="actorUserId">The authenticated caller's own id — always the client who owns this
    /// booking on this path, never trusted as a staff id.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task OnBookingRescheduledAsync(
        Booking booking, IReadOnlyList<string> serviceNames, string? actorUserId, CancellationToken ct)
    {
        var settings = await db.CompanyNotificationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CompanyId == booking.CompanyId, ct);
        var staffPushEnabled = settings?.StaffPushEnabled ?? new CompanyNotificationSettings().StaffPushEnabled;
        if (!staffPushEnabled) return;

        // Defensive mirror of OnBookingCreatedAsync's own guard: the master rescheduling their own
        // booking (should never reach here via the ClientOwner-only call site, but the method itself
        // must not depend on the caller getting that right) gets nothing.
        if (actorUserId is not null && string.Equals(actorUserId, booking.MasterId, StringComparison.Ordinal))
            return;

        var subscriptions = await db.PushSubscriptions.AsNoTracking()
            .Where(s => s.UserId == booking.MasterId).ToListAsync(ct);
        if (subscriptions.Count == 0) return;

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == booking.CompanyId, ct);
        if (company is null) return;

        var visitStartUtc = NotificationTiming.ComputeVisitStartUtc(booking.Date, booking.StartTime, company.TimeZoneId);
        var clientName = await ResolveClientNameAsync(booking, ct);
        var payload = BuildRescheduledPayload(serviceNames, booking.Date, booking.StartTime, clientName, booking.Id);
        var nowUtc = DateTime.UtcNow;
        var expiresAtUtc = nowUtc.AddHours(1) < visitStartUtc ? nowUtc.AddHours(1) : visitStartUtc;

        foreach (var subscription in subscriptions)
        {
            var idempotencyKey = BuildRescheduledIdempotencyKey(
                booking.Id, booking.MasterId, subscription.Id, booking.Date, booking.StartTime);

            // Mirrors NotificationScheduler.cs:362. The key is unique-indexed (AppDbContext.cs:587) and
            // the SaveChangesAsync that persists it lives OUTSIDE the caller's notification try/catch,
            // so an unguarded Add turns a duplicate into a 500 that rolls the whole reschedule back.
            // Duplicates are reachable without any misuse: 10:00 -> 14:00, back to 10:00, then 14:00
            // again re-derives the first row's key. Same slot, same master -- the queued push already
            // says what this one would, so skipping is the right answer, not a discriminator.
            var alreadyQueued = await db.StaffPushNotifications
                .AnyAsync(n => n.IdempotencyKey == idempotencyKey, ct);
            if (alreadyQueued) continue;

            db.StaffPushNotifications.Add(new StaffPushNotification
            {
                Id = Guid.NewGuid(),
                UserId = booking.MasterId,
                CompanyId = booking.CompanyId,
                BookingId = booking.Id,
                SubscriptionId = subscription.Id,
                Type = NotificationType.StaffBookingRescheduled,
                Payload = payload,
                Status = NotificationStatus.Pending,
                ExpiresAtUtc = expiresAtUtc,
                CreatedAt = nowUtc,
                IdempotencyKey = idempotencyKey,
            });
        }
    }

    /// <summary>Includes the new date/time (unlike <see cref="BuildIdempotencyKey"/>'s create-only key)
    /// so a SECOND reschedule of the same booking queues its own row instead of colliding into the
    /// first one's idempotency key and silently vanishing.</summary>
    public static string BuildRescheduledIdempotencyKey(Guid bookingId, string userId, Guid subscriptionId, DateOnly date, TimeOnly startTime) =>
        $"{NotificationType.StaffBookingRescheduled}:{bookingId}:{userId}:{subscriptionId}:{date:O}:{startTime:O}";

    internal static string BuildRescheduledPayload(
        IReadOnlyList<string> serviceNames, DateOnly date, TimeOnly startTime, string clientName, Guid bookingId)
    {
        var services = serviceNames.Count > 0 ? string.Join(", ", serviceNames) : "услуга";
        var body = $"{services} · перенесено на {date:dd.MM.yyyy} в {startTime:HH:mm} · {clientName}";
        return JsonSerializer.Serialize(new
        {
            title = "Запись перенесена",
            body,
            tag = $"b-{bookingId}",
            url = $"/my-bookings?booking={bookingId}",
        });
    }

    public static string BuildIdempotencyKey(Guid bookingId, string userId, Guid subscriptionId) =>
        $"{NotificationType.StaffBookingCreated}:{bookingId}:{userId}:{subscriptionId}";

    /// <summary>§115.6: <c>{ title, body, tag, url }</c> JSON — the service worker's <c>event.data.json()</c>
    /// contract, NOT a free-form string (a plain string here makes JSON parsing throw client-side and the
    /// browser falls back to a blank-body default notification). §105.6 (П8, minimum): body carries
    /// service(s), date/time, client name — no phone, the payload reaches the OS tray, including a locked
    /// screen. <c>tag</c> collapses repeats for the same visit; <c>url</c> is the relative path the service
    /// worker opens on click (origin is added client-side, §115.6 — never emit an absolute URL here).</summary>
    internal static string BuildPayload(
        IReadOnlyList<string> serviceNames, DateOnly date, TimeOnly startTime, string clientName, Guid bookingId)
    {
        var services = serviceNames.Count > 0 ? string.Join(", ", serviceNames) : "услуга";
        var body = $"{services} · {date:dd.MM.yyyy} в {startTime:HH:mm} · {clientName}";
        return JsonSerializer.Serialize(new
        {
            title = "Новая запись",
            body,
            tag = $"b-{bookingId}",
            url = $"/my-bookings?booking={bookingId}",
        });
    }

    private async Task<string> ResolveClientNameAsync(Booking booking, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(booking.GuestName)) return booking.GuestName;

        if (!string.IsNullOrEmpty(booking.ClientId))
        {
            var client = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == booking.ClientId, ct);
            if (client is not null)
            {
                var name = $"{client.FirstName} {client.LastName}".Trim();
                if (name.Length > 0) return name;
            }
        }

        return "Клиент";
    }
}
