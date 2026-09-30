using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Companies;

public static class SalonListingQuery
{
    /// <summary>
    /// The SQL twin of <see cref="SalonListingRules.Evaluate"/>.Visible: Kind = Services ∧ IsActive ∧ ShowInPublicListing ∧
    /// <see cref="PublicListingQuery.WhereAllowsPublicListing"/>. ⚠️ Changing one side without the other breaks the catalog/checklist agreement.
    /// </summary>
    public static IQueryable<Company> VisibleInSalonCatalog(this IQueryable<Company> companies, AppDbContext db, DateTime nowUtc) =>
        companies
            .Where(c => c.Kind == CompanyKind.Services && c.IsActive && c.ShowInPublicListing)
            .WhereAllowsPublicListing(db, nowUtc);
}
