using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Legal;

public sealed record PriceChangeParams(Guid PlanId, decimal OldPricePerMonth, decimal NewPricePerMonth);
public sealed record TermsChangeParams(LegalDocumentType DocumentType, string ChangesSummary);
public sealed record NoticeAttachmentParams(string Title, string Html);

/// <summary>Everything <c>POST /api/admin/notices</c> and <c>POST /api/admin/notices/preview</c> share —
/// API_CONTRACT_CYCLE20.md §434.5/§434.6 read the identical body.</summary>
public sealed record PlatformNoticeBuildRequest(
    PlatformNoticeKind Kind,
    NoticeAudienceType AudienceType,
    Guid[]? AudiencePlanIds,
    Guid? TargetBillingAccountId,
    DateOnly? EffectiveFrom,
    string? Title,
    string? Body,
    string? LinkUrl,
    PriceChangeParams? PriceChange,
    TermsChangeParams? TermsChange,
    NoticeAttachmentParams? Attachment);

/// <summary>ARCHITECTURE_CYCLE20.md §404.3/§404.7 (US-20-03/US-20-07, Т20-02/Т20-07) — the one place that
/// turns a <see cref="PlatformNoticeBuildRequest"/> into a fully-populated (not-yet-saved)
/// <see cref="PlatformNotice"/>, running BOTH the pure <see cref="PlatformNoticeRules"/> checks and the
/// database-dependent existence checks the rules class cannot do on its own. Used by
/// <c>AdminNoticesController</c> for publish (saves) and preview (does not), and separately by
/// <see cref="PublishPhotoRemovedAsync"/> for the one notice the product itself publishes.</summary>
public sealed class PlatformNoticePublisher(AppDbContext db, ILogger<PlatformNoticePublisher> logger)
{
    /// <summary>Validates <paramref name="request"/> against §434.5's matrix (shape, audience-for-kind,
    /// effectiveFrom, existence of the referenced plan/account) and, if it passes, renders the
    /// title/body/templateVersion and returns a fully-populated entity that has NOT been added to the
    /// context or saved yet. Returns an error string instead when validation fails.</summary>
    public async Task<(PlatformNotice? Notice, string? Error)> TryBuildAsync(
        PlatformNoticeBuildRequest request, string createdByUserId, DateTime nowUtc, CancellationToken ct = default)
    {
        if (!PlatformNoticeRules.IsSuperAdminCreatable(request.Kind))
            return (null, "Уведомление вида PhotoRemoved создаётся только системой.");

        var shapeError = PlatformNoticeRules.ValidateParameterShape(
            request.Kind, request.Title is not null, request.Body is not null,
            request.PriceChange is not null, request.TermsChange is not null, request.Attachment is not null);
        if (shapeError is not null) return (null, shapeError);

        var audienceShapeError = PlatformNoticeRules.ValidateAudienceShape(request.AudienceType, request.AudiencePlanIds, request.TargetBillingAccountId);
        if (audienceShapeError is not null) return (null, audienceShapeError);

        var audienceForKindError = PlatformNoticeRules.ValidateAudienceForKind(request.Kind, request.AudienceType, request.TermsChange?.DocumentType);
        if (audienceForKindError is not null) return (null, audienceForKindError);

        var todayMsk = PlatformNoticeRules.TodayMoscow(nowUtc);
        var effectiveFromError = PlatformNoticeRules.ValidateEffectiveFrom(request.Kind, request.AudienceType, request.EffectiveFrom, todayMsk);
        if (effectiveFromError is not null) return (null, effectiveFromError);

        var linkUrlError = PlatformNoticeRules.ValidateLinkUrl(request.LinkUrl);
        if (linkUrlError is not null) return (null, linkUrlError);

        if (request.Title is { Length: > PlatformNoticeRules.MaxTitleLength })
            return (null, $"Заголовок не должен превышать {PlatformNoticeRules.MaxTitleLength} символов.");
        if (request.Body is { Length: > PlatformNoticeRules.MaxBodyLength })
            return (null, $"Текст не должен превышать {PlatformNoticeRules.MaxBodyLength} символов.");
        if (request.TermsChange is { ChangesSummary.Length: > PlatformNoticeRules.MaxChangesSummaryLength })
            return (null, $"Перечень изменений не должен превышать {PlatformNoticeRules.MaxChangesSummaryLength} символов.");
        if (request.Attachment is { Title.Length: > PlatformNoticeRules.MaxAttachmentTitleLength })
            return (null, $"Заголовок приложения не должен превышать {PlatformNoticeRules.MaxAttachmentTitleLength} символов.");
        if (request.Attachment is { Html.Length: > PlatformNoticeRules.MaxAttachmentHtmlLength })
            return (null, $"Приложение не должно превышать {PlatformNoticeRules.MaxAttachmentHtmlLength} символов.");

        // Existence checks — the database-dependent half this class alone can do.
        if (request.AudienceType == NoticeAudienceType.OwnersOnPlans)
        {
            var existingPlanCount = await db.SubscriptionPlanConfigs.CountAsync(p => request.AudiencePlanIds!.Contains(p.Id), ct);
            if (existingPlanCount != request.AudiencePlanIds!.Length)
                return (null, "Один или несколько тарифов из planIds не найдены.");
        }
        if (request.AudienceType == NoticeAudienceType.BillingAccount)
        {
            var accountExists = await db.BillingAccounts.AnyAsync(a => a.Id == request.TargetBillingAccountId, ct);
            if (!accountExists) return (null, "Указанный биллинг-аккаунт не найден.");
        }

        string title;
        string body;
        string? templateVersion = null;

        if (request.Kind == PlatformNoticeKind.PriceChange)
        {
            var plan = await db.SubscriptionPlanConfigs.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PriceChange!.PlanId, ct);
            if (plan is null) return (null, "Указанный тариф не найден.");
            (title, body) = PlatformNoticeTexts.BuildPriceChange(plan.Name, request.PriceChange!.OldPricePerMonth, request.PriceChange.NewPricePerMonth, request.EffectiveFrom!.Value);
            templateVersion = PlatformNoticeTexts.TemplateVersion;
        }
        else if (request.Kind == PlatformNoticeKind.TermsChange)
        {
            if (request.TermsChange!.DocumentType is not (LegalDocumentType.Privacy or LegalDocumentType.TermsClient or LegalDocumentType.TermsOwner))
                return (null, "Недопустимый тип документа для termsChange.documentType.");
            (title, body) = PlatformNoticeTexts.BuildTermsChange(request.TermsChange.DocumentType, request.TermsChange.ChangesSummary, request.EffectiveFrom!.Value);
            templateVersion = PlatformNoticeTexts.TemplateVersion;
        }
        else
        {
            title = request.Title!;
            body = request.Body!;
        }

