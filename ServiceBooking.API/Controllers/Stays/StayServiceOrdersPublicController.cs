using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.WebPush;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;


using ServiceBooking.API.Controllers.Slots;

namespace ServiceBooking.API.Controllers.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.7.5, API_CONTRACT_CYCLE39.md §39.23 — a stand-alone session through the guest's eyes. The 256-bit token in the link IS the access: an unknown token is
/// a 404 with an empty body on every route. The page is polled every 15 s by the frontend.
/// </summary>

[ApiController]
[Route("api/stays/service-orders")]
public class StayServiceOrdersPublicController(
    AppDbContext db, ServiceDtoMapper mapper, ServiceOrderProofService proofs, ServiceOrderTransitionService transitions, StayActorResolver actors,
    StayProofIpLimiter proofIpLimiter, StayGuestPushSubscriptionWriter pushWriter, IOptions<WebPushOptions> webPush) : SlotServiceOrdersPublicControllerBase(db, mapper, proofs, transitions, actors, proofIpLimiter, pushWriter, webPush)
{
    protected override SlotVertical Vertical => SlotVerticals.Stays;
}
