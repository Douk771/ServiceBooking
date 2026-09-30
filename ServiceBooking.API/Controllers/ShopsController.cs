using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Catalog;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §389–§392, §409 — the shop itself: creation, the cabinet list, the address, settings, seller details,
/// the QR code. A shop is a <c>Company</c> with <c>Kind = Orders</c>; its profile, logo, address verification and staff use the
/// existing common company routes. Order of checks everywhere: token → the company exists and is a shop (404) → the role (403).
/// </summary>
[ApiController]
[Route("api/shops")]
[Authorize]
public class ShopsController(
    AppDbContext db, CompanyCreationService companyCreation, ShopAccessResolver access, ShopManageMapper manageMapper,
    PublicSiteLinks links, PhoneVerificationAvailability phoneVerification, ServiceBooking.API.Services.Billing.OrdersPlanResolver ordersPlans) : ControllerBase
{
    /// <summary>§409.1 — POST /api/shops. The same checks as POST /api/companies, plus the shop address policy.</summary>
    [HttpPost]
    [RequiresOwnerTerms]
    public async Task<ActionResult<CreateShopResponse>> Create(CreateShopInput input)
    {
        var outcome = await companyCreation.CreateAsync(
            CompanyKind.Orders,
            new CompanyCreationRequest(input.Name, input.Slug, input.Description, input.Address, input.Phone, input.Email,
                input.CityId, input.TimeZoneId, AllowSelfBooking: false, ShowInPublicListing: false, input.OwnerTerms?.Version),
            User, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
        if (outcome.Error is not null) return outcome.Error;

        var shop = outcome.Company!;
        var dto = await manageMapper.BuildAsync(shop, ShopRole.Owner, HttpContext.RequestAborted);
        return StatusCode(StatusCodes.Status201Created, new CreateShopResponse(dto, outcome.Token!));
    }

    /// <summary>§409.2 — shops where the caller is the owner or a staff member, blocked ones included.</summary>
    [HttpGet("my")]
    public async Task<ActionResult<List<ShopListItemDto>>> GetMine(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var rows = await db.CompanyMembers.AsNoTracking().Where(CompanyMembership.IsStaffRole)
            .Where(cm => cm.UserId == userId && cm.Company.Kind == CompanyKind.Orders)
            .OrderBy(cm => cm.Company.Name).ThenBy(cm => cm.CompanyId)
            .Select(cm => new { cm.Company, cm.Role })
            .ToListAsync(ct);
        return Ok(rows.Select(r => new ShopListItemDto(
            r.Company.Id, r.Company.Name, r.Company.Slug, r.Company.LogoUrl, r.Company.IsActive,
            links.CompanyPageUrl(r.Company), r.Role == UserRole.CompanyOwner ? ShopRole.Owner : ShopRole.Staff)).ToList());
    }

    /// <summary>§409.3 — check an address, or suggest a free one made from the name.</summary>
    [HttpGet("slug-check")]
    public async Task<ActionResult<SlugCheckDto>> CheckSlug([FromQuery] string? slug, [FromQuery] string? name, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var normalized = SlugPolicy.Normalize(slug);
            var refusal = await companyCreation.ShopSlugRefusalAsync(normalized, exceptCompanyId: null);
            return Ok(new SlugCheckDto(normalized, refusal is null, refusal?.Message, refusal?.Code));
        }
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(ShopTexts.SlugOrNameRequired);

        var baseSlug = SlugTransliterator.ToBase(name, SlugPolicy.MaxLength, SlugPolicy.MinLength, SlugPolicy.ReservedSlugs);
        var candidates = SlugTransliterator.Candidates(baseSlug, () => Convert.ToHexString(RandomNumberGenerator.GetBytes(2)).ToLowerInvariant()).ToList();
        // One query for every candidate, not one per attempt.
        var taken = (await db.Companies.AsNoTracking()
            .Where(c => candidates.Contains(c.Slug.ToLower())).Select(c => c.Slug.ToLower()).ToListAsync(ct)).ToHashSet();
        var free = candidates.FirstOrDefault(c => !taken.Contains(c)) ?? candidates[^1];
        return Ok(new SlugCheckDto(free, true, null, null));
    }

    /// <summary>§409.4 — the shop for the cabinet (owner and staff).</summary>
    [HttpGet("{shopId:guid}")]
    public async Task<ActionResult<ShopManageDto>> Get(Guid shopId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ViewShop, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        return Ok(await manageMapper.BuildAsync(result.Shop!, result.Role, ct));
    }

    /// <summary>§409.5 — acceptance rules (owner). Full replacement; effective for NEW orders only (snapshots on the order).</summary>
    [HttpPut("{shopId:guid}/settings")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ShopManageDto>> UpdateSettings(Guid shopId, ShopSettingsInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.EditSettings, ct: ct);
        if (!result.Ok) return result.Error!;

        var settings = await db.ShopSettings.FirstOrDefaultAsync(s => s.CompanyId == shopId, ct);
        if (settings is null)
        {
            settings = new ShopSettings { CompanyId = shopId };
            db.ShopSettings.Add(settings);
        }

        // The strict mode cannot be switched ON while the MAX verification is off — nobody could pass it (R-5). Keeping an
        // already-set mode is allowed, so the owner can still change the other rules on such a shop.
        if (input.CustomerMode == ShopCustomerMode.VerifiedPhoneOnly && settings.CustomerMode != ShopCustomerMode.VerifiedPhoneOnly &&
            !phoneVerification.IsAvailable)
            return Conflict(new CatalogConflictDto(CatalogConflictCode.PhoneVerificationUnavailable, ShopTexts.PhoneVerificationUnavailable));

        settings.CustomerMode = input.CustomerMode;
        settings.AcceptanceMode = input.AcceptanceMode;
        settings.AllowCustomerCancel = input.AllowCustomerCancel;
        settings.TrackStock = input.TrackStock;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await db.SaveChangesAsync(ct);

        return Ok(await manageMapper.BuildAsync(result.Shop!, result.Role, ct));
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE25.md §505.2, API_CONTRACT_CYCLE25.md §532 — whether the shop is in the goods catalog and why not: the owner's switch, the tariff and the
    /// checklist, from the ONE rule (<see cref="CatalogListingRules"/>) the catalog itself uses. Owner and staff read it.
    /// </summary>
    [HttpGet("{shopId:guid}/catalog-listing")]
    public async Task<ActionResult<CatalogListingDto>> GetCatalogListing(Guid shopId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ViewShop, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        return Ok(await BuildCatalogListingAsync(result.Shop!, ct));
    }

    /// <summary>The owner's switch "Показывать магазин в каталоге goods". Switching ON is refused (409, JSON) when the tariff does not allow it; OFF always works.</summary>
    [HttpPut("{shopId:guid}/catalog-listing")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<CatalogListingDto>> PutCatalogListing(Guid shopId, CatalogListingInputDto input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageShop, ct: ct);
        if (!result.Ok) return result.Error!;
        var shop = result.Shop!;
        if (input?.ShowInCatalog is not { } showInCatalog)
            return BadRequest(new ProblemDetails { Title = "Не указано, показывать ли магазин в каталоге", Status = 400 });

        if (showInCatalog && !(await ordersPlans.GetForCompanyAsync(shop.Id, ct)).AllowPublicListing)
            return Conflict(new CatalogConflictDto(CatalogConflictCode.CatalogListingNotAllowedByPlan, CatalogListingRules.NotAllowedByPlanText));

        shop.ShowInPublicListing = showInCatalog;
        await db.SaveChangesAsync(ct);
        return Ok(await BuildCatalogListingAsync(shop, ct));
    }

    private async Task<CatalogListingDto> BuildCatalogListingAsync(Company shop, CancellationToken ct)
    {
        var hasHours = await db.ShopSettings.AsNoTracking().AnyAsync(s => s.CompanyId == shop.Id && s.WorkingHoursJson != null, ct);
        var hasProduct = await db.Products.AsNoTracking().AnyAsync(p => p.CompanyId == shop.Id && p.IsPublished && p.DeletedAtUtc == null, ct);
        var allowed = (await ordersPlans.GetForCompanyAsync(shop.Id, ct)).AllowPublicListing;
        var verdict = CatalogListingRules.Evaluate(new CatalogListingInput(shop.IsActive, hasHours, hasProduct, allowed, shop.ShowInPublicListing));
        return new CatalogListingDto(
            shop.ShowInPublicListing, allowed, verdict.Visible, CatalogListingRules.StatusText(verdict.Visible),
            allowed ? null : CatalogListingRules.NotAllowedByPlanText,
            verdict.Checklist.Select(c => new CatalogListingCheckDto(c.Code, c.Text, c.Done)).ToList());
    }

    /// <summary>§409.6 — seller details (owner) [legal L2]. Full replacement: an empty or missing field is cleared.</summary>
    [HttpPut("{shopId:guid}/seller")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ShopManageDto>> UpdateSeller(Guid shopId, SellerInfoInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.EditSettings, ct: ct);
        if (!result.Ok) return result.Error!;

        var legalName = Trim(input.LegalName);
        var inn = Trim(input.Inn);
        var ogrn = Trim(input.Ogrn);
        var legalAddress = Trim(input.LegalAddress);
        if (legalName is { Length: > 300 }) return BadRequest("Наименование — не длиннее 300 символов");
        if (ogrn is { Length: > 15 }) return BadRequest("ОГРН — не длиннее 15 цифр");
        if (legalAddress is { Length: > 500 }) return BadRequest("Адрес — не длиннее 500 символов");
        if (inn is not null && !IsInnAcceptable(inn, input.LegalForm)) return BadRequest(ShopTexts.InnInvalid);

        var settings = await db.ShopSettings.FirstOrDefaultAsync(s => s.CompanyId == shopId, ct);
        if (settings is null)
        {
            settings = new ShopSettings { CompanyId = shopId };
            db.ShopSettings.Add(settings);
        }
        settings.SellerLegalForm = input.LegalForm;
        settings.SellerLegalName = legalName;
        settings.SellerInn = inn;
        settings.SellerOgrn = ogrn;
        settings.SellerLegalAddress = legalAddress;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await db.SaveChangesAsync(ct);

        return Ok(await manageMapper.BuildAsync(result.Shop!, result.Role, ct));
    }

    /// <summary>§409.7 — change the shop's address (owner). The old link and printed QR codes stop working: no redirect.</summary>
    [HttpPut("{shopId:guid}/slug")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ShopManageDto>> ChangeSlug(Guid shopId, ShopSlugInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.EditSettings, ct: ct);
        if (!result.Ok) return result.Error!;
        var shop = result.Shop!;

        var slug = SlugPolicy.Normalize(input.Slug);
        if (slug != shop.Slug)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // The same lock a creation takes: two owners claiming one address at once cannot both pass the check.
            await AdvisoryLock.AcquireAsync(db, "company-slug");
            var refusal = await companyCreation.ShopSlugRefusalAsync(slug, exceptCompanyId: shop.Id);
            if (refusal is not null) return Conflict(refusal);

            shop.Slug = slug;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        return Ok(await manageMapper.BuildAsync(shop, result.Role, ct));
    }

    /// <summary>§409.8 — the QR code of the shop's link (owner and staff).</summary>
    [HttpGet("{shopId:guid}/qr")]
    public async Task<IActionResult> GetQr(Guid shopId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ViewShop, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var shop = result.Shop!;
        return File(ShopQrCode.EncodePng(links.CompanyPageUrl(shop)), "image/png", $"{shop.Slug}-qr.png");
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Checksum by InnValidator; when the legal form is given, also the length it implies (a company — 10 digits, IP/self-employed — 12).</summary>
    private static bool IsInnAcceptable(string inn, LegalEntityForm? form)
    {
        if (!InnValidator.IsValid(inn)) return false;
        return form switch
        {
            LegalEntityForm.Company => inn.Length == 10,
            LegalEntityForm.Ip or LegalEntityForm.SelfEmployed => inn.Length == 12,
            _ => true
        };
    }
}
