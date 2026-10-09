using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Controllers.Slots;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers.Baths;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.4.1, API_CONTRACT_CYCLE42.md §42.32 — bookings of «Бани» in the cabinet: «День услуг», starts, quote, list, card, payment check, cancellation, proof files and the
/// manual booking (with <c>guestsCount</c>). The engine is <see cref="SlotSessionsControllerBase"/>; the route <c>bookings/{bookingId}/sessions</c> exists only at «Дома» (a bath has no stays).
/// </summary>
[ApiController]
[Route("api/baths/companies/{companyId:guid}")]
[Authorize]
public class BathsSessionsController(
    AppDbContext dbArg, StaysAccessResolver accessArg, ServiceDayService dayArg, ServiceDtoMapper mapperArg, StayDtoMapper bookingMapperArg, ServiceSlotService slotsArg,
    ServiceOrderTransitionService transitionsArg, ServiceSessionAddService sessionAddArg, ServiceOrderCreationService orderCreationArg, ServiceOrderProofService proofsArg,
    StayServiceOrderEventLog orderLogArg, StayActorResolver actorsArg, IStaysClock clockArg, IOptions<StaysOptions> optionsArg)
    : SlotSessionsControllerBase(dbArg, accessArg, dayArg, mapperArg, bookingMapperArg, slotsArg, transitionsArg, sessionAddArg, orderCreationArg, proofsArg, orderLogArg, actorsArg, clockArg, optionsArg)
{
    protected override SlotVertical Vertical => SlotVerticals.Baths;
}
