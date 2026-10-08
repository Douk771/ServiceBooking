using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>Everything one rendering of the owner's numbers needs, read once for a set of billing accounts.</summary>
public sealed class NumbersContext
{
    public required IReadOnlyDictionary<Guid, AccountMessagingState> States { get; init; }
    public required IReadOnlyDictionary<Guid, IReadOnlyList<Company>> CompaniesByAccount { get; init; }
    public required int IdleDays { get; init; }
    public required string? OwnerPhoneMasked { get; init; }
    public required string? RiskVersion { get; init; }
    public required string? OfferVersion { get; init; }
    public required bool OfferConsentCurrent { get; init; }
    public required IReadOnlyDictionary<Guid, string?> TestFailureDetails { get; init; }
}

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.6, §40.7 (API_CONTRACT_CYCLE40.md §40.22, §40.23) — builds <see cref="ChannelDto"/> and
/// <see cref="NumbersOverviewDto"/>: the ONE place where the facts (<see cref="AccountMessagingReader"/>, consent ledger, legal documents)
/// become <see cref="ChannelDisplayFacts"/> and then the pure <see cref="ChannelPresentation"/> rules. Text is assembled here and in the rules,
/// never on the frontend.
/// </summary>
public sealed class NumbersOverviewBuilder(
    AppDbContext db, AccountMessagingReader reader, PlatformSettings platformSettings, LegalDocumentProvider legal, ConsentLedger ledger)
{
    public async Task<NumbersContext> LoadAsync(IEnumerable<Guid> accountIds, string ownerUserId, CancellationToken ct = default)
    {
        var ids = accountIds.Distinct().ToList();
        var states = await reader.LoadAsync(ids, ct: ct);

        var companies = (await db.Companies.AsNoTracking()
                .Where(c => c.BillingAccountId != null && ids.Contains(c.BillingAccountId.Value))
                .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
                .ToListAsync(ct))
            .GroupBy(c => c.BillingAccountId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Company>)g.ToList());

        var snapshot = legal.Current;
        var offerVersion = snapshot?.Get(LegalDocumentType.TermsOwner)?.Version;
        var consent = offerVersion is null
            ? null
            : await ledger.CurrentAsync(ConsentSubject.ForUser(ownerUserId), LegalDocumentType.TermsOwner.ToString(), ConsentPurpose.ChannelOffer, ct);

        var ownerPhone = await db.Users.AsNoTracking().Where(u => u.Id == ownerUserId).Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct);
        var ownerPhoneMasked = PhoneNormalizer.TryNormalize(ownerPhone, out var canonical) ? PhoneDisplayMask.Mask(canonical) : null;

        var failedIds = states.Values.SelectMany(s => s.Channels).Where(c => c.LastTestResult == ChannelTestResult.Failed).Select(c => c.Id).ToList();
        var details = new Dictionary<Guid, string?>();
        if (failedIds.Count > 0)
        {
            var events = await db.ChannelStateEvents.AsNoTracking()
                .Where(e => failedIds.Contains(e.ChannelId) && e.Reason == ChannelStateReason.TestMessageFailed)
                .OrderByDescending(e => e.OccurredAtUtc)
                .Select(e => new { e.ChannelId, e.Detail })
                .ToListAsync(ct);
            foreach (var e in events) details.TryAdd(e.ChannelId, e.Detail);
        }

        return new NumbersContext
        {
            States = states,
            CompaniesByAccount = companies,
            IdleDays = await platformSettings.GetChannelIdleDaysAsync(ct),
            OwnerPhoneMasked = ownerPhoneMasked,
            RiskVersion = snapshot?.Get(LegalDocumentType.ChannelRiskNotice)?.Version,
            OfferVersion = offerVersion,
            OfferConsentCurrent = consent is not null && offerVersion is not null && consent.DocumentVersion == offerVersion,
            TestFailureDetails = details,
        };
    }

    /// <summary>The terms check alone (connect): one legal-snapshot read and one consent query.</summary>
    public async Task<bool> IsTermsAcceptedAsync(NotificationChannel channel, string ownerUserId, CancellationToken ct = default)
    {
        var snapshot = legal.Current;
        var offerVersion = snapshot?.Get(LegalDocumentType.TermsOwner)?.Version;
        var riskVersion = snapshot?.Get(LegalDocumentType.ChannelRiskNotice)?.Version;
        if (offerVersion is null || riskVersion is null) return false;
        if (channel.RiskAcceptedVersion != riskVersion || channel.LegalEntityForm is null || string.IsNullOrEmpty(channel.Inn)) return false;

        var consent = await ledger.CurrentAsync(
            ConsentSubject.ForUser(ownerUserId), LegalDocumentType.TermsOwner.ToString(), ConsentPurpose.ChannelOffer, ct);
        return consent is not null && consent.DocumentVersion == offerVersion;
    }

    /// <summary>§40.6.1: risk of the current version, status and INN declared, and a live offer consent of the current version.</summary>
    public static bool TermsAccepted(NotificationChannel channel, NumbersContext ctx) =>
        ctx.RiskVersion is not null && channel.RiskAcceptedVersion == ctx.RiskVersion &&
        channel.LegalEntityForm is not null && !string.IsNullOrEmpty(channel.Inn) && ctx.OfferConsentCurrent;

    private static AccountMessagingState StateOf(NotificationChannel channel, NumbersContext ctx) =>
        ctx.States.GetValueOrDefault(channel.BillingAccountId ?? Guid.Empty) ?? throw new InvalidOperationException("Account state was not loaded.");

    public static ChannelDisplayFacts FactsOf(NotificationChannel channel, NumbersContext ctx)
    {
        var state = StateOf(channel, ctx);
        var transport = state.For(channel.Transport);
        var payment = transport.Payment;
        var requestNewer = channel.RequestedAtUtc is { } requested && (payment.LastPaymentAt is not { } last || requested > last);
        return new ChannelDisplayFacts(
            channel.Transport, channel.State, channel.LastStateReason,
            channel.PhoneNumber is null ? null : PhoneDisplayMask.Mask(channel.PhoneNumber),
            payment.Paid, payment.PaidUntil, payment.IsTrial, requestNewer,
            IsDuplicate: state.FundingOf(channel) == ChannelFundingState.Unfunded,
            channel.IsSuspendedByAdmin, state.PlatformEnabled, transport.Option.Open, transport.Option.Sellable,
            TermsAccepted(channel, ctx), channel.IdleSinceUtc,
            channel.IdleSinceUtc?.AddDays(ctx.IdleDays), ctx.IdleDays);
    }

    public ChannelDto BuildChannel(NotificationChannel channel, NumbersContext ctx)
    {
        var state = StateOf(channel, ctx);
        var transport = state.For(channel.Transport);
        var facts = FactsOf(channel, ctx);
        var display = ChannelPresentation.Display(facts);
        var fundingState = state.FundingOf(channel);
        var paymentState = fundingState switch
        {
            ChannelFundingState.Funded => ChannelPaymentStatus.Paid,
            _ when channel.IsSuspendedByAdmin => ChannelPaymentStatus.Suspended,
            _ => ChannelPaymentStatus.NotPaid,
        };
        var workingMasked = transport.Primary?.PhoneNumber is { } phone ? PhoneDisplayMask.Mask(phone) : null;
        var fundingText = MessengerTexts.FundingText(
            fundingState, channel.Transport, new TransportPaymentView(transport.Payment.PaidUntil, transport.Payment.IsTrial), workingMasked);
        var companies = ctx.CompaniesByAccount.GetValueOrDefault(channel.BillingAccountId ?? Guid.Empty) ?? [];

        ChannelTestDto? lastTest = channel.LastTestResult is { } result && channel.LastTestResultAtUtc is { } at
            ? new ChannelTestDto(result, at, ChannelTestTexts.For(result, ctx.OwnerPhoneMasked, ctx.TestFailureDetails.GetValueOrDefault(channel.Id)))
            : null;

        return new ChannelDto(
            channel.Id, channel.Transport, channel.State,
            ChannelPresentation.StateText(channel.Transport, channel.State, facts.PhoneMasked, ctx.IdleDays, channel.LastStateReason),
            facts.PhoneMasked, paymentState, transport.Payment.PaidUntil, channel.RequestedAtUtc, channel.ConnectedAtUtc,
            channel.RiskAcceptedAtUtc, channel.IdleSinceUtc, facts.IdleDeadlineUtc, channel.ReplacedByChannelId,
            companies.Select(c => new ChannelCompanyDto(c.Id, c.Name, c.IsActive)).ToList(),
            CanConnect: ChannelPresentation.WizardStep(facts, hasChannel: true) == ChannelWizardStep.Qr,
            CanReplace: ChannelPresentation.CanReplace(channel.State),
            FundingState: fundingState, FundingText: fundingText,
            Inn: channel.Inn, LegalEntityForm: channel.LegalEntityForm,
            DisplayStatus: display.Status, DisplayText: display.Text, Action: display.Action,
            LastTest: lastTest, IsTrial: transport.Payment.IsTrial,
            PaymentPending: !facts.Paid && facts.RequestNewerThanPayment);
    }

    /// <summary>The single channel DTO of a channel loaded by id (own account only).</summary>
    public async Task<ChannelDto> BuildChannelAsync(NotificationChannel channel, string ownerUserId, CancellationToken ct = default)
    {
        var ctx = await LoadAsync([channel.BillingAccountId ?? Guid.Empty], ownerUserId, ct);
        return BuildChannel(channel, ctx);
    }

    /// <summary>§40.23. <see langword="null"/> when the legal documents are not loaded (the caller answers 503).</summary>
    public async Task<NumbersOverviewDto?> BuildAsync(string ownerUserId, CancellationToken ct = default)
    {
        var snapshot = legal.Current;
        var offerDoc = snapshot?.Get(LegalDocumentType.TermsOwner);
        var riskDoc = snapshot?.Get(LegalDocumentType.ChannelRiskNotice);
        if (offerDoc is null || riskDoc is null) return null;

        var accountId = await BillingAccountProvisioner.FindAccountIdAsync(db, ownerUserId) ?? Guid.Empty;
        var ctx = await LoadAsync([accountId], ownerUserId, ct);
        var account = ctx.States[accountId];
        var companies = ctx.CompaniesByAccount.GetValueOrDefault(accountId) ?? [];

        // The latest declared status and INN of the account — the wizard pre-fills its form with them.
        var prefillSource = account.Channels.Where(c => c.Inn != null).OrderByDescending(c => c.CreatedAt).FirstOrDefault();
        var prefill = prefillSource is null ? null : new PrefillDto(prefillSource.LegalEntityForm, prefillSource.Inn);

        var transports = new List<TransportNumbersDto>();
        foreach (var t in account.Transports)
        {
            var live = t.Primary is not null || t.Duplicates.Count > 0;
            if (!t.Option.Open && !live && !t.Paid) continue;
            transports.Add(BuildTransport(t, account, ctx, prefill));
        }

        return new NumbersOverviewDto(
            account.PlatformEnabled, account.PlatformEnabled ? null : MessengerTexts.PlatformDisabled,
            MessengerTexts.NumbersNote,
            companies.Select(c => new OverviewCompanyDto(c.Id, c.Name, c.Kind.ToString(), c.IsActive)).ToList(),
            $"Работает для всех ваших компаний: {companies.Count}",
            new OfferRefDto(offerDoc.Version, MessengerTexts.ConditionsUrl),
            new RiskRefDto(riskDoc.Version, riskDoc.ContentHtml, "/channel-risk"),
            MessengerTexts.StatusNotice,
            transports);
    }

    private TransportNumbersDto BuildTransport(TransportState t, AccountMessagingState account, NumbersContext ctx, PrefillDto? prefill)
    {
        var primary = t.Primary;
        ChannelDisplayFacts facts;
        if (primary is not null)
        {
            facts = FactsOf(primary, ctx);
        }
        else
        {
            // No live number: there is no real state, only the payment/availability part of the facts is meaningful.
            facts = new ChannelDisplayFacts(
                t.Transport, ChannelState.NotConnected, null, null, t.Payment.Paid, t.Payment.PaidUntil, t.Payment.IsTrial,
                RequestNewerThanPayment: false, IsDuplicate: false, Suspended: false, account.PlatformEnabled, t.Option.Open, t.Option.Sellable,
                TermsAccepted: false, null, null, ctx.IdleDays);
        }

        var display = primary is null ? ChannelDisplay.Hidden : ChannelPresentation.Display(facts);
        var pending = primary is not null && !facts.Paid && facts.RequestNewerThanPayment;
        var price = t.Option.Sellable ? t.Option.PricePerMonth : null;
        return new TransportNumbersDto(
            t.Transport, MessengerTexts.DisplayName(t.Transport), t.Option.Open,
            price, price is { } p ? MessengerTexts.PriceText(p) : null, t.Option.Sellable,
            !t.Option.Sellable && !t.Paid ? MessengerTexts.Unavailable(t.Transport) : null,
            t.Paid, t.Payment.PaidUntil, t.Payment.IsTrial, pending,
            ChannelPresentation.CanRequestPayment(facts),
            primary is not null && facts.TermsAccepted,
            ChannelPresentation.WizardStep(facts, hasChannel: primary is not null),
            display.Status, display.Text, display.Action,
            primary is null ? null : BuildChannel(primary, ctx),
            t.Duplicates.Select(d => BuildChannel(d, ctx)).ToList(),
            ChannelPresentation.TransportConnectionNotice(t.Transport),
            MessengerTexts.QrInstruction(t.Transport),
            prefill);
    }
}
