using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Companies;

public enum CompanyKindCheck
{
    Ok,
    NotFound,
    WrongKind
}

/// <summary>
/// ARCHITECTURE_CYCLE23.md §389.1 — the single mechanism that keeps the two products apart. A company is
/// either a salon (Services) or a shop (Orders); booking routes refuse a shop, order routes see a salon as
/// "does not exist". The closed list of call sites is §389.2 — every one of them goes through here, so a new
/// route cannot silently forget the rule in its own private way. Cycle 26: the gallery routes (photos upload/delete/order) were removed from
/// that list — a shop has a gallery too; the other salon routes, including photo-usage and (cycle 31) GET|PUT companies/{id}/catalog-listing, still refuse a shop.
/// Convention (cycle 6): rights first, kind second — except anonymous public routes, which have no rights.
/// </summary>
public static class CompanyKindGuard
{
    /// <summary>409 body (bare string, existing 4xx convention) for a booking route hit with a shop id.</summary>
    public const string ShopRefusalText = "Это магазин: записи, услуги и расписание для него недоступны.";

    /// <summary>Pure decision: does a company of <paramref name="actual"/> kind (null = no such company) satisfy <paramref name="expected"/>?</summary>
    public static CompanyKindCheck Evaluate(CompanyKind? actual, CompanyKind expected) =>
        actual is null ? CompanyKindCheck.NotFound
        : actual == expected ? CompanyKindCheck.Ok
        : CompanyKindCheck.WrongKind;

    public static async Task<CompanyKindCheck> CheckAsync(
        AppDbContext db, Guid companyId, CompanyKind expected, CancellationToken ct = default)
    {
        var kind = await db.Companies.AsNoTracking().Where(c => c.Id == companyId)
            .Select(c => (CompanyKind?)c.Kind).FirstOrDefaultAsync(ct);
        return Evaluate(kind, expected);
    }

    /// <summary>ARCHITECTURE_CYCLE37.md §37.3.1 — the same refusal for a "Дома" company.</summary>
    public const string StaysRefusalText = Stays.StaysTexts.StaysRefusalText;

    /// <summary>The refusal text for a company that is not a salon, by its kind (a salon has none).</summary>
    public static string? RefusalTextFor(CompanyKind kind) => kind switch
    {
        CompanyKind.Services => null,
        CompanyKind.Orders => ShopRefusalText,
        CompanyKind.Stays => StaysRefusalText,
        _ => throw new System.Diagnostics.UnreachableException()
    };

    /// <summary>
    /// For booking/salon routes: 409 with the text of the company's kind (shop or "Дома") when the company is NOT a salon; null when
    /// it is a salon or does not exist (the caller's own "not found" handling stays in charge of that case).
    /// Cycle 37 generalised the former RejectShop*: one change closes the whole closed list of §389.2 for the third kind.
    /// </summary>
    public static async Task<ConflictObjectResult?> RejectNonSalonAsync(
        AppDbContext db, Guid companyId, CancellationToken ct = default)
    {
        var kind = await db.Companies.AsNoTracking().Where(c => c.Id == companyId)
            .Select(c => (CompanyKind?)c.Kind).FirstOrDefaultAsync(ct);
        return kind is { } k ? RejectNonSalon(k) : null;
    }

    /// <summary>Same, when the company kind is already known (entity already loaded).</summary>
    public static ConflictObjectResult? RejectNonSalon(CompanyKind kind) =>
        RefusalTextFor(kind) is { } text ? new ConflictObjectResult(text) : null;
}

