namespace ServiceBooking.API.Services.Showcase;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §574.2, API_CONTRACT_CYCLE28.md §595 — the ONE place that decides whether showcase (fictional) and real
/// accounts may be mixed. Pure: every method takes the marks it needs and returns the Russian refusal text (a 409 <c>text/plain</c>) or
/// null when the operation is allowed. Callers run it after their rights/existence checks and before any write.
///
/// Invariants this protects (§574.1): a showcase company has a showcase billing account, owner and staff; a real one never has a
/// showcase member; the hidden service tariff is only ever assigned to a showcase account.
/// </summary>
public static class ShowcaseMixingGuard
{
    public const string MembersText = "Витринную компанию и настоящие учётные записи смешивать нельзя.";
    public const string TransferText = "Витринную компанию нельзя перенести в настоящий аккаунт, а настоящую — в витринный.";
    public const string ServicePlanText = "Служебный тариф витрины нельзя назначить настоящему аккаунту.";
    public const string ReservedSlugText = "Адрес, начинающийся с «primer-», зарезервирован. Выберите другой.";

    /// <summary>Adding a staff member (or changing the responsible person): the marks of the company and the user must match.</summary>
    public static string? CheckMember(bool companyIsShowcase, bool userIsShowcase) =>
        companyIsShowcase == userIsShowcase ? null : MembersText;

    /// <summary>Company transfer: the company, the target billing account and (when given) the new responsible person must all agree.</summary>
    public static string? CheckTransfer(bool companyIsShowcase, bool targetAccountIsShowcase, bool? newResponsibleIsShowcase = null)
    {
        if (companyIsShowcase != targetAccountIsShowcase) return TransferText;
        if (newResponsibleIsShowcase is { } responsible && responsible != companyIsShowcase) return TransferText;
        return null;
    }

    /// <summary>The hidden service tariff may only be assigned to a showcase billing account.</summary>
    public static string? CheckServicePlan(Guid planConfigId, bool accountIsShowcase) =>
        planConfigId == ShowcaseCatalog.ShowcasePlanId && !accountIsShowcase ? ServicePlanText : null;

    /// <summary>The slug prefix reserved for showcase companies (case-insensitive, after trimming).</summary>
    public static bool IsReservedSlug(string? slug) =>
        slug is not null && slug.Trim().StartsWith(ShowcaseCatalog.SlugPrefix, StringComparison.OrdinalIgnoreCase);
}
