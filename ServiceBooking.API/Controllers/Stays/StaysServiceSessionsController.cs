using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

using ServiceBooking.API.Controllers.Slots;

namespace ServiceBooking.API.Controllers.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.7.3, §39.10, API_CONTRACT_CYCLE39.md §39.29, §39.30 — sessions in the cabinet: «День услуг», the list and the card (both kinds), payment check and cancellation
/// with <c>expectedVersion</c>, the proof files, the starts for the staff (no minimum lead; an unpublished service is allowed), adding a service to a booking (with the mandatory basis,
/// ЮР39-6) and the manual stand-alone order. Rights: ViewBookings to read, ManageBookings to act.
/// </summary>
[ApiController]
[Route("api/stays/companies/{companyId:guid}")]
[Authorize]
public class StaysServiceSessionsController(
    AppDbContext dbArg, StaysAccessResolver accessArg, ServiceDayService dayArg, ServiceDtoMapper mapperArg, StayDtoMapper bookingMapperArg, ServiceSlotService slotsArg, ServiceOrderTransitionService transitionsArg, ServiceSessionAddService sessionAddArg, ServiceOrderCreationService orderCreationArg, ServiceOrderProofService proofsArg, StayServiceOrderEventLog orderLogArg, StayActorResolver actorsArg, IStaysClock clockArg, IOptions<StaysOptions> optionsArg) : SlotSessionsControllerBase(dbArg, accessArg, dayArg, mapperArg, bookingMapperArg, slotsArg, transitionsArg, sessionAddArg, orderCreationArg, proofsArg, orderLogArg, actorsArg, clockArg, optionsArg)
{
    protected override SlotVertical Vertical => SlotVerticals.Stays;

    [HttpPost("bookings/{bookingId:guid}/sessions")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaffStayBookingCardDto>> AddToBooking(Guid companyId, Guid bookingId, StaffAddSessionInput input, CancellationToken ct)
    {
        var r = await Access.ResolveAsync(companyId, User, StaysPermission.ManageBookings, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!r.Ok) return r.Error!;
        var result = await SessionAdd.AddByStaffAsync(companyId, bookingId, input, await Actors.ResolveStaffAsync(User, ct), ct);
        if (result.Error is not null) return result.Error;
        var card = await BookingMapper.ToStaffCardAsync(await Db.StayBookings.AsNoTracking().FirstAsync(b => b.Id == bookingId, ct), withMessages: true, ct);
        return result.Created ? StatusCode(StatusCodes.Status201Created, card) : Ok(card);
    }
}
