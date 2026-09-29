using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE19.md §388.1/§388.2/§413 — company address and the public-address-notice legal
/// gate. Before cycle 19 this controller also verified the address against a map (a licensed geocoder,
/// ARCHITECTURE_CYCLE13.md §207/§209/§220); that whole capability is removed here, `lookup` no longer
/// exists (404 unconditionally, §413.3), and saving no longer writes the five now-shadow address-check
/// columns (§383.3).
///
/// Both routes require <see cref="AuthorizeAttribute"/> and share the <c>address-verify</c> rate limit
/// (§388.2) — kept under its previous name so a production override of
/// <c>RATELIMITS__ADDRESS-VERIFY__*</c> keeps working without a config change.
/// </summary>
[ApiController]
[Route("api/companies")]
public class CompanyAddressController(
    AppDbContext db, SubscriptionResolver subscriptionResolver, AccountUsageReader accountUsageReader,
    LegalDocumentProvider legalProvider, ConsentLedger ledger) : ControllerBase
{
    // ── PUT /api/companies/{id}/address (§413.2) ────────────────────────────────────────────────────

    [HttpPut("{id:guid}/address")]
    [Authorize]
    [EnableRateLimiting("address-verify")]
    public async Task<ActionResult<CompanyAddressUpdateResultDto>> SaveAddress(Guid id, [FromBody] SaveCompanyAddressDto dto)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return NotFound();
        if (!await CanManageCompanyAsync(id)) return Forbid();

        var address = dto.Address ?? "";
        if (address.Length > 300) return BadRequest("Адрес не должен превышать 300 символов");

        // §413.2: written EXACTLY as the client sent it, byte for byte — never trimmed, never
        // normalized. Empty string clears the address (distinct from PUT /api/companies/{id}, where
        // null/absent means "don't touch" — this endpoint's `address` field is never optional).
        // `dto.Verify` is accepted and ignored (there is no geocoder to verify against any more); the
        // five address-check columns are shadow properties this route never writes (§383.3).
        company.Address = address.Length == 0 ? null : address;

        await db.SaveChangesAsync();

        var companyDto = await BuildCompanyDtoAsync(company);
        return Ok(new CompanyAddressUpdateResultDto(companyDto));
    }

    // ── POST /api/companies/address/notice (§413.1) ─────────────────────────────────────────────────

    [HttpPost("address/notice")]
    [Authorize]
    [EnableRateLimiting("address-verify")]
    public async Task<ActionResult<AddressNoticeResultDto>> ConfirmNotice([FromBody] SubmitAddressNoticeDto dto)
    {
        if (!dto.Confirmed) return BadRequest("Подтверждение обязательно.");

        var text = legalProvider.Current?.GetText(LegalTextKey.PublicAddressNotice);
        if (text is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");
        if (dto.TextVersion != text.Version) return Conflict("Текст был обновлён ещё раз — перечитайте и подтвердите заново.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var subject = ConsentSubject.ForUser(userId);
        var record = await ledger.GrantAsync(new ConsentGrant(
            subject, LegalTextKey.PublicAddressNotice, text.Version, text.ContentHash,
            Purpose: null, ConsentAct.Acknowledged, ConsentSource.AddressForm,
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), UserAgent: Request.Headers.UserAgent.ToString()),
            HttpContext.RequestAborted);

        return Ok(new AddressNoticeResultDto(record.DocumentVersion, record.GrantedAtUtc));
    }

    // ── Shared plumbing ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Same rule as <c>CompaniesController.CanManageCompany</c> (owner or SuperAdmin) —
    /// deliberately not a shared method: every controller in this codebase keeps its own private
    /// wrapper around <see cref="CompanyMembership"/> (project convention, see that class's own doc
    /// comment), only the membership predicate itself is shared.</summary>
    // TD-11 (ARCHITECTURE_CYCLE16.md §254): delegates to the single shared implementation.
    // superAdminBypass stays true — this controller's existing behavior.
    private Task<bool> CanManageCompanyAsync(Guid companyId) =>
        CompanyAccess.CanManageCompanyAsync(db, User, companyId);

    /// <summary>Builds the exact same "full CompanyDto, as PUT /api/companies/{id} would" shape §413.2
    /// promises — same four resolution calls (plan, review aggregate, usage, cover) Update/UploadLogo
    /// make in <c>CompaniesController</c>, feeding the SAME <see cref="CompaniesController.MapToDto"/>
    /// so the two endpoints can never quietly return differently-shaped companies.</summary>
    private async Task<CompanyDto> BuildCompanyDtoAsync(Company company)
    {
        var plan = await subscriptionResolver.GetEffectivePlanAsync(company.Id);
        var city = company.CityId.HasValue ? await db.Cities.FindAsync(company.CityId.Value) : null;

        var aggregate = await db.Reviews
            .Where(r => r.CompanyId == company.Id)
            .GroupBy(r => 1)
            .Select(g => new { Count = g.Count(), Average = g.Average(r => (double)r.Rating) })
            .FirstOrDefaultAsync();
        var (averageRating, reviewCount) = aggregate is null ? ((double?)null, 0) : (aggregate.Average, aggregate.Count);

        var employeeCounts = await accountUsageReader.GetCompanySeatsAsync([company.Id]);
        var usageByAccount = company.BillingAccountId.HasValue
            ? await accountUsageReader.GetAsync([company.BillingAccountId.Value])
            : new Dictionary<Guid, AccountUsage>();

        var coverPhoto = await db.CompanyPhotos.Where(p => p.CompanyId == company.Id && p.Position == 0).ToListAsync();
        var cover = CompanyPhotoOrdering.SelectCovers(coverPhoto).GetValueOrDefault(company.Id);

        // §413.2: reached only after SaveAddress's own CanManageCompanyAsync check passed, so this
        // caller always manages the company (review finding, cycle 13 review, blocking #2 — still true).
        return CompaniesController.MapToDto(
            company, plan, averageRating, reviewCount, city,
            employeeCounts.GetValueOrDefault(company.Id),
            company.BillingAccountId.HasValue ? usageByAccount.GetValueOrDefault(company.BillingAccountId.Value) : null,
            cover is null ? null : (cover.Url, cover.ThumbnailUrl),
            canManage: true);
    }
}
