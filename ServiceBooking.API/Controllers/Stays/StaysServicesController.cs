using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;


using ServiceBooking.API.Controllers.Slots;

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
    StaysCompanyService companyService) : SlotServicesCabinetControllerBase(db, access, catalog, schedule, slots, sessionWriter, revision, actors, imageUploadService, storage, clock, options, companyService)
{
    protected override SlotVertical Vertical => SlotVerticals.Stays;
}
