using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Infrastructure.Data;


using ServiceBooking.API.Controllers.Slots;

namespace ServiceBooking.API.Controllers.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.4, §39.7.1, API_CONTRACT_CYCLE39.md §39.22 — the anonymous side of services: the page, the calendar of dates, the starts, the quote and the order
/// of a service without a stay. None of the answers contains the requisites, the occupied intervals, a reason a time is taken or another guest's data.
/// </summary>

[ApiController]
[Route("api/stays/public")]
public class StaysServicesPublicController(
    AppDbContext db, ServiceCatalogService catalog, ServiceSlotService slots, ServiceOrderCreationService orders, ServiceDtoMapper mapper, StaysCompanyService companyService,
    PublicSiteLinks links) : SlotServicesPublicControllerBase(db, catalog, slots, orders, mapper, companyService, links)
{
    protected override SlotVertical Vertical => SlotVerticals.Stays;
}