        var publishedAtUtc = nowUtc;
        var notice = new PlatformNotice
        {
            Id = Guid.NewGuid(),
            Kind = request.Kind,
            AudienceType = request.AudienceType,
            AudiencePlanIds = request.AudienceType == NoticeAudienceType.OwnersOnPlans ? request.AudiencePlanIds : null,
            TargetBillingAccountId = request.AudienceType == NoticeAudienceType.BillingAccount ? request.TargetBillingAccountId : null,
            Title = title,
            Body = body,
            TemplateVersion = templateVersion,
            LinkUrl = request.LinkUrl,
            AttachmentTitle = request.Attachment?.Title,
            AttachmentHtml = request.Attachment?.Html,
            AttachmentSha256 = request.Attachment is null ? null : ComputeSha256Hex(request.Attachment.Html),
            EffectiveFrom = request.EffectiveFrom,
            PublishedAtUtc = publishedAtUtc,
            VisibleUntilUtc = PlatformNoticeRules.ComputeVisibleUntilUtc(publishedAtUtc, request.EffectiveFrom),
            CreatedByUserId = createdByUserId,
        };

        return (notice, null);
    }

    /// <summary>ARCHITECTURE_CYCLE20.md §404.7 (Т20-07) — called AFTER <c>DELETE …/photos/{photoId}</c>
    /// commits its own transaction. Never throws: a failure here must not roll back a photo deletion that
    /// already happened (the obligation to the depicted person is already satisfied), so any error is
    /// logged as a Warning WITHOUT any personal data — not even the requester's identity, which this
    /// method never receives in the first place.</summary>
    public async Task PublishPhotoRemovedAsync(Company company, CancellationToken ct = default)
    {
        var billingAccountId = company.BillingAccountId
            ?? await db.BillingAccounts.Where(a => a.OwnerUserId == company.OwnerUserId).Select(a => (Guid?)a.Id).FirstOrDefaultAsync(ct);

        if (billingAccountId is null)
        {
            logger.LogWarning("PhotoRemoved notice skipped for company {CompanyId}: neither the company nor its owner has a billing account.", company.Id);
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var removalDateMsk = PlatformNoticeRules.TodayMoscow(nowUtc);
        var (title, body) = PlatformNoticeTexts.BuildPhotoRemoved(company.Name, removalDateMsk);

        var notice = new PlatformNotice
        {
            Id = Guid.NewGuid(),
            Kind = PlatformNoticeKind.PhotoRemoved,
            AudienceType = NoticeAudienceType.BillingAccount,
            TargetBillingAccountId = billingAccountId,
            Title = title,
            Body = body,
            TemplateVersion = PlatformNoticeTexts.TemplateVersion,
            LinkUrl = null, // §11.3 — no contact/reference of the requester may leak, not even via a link
            EffectiveFrom = null,
            PublishedAtUtc = nowUtc,
            VisibleUntilUtc = PlatformNoticeRules.ComputeVisibleUntilUtc(nowUtc, null),
            CreatedByUserId = "system",
        };

        try
        {
            db.PlatformNotices.Add(notice);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist PhotoRemoved notice for company {CompanyId}.", company.Id);
        }
    }

    private static string ComputeSha256Hex(string html)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(html);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
