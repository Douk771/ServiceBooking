using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Controllers.Slots;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.Services.Baths;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.WebPush;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers.Baths;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.4.1, §42.10.5, API_CONTRACT_CYCLE42.md §42.25, §42.26 — a booking of «Бани» through the guest's eyes: the page by the 256-bit token in the link
/// (<see cref="SlotServiceOrdersPublicControllerBase"/>, a token of «Дома» is a 404) and the signed-in guest's own list «Мои брони».
/// </summary>
[ApiController]
[Route("api/baths/service-orders")]
public class BathServiceOrdersPublicController(
    AppDbContext db, ServiceDtoMapper mapper, ServiceOrderProofService proofs, ServiceOrderTransitionService transitions, StayActorResolver actors,
    StayProofIpLimiter proofIpLimiter, StayGuestPushSubscriptionWriter pushWriter, IOptions<WebPushOptions> webPush, BathsMyOrdersService myOrders)
    : SlotServiceOrdersPublicControllerBase(db, mapper, proofs, transitions, actors, proofIpLimiter, pushWriter, webPush)
{
    protected override SlotVertical Vertical => SlotVerticals.Baths;

    /// <summary>«Мои брони» (P1): the account's bookings of «Бани» of the last 12 months, active first.</summary>
    [HttpGet("my")]
    [Authorize]
    [EnableRateLimiting("stay-public")]
    public async Task<ActionResult<MyBathOrdersDto>> My(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();
        return Ok(await myOrders.ListAsync(userId, ct));
    }
}
