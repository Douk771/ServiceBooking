using ServiceBooking.API.Controllers.Stays;
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


namespace ServiceBooking.API.Controllers.Slots;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.7.5, API_CONTRACT_CYCLE39.md §39.23 — a stand-alone session through the guest's eyes. The 256-bit token in the link IS the access: an unknown token is
/// a 404 with an empty body on every route. The page is polled every 15 s by the frontend.
/// </summary>

public abstract class SlotServiceOrdersPublicControllerBase(
    AppDbContext db, ServiceDtoMapper mapper, ServiceOrderProofService proofs, ServiceOrderTransitionService transitions, StayActorResolver actors,
    StayProofIpLimiter proofIpLimiter, StayGuestPushSubscriptionWriter pushWriter, IOptions<WebPushOptions> webPush) : ControllerBase
{
    protected abstract SlotVertical Vertical { get; }

    private const int MaxTokenLength = 100;

    [HttpGet("public/{token}")]
    [EnableRateLimiting("stay-public")]
    public async Task<ActionResult<PublicServiceOrderDto>> Get(string token, CancellationToken ct)
    {
        var order = await FindAsync(token, ct);
        return order is null ? NotFound() : Ok(await mapper.ToPublicOrderAsync(order, ct));
    }

    [HttpPost("public/{token}/payment-proofs")]
    [EnableRateLimiting("stay-proof")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<ActionResult<PublicServiceOrderDto>> AttachProof(string token, IFormFile? file, CancellationToken ct)
    {
        if (token.Length > MaxTokenLength) return NotFound();
        if (!proofIpLimiter.TryAcquire(HttpContext.Connection.RemoteIpAddress?.ToString())) return StatusCode(StatusCodes.Status429TooManyRequests, StayProofIpLimiter.Text);
        if (await FindAsync(token, ct) is null) return NotFound();
        var result = await proofs.AttachAsync(token, file, ct);
        switch (result.Outcome)
        {
            case ProofOutcome.NotFound: return NotFound();
            case ProofOutcome.BadFile: return BadRequest(result.Error);
        }
        var dto = await mapper.ToPublicOrderAsync(result.Order!, ct);
        var phone = dto.Company.Phone;
        return result.Outcome switch
        {
            ProofOutcome.Ok => StatusCode(StatusCodes.Status201Created, dto),
            ProofOutcome.NotAllowed => Conflict(new ServiceOrderGuestConflictDto("ProofNotAllowed", ServiceTexts.ProofNotAllowed(dto.StatusText), dto)),
            ProofOutcome.LimitReached => Conflict(new ServiceOrderGuestConflictDto("ProofLimitReached", $"Можно приложить не больше {dto.Proofs.MaxCount} файлов", dto)),
            _ => Conflict(new ServiceOrderGuestConflictDto("HoldExpired", ServiceTexts.HoldExpired(phone), dto)),
        };
    }

    [HttpGet("public/{token}/payment-proofs/{proofId:guid}")]
    [EnableRateLimiting("stay-public")]
    public async Task<IActionResult> GetProof(string token, Guid proofId, CancellationToken ct)
    {
        var order = await FindAsync(token, ct);
        if (order is null) return NotFound();
        var opened = await proofs.OpenAsync(order.Id, proofId, ct);
        if (opened is null) return NotFound();
        StayProofResponse.Apply(Response, opened.Value.ContentType);
        return File(opened.Value.Stream, opened.Value.ContentType);
    }

    [HttpPost("public/{token}/cancel")]
    [EnableRateLimiting("stay-public")]
    public async Task<ActionResult<PublicServiceOrderDto>> Cancel(string token, EmptyInput? _, CancellationToken ct)
    {
        if (token.Length > MaxTokenLength) return NotFound();
        var existing = await FindAsync(token, ct);
        if (existing is null) return NotFound();
        var actor = await actors.ResolveGuestAsync(User, existing.GuestName, ct);
        var result = await transitions.CancelByGuestAsync(token, actor, ct);
        if (result.Outcome == TransitionOutcome.NotFound) return NotFound();
        var dto = await mapper.ToPublicOrderAsync((await FindAsync(token, ct))!, ct);
        if (result.Outcome == TransitionOutcome.Ok) return Ok(dto);
        var message = result.Outcome == TransitionOutcome.HoldExpired || dto.Status == StayBookingStatus.ExpiredUnpaid
            ? ServiceTexts.HoldExpired(dto.Company.Phone)
            : dto.Status == StayBookingStatus.CancelledByGuest || dto.Status == StayBookingStatus.CancelledByOwner ? ServiceTexts.OrderAlreadyCancelled
            : dto.Cancellation.CannotCancelText ?? ServiceTexts.OrderAlreadyCancelled;
        return Conflict(new ServiceOrderGuestConflictDto("CancelNotAllowed", message, dto));
    }

    [HttpPost("public/{token}/push-subscription")]
    [EnableRateLimiting("stay-push")]
    public async Task<IActionResult> SubscribePush(string token, PushSubscriptionInput input, CancellationToken ct)
    {
        var order = await FindAsync(token, ct);
        if (order is null) return NotFound();
        if (!PushEndpointValidator.IsValid(input.Endpoint)) return BadRequest("Некорректный адрес подписки (endpoint).");
        if (input.Keys is null || string.IsNullOrWhiteSpace(input.Keys.P256dh) || input.Keys.P256dh.Length > 200) return BadRequest("Некорректный ключ подписки (p256dh).");
        if (string.IsNullOrWhiteSpace(input.Keys.Auth) || input.Keys.Auth.Length > 100) return BadRequest("Некорректный ключ подписки (auth).");
        if (input.DeviceLabel is { Length: > 100 }) return BadRequest("Слишком длинное название устройства.");

        var enabled = await db.StaysSettings.AsNoTracking().Where(s => s.CompanyId == order.CompanyId).Select(s => (bool?)s.GuestWebPushEnabled).FirstOrDefaultAsync(ct) ?? true;
        if (!enabled) return Conflict(StayBookingsPublicController.CompanyNoPush);
        if (StayStateMachine.IsTerminal(order.Status)) return Conflict(ServiceTexts.ServiceOrderDone);
        if (!string.Equals(webPush.Value.Provider, "web-push", StringComparison.OrdinalIgnoreCase)) return Conflict(StayBookingsPublicController.PlatformNoPush);
        await pushWriter.UpsertForOrderAsync(order.Id, input.Endpoint!, input.Keys.P256dh, input.Keys.Auth, ct);
        return NoContent();
    }

    [HttpPost("public/{token}/push-subscription/remove")]
    [EnableRateLimiting("stay-push")]
    public async Task<IActionResult> UnsubscribePush(string token, PushSubscriptionRemoveInput input, CancellationToken ct)
    {
        var order = await FindAsync(token, ct);
        if (order is null) return NotFound();
        if (string.IsNullOrWhiteSpace(input.Endpoint) || input.Endpoint.Length > 2048) return BadRequest("Некорректный адрес подписки (endpoint).");
        await pushWriter.RemoveForOrderAsync(order.Id, input.Endpoint, ct);
        return NoContent();
    }

    private async Task<StayServiceOrder?> FindAsync(string token, CancellationToken ct) =>
        token.Length > MaxTokenLength ? null : await db.StayServiceOrders.AsNoTracking().FirstOrDefaultAsync(o => o.PublicToken == token && db.Companies.Any(c => c.Id == o.CompanyId && c.Kind == Vertical.Kind), ct);
}
