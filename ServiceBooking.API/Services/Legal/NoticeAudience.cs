using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §404.2 (US-20-03) — facts about the CALLING user, loaded once per
/// <c>GET /api/legal/notices</c> request, that <see cref="NoticeAudience.Matches"/> needs to decide
/// whether a given <see cref="PlatformNotice"/> is addressed to them. Deliberately a flat record with no
/// EF/HTTP types, so the matching rule itself stays a pure function.
/// </summary>
/// <param name="IsSuperAdmin">SuperAdmin never receives <see cref="NoticeAudienceType.AllClients"/>
/// notices (D2 п. 21.2) — they are staff, not a client of the platform.</param>
/// <param name="HeldBillingAccountId">The billing account this user OWNS (holds), if any — a company
/// manager who is not the owner holds none (§404.2's "управляющим владельческие уведомления не
/// показываются").</param>
/// <param name="HeldPlanConfigId">The plan currently assigned to <paramref name="HeldBillingAccountId"/>,
/// or null if the account has no subscription row / no plan assigned (the "Free by omission" case).</param>
/// <param name="SystemFreePlanConfigId">The id of the one plan flagged <c>IsSystemFree</c>, only needed
/// (and only ever looked up by the caller) when <paramref name="HeldPlanConfigId"/> is null and at least
/// one visible notice is <see cref="NoticeAudienceType.OwnersOnPlans"/> — §404.2's "если в списке есть
/// системный Free, подходят и аккаунты без подписки".</param>
public sealed record NoticeCallerFacts(
    bool IsSuperAdmin,
    Guid? HeldBillingAccountId,
    Guid? HeldPlanConfigId,
    Guid? SystemFreePlanConfigId);

/// <summary>
/// ARCHITECTURE_CYCLE20.md §404.2 — "Чистая функция Services/Legal/NoticeAudience.Matches(notice,
/// CallerFacts)". Computed at READ time against the caller's CURRENT facts, never snapshotted at publish
/// time (§404.1's own doc comment on <see cref="PlatformNotice"/>).
/// </summary>
public static class NoticeAudience
{
    public static bool Matches(PlatformNotice notice, NoticeCallerFacts facts) => notice.AudienceType switch
    {
        NoticeAudienceType.AllOwners => facts.HeldBillingAccountId is not null,
        NoticeAudienceType.OwnersOnPlans => facts.HeldBillingAccountId is not null && MatchesPlan(notice.AudiencePlanIds, facts),
        NoticeAudienceType.BillingAccount => facts.HeldBillingAccountId is not null && facts.HeldBillingAccountId == notice.TargetBillingAccountId,
        // §404.2 row 4: "любой аутентифицированный неудалённый пользователь, кроме SuperAdmin" —
        // authentication and "not deleted" are already guaranteed by the caller ([Authorize] plus the
        // existing token-validation pipeline that rejects a deleted account's token elsewhere); only the
        // SuperAdmin carve-out is this function's own job.
        NoticeAudienceType.AllClients => !facts.IsSuperAdmin,
        _ => false,
    };

    private static bool MatchesPlan(Guid[]? audiencePlanIds, NoticeCallerFacts facts)
    {
        if (audiencePlanIds is not { Length: > 0 }) return false;
        if (facts.HeldPlanConfigId is { } planId) return Array.IndexOf(audiencePlanIds, planId) >= 0;
        return facts.SystemFreePlanConfigId is { } freeId && Array.IndexOf(audiencePlanIds, freeId) >= 0;
    }
}
