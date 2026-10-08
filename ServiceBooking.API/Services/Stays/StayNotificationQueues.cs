using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Orders.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>Who of a «Дома» company gets booking messages: the owner and the managers (a housekeeper never does). Used when queueing AND when sending.</summary>
public static class StayStaffRecipients
{
    public static Task<List<string>> UserIdsAsync(AppDbContext db, Guid companyId, string? exceptUserId, CancellationToken ct) =>
        db.CompanyMembers.AsNoTracking()
            .Where(cm => cm.CompanyId == companyId && (cm.Role == UserRole.CompanyOwner || (cm.Role == UserRole.Master && cm.StaffPosition == StaffPosition.Manager)))
            .Where(cm => cm.UserId != exceptUserId)
            .Select(cm => cm.UserId).Distinct().ToListAsync(ct);

    public static Task<bool> IsRecipientAsync(AppDbContext db, Guid companyId, string userId, CancellationToken ct = default) =>
        db.CompanyMembers.AsNoTracking().AnyAsync(cm => cm.CompanyId == companyId && cm.UserId == userId &&
            (cm.Role == UserRole.CompanyOwner || (cm.Role == UserRole.Master && cm.StaffPosition == StaffPosition.Manager)), ct);
}

/// <summary>
/// What a notification is about: a booking of a house OR a stand-alone order of a service (ARCHITECTURE_CYCLE39.md §39.9.2). The queues write exactly one of the two keys
/// (the CHECK constraints of the tables allow at most one subject).
/// </summary>
public readonly record struct StayNotificationSubject(Guid CompanyId, string? GuestUserId, Guid? StayBookingId, Guid? StayServiceOrderId)
{
    public static StayNotificationSubject Of(StayBooking b) => new(b.CompanyId, b.GuestUserId, b.Id, null);

    public static StayNotificationSubject Of(StayServiceOrder o) => new(o.CompanyId, o.GuestUserId, null, o.Id);

    /// <summary>The key part that tells subjects apart in an idempotency key of a message.</summary>
    public string KeyPart => StayBookingId is not null ? "stay" : "so";
}

/// <summary>Push to the devices of the owner and managers — every device of the recipient (cycle 33); the key is one row per event and device.</summary>
public sealed class StayStaffPushQueue(AppDbContext db, IStaysClock clock)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    public Task QueueAsync(StayBooking booking, Guid eventId, NotificationType type, PushPayload payload, CancellationToken ct) =>
        QueueAsync(StayNotificationSubject.Of(booking), eventId, type, payload, ct);

    public async Task QueueAsync(StayNotificationSubject subject, Guid eventId, NotificationType type, PushPayload payload, CancellationToken ct)
    {
        var userIds = await StayStaffRecipients.UserIdsAsync(db, subject.CompanyId, subject.GuestUserId, ct);
        if (userIds.Count == 0) return;
        var subscriptions = await db.PushSubscriptions.AsNoTracking().Where(s => userIds.Contains(s.UserId)).ToListAsync(ct);
        var now = clock.UtcNow;
        foreach (var subscription in subscriptions)
            db.StaffPushNotifications.Add(new StaffPushNotification
            {
                Id = Guid.NewGuid(), UserId = subscription.UserId, CompanyId = subject.CompanyId, StayBookingId = subject.StayBookingId, StayServiceOrderId = subject.StayServiceOrderId,
                SubscriptionId = subscription.Id, Type = type,
                // the url is absolute (a push may be opened by the worker of another site)
                Payload = StaffPushPayloadJson.Build(payload.Title, payload.Body, payload.Tag, payload.Url),
                Status = NotificationStatus.Pending, ExpiresAtUtc = now.Add(Ttl), CreatedAt = now,
                IdempotencyKey = $"{type}:{eventId}:{subscription.UserId}:{subscription.Id}",
            });
    }
}

public sealed class StayStaffMaxQueue(AppDbContext db, IStaysClock clock)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    public Task QueueAsync(StayBooking booking, Guid eventId, NotificationType type, string text, CancellationToken ct) =>
        QueueAsync(StayNotificationSubject.Of(booking), eventId, type, text, ct);

    public async Task QueueAsync(StayNotificationSubject subject, Guid eventId, NotificationType type, string text, CancellationToken ct)
    {
        var userIds = await StayStaffRecipients.UserIdsAsync(db, subject.CompanyId, subject.GuestUserId, ct);
        if (userIds.Count == 0) return;
        var chatKeys = await db.StaffMaxLinks.AsNoTracking().Where(l => userIds.Contains(l.UserId) && l.Status == StaffMaxLinkStatus.Active)
            .Select(l => l.ChatKey).Distinct().ToListAsync(ct);
        var now = clock.UtcNow;
        foreach (var chatKey in chatKeys)
        {
            var key = $"{type}:{eventId}:{chatKey}";
            if (await db.StaffMaxMessages.AnyAsync(m => m.IdempotencyKey == key, ct)) continue;
            db.StaffMaxMessages.Add(new StaffMaxMessage
            {
                Id = Guid.NewGuid(), CompanyId = subject.CompanyId, StayBookingId = subject.StayBookingId, StayServiceOrderId = subject.StayServiceOrderId, ChatKey = chatKey, Type = type, Text = text,
                Status = NotificationStatus.Pending, ExpiresAtUtc = now.Add(Ttl), CreatedAt = now, IdempotencyKey = key,
            });
        }
    }
}

/// <summary>Web-push to the browsers a guest subscribed on the booking page. `marker` is the journal event id or a one-shot marker name; the key is unique per subscription.</summary>
public sealed class StayGuestPushQueue(AppDbContext db, IStaysClock clock)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(2);

    public Task QueueAsync(StayBooking booking, string marker, NotificationType type, PushPayload payload, CancellationToken ct) =>
        QueueAsync(StayNotificationSubject.Of(booking), marker, type, payload, ct);

    public async Task QueueAsync(StayNotificationSubject subject, string marker, NotificationType type, PushPayload payload, CancellationToken ct)
    {
        var subscriptions = subject.StayBookingId is { } bookingId
            ? await db.StayGuestPushSubscriptions.AsNoTracking().Where(s => s.StayBookingId == bookingId).ToListAsync(ct)
            : await db.StayGuestPushSubscriptions.AsNoTracking().Where(s => s.StayServiceOrderId == subject.StayServiceOrderId).ToListAsync(ct);
        if (subscriptions.Count == 0) return;
        var now = clock.UtcNow;
        var body = JsonSerializer.Serialize(new { title = payload.Title, body = payload.Body, tag = payload.Tag, url = payload.Url }, StaffPushPayloadJson.Options);
        foreach (var subscription in subscriptions)
        {
            var key = $"{type}:{marker}:{subscription.Id}";
            if (await db.StayGuestPushNotifications.AnyAsync(n => n.IdempotencyKey == key, ct)) continue;
            db.StayGuestPushNotifications.Add(new StayGuestPushNotification
            {
                Id = Guid.NewGuid(), StayBookingId = subject.StayBookingId, StayServiceOrderId = subject.StayServiceOrderId, CompanyId = subject.CompanyId, SubscriptionId = subscription.Id,
                Type = type, Payload = body,
                Status = NotificationStatus.Pending, ExpiresAtUtc = now.Add(Ttl), CreatedAt = now, IdempotencyKey = key,
            });
        }
    }
}
