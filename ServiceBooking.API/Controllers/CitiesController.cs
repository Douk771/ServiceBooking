using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Cities;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>Reference directory of Russian cities/towns (ARCHITECTURE_CYCLE4.md §34.2, US-30).
/// Anonymous — used by the company creation/edit forms before the caller is necessarily authenticated
/// in the owner-onboarding flow, and there is nothing sensitive in a city list.</summary>
[ApiController]
[Route("api/cities")]
public class CitiesController(AppDbContext db) : ControllerBase
{
    private const int DefaultTake = 20;
    private const int MaxTake = 20;

    /// <summary>API_CONTRACT_CYCLE4.md §31.1. Empty <c>search</c> → first <paramref name="take"/> by
    /// alphabet. Otherwise: prefix match first (<c>LIKE 'x%'</c>, indexed by <c>SearchName</c>), then —
    /// only if that alone doesn't fill the page — a substring fallback (<c>LIKE '%x%'</c>), so "Дону"
    /// still finds "Ростов-на-Дону" without every query paying for a substring scan. Deliberately no
    /// full-text/trigram index at this row count (§34.2) — do not "optimize" this without re-reading it.</summary>
    [HttpGet]
    public async Task<ActionResult<CityListDto>> Search([FromQuery] string? search, [FromQuery] int? take)
    {
        var limit = take is null or <= 0 ? DefaultTake : Math.Min(take.Value, MaxTake);
        var query = CitySearch.Normalize(search);

        List<City> cities;
        if (string.IsNullOrEmpty(query))
        {
            cities = await db.Cities.Where(c => c.IsActive)
                .OrderBy(c => c.Name).Take(limit).ToListAsync();
        }
        else
        {
            var prefixMatches = await db.Cities
                .Where(c => c.IsActive && EF.Functions.ILike(c.SearchName, query + "%"))
                .OrderBy(c => c.Name).Take(limit).ToListAsync();

            if (prefixMatches.Count >= limit)
            {
                cities = prefixMatches;
            }
            else
            {
                var alreadyFoundIds = prefixMatches.Select(c => c.Id).ToList();
                var remaining = limit - prefixMatches.Count;
                var substringMatches = await db.Cities
                    .Where(c => c.IsActive && !alreadyFoundIds.Contains(c.Id) &&
                                EF.Functions.ILike(c.SearchName, "%" + query + "%"))
                    .OrderBy(c => c.Name).Take(remaining).ToListAsync();

                cities = [.. prefixMatches, .. substringMatches];
            }
        }

        var now = DateTime.UtcNow;
        return Ok(new CityListDto(cities.Select(c => MapToDto(c, now)).ToList()));
    }

    private static CityDto MapToDto(City c, DateTime nowUtc)
    {
        TimeZoneOffset.TryGetUtcOffsetMinutes(c.TimeZoneId, nowUtc, out var offsetMinutes);
        return new CityDto(c.Id, c.Name, c.Region, c.TimeZoneId, offsetMinutes, $"{c.Name}, {c.Region}");
    }
}
