using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.DTOs.Companies;

// ARCHITECTURE_CYCLE10.md §106, API_CONTRACT_CYCLE10.md §125. IsCover is a derived convenience field
// (Position == 0), sent for the frontend rather than recomputed there.
public record CompanyPhotoDto(Guid Id, string Url, string ThumbnailUrl, int Width, int Height, int Position, bool IsCover)
{
    public static CompanyPhotoDto From(CompanyPhoto p) =>
        new(p.Id, p.Url, p.ThumbnailUrl, p.Width, p.Height, p.Position, p.Position == 0);
}

// API_CONTRACT_CYCLE10.md §128.2 — PUT /api/companies/{id}/photos/order body: a FULL permutation of the
// company's current photo ids, first = new cover.
public record ReorderCompanyPhotosDto(List<Guid> PhotoIds);
