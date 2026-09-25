using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// Cycle 18 (ARCHITECTURE_CYCLE18.md §332.4) — append-only log of trial grants. Table
/// <c>TrialGrants</c>. Two jobs in one row: (1) the database-level guarantee of once-only-ness (a
/// partial unique index on <see cref="BillingAccountId"/> excluding <see cref="TrialGrantSource.SuperAdminOverride"/>
/// grants), and (2) the evidentiary record that the activation terms were shown and — Т1 — WHEN, at
/// WHICH version, with WHAT hash. Never deleted, never included in any retention rule (it's proof of a
/// duty discharged, and a dispute about it can arise within the general 3-year limitation period).
/// </summary>
public class TrialGrant
{
    public Guid Id { get; set; }
    public Guid BillingAccountId { get; set; }
    public BillingAccount? BillingAccount { get; set; }

    // The trial plan AT GRANT TIME (it could be renamed later; this is a snapshot reference, not a
    // "current trial plan" pointer).
    public Guid? PlanConfigId { get; set; }

    public DateTime GrantedAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public int DurationDays { get; set; }
    public int MailingWindowDays { get; set; }
    public string WarningThresholdsDays { get; set; } = string.Empty;  // string(32), Д19

    public TrialGrantSource Source { get; set; }
    public string GrantedByUserId { get; set; } = string.Empty;

    // Mandatory and non-empty ONLY for Source == SuperAdminOverride (Д1-бис). Null for an ordinary
    // grant — no emergency bypass happened, so no reason is required.
    public string? Reason { get; set; }      // string(500)

    // ── Т1: proof that the duty to inform was discharged ─────────────────────────────────────────
    // Version of the TrialActivationTerms edition and the SHA-256 of its TEMPLATE (not the rendered
    // string with substitutions — otherwise the hash would depend on the date). Written by the
    // server, never trusted from the client: the client-sent version is only compared against the
    // current one; the hash is always the server's own.
    public string TermsVersion { get; set; } = string.Empty;     // string(32), NOT NULL
    public string TermsTextSha256 { get; set; } = string.Empty;  // string(64), NOT NULL

    // Moment shown / moment acknowledged. Equal to GrantedAtUtc for an owner self-service grant. Null
    // until POST /api/billing/trial/terms-acknowledgement for a superadmin-granted trial.
    // 🔴 SET-ONCE: the only allowed transition is null → a value. No code path may overwrite an
    // already-recorded value — otherwise the log stops answering "when did the person first learn".
    public DateTime? TermsShownAtUtc { get; set; }
    public DateTime? TermsAcknowledgedAtUtc { get; set; }
}
