using System.Linq.Expressions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Companies;

public static class SalonListingQuery
{
    /// <summary>Cycle 49: "has a service" = an active service. The single definition, used by the catalog SQL and by the checklist.</summary>
    public static readonly Expression<Func<Service, bool>> ActiveService = s => s.IsActive;

    /// <summary>Cycle 49: "has a staff member" = Master or CompanyOwner who provides services. Single definition for both sides.</summary>
    public static readonly Expression<Func<CompanyMember, bool>> ServiceProvider =
        m => m.ProvidesServices && (m.Role == UserRole.Master || m.Role == UserRole.CompanyOwner);

    /// <summary>
    /// The SQL twin of <see cref="SalonListingRules.Evaluate"/>.Visible: Kind = Services ∧ IsActive ∧ ShowInPublicListing ∧
    /// an active service ∧ a service provider ∧ <see cref="PublicListingQuery.WhereAllowsPublicListing"/>.
    /// ⚠️ Changing one side without the other breaks the catalog/checklist agreement (five conditions).
    /// </summary>
    public static IQueryable<Company> VisibleInSalonCatalog(this IQueryable<Company> companies, AppDbContext db, DateTime nowUtc)
    {
        var activeServices = db.Services.Where(ActiveService);
        var providers = db.CompanyMembers.Where(ServiceProvider);
        return companies
            .Where(c => c.Kind == CompanyKind.Services && c.IsActive && c.ShowInPublicListing)
            .Where(c => activeServices.Any(s => s.CompanyId == c.Id))
            .Where(c => providers.Any(m => m.CompanyId == c.Id))
            .WhereAllowsPublicListing(db, nowUtc);
    }
}
