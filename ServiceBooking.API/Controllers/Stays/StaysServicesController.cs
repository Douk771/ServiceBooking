using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.2.3, §39.12, API_CONTRACT_CYCLE39.md §39.26, §39.28 — services in the cabinet: card, photos, price rules, positions, publication, the weekly template and the manual
/// dates. Rights: ManageServices = owner; EditServiceContent = owner + manager (description, photos); ManageServiceDates = owner + manager (manual dates). Not a member — 404, no right — 403.
/// </summary>
[ApiController]
[Route("api/stays/companies/{companyId:guid}/services")]
[Authorize]
public class StaysServicesController(
    AppDbContext db, StaysAccessResolver access, ServiceCatalogService catalog, ServiceScheduleWriter schedule, ServiceSlotService slots, ServiceSessionWriter sessionWriter,
    StayBookingEventLog revision, StayActorResolver actors, ImageUploadService imageUploadService, FileStorage storage, IStaysClock clock, IOptions<StaysOptions> options,
    StaysCompanyService companyService) : ControllerBase
{
    // ── list / create / order / get / delete ──

    [HttpGet]
    public async Task<ActionResult<List<ServiceListItemDto>>> List(Guid companyId, CancellationToken ct)
    {
        var r = await access.ResolveAnyAsync(companyId, User, [StaysPermission.EditServiceContent, StaysPermission.ManageServiceDates], asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        return Ok(await catalog.ListAsync(r.Company!, await companyService.LoadSettingsAsync(companyId, ct: ct), ct));
    }

    [HttpPost]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ServiceManageDto>> Create(Guid companyId, ServiceCreateInput input, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageServices, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 100) return BadRequest("Укажите название услуги");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"stay-services:{companyId}");
        var max = options.Value.Services.MaxPerCompany;
        if (await db.StayServices.CountAsync(s => s.CompanyId == companyId, ct) >= max)
            return Conflict(new StaysServiceConflictDto(nameof(StaysServiceConflictCode.ServiceLimitReached), $"У компании может быть не больше {max} услуг"));
        var slug = await SuggestSlugAsync(companyId, name, ct);
        var position = (await db.StayServices.Where(s => s.CompanyId == companyId).MaxAsync(s => (int?)s.Position, ct) ?? -1) + 1;
        var now = clock.UtcNow;
        var service = new StayService
        {
            Id = Guid.NewGuid(), CompanyId = companyId, Slug = slug, Name = name, Position = position,
            CancellationBoundaryHours = options.Value.Services.CancellationBoundaryHours.Default, CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        db.StayServices.Add(service);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return StatusCode(StatusCodes.Status201Created, await catalog.BuildManageAsync(r.Company!, service, await companyService.LoadSettingsAsync(companyId, ct: ct), ct));
    }

    [HttpPut("order")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<List<ServiceListItemDto>>> Reorder(Guid companyId, IdsOrderInput input, CancellationToken ct)
    {
        var r = await access.ResolveAsync(companyId, User, StaysPermission.ManageServices, asNoTracking: true, ct: ct);
        if (!r.Ok) return r.Error!;
        var list = await db.StayServices.Where(s => s.CompanyId == companyId && s.ArchivedAtUtc == null).ToListAsync(ct);
        var ids = input.Ids ?? [];
        if (ids.Count != list.Count || ids.Distinct().Count() != ids.Count || ids.Any(i => list.All(s => s.Id != i))) return BadRequest(ServiceTexts.ListMismatch);
        for (var i = 0; i < ids.Count; i++) list.First(s => s.Id == ids[i]).Position = i;
        await db.SaveChangesAsync(ct);
        return Ok(await catalog.ListAsync(r.Company!, await companyService.LoadSettingsAsync(companyId, ct: ct), ct));
    }

    [HttpGet("{serviceId:guid}")]
    public async Task<ActionResult<ServiceManageDto>> Get(Guid companyId, Guid serviceId, CancellationToken ct)
    {
        var (r, service) = await LoadAnyAsync(companyId, serviceId, [StaysPermission.EditServiceContent, StaysPermission.ManageServiceDates], ct, track: false);
        if (service is null) return r.Error!;
        return Ok(await catalog.BuildManageAsync(r.Company!, service, await companyService.LoadSettingsAsync(companyId, ct: ct), ct));
    }

    [HttpDelete("{serviceId:guid}")]
    [RequiresOwnerTerms]
    public async Task<IActionResult> Delete(Guid companyId, Guid serviceId, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct);
        if (service is null) return r.Error!;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await sessionWriter.LockServiceAsync(serviceId);
        if (await db.StayServiceSessions.AnyAsync(s => s.ServiceId == serviceId, ct) || await db.StayServiceOrders.AnyAsync(o => o.ServiceId == serviceId, ct))
            return Conflict(new StaysServiceConflictDto(nameof(StaysServiceConflictCode.ServiceHasSessions), "У услуги есть сеансы — её можно только архивировать"));
        var photos = await db.StayServicePhotos.Where(p => p.ServiceId == serviceId).Select(p => new { p.Url, p.ThumbnailUrl }).ToListAsync(ct);
        db.StayServices.Remove(service);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        foreach (var p in photos) { storage.DeletePublic(p.Url); storage.DeletePublic(p.ThumbnailUrl); }
        await revision.BumpRevisionAsync(companyId);
        return NoContent();
    }

    // ── the card ──

    [HttpPut("{serviceId:guid}/setup")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ServiceManageDto>> Setup(Guid companyId, Guid serviceId, ServiceSetupInput input, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct);
        if (service is null) return r.Error!;
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 100) return BadRequest("Укажите название услуги");
        var slug = StaysSlugPolicy.Normalize(input.Slug);
        if (!StaysSlugPolicy.IsValidServiceSlug(slug)) return BadRequest("Адрес услуги — латиница, цифры и дефис, 2–50 символов");
        if (input.MinHours is < 1 or > 12) return BadRequest("Минимум часов — от 1 до 12");
        if (input.MaxHours < input.MinHours || input.MaxHours > 12) return BadRequest("Максимум часов — от минимума до 12");
        if (input.StepMinutes is not (30 or 60)) return BadRequest("Шаг старта — 30 или 60 минут");
        if (input.BufferMinutes is < 0 or > 240 || input.BufferMinutes % 15 != 0) return BadRequest("Время на подготовку — от 0 до 240 минут с шагом 15");
        if (input.MinLeadMinutes is < 0 or > 2880 || input.MinLeadMinutes % 30 != 0) return BadRequest("Минимальное время до начала — от 0 до 48 часов с шагом 30 минут");
        if (input.StandalonePrepayPercent is < 1 or > 100) return BadRequest("Предоплата — от 1 до 100 % или без предоплаты");
        if (!Enum.IsDefined(input.CancellationPolicy)) return BadRequest("Неизвестный шаблон отмены");
        var range = options.Value.Services.CancellationBoundaryHours;
        if (input.CancellationBoundaryHours < range.Min || input.CancellationBoundaryHours > range.Max)
            return BadRequest($"Срок для полного возврата — от {range.Min} до {range.Max} часов до начала");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"stay-services:{companyId}");
        if (await db.StayServices.AnyAsync(s => s.CompanyId == companyId && s.Slug == slug && s.Id != serviceId, ct))
            return Conflict(new StaysServiceConflictDto(nameof(StaysServiceConflictCode.SlugTaken), "Такой адрес уже есть у другой услуги"));
        service.Name = name;
        service.Slug = slug;
        service.MinHours = input.MinHours;
        service.MaxHours = input.MaxHours;
        service.StepMinutes = input.StepMinutes;
        service.BufferMinutes = input.BufferMinutes;
        service.ShowBufferToGuests = input.ShowBufferToGuests;
        service.MinLeadMinutes = input.MinLeadMinutes;
        service.StandalonePrepayPercent = input.StandalonePrepayPercent;
        service.CancellationPolicy = input.CancellationPolicy;
        service.CancellationBoundaryHours = input.CancellationBoundaryHours;
        service.AvailableForHouseBookings = input.AvailableForHouseBookings;
        service.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(await catalog.BuildManageAsync(r.Company!, service, await companyService.LoadSettingsAsync(companyId, ct: ct), ct));
    }

    [HttpPut("{serviceId:guid}/content")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ServiceManageDto>> Content(Guid companyId, Guid serviceId, ServiceContentInput input, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.EditServiceContent, ct);
        if (service is null) return r.Error!;
        if (input.Description is { Length: > 2000 }) return BadRequest("Описание — не длиннее 2000 символов");
        service.Description = StaysSettingsRules.Trim(input.Description);
        service.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(await catalog.BuildManageAsync(r.Company!, service, await companyService.LoadSettingsAsync(companyId, ct: ct), ct));
    }

    [HttpPost("{serviceId:guid}/publish")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ServiceManageDto>> Publish(Guid companyId, Guid serviceId, EmptyInput? _, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct);
        if (service is null) return r.Error!;
        var settings = await companyService.LoadSettingsAsync(companyId, ct: ct);
        var problems = await catalog.PublishProblemsAsync(service, r.Company!, settings, ct);
        if (problems.Count > 0) return Conflict(new StaysServiceConflictDto(problems[0].ToString(), ServicePublishRules.Message(problems[0])));
        service.IsPublished = true;
        service.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await revision.BumpRevisionAsync(companyId);
        return Ok(await catalog.BuildManageAsync(r.Company!, service, settings, ct));
    }

    [HttpPost("{serviceId:guid}/unpublish")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ServiceManageDto>> Unpublish(Guid companyId, Guid serviceId, EmptyInput? _, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct);
        if (service is null) return r.Error!;
        service.IsPublished = false;
        service.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await revision.BumpRevisionAsync(companyId);
        return Ok(await catalog.BuildManageAsync(r.Company!, service, await companyService.LoadSettingsAsync(companyId, ct: ct), ct));
    }

    [HttpPost("{serviceId:guid}/archive")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ServiceManageDto>> Archive(Guid companyId, Guid serviceId, EmptyInput? _, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct);
        if (service is null) return r.Error!;
        service.IsPublished = false;
        service.ArchivedAtUtc ??= clock.UtcNow;
        service.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        await revision.BumpRevisionAsync(companyId);
        return Ok(await catalog.BuildManageAsync(r.Company!, service, await companyService.LoadSettingsAsync(companyId, ct: ct), ct));
    }

    // ── photos ──

    [HttpPost("{serviceId:guid}/photos")]
    [EnableRateLimiting("company-photos")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<ServicePhotoDto>> UploadPhoto(Guid companyId, Guid serviceId, IFormFile? file, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.EditServiceContent, ct, track: false);
        if (service is null) return r.Error!;
        var validation = await imageUploadService.ReadAndProcessAsync(file, [ImageProfile.CompanyPhoto, ImageProfile.CompanyPhotoThumb]);
        if (!validation.Success) return BadRequest(validation.ErrorMessage);
        var full = validation.Images![0];
        var thumb = validation.Images![1];

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"service-photos:{serviceId}");
        var count = await db.StayServicePhotos.CountAsync(p => p.ServiceId == serviceId, ct);
        var maxPhotos = options.Value.Services.MaxPhotos;
        if (count >= maxPhotos) return Conflict(new StaysServiceConflictDto(nameof(StaysServiceConflictCode.PhotoLimitReached), $"У услуги может быть не больше {maxPhotos} фото"));
        var url = await storage.SavePublicAsync(PublicArea.StayServices, full.Bytes, full.Extension);
        var thumbUrl = await storage.SavePublicAsync(PublicArea.StayServices, thumb.Bytes, thumb.Extension);
        var photo = new StayServicePhoto { Id = Guid.NewGuid(), ServiceId = serviceId, CompanyId = companyId, Url = url, ThumbnailUrl = thumbUrl, Position = count, CreatedAtUtc = clock.UtcNow };
        db.StayServicePhotos.Add(photo);
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
        return StatusCode(StatusCodes.Status201Created, ServiceCatalogService.PhotoDto(photo));
    }

    [HttpPut("{serviceId:guid}/photos/order")]
    [EnableRateLimiting("company-photos-edit")]
    public async Task<ActionResult<List<ServicePhotoDto>>> ReorderPhotos(Guid companyId, Guid serviceId, IdsOrderInput input, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.EditServiceContent, ct, track: false);
        if (service is null) return r.Error!;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"service-photos:{serviceId}");
        var photos = await db.StayServicePhotos.Where(p => p.ServiceId == serviceId).ToListAsync(ct);
        var ids = input.Ids ?? [];
        if (ids.Count != photos.Count || ids.Distinct().Count() != ids.Count || ids.Any(i => photos.All(p => p.Id != i))) return BadRequest("Передайте полный список фото услуги");
        for (var i = 0; i < ids.Count; i++) photos.First(p => p.Id == ids[i]).Position = i;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(photos.OrderBy(p => p.Position).Select(ServiceCatalogService.PhotoDto).ToList());
    }

    [HttpDelete("{serviceId:guid}/photos/{photoId:guid}")]
    [EnableRateLimiting("company-photos-edit")]
    public async Task<IActionResult> DeletePhoto(Guid companyId, Guid serviceId, Guid photoId, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.EditServiceContent, ct, track: false);
        if (service is null) return r.Error!;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"service-photos:{serviceId}");
        var photos = await db.StayServicePhotos.Where(p => p.ServiceId == serviceId).OrderBy(p => p.Position).ToListAsync(ct);
        var photo = photos.FirstOrDefault(p => p.Id == photoId);
        if (photo is null) return NotFound();
        db.StayServicePhotos.Remove(photo);
        var position = 0;
        foreach (var p in photos.Where(p => p.Id != photoId)) p.Position = position++;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        storage.DeletePublic(photo.Url);
        storage.DeletePublic(photo.ThumbnailUrl);
        return NoContent();
    }

    // ── price rules ──

    [HttpGet("{serviceId:guid}/price-rules")]
    public async Task<ActionResult<PriceRulesDto>> PriceRules(Guid companyId, Guid serviceId, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct, track: false);
        return service is null ? r.Error! : Ok(await catalog.PriceRulesAsync(service, ct));
    }

    [HttpPost("{serviceId:guid}/price-rules")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<PriceRulesDto>> AddPriceRule(Guid companyId, Guid serviceId, PriceRuleInput input, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct, track: false);
        if (service is null) return r.Error!;
        return await WriteRuleAsync(service, null, input, created: true, ct);
    }

    [HttpPut("{serviceId:guid}/price-rules/{ruleId:guid}")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<PriceRulesDto>> UpdatePriceRule(Guid companyId, Guid serviceId, Guid ruleId, PriceRuleInput input, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct, track: false);
        if (service is null) return r.Error!;
        return await WriteRuleAsync(service, ruleId, input, created: false, ct);
    }

    [HttpDelete("{serviceId:guid}/price-rules/{ruleId:guid}")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<PriceRulesDto>> DeletePriceRule(Guid companyId, Guid serviceId, Guid ruleId, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct, track: false);
        if (service is null) return r.Error!;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await sessionWriter.LockServiceAsync(serviceId);
        var rule = await db.StayServicePriceRules.FirstOrDefaultAsync(x => x.Id == ruleId && x.ServiceId == serviceId, ct);
        if (rule is null) return NotFound();
        db.StayServicePriceRules.Remove(rule);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(await catalog.PriceRulesAsync(service, ct));
    }

    private async Task<ActionResult<PriceRulesDto>> WriteRuleAsync(StayService service, Guid? ruleId, PriceRuleInput input, bool created, CancellationToken ct)
    {
        var spec = new PriceRuleSpec(input.DaysMask, input.FromHour, input.ToHour, input.PriceRub);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await sessionWriter.LockServiceAsync(service.Id);
        var existing = await db.StayServicePriceRules.Where(x => x.ServiceId == service.Id).ToListAsync(ct);
        var current = ruleId is null ? null : existing.FirstOrDefault(x => x.Id == ruleId);
        if (ruleId is not null && current is null) return NotFound();
        var check = ServicePriceRules.Validate(spec, existing.Select(x => (x.Id, new PriceRuleSpec(x.DaysMask, x.FromHour, x.ToHour, x.PriceRub))), ruleId, slots.BusinessDayStart);
        if (!check.Ok)
        {
            if (check.Error != PriceRuleError.PriceRuleOverlap) return BadRequest(ServicePriceRules.Message(check.Error!.Value));
            var other = existing.First(x => x.Id == check.ConflictingRuleId);
            var dto = ServiceCatalogService.RuleDto(other);
            return Conflict(new StaysServiceConflictDto(nameof(StaysServiceConflictCode.PriceRuleOverlap), ServicePriceRules.Message(PriceRuleError.PriceRuleOverlap, dto.Label, other.PriceRub), dto));
        }
        var now = clock.UtcNow;
        if (current is null)
        {
            if (existing.Count >= options.Value.Services.MaxPriceRulesPerService)
                return Conflict(new StaysServiceConflictDto(nameof(StaysServiceConflictCode.PriceRuleLimitReached), "У услуги может быть не больше 50 правил цены"));
            db.StayServicePriceRules.Add(new StayServicePriceRule
            {
                Id = Guid.NewGuid(), ServiceId = service.Id, DaysMask = input.DaysMask, FromHour = input.FromHour, ToHour = input.ToHour, PriceRub = input.PriceRub,
                CreatedAtUtc = now, UpdatedAtUtc = now,
            });
        }
        else
        {
            current.DaysMask = input.DaysMask;
            current.FromHour = input.FromHour;
            current.ToHour = input.ToHour;
            current.PriceRub = input.PriceRub;
            current.UpdatedAtUtc = now;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        var body = await catalog.PriceRulesAsync(service, ct);
        return created ? StatusCode(StatusCodes.Status201Created, body) : Ok(body);
    }

    // ── positions ──

    [HttpGet("{serviceId:guid}/items")]
    public async Task<ActionResult<List<ServiceItemDto>>> Items(Guid companyId, Guid serviceId, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct, track: false);
        if (service is null) return r.Error!;
        return Ok((await db.StayServiceItems.AsNoTracking().Where(i => i.ServiceId == serviceId).OrderBy(i => i.Position).ToListAsync(ct)).Select(ItemDto).ToList());
    }

    [HttpPost("{serviceId:guid}/items")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ServiceItemDto>> AddItem(Guid companyId, Guid serviceId, ServiceItemInput input, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct, track: false);
        if (service is null) return r.Error!;
        var error = ItemError(input, out var name);
        if (error is not null) return BadRequest(error);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"service-items:{serviceId}");
        var count = await db.StayServiceItems.CountAsync(i => i.ServiceId == serviceId, ct);
        var max = options.Value.Services.MaxItemsPerService;
        if (count >= max) return Conflict(new StaysServiceConflictDto(nameof(StaysServiceConflictCode.ItemLimitReached), $"У услуги может быть не больше {max} позиций"));
        var position = (await db.StayServiceItems.Where(i => i.ServiceId == serviceId).MaxAsync(i => (int?)i.Position, ct) ?? -1) + 1;
        var item = new StayServiceItem { Id = Guid.NewGuid(), ServiceId = serviceId, Name = name, PriceRub = input.PriceRub, MaxPerSession = input.MaxPerSession, IsActive = input.IsActive, Position = position };
        db.StayServiceItems.Add(item);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return StatusCode(StatusCodes.Status201Created, ItemDto(item));
    }

    [HttpPut("{serviceId:guid}/items/order")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<List<ServiceItemDto>>> ReorderItems(Guid companyId, Guid serviceId, IdsOrderInput input, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct, track: false);
        if (service is null) return r.Error!;
        var items = await db.StayServiceItems.Where(i => i.ServiceId == serviceId).ToListAsync(ct);
        var ids = input.Ids ?? [];
        if (ids.Count != items.Count || ids.Distinct().Count() != ids.Count || ids.Any(i => items.All(x => x.Id != i))) return BadRequest("Передайте полный список позиций услуги");
        for (var i = 0; i < ids.Count; i++) items.First(x => x.Id == ids[i]).Position = i;
        await db.SaveChangesAsync(ct);
        return Ok(items.OrderBy(i => i.Position).Select(ItemDto).ToList());
    }

    [HttpPut("{serviceId:guid}/items/{itemId:guid}")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ServiceItemDto>> UpdateItem(Guid companyId, Guid serviceId, Guid itemId, ServiceItemInput input, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct, track: false);
        if (service is null) return r.Error!;
        var error = ItemError(input, out var name);
        if (error is not null) return BadRequest(error);
        var item = await db.StayServiceItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ServiceId == serviceId, ct);
        if (item is null) return NotFound();
        item.Name = name;
        item.PriceRub = input.PriceRub;
        item.MaxPerSession = input.MaxPerSession;
        item.IsActive = input.IsActive;
        await db.SaveChangesAsync(ct);
        return Ok(ItemDto(item));
    }

    [HttpDelete("{serviceId:guid}/items/{itemId:guid}")]
    [RequiresOwnerTerms]
    public async Task<IActionResult> DeleteItem(Guid companyId, Guid serviceId, Guid itemId, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct, track: false);
        if (service is null) return r.Error!;
        var item = await db.StayServiceItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ServiceId == serviceId, ct);
        if (item is null) return NotFound();
        db.StayServiceItems.Remove(item); // always allowed: a session keeps the snapshot of its positions
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static ServiceItemDto ItemDto(StayServiceItem i) => new(i.Id, i.Name, i.PriceRub, i.MaxPerSession, i.IsActive, i.Position);

    private static string? ItemError(ServiceItemInput input, out string name)
    {
        name = (input.Name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 100) return "Название позиции — от 1 до 100 символов";
        if (input.PriceRub is < 0 or > 100_000) return "Цена — от 0 до 100 000 ₽";
        if (input.MaxPerSession is < 1 or > 50) return "Максимум на сеанс — от 1 до 50";
        return null;
    }

    // ── schedule ──

    [HttpGet("{serviceId:guid}/weekly-schedule")]
    public async Task<ActionResult<WeeklyScheduleDto>> Weekly(Guid companyId, Guid serviceId, CancellationToken ct)
    {
        var (r, service) = await LoadAnyAsync(companyId, serviceId, [StaysPermission.ManageServices, StaysPermission.ManageServiceDates], ct, track: false);
        return service is null ? r.Error! : Ok(await schedule.WeeklyAsync(serviceId, ct));
    }

    [HttpPut("{serviceId:guid}/weekly-schedule")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ScheduleSaveResultDto>> SaveWeekly(Guid companyId, Guid serviceId, WeeklyScheduleInput input, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServices, ct, track: false);
        if (service is null) return r.Error!;
        var result = await schedule.SaveWeeklyAsync(new ServiceScope(service, r.Company!, await companyService.LoadSettingsAsync(companyId, ct: ct)), input, await actors.ResolveStaffAsync(User, ct), ct);
        return result.Error is not null ? BadRequest(result.Error) : Ok(result.Result);
    }

    [HttpGet("{serviceId:guid}/date-overrides")]
    public async Task<ActionResult<ServiceMonthDto>> Month(Guid companyId, Guid serviceId, [FromQuery] string? month, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServiceDates, ct, track: false);
        if (service is null) return r.Error!;
        if (!ServiceScheduleWriter.TryMonth(month, out var first)) return BadRequest("Неверный формат даты");
        return Ok(await schedule.MonthAsync(new ServiceScope(service, r.Company!, await companyService.LoadSettingsAsync(companyId, ct: ct)), first, ct));
    }

    [HttpPut("{serviceId:guid}/date-overrides/{date}")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ScheduleSaveResultDto>> SetOverride(Guid companyId, Guid serviceId, string date, DateOverrideInput input, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServiceDates, ct, track: false);
        if (service is null) return r.Error!;
        if (!StaysCatalogService.TryDate(date, out var d)) return BadRequest("Неверный формат даты");
        var result = await schedule.SetOverrideAsync(new ServiceScope(service, r.Company!, await companyService.LoadSettingsAsync(companyId, ct: ct)), d, input, await actors.ResolveStaffAsync(User, ct), ct);
        return result.Error is not null ? BadRequest(result.Error) : Ok(result.Result);
    }

    [HttpDelete("{serviceId:guid}/date-overrides/{date}")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ScheduleSaveResultDto>> RemoveOverride(Guid companyId, Guid serviceId, string date, CancellationToken ct)
    {
        var (r, service) = await LoadAsync(companyId, serviceId, StaysPermission.ManageServiceDates, ct, track: false);
        if (service is null) return r.Error!;
        if (!StaysCatalogService.TryDate(date, out var d)) return BadRequest("Неверный формат даты");
        var result = await schedule.RemoveOverrideAsync(new ServiceScope(service, r.Company!, await companyService.LoadSettingsAsync(companyId, ct: ct)), d, await actors.ResolveStaffAsync(User, ct), ct);
        return result.Error is not null ? BadRequest(result.Error) : Ok(result.Result);
    }

    // ── helpers ──

    private async Task<(StaysAccessResult Access, StayService? Service)> LoadAsync(Guid companyId, Guid serviceId, StaysPermission permission, CancellationToken ct, bool track = true) =>
        await LoadAnyAsync(companyId, serviceId, [permission], ct, track);

    private async Task<(StaysAccessResult Access, StayService? Service)> LoadAnyAsync(
        Guid companyId, Guid serviceId, IReadOnlyCollection<StaysPermission> anyOf, CancellationToken ct, bool track = true)
    {
        var r = await access.ResolveAnyAsync(companyId, User, anyOf, asNoTracking: true, ct: ct);
        if (!r.Ok) return (r, null);
        var query = track ? db.StayServices : db.StayServices.AsNoTracking();
        var service = await query.FirstOrDefaultAsync(s => s.Id == serviceId && s.CompanyId == companyId, ct);
        return service is null ? (new StaysAccessResult(new NotFoundResult()), null) : (r, service);
    }

    private async Task<string> SuggestSlugAsync(Guid companyId, string name, CancellationToken ct)
    {
        var baseSlug = ServiceBooking.API.Services.Shops.SlugTransliterator.ToBase(name, 50, 2, new HashSet<string>());
        var candidates = ServiceBooking.API.Services.Shops.SlugTransliterator.Candidates(baseSlug, () => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(2)).ToLowerInvariant()).ToList();
        var taken = (await db.StayServices.AsNoTracking().Where(s => s.CompanyId == companyId && candidates.Contains(s.Slug)).Select(s => s.Slug).ToListAsync(ct)).ToHashSet();
        return candidates.FirstOrDefault(c => !taken.Contains(c)) ?? candidates[^1];
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
