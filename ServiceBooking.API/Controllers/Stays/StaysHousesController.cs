using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace ServiceBooking.API.Controllers.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.2.3, §37.6, API_CONTRACT_CYCLE37.md §37.28 — houses in the cabinet: card, photos, prices of two modes, registry,
/// the owner's attestation and publication (ЮР-2: a house WITHOUT a registry number is published, any kind, under the attestation), archive, QR.
/// Rights: <see cref="StaysPermission"/> (ManageHouses = owner; EditHouseContent = owner + manager).
/// </summary>
[ApiController]
[Route("api/stays/companies/{companyId:guid}/houses")]
[Authorize]
public class StaysHousesController(
    AppDbContext db, StaysAccessResolver access, HouseService houses, StaysPlanResolver plans, ImageUploadService imageUploadService,
    FileStorage storage, ServiceBooking.API.Services.PublicSites.PublicSiteLinks links, IStaysClock clock, IOptions<StaysOptions> options,
    StaysCatalogService catalog) : ControllerBase
{
    private static readonly Regex RegistryNumberPattern = new(@"^[0-9A-Za-zА-Яа-яЁё\-]{5,32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // ── list / create / order / get / delete ──

    [HttpGet]
    public async Task<ActionResult<List<HouseListItemDto>>> List(Guid companyId, CancellationToken ct)
    {
        var r = await access.ResolveAnyAsync(companyId, User, [StaysPermission.ViewCabinet, StaysPermission.EditHouseContent], asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        return Ok(await houses.ListAsync(r.Company!, ct));
    }

    [HttpPost]
    [RequiresOwnerTerms]
    public async Task<ActionResult<HouseManageDto>> Create(Guid companyId, HouseCreateInput input, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageHouses, ct: ct);
        if (!r.Ok) return r.Error!;
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 100) return BadRequest("Укажите название дома");
        if (input.Capacity is < 1 or > 50) return BadRequest("Вместимость — от 1 до 50");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"stay-houses:{companyId}");
        var slug = await houses.SuggestSlugAsync(companyId, name, ct);
        var position = (await db.Houses.Where(h => h.CompanyId == companyId).MaxAsync(h => (int?)h.Position, ct) ?? -1) + 1;
        var now = clock.UtcNow;
        var house = new House
        {
            Id = Guid.NewGuid(), CompanyId = companyId, Slug = slug, Name = name, Capacity = input.Capacity,
            PriceMode = HousePriceMode.Constant, Position = position, CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        db.Houses.Add(house);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return StatusCode(StatusCodes.Status201Created, await houses.BuildManageAsync(r.Company!, house, ct));
    }

    [HttpPut("order")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<List<HouseListItemDto>>> Reorder(Guid companyId, IdsOrderInput input, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageHouses, ct: ct);
        if (!r.Ok) return r.Error!;
        var list = await db.Houses.Where(h => h.CompanyId == companyId && h.ArchivedAtUtc == null).ToListAsync(ct);
        var ids = input.Ids ?? [];
        if (ids.Count != list.Count || ids.Distinct().Count() != ids.Count || ids.Any(i => list.All(h => h.Id != i)))
            return BadRequest("Передайте полный список домов без архивных");
        for (var i = 0; i < ids.Count; i++) list.First(h => h.Id == ids[i]).Position = i;
        await db.SaveChangesAsync(ct);
        return Ok(await houses.ListAsync(r.Company!, ct));
    }

    [HttpGet("{houseId:guid}")]
    public async Task<ActionResult<HouseManageDto>> Get(Guid companyId, Guid houseId, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.EditHouseContent, ct, track: false);
        if (house is null) return r.Error!;
        return Ok(await houses.BuildManageAsync(r.Company!, house, ct));
    }

    [HttpDelete("{houseId:guid}")]
    [RequiresOwnerTerms]
    public async Task<IActionResult> Delete(Guid companyId, Guid houseId, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.ManageHouses, ct);
        if (house is null) return r.Error!;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"stay-house:{houseId}");
        if (await db.StayBookings.AnyAsync(b => b.HouseId == houseId, ct))
            return Conflict(new StaysConflictDto("HouseHasBookings", "У дома есть брони — его можно только архивировать"));
        // The attestations of the owner are evidence and are never deleted (ЮР-2): a house that was published once can only be archived.
        if (await db.HouseRegistryAttestations.AnyAsync(a => a.HouseId == houseId, ct))
            return Conflict(new StaysConflictDto("HouseHasBookings", "Дом уже публиковался — его можно только архивировать"));

        var photoUrls = await db.HousePhotos.Where(p => p.HouseId == houseId).Select(p => new { p.Url, p.ThumbnailUrl }).ToListAsync(ct);
        await db.HouseOccupancies.Where(o => o.HouseId == houseId).ExecuteDeleteAsync(ct);
        var blockIds = db.HouseBlocks.Where(b => b.HouseId == houseId).Select(b => b.Id);
        await db.HouseBlockEvents.Where(e => blockIds.Contains(e.HouseBlockId)).ExecuteDeleteAsync(ct);
        await db.HouseBlocks.Where(b => b.HouseId == houseId).ExecuteDeleteAsync(ct);
        db.Houses.Remove(house);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        foreach (var p in photoUrls) { storage.DeletePublic(p.Url); storage.DeletePublic(p.ThumbnailUrl); }
        return NoContent();
    }

    // ── card ──

    [HttpPut("{houseId:guid}/setup")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<HouseManageDto>> Setup(Guid companyId, Guid houseId, HouseSetupInput input, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.ManageHouses, ct);
        if (house is null) return r.Error!;
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 100) return BadRequest("Укажите название дома");
        if (input.Capacity is < 1 or > 50) return BadRequest("Вместимость — от 1 до 50");
        var slug = StaysSlugPolicy.Normalize(input.Slug);
        if (!StaysSlugPolicy.IsValidHouseSlug(slug)) return BadRequest("Адрес дома — латиница, цифры и дефис, 2–50 символов");
        if (input.ExtraBedsEnabled && input.ExtraBedsMax is < 1 or > 10) return BadRequest("Доп. мест — от 1 до 10");
        if (input.ExtraBedPriceRub is < 0 or > 100_000) return BadRequest("Сумма — от 0 до 100 000 ₽");

        if (StaysSlugPolicy.IsReservedHouseSlug(slug)) return Conflict(new StaysConflictDto("SlugReserved", "Этот адрес зарезервирован — выберите другой"));
        if (slug != house.Slug)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await AdvisoryLock.AcquireAsync(db, $"stay-houses:{companyId}");
            if (await db.Houses.AnyAsync(h => h.CompanyId == companyId && h.Slug == slug && h.Id != houseId, ct))
                return Conflict(new StaysConflictDto("SlugTaken", "Адрес уже занят — выберите другой"));
            Apply(house, name, slug, input);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        else
        {
            Apply(house, name, slug, input);
            await db.SaveChangesAsync(ct);
        }
        return Ok(await houses.BuildManageAsync(r.Company!, house, ct));
    }

    private void Apply(House house, string name, string slug, HouseSetupInput input)
    {
        house.Name = name;
        house.Slug = slug;
        house.Capacity = input.Capacity;
        house.ExtraBedsEnabled = input.ExtraBedsEnabled;
        house.ExtraBedsMax = input.ExtraBedsEnabled ? input.ExtraBedsMax : 0;
        house.ExtraBedPriceRub = input.ExtraBedsEnabled ? input.ExtraBedPriceRub : 0;
        house.DogsForbidden = input.DogsForbidden;
        house.HasCot = input.HasCot;
        house.UpdatedAtUtc = clock.UtcNow;
    }

    [HttpPut("{houseId:guid}/content")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<HouseManageDto>> Content(Guid companyId, Guid houseId, HouseContentInput input, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.EditHouseContent, ct);
        if (house is null) return r.Error!;
        if (input.Description is { Length: > 4000 }) return BadRequest("Описание — не длиннее 4000 символов");
        if (input.Address is { Length: > 500 }) return BadRequest("Адрес — не длиннее 500 символов");
        if (input.CheckInInfoText is { Length: > 2000 }) return BadRequest("Текст к заселению — не длиннее 2000 символов");

        var amenities = new List<HouseAmenity>();
        foreach (var code in input.Amenities ?? [])
        {
            if (!Enum.TryParse<HouseAmenity>(code, ignoreCase: false, out var a) || !Enum.IsDefined(a)) return BadRequest("Неизвестное удобство");
            amenities.Add(a);
        }

        string? yandex = null, twoGis = null;
        if (!string.IsNullOrWhiteSpace(input.YandexMapsUrl) &&
            !MapLinkValidation.TryNormalize(input.YandexMapsUrl, MapLinkService.Yandex, out yandex, out var yandexError)) return BadRequest(yandexError);
        if (!string.IsNullOrWhiteSpace(input.TwoGisUrl) &&
            !MapLinkValidation.TryNormalize(input.TwoGisUrl, MapLinkService.TwoGis, out twoGis, out var twoGisError)) return BadRequest(twoGisError);

        house.Description = StaysSettingsRules.Trim(input.Description);
        house.AmenitiesMask = HouseService.ToMask(amenities);
        house.Address = StaysSettingsRules.Trim(input.Address);
        house.YandexMapsUrl = yandex;
        house.TwoGisUrl = twoGis;
        house.CheckInInfoText = StaysSettingsRules.Trim(input.CheckInInfoText);
        house.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(await houses.BuildManageAsync(r.Company!, house, ct));
    }

    // ── prices ──

    [HttpPut("{houseId:guid}/pricing")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<HouseManageDto>> Pricing(Guid companyId, Guid houseId, HousePricingInput input, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.ManageHouses, ct);
        if (house is null) return r.Error!;
        if (!Enum.IsDefined(input.Mode)) return BadRequest("Неизвестный режим цены");
        if (input.ConstantPriceRub is { } c && (c < 1 || c > HouseService.MaxPriceRub)) return BadRequest("Цена — от 1 до 1 000 000 ₽");

        // The constant price is KEPT when switching to ByDates (not erased), and only replaced when a new one is sent.
        if (input.ConstantPriceRub is { } price) house.ConstantPriceRub = price;
        house.PriceMode = input.Mode;
        if (house.IsPublished)
        {
            var periods = await db.HousePricePeriods.AsNoTracking().Where(p => p.HouseId == houseId).ToListAsync(ct);
            if (!HouseService.HasPrice(house, periods, houses.TodayOf(r.Company!)))
                return Conflict(new StaysConflictDto("NoPrice", HousePublishRules.Text(HousePublishProblem.NoPrice)));
        }
        house.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(await houses.BuildManageAsync(r.Company!, house, ct));
    }

    [HttpGet("{houseId:guid}/price-periods")]
    public async Task<ActionResult<List<PricePeriodDto>>> ListPeriods(Guid companyId, Guid houseId, [FromQuery] bool includePast, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.ManageHouses, ct, track: false);
        if (house is null) return r.Error!;
        var today = houses.TodayOf(r.Company!);
        var q = db.HousePricePeriods.AsNoTracking().Where(p => p.HouseId == houseId);
        if (!includePast) q = q.Where(p => p.EndDate >= today);
        return Ok((await q.OrderBy(p => p.StartDate).ThenBy(p => p.EndDate).ToListAsync(ct)).Select(HouseService.ToDto).ToList());
    }

    [HttpPost("{houseId:guid}/price-periods")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<PricePeriodDto>> CreatePeriod(Guid companyId, Guid houseId, PricePeriodInput input, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.ManageHouses, ct);
        if (house is null) return r.Error!;
        var error = HousePricePeriodRules.Validate(input.StartDate, input.EndDate, input.PriceRub);
        if (error is not null) return BadRequest(error);

        var period = new HousePricePeriod
        {
            Id = Guid.NewGuid(), HouseId = houseId, StartDate = input.StartDate!.Value, EndDate = input.EndDate!.Value, PriceRub = input.PriceRub,
            CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        var conflict = await SavePeriodAsync(houseId, period, isNew: true, ct);
        if (conflict is not null) return conflict;
        return StatusCode(StatusCodes.Status201Created, HouseService.ToDto(period));
    }

    [HttpPut("{houseId:guid}/price-periods/{periodId:guid}")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<PricePeriodDto>> UpdatePeriod(Guid companyId, Guid houseId, Guid periodId, PricePeriodInput input, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.ManageHouses, ct);
        if (house is null) return r.Error!;
        var period = await db.HousePricePeriods.FirstOrDefaultAsync(p => p.Id == periodId && p.HouseId == houseId, ct);
        if (period is null) return NotFound();
        var error = HousePricePeriodRules.Validate(input.StartDate, input.EndDate, input.PriceRub);
        if (error is not null) return BadRequest(error);

        period.StartDate = input.StartDate!.Value;
        period.EndDate = input.EndDate!.Value;
        period.PriceRub = input.PriceRub;
        period.UpdatedAtUtc = clock.UtcNow;
        var conflict = await SavePeriodAsync(houseId, period, isNew: false, ct);
        if (conflict is not null) return conflict;
        return Ok(HouseService.ToDto(period));
    }

    [HttpDelete("{houseId:guid}/price-periods/{periodId:guid}")]
    [RequiresOwnerTerms]
    public async Task<IActionResult> DeletePeriod(Guid companyId, Guid houseId, Guid periodId, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.ManageHouses, ct);
        if (house is null) return r.Error!;
        var period = await db.HousePricePeriods.FirstOrDefaultAsync(p => p.Id == periodId && p.HouseId == houseId, ct);
        if (period is null) return NotFound();
        db.HousePricePeriods.Remove(period);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Checks overlap under the house lock, saves; the database constraint is the net for a race. Returns the 409 or null.</summary>
    private async Task<ActionResult?> SavePeriodAsync(Guid houseId, HousePricePeriod period, bool isNew, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"stay-house-prices:{houseId}");
        var others = await db.HousePricePeriods.AsNoTracking().Where(p => p.HouseId == houseId).ToListAsync(ct);
        var conflict = HousePricePeriodRules.FindConflict(others, period.StartDate, period.EndDate, isNew ? null : period.Id);
        if (conflict is not null)
        {
            db.ChangeTracker.Clear();
            return Conflict(new StaysConflictDto("PricePeriodOverlap", HousePricePeriodRules.OverlapText(conflict), HouseService.ToDto(conflict)));
        }
        if (isNew) db.HousePricePeriods.Add(period);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23P01" or "23505" })
        {
            db.ChangeTracker.Clear();
            var now = await db.HousePricePeriods.AsNoTracking().Where(p => p.HouseId == houseId).ToListAsync(ct);
            var c = HousePricePeriodRules.FindConflict(now, period.StartDate, period.EndDate, isNew ? null : period.Id);
            return Conflict(c is null
                ? new StaysConflictDto("PricePeriodOverlap", "Период пересекается с другим периодом")
                : new StaysConflictDto("PricePeriodOverlap", HousePricePeriodRules.OverlapText(c), HouseService.ToDto(c)));
        }
        return null;
    }

    // ── registry, attestation, publication ──

    [HttpPut("{houseId:guid}/registry")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<HouseManageDto>> Registry(Guid companyId, Guid houseId, HouseRegistryInput input, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.ManageHouses, ct);
        if (house is null) return r.Error!;
        if (input.ObjectKind is not { } kind || !Enum.IsDefined(kind)) return BadRequest("Укажите вид объекта");
        var number = StaysSettingsRules.Trim(input.RegistryNumber);
        if (number is not null && !RegistryNumberPattern.IsMatch(number)) return BadRequest("Номер в реестре — 5–32 символа: буквы, цифры, дефис");
        var url = StaysSettingsRules.Trim(input.RegistryUrl);
        if (url is not null && (url.Length > 500 || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(url, UriKind.Absolute, out _)))
            return BadRequest("Ссылка на запись в реестре должна начинаться с https://");

        var changed = house.ObjectKind != kind || house.RegistryNumber != number || house.RegistryUrl != url;
        if (house.IsPublished && changed)
        {
            // Invariant 7 (§37.2.8): a published house always has an attestation for what it shows now.
            if (input.Attestation is not { Accepted: true } att || att.NoticeVersion != StaysTexts.RegistryNoticeVersion())
                return Conflict(new StaysConflictDto("AttestationRequired", HousePublishRules.Text(HousePublishProblem.AttestationRequired)));
            house.ObjectKind = kind; house.RegistryNumber = number; house.RegistryUrl = url;
            AddAttestation(house, att.NoticeVersion!);
        }
        else
        {
            house.ObjectKind = kind; house.RegistryNumber = number; house.RegistryUrl = url;
        }
        house.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(await houses.BuildManageAsync(r.Company!, house, ct));
    }

    [HttpPost("{houseId:guid}/publish")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<HouseManageDto>> Publish(Guid companyId, Guid houseId, HousePublishInput input, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.ManageHouses, ct);
        if (house is null) return r.Error!;
        var company = r.Company!;
        if (house.IsPublished) return Ok(await houses.BuildManageAsync(company, house, ct));

        var periods = await db.HousePricePeriods.AsNoTracking().Where(p => p.HouseId == houseId).ToListAsync(ct);
        var attested = input.Attestation is { Accepted: true } a && a.NoticeVersion == StaysTexts.RegistryNoticeVersion();
        var problem = HousePublishRules.Check(house.ArchivedAtUtc != null, HouseService.HasPrice(house, periods, houses.TodayOf(company)), house.ObjectKind, attested);
        if (problem is { } p) return Conflict(new StaysConflictDto(p.ToString(), HousePublishRules.Text(p)));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (company.BillingAccountId is { } accountId) await AdvisoryLock.AcquireAsync(db, $"billing-account:{accountId}");
        var plan = await plans.GetForCompanyAsync(company, clock.UtcNow, ct);
        if (!plan.HasActivePlan) return StatusCode(402, BillingTexts.HouseNoPlan);
        if (plan.MaxHouses is { } max)
        {
            var published = await plans.CountPublishedHousesForCompanyAsync(company, ct);
            if (published >= max) return StatusCode(402, BillingTexts.HouseLimitReached(plan.PlanName!, max));
        }

        house.IsPublished = true;
        house.UpdatedAtUtc = clock.UtcNow;
        AddAttestation(house, input.Attestation!.NoticeVersion!);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        catalog.InvalidateBase();
        return Ok(await houses.BuildManageAsync(company, house, ct));
    }

    [HttpPost("{houseId:guid}/unpublish")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<HouseManageDto>> Unpublish(Guid companyId, Guid houseId, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.ManageHouses, ct);
        if (house is null) return r.Error!;
        house.IsPublished = false;
        house.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        catalog.InvalidateBase();
        return Ok(await houses.BuildManageAsync(r.Company!, house, ct));
    }

    [HttpPost("{houseId:guid}/archive")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<HouseManageDto>> Archive(Guid companyId, Guid houseId, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.ManageHouses, ct);
        if (house is null) return r.Error!;
        house.IsPublished = false;
        house.ArchivedAtUtc ??= clock.UtcNow;
        house.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        catalog.InvalidateBase();
        return Ok(await houses.BuildManageAsync(r.Company!, house, ct));
    }

    private void AddAttestation(House house, string noticeVersion) => db.HouseRegistryAttestations.Add(new HouseRegistryAttestation
    {
        Id = Guid.NewGuid(), HouseId = house.Id, CompanyId = house.CompanyId, ObjectKind = house.ObjectKind!.Value,
        RegistryNumber = house.RegistryNumber, RegistryUrl = house.RegistryUrl, NoticeVersion = noticeVersion,
        AttestedAtUtc = clock.UtcNow, AttestedByUserId = UserId, IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
    });

    // ── photos ──

    [HttpPost("{houseId:guid}/photos")]
    [EnableRateLimiting("company-photos")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<HousePhotoDto>> UploadPhoto(Guid companyId, Guid houseId, IFormFile? file, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.EditHouseContent, ct, track: false);
        if (house is null) return r.Error!;
        var validation = await imageUploadService.ReadAndProcessAsync(file, [ImageProfile.CompanyPhoto, ImageProfile.CompanyPhotoThumb]);
        if (!validation.Success) return BadRequest(validation.ErrorMessage);
        var full = validation.Images![0];
        var thumb = validation.Images![1];

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"house-photos:{houseId}");
        var count = await db.HousePhotos.CountAsync(p => p.HouseId == houseId, ct);
        var maxPhotos = options.Value.MaxPhotosPerHouse;
        if (count >= maxPhotos)
            return Conflict(new StaysConflictDto("PhotoLimitReached", $"У дома может быть не больше {maxPhotos} фото"));

        var url = await storage.SavePublicAsync(PublicArea.Houses, full.Bytes, full.Extension);
        var thumbUrl = await storage.SavePublicAsync(PublicArea.Houses, thumb.Bytes, thumb.Extension);
        var photo = new HousePhoto { Id = Guid.NewGuid(), HouseId = houseId, CompanyId = companyId, Url = url, ThumbnailUrl = thumbUrl, Position = count, CreatedAtUtc = clock.UtcNow };
        db.HousePhotos.Add(photo);
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            storage.DeletePublic(url);
            storage.DeletePublic(thumbUrl);
            throw;
        }
        return StatusCode(StatusCodes.Status201Created, new HousePhotoDto(photo.Id, photo.Url, photo.ThumbnailUrl, photo.Position));
    }

    [HttpPut("{houseId:guid}/photos/order")]
    [EnableRateLimiting("company-photos-edit")]
    public async Task<ActionResult<List<HousePhotoDto>>> ReorderPhotos(Guid companyId, Guid houseId, IdsOrderInput input, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.EditHouseContent, ct, track: false);
        if (house is null) return r.Error!;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"house-photos:{houseId}");
        var photos = await db.HousePhotos.Where(p => p.HouseId == houseId).ToListAsync(ct);
        var ids = input.Ids ?? [];
        if (ids.Count != photos.Count || ids.Distinct().Count() != ids.Count || ids.Any(i => photos.All(p => p.Id != i)))
            return BadRequest("Передайте полный список фото дома");
        for (var i = 0; i < ids.Count; i++) photos.First(p => p.Id == ids[i]).Position = i;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(photos.OrderBy(p => p.Position).Select(p => new HousePhotoDto(p.Id, p.Url, p.ThumbnailUrl, p.Position)).ToList());
    }

    [HttpDelete("{houseId:guid}/photos/{photoId:guid}")]
    [EnableRateLimiting("company-photos-edit")]
    public async Task<IActionResult> DeletePhoto(Guid companyId, Guid houseId, Guid photoId, CancellationToken ct)
    {
        var (r, house) = await LoadAsync(companyId, houseId, StaysPermission.EditHouseContent, ct, track: false);
        if (house is null) return r.Error!;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"house-photos:{houseId}");
        var photos = await db.HousePhotos.Where(p => p.HouseId == houseId).OrderBy(p => p.Position).ToListAsync(ct);
        var photo = photos.FirstOrDefault(p => p.Id == photoId);
        if (photo is null) return NotFound();
        db.HousePhotos.Remove(photo);
        var position = 0;
        foreach (var p in photos.Where(p => p.Id != photoId)) p.Position = position++;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        storage.DeletePublic(photo.Url);
        storage.DeletePublic(photo.ThumbnailUrl);
        return NoContent();
    }

    [HttpGet("{houseId:guid}/qr")]
    public async Task<IActionResult> Qr(Guid companyId, Guid houseId, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ViewCabinet, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        var house = await db.Houses.AsNoTracking().FirstOrDefaultAsync(h => h.Id == houseId && h.CompanyId == companyId, ct);
        if (house is null) return NotFound();
        return File(ShopQrCode.EncodePng(links.HousePageUrl(r.Company!.Slug, house.Slug)), "image/png", $"{r.Company.Slug}-{house.Slug}-qr.png");
    }

    // ── helpers ──

    private async Task<(StaysAccessResult Access, House? House)> LoadAsync(
        Guid companyId, Guid houseId, StaysPermission permission, CancellationToken ct, bool track = true)
    {
        var r = await access.ResolveAsync(companyId, User, permission, asNoTracking: true, ct: ct);
        if (!r.Ok) return (r, null);
        var query = track ? db.Houses : db.Houses.AsNoTracking();
        var house = await query.FirstOrDefaultAsync(h => h.Id == houseId && h.CompanyId == companyId, ct);
        return house is null ? (new StaysAccessResult(new NotFoundResult()), null) : (r, house);
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
