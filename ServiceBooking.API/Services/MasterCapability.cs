using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services;

/// <summary>
/// US-67 (ARCHITECTURE_CYCLE6.md §47.1 p.2): "does this master perform this service" — the exact same
/// rule <c>CompaniesController.GetMasters</c> already applies when filtering by a single service
/// (a service nobody has an explicit <c>MasterService</c> row for is treated as "anyone can do it",
/// the existing fallback), extended here to a whole visit: the master must be able to do EVERY
/// selected service, not just at least one.
/// </summary>
public static class MasterCapability
{
    /// <summary>Returns the ids of <paramref name="serviceIds"/> the given master can NOT perform —
    /// empty when the master can do all of them. A service with zero MasterService rows for ANY master
    /// is skipped (the existing "no assignments exist yet" fallback) because it means everyone can
    /// perform it.</summary>
    public static async Task<List<Guid>> FindUnsupportedServicesAsync(
        AppDbContext db, string masterId, IReadOnlyList<Guid> serviceIds)
    {
        if (serviceIds.Count == 0) return [];

        var assignmentsByService = await db.MasterServices
            .Where(ms => serviceIds.Contains(ms.ServiceId))
            .Select(ms => new { ms.ServiceId, ms.MasterId })
            .ToListAsync();

        var servicesWithAnyAssignment = assignmentsByService.Select(a => a.ServiceId).ToHashSet();
        var servicesThisMasterCanDo = assignmentsByService
            .Where(a => a.MasterId == masterId)
            .Select(a => a.ServiceId)
            .ToHashSet();

        return serviceIds
            .Where(id => servicesWithAnyAssignment.Contains(id) && !servicesThisMasterCanDo.Contains(id))
            .ToList();
    }
}
