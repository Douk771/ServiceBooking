using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text;
using ServiceBooking.API.Services.Billing;
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
        Company Company, EffectivePlan Plan,
        // ARCHITECTURE_CYCLE9.md §104.3/§104.5: a company may hold one assignment PER TRANSPORT now —
        // every live channel is carried, not an arbitrary single one. Empty means "no assignment at all",
        // same meaning the old nullable Channel's null carried.
        IReadOnlyList<NotificationChannel> Channels, CompanyNotificationSettings? Settings,
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

        var plan = await subscriptionResolver.GetEffectivePlanAsync(company.Id);

        // ARCHITECTURE_CYCLE9.md §104.3: every live assignment, one per transport at most — not an
        // arbitrary FirstOrDefault. N9: ordered by Transport so ctx.Channels[0] (the "representative"
        // channel used for the journal's ChannelId on Expired/Skipped rows, see QueueAsync) is stable
        // across passes instead of depending on whatever order the database happens to return.
        var channels = await db.ChannelCompanyAssignments.AsNoTracking()
            .Include(a => a.Channel)
            .Where(a => a.CompanyId == booking.CompanyId)
            .OrderBy(a => a.Transport)
            .Select(a => a.Channel)
            .ToListAsync(ct);

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

        return new SchedulingContext(company, plan, channels, settings, names, master,
            recipientPhone, recipientName, optedOut, recipientUserId);
    }

    private async Task<bool> IsChannelFundedAsync(NotificationChannel? channel, EffectivePlan plan, CancellationToken ct)
    {
        if (channel is null) return false;
        if (channel.BillingAccountId is not { } accountId)
        {
            // Pre-cycle-5-backfill edge case (no BillingAccountId yet on this channel row) — fail
            // closed rather than guess at funding for a number that isn't tied to an account yet.
            return false;
        }

        var siblings = await db.NotificationChannels.AsNoTracking()
            .Where(c => c.BillingAccountId == accountId)
            .ToListAsync(ct);
        var ranking = ChannelFunding.Rank(siblings, plan.PaidNotificationNumbers);
        return ranking.TryGetValue(channel.Id, out var state) && state == ChannelFundingState.Funded;
    }

    /// <summary>N10: <see cref="IsChannelFundedAsync"/> called once per channel of <paramref name="channels"/>
    /// re-fetches every sibling channel on that channel's billing account EACH time — for a company with
    /// two assigned transports (WhatsApp + MAX) that is 2 full-account queries to rank funding for one
    /// event, every time an event is queued. Ranking only depends on the DISTINCT billing accounts behind
    /// <paramref name="channels"/> (almost always exactly one), so this groups by account and ranks each
    /// account's siblings exactly once.</summary>
    private async Task<Dictionary<Guid, bool>> BuildFundingLookupAsync(
        IReadOnlyList<NotificationChannel> channels, EffectivePlan plan, CancellationToken ct)
    {
        var result = new Dictionary<Guid, bool>();
        var accountIds = channels
            .Where(c => c.BillingAccountId is not null)
            .Select(c => c.BillingAccountId!.Value)
            .Distinct()
            .ToList();
        if (accountIds.Count == 0) return result;

        var siblings = await db.NotificationChannels.AsNoTracking()
            .Where(c => c.BillingAccountId != null && accountIds.Contains(c.BillingAccountId!.Value))
            .ToListAsync(ct);

        foreach (var accountId in accountIds)
        {
            var accountSiblings = siblings.Where(c => c.BillingAccountId == accountId).ToList();
            var ranking = ChannelFunding.Rank(accountSiblings, plan.PaidNotificationNumbers);
            foreach (var (channelId, state) in ranking)
                result[channelId] = state == ChannelFundingState.Funded;
        }

        return result;
    }

    private static DateTime ComputeVisitStartUtc(SchedulingContext ctx, Booking booking) =>
        NotificationTiming.ComputeVisitStartUtc(booking.Date, booking.StartTime, ctx.Company.TimeZoneId);

    // DeploymentSafetyChecks.ValidateProviderDeliveryConsentMode already fails startup on an unrecognized
    // value (runs unconditionally, every environment) — the AccountsOnly fallback here is unreachable in
    // any process that actually started, not a silent behavior change for a bad config.
    private static ProviderDeliveryConsentMode ParseProviderDeliveryConsentMode(string raw) =>
        Enum.TryParse<ProviderDeliveryConsentMode>(raw, ignoreCase: true, out var mode)
            ? mode : ProviderDeliveryConsentMode.AccountsOnly;

    /// <summary>ARCHITECTURE_CYCLE9.md §104.5's "до → после" table — the transport is now part of the
    /// key. Applied to EVERY row this method writes, not only Pending ones (Expired/Skipped rows use the
    /// company's priorityTransport as the representative value — see <see cref="QueueAsync"/>): keeping
    /// one uniform key shape means the idempotency guarantee (§23.5's unique index) never has to special-
    /// case which status a row ended up in.</summary>
    private static string BuildIdempotencyKey(NotificationType type, Guid bookingId, string phone, int generation, NotificationTransport transport) =>
        $"{type}:{bookingId}:{phone}:{generation}:{transport}";

    private async Task QueueAsync(
        SchedulingContext ctx, Booking booking, NotificationType type,
        DateTime dueAtUtc, DateTime visitStartUtc, int generation, CancellationToken ct)
    {
        // No usable phone at all (shouldn't happen — BookingsController requires one on every path — but
        // a background/manual data edge case must not throw here, it must simply produce nothing to send).
        if (string.IsNullOrEmpty(ctx.RecipientPhone)) return;

        var nowUtc = DateTime.UtcNow;
        var priorityTransport = ctx.Settings?.PriorityTransport ?? new CompanyNotificationSettings().PriorityTransport;
        var representativeChannelId = ctx.Channels.Count > 0 ? ctx.Channels[0].Id : (Guid?)null;

        if (NotificationTiming.IsExpired(visitStartUtc, nowUtc))
        {
            await AddSingleRowIfNotAlreadyQueuedAsync(ctx, booking, type, "", dueAtUtc, visitStartUtc, generation,
                priorityTransport, representativeChannelId, NotificationStatus.Expired, NotificationReason.VisitAlreadyStarted, ct);
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

        // ARCHITECTURE_CYCLE9.md §104.5/§104.6: channel-INDEPENDENT gate checks first — opt-out, provider
        // consent, plan.PaidNotificationNumbers == 0, "no assignment at all", type disabled, lead time.
        // channelIsFunded is forced TRUE here deliberately: NotificationGate.Evaluate's funding branch
        // reuses the same NotOnPaidPlan reason as the account-wide "zero paid numbers" branch (its own
        // doc comment says so), so forcing true here guarantees that if NotOnPaidPlan still comes back,
        // it can ONLY be the account-wide reason — per-CHANNEL funding is routing's own job below, and
        // (per §104.5) an unfunded/unusable PRIORITY channel is reported as PriorityChannelUnavailable,
        // not NotOnPaidPlan — a deliberate, routing-aware reason, not a WhatsApp-era reason repurposed.
        var globalGate = NotificationGate.Evaluate(
            ctx.Plan, type, companyHasAssignment: ctx.Channels.Count > 0,
            channel: ctx.Channels.Count > 0 ? ctx.Channels[0] : null,
            ctx.Settings, ctx.RecipientOptedOut, nowUtc, visitStartUtc,
            channelIsFunded: true,
            ParseProviderDeliveryConsentMode(options.Value.ProviderDeliveryConsent), recipientHasProviderDeliveryConsent);

        if (globalGate.Outcome == NotificationGateOutcome.Blocked)
        {
            await AddSingleRowIfNotAlreadyQueuedAsync(ctx, booking, type, "", dueAtUtc, visitStartUtc, generation,
                priorityTransport, representativeChannelId, NotificationStatus.Skipped, globalGate.Reason, ct);
            return;
        }

        // Past the global gate — route to one or more specific channels (§104.5/US-125). "Usable" here is
        // deliberately FUNDING ONLY, not "funded AND Connected": ChannelState is NOT checked here, on
        // purpose, and this is a conscious, documented departure from §104.5's own listed "негоден"
        // bullets (which name "не Connected" alongside "не оплачен"). Reason: NotificationGate's
        // pre-cycle-9 contract (its own doc comment) is explicit that queueing must NEVER regress a
        // channel that is mid-reconnect — "unconnected doesn't discard queued rows, it just doesn't
        // dispatch them yet" is the DISPATCHER's job, not the queue's — and this is exactly what
        // NotificationQueueingTests' SeedConnectedAssignedChannelAsync helper is built and commented
        // around (it seeds State = Disconnected on purpose, specifically because "NotificationGate.Evaluate
        // deliberately does not look at ChannelState at all"). Checking Connected here would silently
        // turn a transient reconnect window into a lost message (Skipped/PriorityChannelUnavailable)
        // instead of "stays Pending, sends once reconnected" — a real behavior change for every existing
        // single-channel company that §104.5's own П12 promises NOT to make. The UI-facing
        // connectedTransports/priorityChannelHealthy fields (CompanyNotificationsController) still check
        // Connected — that is a live STATUS signal for the owner, deliberately more conservative than
        // what actually gates a send.
        // N10: one batched funding lookup for the whole event instead of one full
        // "load every sibling channel on this billing account" query PER channel — ranking only depends
        // on which billing account(s) ctx.Channels sit on, computed once here rather than once per
        // candidate (this scales with distinct accounts, almost always 1, not with channel count).
        var fundedByChannelId = await BuildFundingLookupAsync(ctx.Channels, ctx.Plan, ct);
        var candidates = ctx.Channels
            .Select(channel => new NotificationRouting.Candidate(
                channel.Id, channel.Transport, fundedByChannelId.GetValueOrDefault(channel.Id)))
            .ToList();

        var mode = ctx.Settings?.DeliveryMode ?? new CompanyNotificationSettings().DeliveryMode;
        var routing = NotificationRouting.SelectTargets(mode, priorityTransport, candidates);

        if (routing.Targets.Count == 0)
        {
            // AllChannels with zero usable candidates has no routing-specific reason (§104.5 doesn't name
            // one for it) — NotOnPaidPlan is this method's own choice, reusing the existing "nothing
            // usable" vocabulary rather than inventing a new NotificationReason member for a case the
            // architecture doesn't call out by name.
            var reason = routing.SkipReason ?? NotificationReason.NotOnPaidPlan;
            var channelId = routing.UnavailableChannelId ?? representativeChannelId;
            await AddSingleRowIfNotAlreadyQueuedAsync(ctx, booking, type, "", dueAtUtc, visitStartUtc, generation,
                priorityTransport, channelId, NotificationStatus.Skipped, reason, ct);
            return;
        }

        // §104.5: "Строк на событие: 1 в режиме PriorityChannel, по 1 на транспорт в режиме AllChannels."
        // Idempotency is checked PER TARGET, independently — not once for the whole event.
        var body = await RenderBodyAsync(ctx, booking, type);
        foreach (var target in routing.Targets)
        {
            var idempotencyKey = BuildIdempotencyKey(type, booking.Id, ctx.RecipientPhone, generation, target.Transport);
            var alreadyQueued = await db.OutboundNotifications.AnyAsync(n => n.IdempotencyKey == idempotencyKey, ct);
            if (alreadyQueued) continue;

            db.OutboundNotifications.Add(BuildRow(ctx, booking, type, body, dueAtUtc, visitStartUtc, generation,
                idempotencyKey, target.Transport, target.ChannelId, NotificationStatus.Pending, null));
        }
    }

    /// <summary>Shared by the three "one row for this whole event, no specific routing target"
    /// cases above (Expired, globally-blocked, routing-empty) — each checks the SAME per-transport
    /// idempotency guard a per-target Pending row uses, using <paramref name="transport"/> (the company's
    /// own priorityTransport, since there is no target to name one) as the representative value.</summary>
    private async Task AddSingleRowIfNotAlreadyQueuedAsync(
        SchedulingContext ctx, Booking booking, NotificationType type, string body,
        DateTime dueAtUtc, DateTime visitStartUtc, int generation, NotificationTransport transport, Guid? channelId,
        NotificationStatus status, NotificationReason? reason, CancellationToken ct)
    {
        var idempotencyKey = BuildIdempotencyKey(type, booking.Id, ctx.RecipientPhone!, generation, transport);
        var alreadyQueued = await db.OutboundNotifications.AnyAsync(n => n.IdempotencyKey == idempotencyKey, ct);
        if (alreadyQueued) return;

        db.OutboundNotifications.Add(BuildRow(ctx, booking, type, body, dueAtUtc, visitStartUtc, generation,
            idempotencyKey, transport, channelId, status, reason));
    }

    private OutboundNotification BuildRow(
        SchedulingContext ctx, Booking booking, NotificationType type, string body,
        DateTime dueAtUtc, DateTime visitStartUtc, int generation, string idempotencyKey,
        NotificationTransport transport, Guid? channelId,
        NotificationStatus status, NotificationReason? reason) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = booking.CompanyId,
        ChannelId = channelId,
        Transport = transport,
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
