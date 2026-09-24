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
