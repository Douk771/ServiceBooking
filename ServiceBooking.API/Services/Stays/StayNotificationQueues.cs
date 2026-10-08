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

/// <summary>Push to the devices of the owner and managers — every device of the recipient (cycle 33); the key is one row per event and device.</summary>
public sealed class StayStaffPushQueue(AppDbContext db, StaffPushLinks links, IStaysClock clock)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    public async Task QueueAsync(StayBooking booking, Guid eventId, NotificationType type, PushPayload payload, CancellationToken ct)
    {
        var userIds = await StayStaffRecipients.UserIdsAsync(db, booking.CompanyId, booking.GuestUserId, ct);
        if (userIds.Count == 0) return;
        var subscriptions = await db.PushSubscriptions.AsNoTracking().Where(s => userIds.Contains(s.UserId)).ToListAsync(ct);
        var now = clock.UtcNow;
        foreach (var subscription in subscriptions)
            db.StaffPushNotifications.Add(new StaffPushNotification
            {
                Id = Guid.NewGuid(), UserId = subscription.UserId, CompanyId = booking.CompanyId, StayBookingId = booking.Id, SubscriptionId = subscription.Id, Type = type,
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

    public async Task QueueAsync(StayBooking booking, Guid eventId, NotificationType type, string text, CancellationToken ct)
    {
        var userIds = await StayStaffRecipients.UserIdsAsync(db, booking.CompanyId, booking.GuestUserId, ct);
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
                Id = Guid.NewGuid(), CompanyId = booking.CompanyId, StayBookingId = booking.Id, ChatKey = chatKey, Type = type, Text = text,
                Status = NotificationStatus.Pending, ExpiresAtUtc = now.Add(Ttl), CreatedAt = now, IdempotencyKey = key,
            });
        }
    }
}

/// <summary>Web-push to the browsers a guest subscribed on the booking page. `marker` is the journal event id or a one-shot marker name; the key is unique per subscription.</summary>
public sealed class StayGuestPushQueue(AppDbContext db, IStaysClock clock)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromHours(2);

    public async Task QueueAsync(StayBooking booking, string marker, NotificationType type, PushPayload payload, CancellationToken ct)
    {
        var subscriptions = await db.StayGuestPushSubscriptions.AsNoTracking().Where(s => s.StayBookingId == booking.Id).ToListAsync(ct);
        if (subscriptions.Count == 0) return;
        var now = clock.UtcNow;
        var body = JsonSerializer.Serialize(new { title = payload.Title, body = payload.Body, tag = payload.Tag, url = payload.Url }, StaffPushPayloadJson.Options);
        foreach (var subscription in subscriptions)
        {
            var key = $"{type}:{marker}:{subscription.Id}";
            if (await db.StayGuestPushNotifications.AnyAsync(n => n.IdempotencyKey == key, ct)) continue;
            db.StayGuestPushNotifications.Add(new StayGuestPushNotification
            {
                Id = Guid.NewGuid(), StayBookingId = booking.Id, CompanyId = booking.CompanyId, SubscriptionId = subscription.Id, Type = type, Payload = body,
                Status = NotificationStatus.Pending, ExpiresAtUtc = now.Add(Ttl), CreatedAt = now, IdempotencyKey = key,
            });
        }
    }
}
