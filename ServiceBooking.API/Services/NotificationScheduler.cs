using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services;

/// <summary>
/// Where a booking event becomes queued <see cref="OutboundNotification"/> rows
/// (ARCHITECTURE_CYCLE4.md §25.3). Scoped, three entry points that mirror the three call sites in
/// <c>BookingsController</c> (create, cancel, reschedule).
///
/// <b>Deliberately does not call <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>.</b> Every method here only
/// tracks changes on the SAME <see cref="AppDbContext"/> the caller's controller action already has —
/// added rows via <c>AddRange</c>, mutated <c>Status</c> on existing tracked rows — and the caller's own
/// transaction/<c>SaveChangesAsync</c> commits them together with the booking write itself (US-28 p.4:
/// queueing happens in the same transaction as the booking, with zero network calls in the HTTP request).
///
/// Placed in <c>Services/</c>, not <c>Services/Notifications/</c> (this cycle's file-ownership split
/// between two backend developers working the same branch in parallel) even though the architecture
/// doc's file map lists it under that folder.
/// </summary>
public sealed class NotificationScheduler(
    AppDbContext db,
    SubscriptionResolver subscriptionResolver,
    ConsentLedger consentLedger,
    IOptions<NotificationOptions> options)
{
    public async Task OnBookingCreatedAsync(Booking booking, IReadOnlyList<string>? serviceNames, CancellationToken ct)
    {
        var ctx = await BuildContextAsync(booking, serviceNames, ct);
        if (ctx is null) return;

        var nowUtc = DateTime.UtcNow;
        var visitStartUtc = ComputeVisitStartUtc(ctx, booking);

        await QueueAsync(ctx, booking, NotificationType.BookingConfirmed, dueAtUtc: nowUtc,
            visitStartUtc, generation: 0, ct);

        var leadMinutes = ctx.Settings?.ReminderLeadMinutes ?? new CompanyNotificationSettings().ReminderLeadMinutes;
        var reminderDueAtUtc = NotificationTiming.ComputeReminderDueAtUtc(
            ReminderRowId(booking.Id, generation: 0), visitStartUtc, leadMinutes, options.Value.ReminderJitterMinutes);
        await QueueAsync(ctx, booking, NotificationType.Reminder, reminderDueAtUtc, visitStartUtc, generation: 0, ct);
    }

    public async Task OnBookingCancelledAsync(Booking booking, CancellationToken ct)
    {
        var ctx = await BuildContextAsync(booking, null, ct);
        if (ctx is null) return;

        await CancelPendingAsync(booking.Id, ct);

        var nowUtc = DateTime.UtcNow;
        var visitStartUtc = ComputeVisitStartUtc(ctx, booking);
        await QueueAsync(ctx, booking, NotificationType.BookingCancelled, dueAtUtc: nowUtc, visitStartUtc,
            generation: await NextGenerationAsync(booking.Id, ct), ct);
    }

    public async Task OnBookingRescheduledAsync(Booking booking, CancellationToken ct)
    {
        var ctx = await BuildContextAsync(booking, null, ct);
        if (ctx is null) return;

        // Only the still-pending REMINDER is superseded — its due time and rendered {Дата}/{Время} are
        // tied to the visit that just moved. A pending BookingConfirmed row (rare: only if the channel
        // was never reachable) is left alone; it confirms the booking existing at all, not a specific
        // time, and the owner's own "перенесена" message (below) carries the new time to the client.
        var pendingReminders = await db.OutboundNotifications
            .Where(n => n.BookingId == booking.Id && n.Status == NotificationStatus.Pending &&
                        n.Type == NotificationType.Reminder)
            .ToListAsync(ct);
        foreach (var row in pendingReminders)
        {
            row.Status = NotificationStatus.Cancelled;
            row.Reason = NotificationReason.BookingOrAssignmentCancelled;
        }

        var generation = await NextGenerationAsync(booking.Id, ct);
        var nowUtc = DateTime.UtcNow;
        var visitStartUtc = ComputeVisitStartUtc(ctx, booking);

        await QueueAsync(ctx, booking, NotificationType.BookingRescheduled, dueAtUtc: nowUtc, visitStartUtc, generation, ct);

        var leadMinutes = ctx.Settings?.ReminderLeadMinutes ?? new CompanyNotificationSettings().ReminderLeadMinutes;
        var reminderDueAtUtc = NotificationTiming.ComputeReminderDueAtUtc(
            ReminderRowId(booking.Id, generation), visitStartUtc, leadMinutes, options.Value.ReminderJitterMinutes);
        await QueueAsync(ctx, booking, NotificationType.Reminder, reminderDueAtUtc, visitStartUtc, generation, ct);
    }

    // ── Shared plumbing ──────────────────────────────────────────────────────────────────────────

    private sealed record SchedulingContext(
        Company Company, EffectivePlan Plan, NotificationChannel? Channel, CompanyNotificationSettings? Settings,
        // US-67 (ARCHITECTURE_CYCLE6.md §47.2): the visit's service names, in visit order — one element
        // for a pre-cycle single-service booking, several for a multi-service one. The template renders
        // them joined by ", " (NotificationScheduler.RenderBodyAsync).
        IReadOnlyList<string> ServiceNames, AppUser Master, string? RecipientPhone, string RecipientName, bool RecipientOptedOut,
        string? RecipientUserId);

    /// <param name="booking">The booking the notification is about.</param>
    /// <param name="serviceNames">Known at Create time (the caller already resolved and validated the
    /// visit's services, before BookingServices rows exist in the DB yet) — pass it there. Null for
    /// Cancel/Reschedule, whose BookingServices rows already exist, so they're read from the DB;
    /// falls back to the single legacy Booking.ServiceId lookup if that table somehow has no rows yet
    /// (defensive only — the migration backfill guarantees at least one row for every booking).</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task<SchedulingContext?> BuildContextAsync(Booking booking, IReadOnlyList<string>? serviceNames, CancellationToken ct)
    {
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == booking.CompanyId, ct);
        var master = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == booking.MasterId, ct);
        if (company is null || master is null) return null;

        List<string> names;
        if (serviceNames is { Count: > 0 })
        {
            names = serviceNames.ToList();
        }
        else
        {
            names = await db.BookingServices.AsNoTracking()
                .Where(bs => bs.BookingId == booking.Id)
                .OrderBy(bs => bs.Position)
                .Select(bs => bs.NameSnapshot)
                .ToListAsync(ct);
            if (names.Count == 0)
            {
                var service = await db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == booking.ServiceId, ct);
                if (service is null) return null;
                names = [service.Name];
            }
        }

        var plan = await subscriptionResolver.GetEffectivePlanForOwnerAsync(company.OwnerUserId);

        var assignment = await db.ChannelCompanyAssignments.AsNoTracking()
            .Include(a => a.Channel)
            .FirstOrDefaultAsync(a => a.CompanyId == booking.CompanyId, ct);

        var settings = await db.CompanyNotificationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.CompanyId == booking.CompanyId, ct);

        string? recipientPhone;
        string recipientName;
        // T-24 (ARCHITECTURE_CYCLE5.md §52.3): null here means "no account" — the guest path can never
        // have granted PdnConsent/ProviderDelivery in the first place, since nobody asked them (they have
        // no profile to ask through). Kept distinct from the client branch below even though
        // booking.ClientId itself is already available, so this stays the one place recipient identity is
        // resolved for both the phone/name AND the consent lookup.
        string? recipientUserId;
        if (!string.IsNullOrEmpty(booking.GuestPhone))
        {
            recipientPhone = booking.GuestPhone;
            recipientName = booking.GuestName ?? "";
            recipientUserId = null;
        }
        else if (!string.IsNullOrEmpty(booking.ClientId))
        {
            var client = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == booking.ClientId, ct);
            recipientPhone = client?.PhoneNumber;
            recipientName = client is not null ? $"{client.FirstName} {client.LastName}" : "";
            recipientUserId = client?.Id;
        }
        else
        {
            recipientPhone = null;
            recipientName = "";
            recipientUserId = null;
        }

        var optedOut = recipientPhone is not null &&
            await db.NotificationOptOuts.AsNoTracking().AnyAsync(o => o.Phone == recipientPhone, ct);

        return new SchedulingContext(company, plan, assignment?.Channel, settings, names, master,
            recipientPhone, recipientName, optedOut, recipientUserId);
    }

    private static DateTime ComputeVisitStartUtc(SchedulingContext ctx, Booking booking) =>
        NotificationTiming.ComputeVisitStartUtc(booking.Date, booking.StartTime, ctx.Company.TimeZoneId);

    // DeploymentSafetyChecks.ValidateProviderDeliveryConsentMode already fails startup on an unrecognized
    // value (runs unconditionally, every environment) — the AccountsOnly fallback here is unreachable in
    // any process that actually started, not a silent behavior change for a bad config.
    private static ProviderDeliveryConsentMode ParseProviderDeliveryConsentMode(string raw) =>
        Enum.TryParse<ProviderDeliveryConsentMode>(raw, ignoreCase: true, out var mode)
            ? mode : ProviderDeliveryConsentMode.AccountsOnly;

    private async Task QueueAsync(
        SchedulingContext ctx, Booking booking, NotificationType type,
        DateTime dueAtUtc, DateTime visitStartUtc, int generation, CancellationToken ct)
    {
        // No usable phone at all (shouldn't happen — BookingsController requires one on every path — but
        // a background/manual data edge case must not throw here, it must simply produce nothing to send).
        if (string.IsNullOrEmpty(ctx.RecipientPhone)) return;

        var nowUtc = DateTime.UtcNow;
        var idempotencyKey = $"{type}:{booking.Id}:{ctx.RecipientPhone}:{generation}";

        // Defensive: NotificationScheduler is only ever called once per event by BookingsController, but
        // a retried request after a transient DB error could re-run the same call before the first
        // attempt's transaction committed anywhere else. The unique index on IdempotencyKey is the real
        // guard (§23.5); this check just avoids a doomed-to-fail insert attempt in the common case.
        var alreadyQueued = await db.OutboundNotifications.AnyAsync(n => n.IdempotencyKey == idempotencyKey, ct);
        if (alreadyQueued) return;

        if (NotificationTiming.IsExpired(visitStartUtc, nowUtc))
        {
            db.OutboundNotifications.Add(BuildRow(ctx, booking, type, "", dueAtUtc, visitStartUtc, generation,
                idempotencyKey, NotificationStatus.Expired, NotificationReason.VisitAlreadyStarted));
            return;
        }

        // T-24 (ARCHITECTURE_CYCLE5.md §52.3): only looked up for recipients WITH an account — a guest
        // (RecipientUserId null) passes null through unchanged, which NotificationGate reads as "no
        // account" and never blocks under the shipped AccountsOnly default. One extra indexed read here,
        // not on the dispatcher's hot path (§45.1: this method already reads the booking/company/settings).
        bool? recipientHasProviderDeliveryConsent = null;
        if (ctx.RecipientUserId is not null)
        {
            var consentState = await consentLedger.CurrentAsync(
                ConsentSubject.ForUser(ctx.RecipientUserId), LegalDocumentType.PdnConsent.ToString(),
                ConsentPurpose.ProviderDelivery, ct);
            recipientHasProviderDeliveryConsent = consentState is not null;
        }

        var gate = NotificationGate.Evaluate(
            ctx.Plan, type, ctx.Channel is not null, ctx.Channel, ctx.Settings, ctx.RecipientOptedOut, nowUtc, visitStartUtc,
            ParseProviderDeliveryConsentMode(options.Value.ProviderDeliveryConsent), recipientHasProviderDeliveryConsent);

        if (gate.Outcome == NotificationGateOutcome.Blocked)
        {
            db.OutboundNotifications.Add(BuildRow(ctx, booking, type, "", dueAtUtc, visitStartUtc, generation,
                idempotencyKey, NotificationStatus.Skipped, gate.Reason));
            return;
        }

        var body = await RenderBodyAsync(ctx, booking, type);
        db.OutboundNotifications.Add(BuildRow(ctx, booking, type, body, dueAtUtc, visitStartUtc, generation,
            idempotencyKey, NotificationStatus.Pending, null));
    }

    private OutboundNotification BuildRow(
        SchedulingContext ctx, Booking booking, NotificationType type, string body,
        DateTime dueAtUtc, DateTime visitStartUtc, int generation, string idempotencyKey,
        NotificationStatus status, NotificationReason? reason) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = booking.CompanyId,
        ChannelId = ctx.Channel?.Id,
        BookingId = booking.Id,
        Type = type,
        RecipientPhone = ctx.RecipientPhone!,
        RecipientName = ctx.RecipientName,
        RecipientUserId = booking.ClientId,
        Body = body,
        DueAtUtc = dueAtUtc,
        VisitStartUtc = visitStartUtc,
        Status = status,
        Reason = reason,
        Generation = generation,
        IdempotencyKey = idempotencyKey,
    };

    private async Task<string> RenderBodyAsync(SchedulingContext ctx, Booking booking, NotificationType type)
    {
        var templateBody = await GetTemplateBodyAsync(ctx.Company.Id, type);

        var templateContext = new TemplateContext(
            ClientName: ctx.RecipientName,
            // US-67 (SPEC_CYCLE6_BOOKING_FIXES.md §2, US-67): comma-joined, no trailing/leading blanks, never "undefined" —
            // ServiceNames is never empty (see BuildContextAsync).
            ServiceName: string.Join(", ", ctx.ServiceNames),
            MasterName: $"{ctx.Master.FirstName} {ctx.Master.LastName}",
            Date: booking.Date.ToString("dd.MM.yyyy"),
            Time: booking.StartTime.ToString("HH:mm"),
            CompanyName: ctx.Company.Name,
            Address: ctx.Company.Address,
            CompanyPhone: ctx.Company.Phone,
            CancellationReason: type == NotificationType.BookingCancelled ? booking.CancellationReason : null,
            NewDate: type == NotificationType.BookingRescheduled ? booking.Date.ToString("dd.MM.yyyy") : null,
            NewTime: type == NotificationType.BookingRescheduled ? booking.StartTime.ToString("HH:mm") : null);

        var rendered = NotificationTemplateRenderer.Render(templateBody, templateContext);

        var unsubscribeKey = options.Value.UnsubscribeKey;
        if (string.IsNullOrEmpty(unsubscribeKey) || string.IsNullOrEmpty(ctx.RecipientPhone))
            return rendered;

        var token = UnsubscribeTokens.Build(ctx.RecipientPhone, Encoding.UTF8.GetBytes(unsubscribeKey));
        var unsubscribeLine = $"Отказаться от уведомлений: https://{NotificationTemplateValidator.OwnDomain}/u/{token}";
        return NotificationTemplateRenderer.AppendUnsubscribeLine(rendered, unsubscribeLine);
    }

    // NotificationTemplates is a tiny, per-company-per-type row set — one extra scalar query per queued
    // notification is an acceptable cost here (booking events, not the dispatcher's hot path, §26).
    private async Task<string> GetTemplateBodyAsync(Guid companyId, NotificationType type)
    {
        var template = await db.NotificationTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Type == type);
        return string.IsNullOrEmpty(template?.Body) ? DefaultTemplates.For(type) : template.Body;
    }

    private async Task CancelPendingAsync(Guid bookingId, CancellationToken ct)
    {
        var pending = await db.OutboundNotifications
            .Where(n => n.BookingId == bookingId && n.Status == NotificationStatus.Pending)
            .ToListAsync(ct);
        foreach (var row in pending)
        {
            row.Status = NotificationStatus.Cancelled;
            row.Reason = NotificationReason.BookingOrAssignmentCancelled;
        }
    }

    private async Task<int> NextGenerationAsync(Guid bookingId, CancellationToken ct)
    {
        var maxGeneration = await db.OutboundNotifications
            .Where(n => n.BookingId == bookingId)
            .Select(n => (int?)n.Generation)
            .MaxAsync(ct);
        return (maxGeneration ?? -1) + 1;
    }

    /// <summary>Deterministic per-row id used only to seed <see cref="NotificationTiming.JitterMinutes"/>
    /// before the real row id exists — reminder due time is computed BEFORE <see cref="Guid.NewGuid"/> is
    /// called for the row itself (<see cref="BuildRow"/> generates the actual id independently). Basing
    /// the jitter seed on (bookingId, generation) instead keeps it just as deterministic — recomputing
    /// for the same booking/generation always gives the same jitter — without requiring the two to share
    /// a literal Guid.</summary>
    private static Guid ReminderRowId(Guid bookingId, int generation)
    {
        var bytes = bookingId.ToByteArray();
        bytes[0] ^= (byte)generation;
        return new Guid(bytes);
    }
}
