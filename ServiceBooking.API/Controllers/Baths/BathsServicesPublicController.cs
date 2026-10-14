using Microsoft.AspNetCore.Mvc;
using ServiceBooking.API.Controllers.Slots;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers.Baths;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.4.1, API_CONTRACT_CYCLE42.md §42.24 — a resource of «Бани» through the guest's eyes: the page, the dates, the starts, the quote and the booking.
/// The engine is the one of cycle 39 (<see cref="SlotServicesPublicControllerBase"/>) of the «Бани» vertical; there is no «to the stay» mode (house parameters are ignored).
/// </summary>
[ApiController]
[Route("api/baths/public")]
public class BathsServicesPublicController(
    AppDbContext db, ServiceCatalogService catalog, ServiceSlotService slots, ServiceOrderCreationService orders, ServiceDtoMapper mapper, StaysCompanyService companyService,
    PublicSiteLinks links) : SlotServicesPublicControllerBase(db, catalog, slots, orders, mapper, companyService, links)
{
    protected override SlotVertical Vertical => SlotVerticals.Baths;
}
