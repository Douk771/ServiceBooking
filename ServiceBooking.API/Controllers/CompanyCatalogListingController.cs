using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceBooking.API.DTOs.Catalog;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE31.md §31.5.3 — the salon's "Каталог ezbook.ru" block: whether the salon is in the catalog and why not,
/// from the ONE rule (<see cref="SalonListingRules"/>) the catalog itself uses. Owner (or SuperAdmin) only; a foreign or missing
/// company is a plain 404. No rate limit, like the shop route: GET is cheap, PUT is gated by rights.
/// </summary>
[ApiController]
[Route("api/companies/{id:guid}/catalog-listing")]
[Authorize]
public class CompanyCatalogListingController(AppDbContext db, SubscriptionResolver subscriptionResolver) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CatalogListingDto>> Get(Guid id, CancellationToken ct)
    {
        var (company, error) = await LoadAsync(id, ct);
        if (error is not null) return error;
        return Ok(Build(company!, await IsAllowedByPlanAsync(company!.Id)));
    }

    /// <summary>Switching ON is refused (409, JSON) when the tariff does not allow it; OFF always works.</summary>
    [HttpPut]
    [RequiresOwnerTerms]
    public async Task<ActionResult<CatalogListingDto>> Put(Guid id, CatalogListingInputDto input, CancellationToken ct)
    {
        var (company, error) = await LoadAsync(id, ct);
        if (error is not null) return error;

        if (input?.ShowInCatalog is not { } show)
            return BadRequest(SalonListingRules.MissingValueText);

        var allowed = await IsAllowedByPlanAsync(id);
        if (show && !allowed)
            return Conflict(new CatalogConflictDto(CatalogConflictCode.CatalogListingNotAllowedByPlan, SalonListingRules.NotAllowedByPlanHintText));

        company!.ShowInPublicListing = show;
        await db.SaveChangesAsync(ct);
        return Ok(Build(company, allowed));
    }

    private async Task<(Company? Company, ActionResult? Error)> LoadAsync(Guid id, CancellationToken ct)
    {
        var company = await db.Companies.FindAsync([id], ct);
        if (company is null || !await CompanyAccess.CanManageCompanyAsync(db, User, id)) return (null, NotFound());
        var shopRefusal = CompanyKindGuard.RejectNonSalon(company.Kind);
        return shopRefusal is not null ? (null, shopRefusal) : (company, null);
    }

    private async Task<bool> IsAllowedByPlanAsync(Guid companyId) =>
        (await subscriptionResolver.GetEffectivePlanAsync(companyId)).AllowPublicListing;

    private static CatalogListingDto Build(Company company, bool allowed)
    {
        var verdict = SalonListingRules.Evaluate(new SalonListingInput(company.IsActive, allowed, company.ShowInPublicListing));
        return new CatalogListingDto(
            company.ShowInPublicListing, allowed, verdict.Visible, SalonListingRules.StatusText(verdict.Visible),
            allowed ? null : SalonListingRules.NotAllowedByPlanHintText,
            verdict.Checklist.Select(c => new CatalogListingCheckDto(c.Code, c.Text, c.Done)).ToList());
    }
}
