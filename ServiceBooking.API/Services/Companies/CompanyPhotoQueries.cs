using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Companies;

/// <summary>ARCHITECTURE_CYCLE26.md §547 — one ordered read of a company gallery, shared by GET …/photos and the shop storefront.</summary>
public static class CompanyPhotoQueries
{
    /// <summary>Order: Position, CreatedAtUtc, Id. Uses the (CompanyId, Position) index.</summary>
    public static async Task<List<CompanyPhotoDto>> OrderedAsync(AppDbContext db, Guid companyId, CancellationToken ct)
    {
        var photos = await db.CompanyPhotos.AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.Position).ThenBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .ToListAsync(ct);
        return photos.Select(CompanyPhotoDto.From).ToList();
    }
}
