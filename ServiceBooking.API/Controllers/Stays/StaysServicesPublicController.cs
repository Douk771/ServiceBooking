using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.4, §39.7.1, API_CONTRACT_CYCLE39.md §39.22 — the anonymous side of services: the page, the calendar of dates, the starts, the quote and the order
/// of a service without a stay. None of the answers contains the requisites, the occupied intervals, a reason a time is taken or another guest's data.
/// </summary>
[ApiController]
[Route("api/stays/public")]
public class StaysServicesPublicController(
    AppDbContext db, ServiceCatalogService catalog, ServiceSlotService slots, ServiceOrderCreationService orders, ServiceDtoMapper mapper, StaysCompanyService companyService,
    PublicSiteLinks links) : ControllerBase
{
    [HttpGet("companies/{slug}/services/{serviceSlug}")]
    [EnableRateLimiting("stays-public")]
    public async Task<ActionResult<PublicServiceDto>> Page(string slug, string serviceSlug, CancellationToken ct)
    {
        var page = await catalog.PageAsync(slug, serviceSlug, ct);
        return page is null ? NotFound() : Ok(page);
    }

    [HttpGet("services/{serviceId:guid}/availability")]
    [EnableRateLimiting("stays-public")]
    public async Task<ActionResult<ServiceAvailabilityDto>> Availability(
        Guid serviceId, [FromQuery] string? from, [FromQuery] int? days, [FromQuery] Guid? houseId, [FromQuery] string? checkIn, [FromQuery] string? checkOut, CancellationToken ct)
    {
        var scope = await slots.FindPublicAsync(serviceId, ct);
        if (scope is null) return NotFound();
        var today = slots.TodayOf(scope.Company);
        var start = today;
        if (!string.IsNullOrWhiteSpace(from))
        {
            if (!StaysCatalogService.TryDate(from, out start)) return BadRequest("Неверный период");
        }
        var max = scope.Settings.HorizonDays;
        var count = days ?? 14;
        if (count is < 1 or > 31) return BadRequest("Неверный период");
        var (stay, error) = await StayModeAsync(scope, houseId, checkIn, checkOut, ct);
        if (error is not null) return error;
        if (stay is not null && !scope.Service.AvailableForHouseBookings) return Ok(new ServiceAvailabilityDto(serviceId, today, []));
        _ = max;
        return Ok(new ServiceAvailabilityDto(serviceId, today, await slots.AvailabilityAsync(scope, start, count, staff: false, stay, ct)));
    }

    [HttpGet("services/{serviceId:guid}/starts")]
    [EnableRateLimiting("stays-public")]
    public async Task<ActionResult<ServiceStartsDto>> Starts(
        Guid serviceId, [FromQuery] string? date, [FromQuery] Guid? houseId, [FromQuery] string? checkIn, [FromQuery] string? checkOut, CancellationToken ct)
    {
        if (!StaysCatalogService.TryDate(date, out var d)) return BadRequest(string.IsNullOrWhiteSpace(date) ? "Выберите дату" : "Неверный формат даты");
        var scope = await slots.FindPublicAsync(serviceId, ct);
        if (scope is null) return NotFound();
        var (stay, error) = await StayModeAsync(scope, houseId, checkIn, checkOut, ct);
        if (error is not null) return error;
        return Ok(await slots.StartsAsync(scope, d, staff: false, stay, ct));
    }

    [HttpPost("services/{serviceId:guid}/quote")]
    [EnableRateLimiting("stays-public")]
    public async Task<ActionResult<ServiceQuoteDto>> Quote(Guid serviceId, PublicServiceQuoteInput input, CancellationToken ct)
    {
        var formError = ServiceOrderCreationService.ValidateSelection(input.BusinessDate, input.StartMinute, input.Hours, input.Items, out var selection);
        if (formError is not null) return BadRequest(formError);
        var scope = await slots.FindPublicAsync(serviceId, ct);
        if (scope is null) return NotFound();
        var (stay, error) = await StayModeAsync(scope, input.HouseId, input.CheckIn?.ToString("yyyy-MM-dd"), input.CheckOut?.ToString("yyyy-MM-dd"), ct);
        if (error is not null) return error;
        var prepay = stay is null ? scope.Service.StandalonePrepayPercent : null;
        var gate = await companyService.EvaluateGateAsync(scope.Company, scope.Settings, prepay ?? 0, ct);
        var evaluation = await slots.EvaluateAsync(scope, selection, staff: false, stay, includeExpiredHolds: false, extraOccupied: null, prepay, ct);
        return Ok(ServiceQuoteBuilder.Build(scope, evaluation, prepay, gate, scope.Settings.HoldMinutes));
    }

    [HttpPost("services/{serviceId:guid}/orders")]
    [EnableRateLimiting("stay-service-create")]
    public async Task<ActionResult<CreateServiceOrderResponse>> CreateOrder(Guid serviceId, CreateServiceOrderInput input, CancellationToken ct)
    {
        var result = await orders.CreateAsync(serviceId, input, User, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        if (result.Error is not null) return result.Error;
        var order = result.Order!;
        var body = new CreateServiceOrderResponse(order.PublicToken, links.StayServiceOrderPageUrl(order.PublicToken), await mapper.ToPublicOrderAsync(order, ct));
        return result.Created ? StatusCode(StatusCodes.Status201Created, body) : Ok(body);
    }

    /// <summary>The «to the stay» mode of the booking form: all three of house and both dates, or none (400 «Укажите дом и обе даты проживания»).</summary>
    private async Task<(StayRangeSpec? Stay, ActionResult? Error)> StayModeAsync(ServiceScope scope, Guid? houseId, string? checkIn, string? checkOut, CancellationToken ct)
    {
        if (houseId is null && string.IsNullOrWhiteSpace(checkIn) && string.IsNullOrWhiteSpace(checkOut)) return (null, null);
        if (houseId is null || !StaysCatalogService.TryDate(checkIn, out var ci) || !StaysCatalogService.TryDate(checkOut, out var co) || co <= ci)
            return (null, BadRequest("Укажите дом и обе даты проживания"));
        var ok = await db.Houses.AsNoTracking().AnyAsync(h => h.Id == houseId && h.CompanyId == scope.Company.Id && h.IsPublished && h.ArchivedAtUtc == null, ct);
        if (!ok) return (null, NotFound());
        return (ServiceSlotService.StayRangeOf(scope.Company.TimeZoneId, ci, scope.Settings.CheckInTime, co, scope.Settings.CheckOutTime), null);
    }
}
