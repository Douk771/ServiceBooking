using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Controllers.Slots;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Baths;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers.Baths;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.4.1, API_CONTRACT_CYCLE42.md §42.29 — resources («бани») in the cabinet: the engine of cycle 39 (<see cref="SlotServicesCabinetControllerBase"/>) of the «Бани» vertical.
/// Differences are data of <see cref="SlotVerticals.Baths"/>: capacity, publish limit under the billing-account lock, the resource address by BathsSlugPolicy, no house bookings.
/// </summary>
[ApiController]
[Route("api/baths/companies/{companyId:guid}/services")]
[Authorize]
public class BathsServicesController(
    AppDbContext db, StaysAccessResolver access, ServiceCatalogService catalog, ServiceScheduleWriter schedule, ServiceSlotService slots, ServiceSessionWriter sessionWriter,
    StayBookingEventLog revision, StayActorResolver actors, ImageUploadService imageUploadService, FileStorage storage, IStaysClock clock, IOptions<StaysOptions> options,
    StaysCompanyService companyService, ServiceItemWriter itemWriter, BathsPublishGate publishGate, BathsCatalogService catalogCache)
    : SlotServicesCabinetControllerBase(db, access, catalog, schedule, slots, sessionWriter, revision, actors, imageUploadService, storage, clock, options, companyService, itemWriter, publishGate)
{
    protected override SlotVertical Vertical => SlotVerticals.Baths;

    protected override void OnCatalogChanged() => catalogCache.InvalidateBase();
}
