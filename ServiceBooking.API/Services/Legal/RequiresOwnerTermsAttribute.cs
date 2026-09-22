using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// Blocks one owner action with 451 when the caller's token carries a STALE accepted version of
/// TermsOwner — ARCHITECTURE_CYCLE5.md §46.3's OwnerScope gate, deliberately NOT the global
/// <see cref="LegalConsentFilter"/>: a company owner is very often also a client of their own platform,
/// and a blanket 451 over TermsOwner would lock them out of their own booking history and profile too.
/// Applied per-action (API_CONTRACT_CYCLE5.md §42.2's fixed list), not globally.
///
/// 🔴 Missing claim ("lco") is treated as "not yet applicable", NOT as a mismatch — unlike
/// LegalConsentFilter's Privacy/TermsClient claims, which every authenticated account has from
/// registration onward, a first-time company creator has no TermsOwner claim at all. That first
/// acceptance is gated by the request BODY (`POST /api/companies`'s `ownerTerms.version`, checked inside
/// the controller action, §42.1) — not by this attribute. This attribute only re-enforces acceptance
/// for someone who accepted ONCE and is now stale (a Material redaction happened since), exactly the
/// same "claim baked in at token issuance" mechanism the global gate uses.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresOwnerTermsAttribute : Attribute, IFilterFactory
{
    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) =>
        ActivatorUtilities.CreateInstance<RequiresOwnerTermsFilter>(serviceProvider);
}

internal sealed class RequiresOwnerTermsFilter(LegalDocumentProvider provider) : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        // [Authorize] (present on every action this attribute decorates) already turns an unauthenticated
        // caller into 401 before this filter's result would matter — nothing to enforce here either way.
        if (user.Identity is not { IsAuthenticated: true }) return Task.CompletedTask;

        var doc = provider.Current?.Get(LegalDocumentType.TermsOwner);
        // No manifest loaded (only reachable outside Production) or an Editorial redaction — nothing to
        // block on, same "Material always wins, Editorial never blocks" rule as the global gate.
        if (doc is null || doc.ChangeKind != LegalChangeKind.Material) return Task.CompletedTask;

        var acceptedVersion = user.FindFirst("lco")?.Value;
        // See the class doc comment — absent claim means "never accepted yet", handled by the request
        // body on POST /api/companies, not blocked here.
        if (acceptedVersion is null || acceptedVersion == doc.Version) return Task.CompletedTask;

        // API_CONTRACT_CYCLE5.md §38.2: the ONE 451 in the product that carries a JSON body — the
        // frontend needs the type/version to open the acceptance modal, distinguished from the global
        // gate's plain-text 451 by Content-Type.
        context.Result = new JsonResult(new { reason = "OwnerTermsNotAccepted", documentType = "TermsOwner", version = doc.Version })
        {
            StatusCode = StatusCodes.Status451UnavailableForLegalReasons
        };
        return Task.CompletedTask;
    }
}
