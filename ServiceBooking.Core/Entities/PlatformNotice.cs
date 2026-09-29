using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §404.1 (US-20-03, C18-8(б), Т20-02) — a published in-cabinet notice. Never
/// edited after publication (there is no PUT), only revoked; the audience is computed at read time
/// against the current caller (see <c>Services.Legal.NoticeAudience</c>), not snapshotted here.
/// </summary>
public class PlatformNotice
{
    public Guid Id { get; set; }

    public PlatformNoticeKind Kind { get; set; }

    public NoticeAudienceType AudienceType { get; set; }

    // Only meaningful (and only ever set) for AudienceType == OwnersOnPlans. Native Postgres uuid[] via
    // Npgsql (ARCHITECTURE_CYCLE20.md §404.1) — no value converter needed.
    public Guid[]? AudiencePlanIds { get; set; }

    // Only meaningful (and only ever set) for AudienceType == BillingAccount. No FK on purpose — the
    // notice must remain readable/auditable even if the account is later deleted.
    public Guid? TargetBillingAccountId { get; set; }

    public string Title { get; set; } = string.Empty;

    // Plain text, a SNAPSHOT of what the addressee will see — assembled server-side once, at publish
    // time, from PlatformNoticeTexts' templates for the templated kinds (§404.3), or typed by hand by
    // the superadmin for Suspension/NewProcessor/Other. Re-reading it later never re-renders anything.
    public string Body { get; set; } = string.Empty;

    // Which PlatformNoticeTexts template version produced Body, for the templated kinds only.
    public string? TemplateVersion { get; set; }

    // Relative path only (enforced at the API boundary, not here) — never an absolute URL.
    public string? LinkUrl { get; set; }

    // A snapshot of a future document edition attached to a TermsChange notice (§404.4) so the
    // addressee can read it before EffectiveFrom without a second "current" edition existing anywhere.
    public string? AttachmentTitle { get; set; }
    public string? AttachmentHtml { get; set; }
    public string? AttachmentSha256 { get; set; }

    public DateOnly? EffectiveFrom { get; set; }

    public DateTime PublishedAtUtc { get; set; } = DateTime.UtcNow;

    // max(PublishedAtUtc + 365d, (EffectiveFrom ?? PublishedAtUtc) + 30d) — computed once at publish
    // time by PlatformNoticeRules, not recomputed on read, so the visibility window a reader was
    // promised never silently changes under them.
    public DateTime VisibleUntilUtc { get; set; }

    // "system" for the one notice the product itself publishes (PhotoRemoved) — otherwise a superadmin's
    // user id.
    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedByUserId { get; set; }
    public string? RevokeReason { get; set; }

    public List<PlatformNoticeAcknowledgement> Acknowledgements { get; set; } = [];
}
