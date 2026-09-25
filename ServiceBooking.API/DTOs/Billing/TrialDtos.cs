namespace ServiceBooking.API.DTOs.Billing;

/// <summary>
/// Cycle 18 (API_CONTRACT_CYCLE18.md §362) — GET /api/billing/trial. <c>state</c> is a closed set of
/// four values: "Available", "Unavailable", "Active", "Expired". The frontend does not compute
/// availability itself (US-18-12) — every one of these fields is server output.
/// 🔴 Never returned as null just because the trial once existed — at state == "Expired" this object
/// must still carry endsAt/warning, or the Т3 "message survives 30 days" guarantee disappears from the
/// screen (§362).
/// </summary>
public record TrialStateDto(
    string State,
    Guid? PlanId,
    string? PlanName,
    int? DurationDays,
    int? MailingWindowDays,
    IReadOnlyList<int>? WarningThresholdsDays,
    DateTime? EndsAtPreview,
    string? RefusalCode,
    string Message,
    TrialActivationTermsDto? ActivationTerms,
    string? PlanChangeNotice,
    DateTime? StartedAt,
    DateTime? EndsAt,
    int? DaysLeft,
    string? GrantSource,
    TrialMailingWindowDto MailingWindow,
    TrialWarningDto? Warning,
    IReadOnlyList<string> Includes,
    TrialLimitsDto? Limits);

/// <summary>§362.1 (Т1/Т5) — the activation-terms block: text, version, hash, and whether this specific
/// owner still needs to acknowledge it (superadmin-granted trial, §335.4).</summary>
public record TrialActivationTermsDto(
    string Version, string Sha256, string Text, bool AcknowledgementRequired,
    DateTime? ShownAt, DateTime? AcknowledgedAt);

/// <summary>States: NotStarted | Running | EndingSoon | Ended | NotApplicable.</summary>
public record TrialMailingWindowDto(string State, DateTime? StartedAt, DateTime? EndsAt, int? DaysLeft, string Text);

/// <summary>Mirrors the shape of the existing (cycle 17) SubscriptionWarningDto plus the two fields Т3
/// needs (§362.2): <c>Dismissible</c> is a server decision (always false for TrialExpired), and
/// <c>VisibleUntilUtc</c> is the legal floor on how long the message must keep being served.</summary>
public record TrialWarningDto(
    string Code, string Text, IReadOnlyList<string> Affected, bool Dismissible, DateTime? VisibleUntilUtc);

public record TrialLimitsDto(int? Companies, int? Employees, int? PhotoQuotaMb);

/// <summary>POST /api/billing/trial body (§363) — the version of TrialActivationTerms the owner was
/// just shown. Server compares it against TrialTermsRegistry.CurrentVersion; a mismatch/empty value is
/// 409 TrialTermsVersionMismatch, never silently accepted.</summary>
public record TrialActivationRequestDto(string TermsVersion);

/// <summary>§360.1 — the ONE deliberate exception to "4xx bodies are plain text": a trial refusal is
/// JSON so the frontend gets both a machine code to branch on and ready Russian text to print.</summary>
public record TrialRefusalDto(string Code, string Message);
