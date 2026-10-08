using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServiceBooking.API.DTOs.Stays;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Stays;

namespace ServiceBooking.API.Controllers.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.11, §37.22–§37.24 — the anonymous side of «Дома»: amenities, catalog, company and house pages, the calendar of nights, the quote and the
/// creation of a booking. None of the answers contains the requisites for payment, a guest's data or the reason a night is taken.
/// </summary>
[ApiController]
[Route("api/stays/public")]
public class StaysPublicController(
    StaysCatalogService catalog, StaysHousePageService pages, StayBookingCreationService creation, StayDtoMapper mapper,
    ServiceBooking.Infrastructure.Data.AppDbContext db, ServiceCatalogService serviceCatalog, StaysCompanyService companyService,
    Microsoft.Extensions.Options.IOptions<StaysOptions> options) : ControllerBase
{
    [HttpGet("amenities")]
    [EnableRateLimiting("stays-public")]
    public ActionResult<List<HouseAmenityDto>> Amenities() => Ok(HouseService.AllAmenities());

    [HttpGet("catalog")]
    [EnableRateLimiting("stays-public")]
    public async Task<ActionResult<StayCatalogPage>> Catalog(
        [FromQuery] string? checkIn, [FromQuery] string? checkOut, [FromQuery] int guests = 1, [FromQuery] int? maxPricePerNight = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var (error, result) = await catalog.CatalogAsync(checkIn, checkOut, guests, maxPricePerNight, page, pageSize, ct);
        return error ?? Ok(result);
    }

    [HttpGet("companies/{slug}")]
    [EnableRateLimiting("stays-public")]
    public async Task<ActionResult<PublicStaysCompanyDto>> Company(
        string slug, [FromQuery] string? checkIn, [FromQuery] string? checkOut, [FromQuery] int guests = 1, CancellationToken ct = default)
    {
        var company = await catalog.CompanyPageAsync(slug, checkIn, checkOut, guests, ct);
        if (company is null) return NotFound();
        var entity = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(db.Companies.AsNoTracking(), c => c.Id == company.Id, ct);
        var settings = await companyService.LoadSettingsAsync(entity.Id, ct: ct);
        var gate = await companyService.EvaluateGateAsync(entity, settings, ct);
        return Ok(company with { Services = await serviceCatalog.SummariesAsync(entity, settings, gate, ct), AcceptsServiceOrdersWithoutStay = settings.AcceptServiceOrdersWithoutStay });
    }

    [HttpGet("companies/{slug}/houses/{houseSlug}")]
    [EnableRateLimiting("stays-public")]
    public async Task<ActionResult<PublicHouseDto>> House(string slug, string houseSlug, CancellationToken ct)
    {
        var house = await pages.HouseAsync(slug, houseSlug, ct);
        if (house is null) return NotFound();
        var entity = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(db.Companies.AsNoTracking(), c => c.Slug == slug, ct);
        return Ok(house with { ServicesForStay = await serviceCatalog.ServicesForStayAsync(entity, ct) });
    }

    [HttpGet("houses/{houseId:guid}/calendar")]
    [EnableRateLimiting("stays-public")]
    public async Task<ActionResult<HouseCalendarDto>> Calendar(Guid houseId, [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
    {
        var (error, calendar) = await pages.CalendarAsync(houseId, from, to, ct);
        return error ?? Ok(calendar);
    }

    /// <summary>Always 200; reserves nothing.</summary>
    [HttpPost("houses/{houseId:guid}/quote")]
    [EnableRateLimiting("stays-public")]
    public async Task<ActionResult<StayQuoteDto>> Quote(Guid houseId, StayQuoteInput input, CancellationToken ct)
    {
        if (input.CheckIn is null || input.CheckOut is null) return BadRequest("Укажите даты заезда и выезда");
        if (input.Adults is < 1 or > 30) return BadRequest("Взрослых — от 1 до 30");
        if (input.Children is < 0 or > 30) return BadRequest("Детей — от 0 до 30");
        if (input.Dogs is < 0 or > 20) return BadRequest("Собак — от 0 до 20");
        if (input.Services is { Count: > 0 })
        {
            if (input.Services.Count > options.Value.Services.MaxSessionsInBookingForm) return BadRequest(ServiceTexts.TooManySessions(options.Value.Services.MaxSessionsInBookingForm));
            foreach (var chosen in input.Services)
            {
                if (chosen.ServiceId is null) return BadRequest("Выберите услугу");
                var formError = ServiceOrderCreationService.ValidateSelection(chosen.BusinessDate, chosen.StartMinute, chosen.Hours, chosen.Items, out _);
                if (formError is not null) return BadRequest(formError);
            }
        }
        var ctx = await creation.FindPublicHouseAsync(houseId, ct);
        if (ctx is null) return NotFound();
        return Ok(await creation.QuoteAsync(ctx, new StayStayInput(input.CheckIn.Value, input.CheckOut.Value, input.Adults, input.Children, input.Dogs, input.NeedCot), checkGate: true, ct, input.Services));
    }

    [HttpPost("houses/{houseId:guid}/bookings")]
    [EnableRateLimiting("stay-create")]
    public async Task<ActionResult<CreateStayBookingResponse>> CreateBooking(Guid houseId, CreateStayBookingInput input, CancellationToken ct)
    {
        var result = await creation.CreateAsync(houseId, input, User, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        if (result.Error is not null) return result.Error;
        var booking = result.Booking!;
        var body = new CreateStayBookingResponse(booking.PublicToken, mapper.BookingUrl(booking), await mapper.ToPublicAsync(booking, ct));
        return result.Created ? StatusCode(StatusCodes.Status201Created, body) : Ok(body);
    }
}
