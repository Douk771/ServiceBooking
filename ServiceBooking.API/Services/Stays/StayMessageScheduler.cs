using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.12.3 — queues a messenger message to the guest of a booking through the SAME gate, routing and funding as every other message:
/// opt-out, the paid numbers of the account, the channel, the provider-delivery consent. Never sends by itself; `VisitStartUtc` of the row is the moment it is
/// outdated (queued + <c>Stays:GuestMessageTtlMinutes</c>) — the dispatcher expires it as <c>StayMessageOutdated</c>.
/// </summary>
public sealed class StayMessageScheduler(
    AppDbContext db, AccountMessagingReader messagingReader, ConsentLedger consentLedger, PublicSiteLinks links,
    IOptions<NotificationOptions> notificationOptions, IOptions<StaysOptions> options, IStaysClock clock)
{
    public async Task QueueAsync(StayBooking booking, Company company, string marker, NotificationType type, StayTextFacts facts, CancellationToken ct)
    {
        var phone = booking.GuestPhone;
        if (string.IsNullOrEmpty(phone) || booking.PersonalDataErased) return; // depersonalised: nobody to write to

        var now = clock.UtcNow;
        var outdatedAtUtc = now.AddMinutes(options.Value.GuestMessageTtlMinutes);
        var channels = await db.ChannelCompanyAssignments.AsNoTracking().Include(a => a.Channel)
            .Where(a => a.CompanyId == company.Id).OrderBy(a => a.Transport).Select(a => a.Channel).ToListAsync(ct);
        var settings = await db.CompanyNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == company.Id, ct);
        var messaging = await messagingReader.ForCompanyAsync(company.Id, ct: ct);
        var optedOut = await db.NotificationOptOuts.AsNoTracking().AnyAsync(o => o.Phone == phone, ct);  // SUBJECT-PHONE-GATE: not-account-scoped — dispatch-time check against the message's own recipient phone (the booking's), not an account reading another subject's data

        bool? hasConsent = null;
        if (booking.GuestUserId is not null)
            hasConsent = await consentLedger.CurrentAsync(ConsentSubject.ForUser(booking.GuestUserId), LegalDocumentType.PdnConsent.ToString(), ConsentPurpose.ProviderDelivery, ct) is not null;

        var priorityTransport = settings?.PriorityTransport ?? new CompanyNotificationSettings().PriorityTransport;
        var representativeChannelId = channels.Count > 0 ? channels[0].Id : (Guid?)null;
        var gate = NotificationGate.Evaluate(
            messaging.AnyPaid, type, companyHasAssignment: channels.Count > 0, channel: channels.Count > 0 ? channels[0] : null, settings, optedOut,
            now, outdatedAtUtc, channelIsFunded: true,
            Enum.TryParse<ProviderDeliveryConsentMode>(notificationOptions.Value.ProviderDeliveryConsent, ignoreCase: true, out var consentMode)
                ? consentMode : ProviderDeliveryConsentMode.AccountsOnly,
            hasConsent);
        if (gate.Outcome == NotificationGateOutcome.Blocked)
        {
            await AddIfNew(booking, company, marker, type, string.Empty, priorityTransport, representativeChannelId, outdatedAtUtc, NotificationStatus.Skipped, gate.Reason, ct);
            return;
        }

        var funding = channels.ToDictionary(c => c.Id, c => messaging.FundingOf(c) == ChannelFundingState.Funded);
        var candidates = channels.Select(c => new NotificationRouting.Candidate(c.Id, c.Transport, funding.GetValueOrDefault(c.Id))).ToList();
        var routing = NotificationRouting.SelectTargets(settings?.DeliveryMode ?? new CompanyNotificationSettings().DeliveryMode, priorityTransport, candidates);
        if (routing.Targets.Count == 0)
        {
            await AddIfNew(booking, company, marker, type, string.Empty, priorityTransport, routing.UnavailableChannelId ?? representativeChannelId,
                outdatedAtUtc, NotificationStatus.Skipped, routing.SkipReason ?? NotificationReason.NotOnPaidPlan, ct);
            return;
        }

        var body = StayNotificationTexts.Messenger(type, facts, links.StayBookingPageUrl(booking.PublicToken), UnsubscribeUrl(phone));
        foreach (var target in routing.Targets)
            await AddIfNew(booking, company, marker, type, body, target.Transport, target.ChannelId, outdatedAtUtc, NotificationStatus.Pending, null, ct);
    }

    private string? UnsubscribeUrl(string phone)
    {
        var key = notificationOptions.Value.UnsubscribeKey;
        return string.IsNullOrEmpty(key) ? null : links.UnsubscribeUrl(UnsubscribeTokens.Build(phone, Encoding.UTF8.GetBytes(key)));
    }

    private async Task AddIfNew(
        StayBooking booking, Company company, string marker, NotificationType type, string body, NotificationTransport transport, Guid? channelId,
        DateTime outdatedAtUtc, NotificationStatus status, NotificationReason? reason, CancellationToken ct)
    {
        // One event/marker — one message per channel: the key is the whole guarantee (unique index), the existence check only avoids an exception.
        var key = $"{type}:stay:{marker}:{transport}";
        if (await db.OutboundNotifications.AnyAsync(n => n.IdempotencyKey == key, ct)) return;
        db.OutboundNotifications.Add(new OutboundNotification
        {
            Id = Guid.NewGuid(), CompanyId = company.Id, ChannelId = channelId, Transport = transport, StayBookingId = booking.Id, Type = type,
            RecipientPhone = booking.GuestPhone!, RecipientName = booking.GuestName, RecipientUserId = booking.GuestUserId, Body = body,
            DueAtUtc = clock.UtcNow, VisitStartUtc = outdatedAtUtc, Status = status, Reason = reason, Generation = 0, IdempotencyKey = key,
        });
    }
}
