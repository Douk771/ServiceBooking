using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §404.3 (US-20-03, Т20-02), API_CONTRACT_CYCLE20.md §434.5's kind matrix — a
/// pure class, no EF/HTTP types, deciding whether a manually-published <see cref="Core.Entities.PlatformNotice"/>
/// is shaped correctly: which audience types a kind allows, which parameters it requires/forbids, the
/// minimum days between "today" and <c>effectiveFrom</c>, and the visibility-window computation. All
/// existence checks that need a database (does this plan/billing account actually exist) are the
/// CALLER's job — this class only validates what can be decided from the input's own shape.
/// </summary>
public static class PlatformNoticeRules
{
    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 4000;
    public const int MaxChangesSummaryLength = 1000;
    public const int MaxAttachmentTitleLength = 200;
    public const int MaxAttachmentHtmlLength = 1_000_000;
    public const int MaxRevokeReasonLength = 500;
    public const int MaxLinkUrlLength = 500;

    private static readonly TimeZoneInfo MoscowZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");

    /// <summary>§404.3 "«Сегодня» — дата по Europe/Moscow".</summary>
    public static DateOnly TodayMoscow(DateTime nowUtc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, MoscowZone));

    /// <summary>§434.5's row 6 (PhotoRemoved): "создаётся только системой" — the one kind SuperAdmin can
    /// never publish through <c>POST /api/admin/notices</c>.</summary>
    public static bool IsSuperAdminCreatable(PlatformNoticeKind kind) => kind != PlatformNoticeKind.PhotoRemoved;

    public static bool IsOwnerFacingAudience(NoticeAudienceType type) =>
        type is NoticeAudienceType.AllOwners or NoticeAudienceType.OwnersOnPlans or NoticeAudienceType.BillingAccount;

    /// <summary>§434.5's "Адресаты" column, plus the `TermsChange` documentType split (§404.3):
    /// `TermsOwner` → only owner-facing, `TermsClient` → only `AllClients`, `Privacy` → any. Returns the
    /// Russian 400 text, or null when the pairing is allowed.</summary>
    public static string? ValidateAudienceForKind(PlatformNoticeKind kind, NoticeAudienceType audienceType, LegalDocumentType? termsDocumentType)
    {
        switch (kind)
        {
            case PlatformNoticeKind.PriceChange:
                return audienceType == NoticeAudienceType.AllClients
                    ? "Уведомление об изменении цены адресуется только владельцам."
                    : null;

            case PlatformNoticeKind.TermsChange:
                return termsDocumentType switch
                {
                    LegalDocumentType.TermsOwner when !IsOwnerFacingAudience(audienceType) =>
                        "Уведомление о новой редакции Соглашения с компанией адресуется только владельцам.",
                    LegalDocumentType.TermsClient when audienceType != NoticeAudienceType.AllClients =>
                        "Уведомление о новой редакции Пользовательского соглашения адресуется всем клиентам.",
                    LegalDocumentType.Privacy => null,
                    LegalDocumentType.TermsOwner or LegalDocumentType.TermsClient => null,
                    _ => "Неизвестный тип документа для termsChange.documentType.",
                };

            case PlatformNoticeKind.Suspension:
                return audienceType != NoticeAudienceType.BillingAccount
                    ? "Уведомление о приостановлении адресуется только конкретному аккаунту."
                    : null;

            case PlatformNoticeKind.NewProcessor:
            case PlatformNoticeKind.Other:
                return null;

            default:
                return "Этот вид уведомления нельзя опубликовать вручную.";
        }
    }

    /// <summary>§434.5's "параметры чужого вида — отказ, а не молчаливое игнорирование" plus each row's
    /// own required/forbidden fields. Presence-only checks — content length is validated separately.</summary>
    public static string? ValidateParameterShape(
        PlatformNoticeKind kind, bool hasTitle, bool hasBody, bool hasPriceChange, bool hasTermsChange, bool hasAttachment)
    {
        if (hasPriceChange && kind != PlatformNoticeKind.PriceChange)
            return "Параметр priceChange допустим только для вида PriceChange.";
        if (hasTermsChange && kind != PlatformNoticeKind.TermsChange)
            return "Параметр termsChange допустим только для вида TermsChange.";

        switch (kind)
        {
            case PlatformNoticeKind.PriceChange:
                if (!hasPriceChange) return "Для PriceChange укажите параметры priceChange.";
                if (hasTitle || hasBody) return "Заголовок и текст для PriceChange собирает сервер — не присылайте их.";
                return null;

            case PlatformNoticeKind.TermsChange:
                if (!hasTermsChange) return "Для TermsChange укажите параметры termsChange.";
                if (hasTitle || hasBody) return "Заголовок и текст для TermsChange собирает сервер — не присылайте их.";
                if (!hasAttachment) return "Для TermsChange обязательно приложение со снимком новой редакции.";
                return null;

            case PlatformNoticeKind.Suspension:
                if (!hasTitle || !hasBody) return "Для Suspension укажите заголовок и текст.";
                if (hasAttachment) return "Для Suspension приложение не предусмотрено.";
                return null;

            case PlatformNoticeKind.NewProcessor:
            case PlatformNoticeKind.Other:
                if (!hasTitle || !hasBody) return $"Для {kind} укажите заголовок и текст.";
                return null;

            default:
                return "Этот вид уведомления нельзя опубликовать вручную.";
        }
    }

    /// <summary>§434.5's `effectiveFrom` column. Returns the Russian 400 text, or null when the supplied
    /// value (possibly null) is acceptable for this kind/audience pairing.</summary>
    public static string? ValidateEffectiveFrom(PlatformNoticeKind kind, NoticeAudienceType audienceType, DateOnly? effectiveFrom, DateOnly todayMsk)
    {
        switch (kind)
        {
            case PlatformNoticeKind.PriceChange:
                return RequireMinDaysFromToday(effectiveFrom, todayMsk, 30,
                    "Дата вступления в силу обязательна для изменения цены.");

            case PlatformNoticeKind.TermsChange:
                var termsMinDays = IsOwnerFacingAudience(audienceType) ? 15 : 10;
                return RequireMinDaysFromToday(effectiveFrom, todayMsk, termsMinDays,
                    "Дата вступления в силу обязательна для новой редакции документа.");

            case PlatformNoticeKind.NewProcessor:
                // D3 п. 11.9.5 (legal-counsel addendum, LEGAL_REVIEW_CYCLE20.md §11 ⚠️, codified into
                // §404.3/§434.5): the 15-day floor applies only when the notice reaches an OWNER — for
                // AllClients the date is still mandatory, but no minimum lead time is enforced.
                if (effectiveFrom is null) return "Дата вступления в силу обязательна.";
                return IsOwnerFacingAudience(audienceType)
                    ? RequireMinDaysFromToday(effectiveFrom, todayMsk, 15,
                        "Дата вступления в силу обязательна для уведомления о новом обработчике.")
                    : null;

            default:
                // Suspension, Other — effectiveFrom is optional and never checked (§404.3).
                return null;
        }
    }

    private static string? RequireMinDaysFromToday(DateOnly? effectiveFrom, DateOnly todayMsk, int minDays, string missingMessage)
    {
        if (effectiveFrom is null) return missingMessage;
        var earliest = todayMsk.AddDays(minDays);
        return effectiveFrom < earliest
            ? $"Дата вступления в силу должна быть не раньше {earliest:dd.MM.yyyy} (не менее {minDays} дней)."
            : null;
    }

    /// <summary>§434.5's "audience.planIds непуст для OwnersOnPlans (и только для него); audience.
    /// billingAccountId — для BillingAccount (и только для него)". Existence checks are the caller's job.</summary>
    public static string? ValidateAudienceShape(NoticeAudienceType type, Guid[]? planIds, Guid? billingAccountId)
    {
        if (type == NoticeAudienceType.OwnersOnPlans)
        {
            if (planIds is not { Length: > 0 }) return "Для адресата OwnersOnPlans укажите непустой список тарифов.";
        }
        else if (planIds is { Length: > 0 })
            return "Список тарифов допустим только для адресата OwnersOnPlans.";

        if (type == NoticeAudienceType.BillingAccount)
        {
            if (billingAccountId is null) return "Для адресата BillingAccount укажите аккаунт.";
        }
        else if (billingAccountId is not null)
            return "Идентификатор аккаунта допустим только для адресата BillingAccount.";

        return null;
    }

    /// <summary>§434.5's "linkUrl — только относительный путь, начинающийся с / и не с //, ≤ 500".</summary>
    public static string? ValidateLinkUrl(string? linkUrl)
    {
        if (linkUrl is null) return null;
        if (linkUrl.Length > MaxLinkUrlLength) return $"Ссылка не должна превышать {MaxLinkUrlLength} символов.";
        if (!linkUrl.StartsWith('/') || linkUrl.StartsWith("//"))
            return "Ссылка должна быть относительным путём, начинающимся с одного /.";
        return null;
    }

    /// <summary>§404.1: <c>max(PublishedAtUtc + 365d, (EffectiveFrom ?? PublishedAtUtc) + 30d)</c> —
    /// computed once at publish time, never recomputed on read.</summary>
    public static DateTime ComputeVisibleUntilUtc(DateTime publishedAtUtc, DateOnly? effectiveFrom)
    {
        var byPublish = publishedAtUtc.AddDays(365);
        var effectiveBaseUtc = effectiveFrom is { } d
            ? DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc)
            : publishedAtUtc;
        var byEffective = effectiveBaseUtc.AddDays(30);
        return byPublish > byEffective ? byPublish : byEffective;
    }
}
