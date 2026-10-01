using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServiceBooking.API.DTOs.StaffMax;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.API.Services.StaffMax;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §498, API_CONTRACT_CYCLE25.md §523 — "MAX for staff": the caller's own connection of a MAX chat, to which the orders of the
/// shops they work in are sent. Account-level (not a shop route), scoped to the caller's own user id — there is no way to see or change someone else's.
/// The chat id is never returned.
/// </summary>
[ApiController]
[Route("api/staff-max")]
[Authorize]
public class StaffMaxController(StaffMaxLinkService service) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<ActionResult<StaffMaxStatusDto>> GetStatus(CancellationToken ct) => Ok(await service.GetStatusAsync(UserId, ct));

    [DemoForbidden]
    [HttpPost("link-sessions")]
    [EnableRateLimiting("staff-max-link")]
    public async Task<ActionResult<StaffMaxLinkSessionDto>> CreateLinkSession(CancellationToken ct)
    {
        var (conflict, session) = await service.CreateSessionAsync(UserId, ct);
        if (conflict is not null) return Conflict(conflict);
        return StatusCode(StatusCodes.Status201Created, session);
    }

    [HttpDelete("link")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await service.DisconnectAsync(UserId, ct);
        return NoContent();
    }
}
