using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the notification-channel half of the former
/// <c>AdminController</c> — same <c>api/admin</c> prefix, same SuperAdmin gate, same per-action routes.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SuperAdmin")]
public class AdminChannelsController(AppDbContext db, ChannelFundingReader fundingReader, AdminChannelsBuilder adminChannels) : ControllerBase
{
    // ── Notification channels (ARCHITECTURE_CYCLE4.md §34, T4-B11) ───────────────

    [HttpGet("notification-channels")]
    public async Task<ActionResult<PagedResult<AdminChannelDto>>> GetNotificationChannels(
        [FromQuery] ChannelState? state, [FromQuery] ChannelPaymentStatus? paymentState,
        [FromQuery] NotificationTransport? transport,
        [FromQuery] ChannelDisplayStatus? displayStatus, [FromQuery] AdminChannelPaymentFilter? payment, [FromQuery] bool includeReplaced,
        [FromQuery] int? page, [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var query = db.NotificationChannels.AsNoTracking().AsQueryable();
        if (state.HasValue) query = query.Where(c => c.State == state);
        // ARCHITECTURE_CYCLE9.md §114.3 (US-121) — ?transport= filter, additive.
        if (transport.HasValue) query = query.Where(c => c.Transport == transport);
        // Cycle 40 (§40.13): replaced numbers are history — hidden unless asked for (or asked for by state).
        if (!includeReplaced && state != ChannelState.Replaced) query = query.Where(c => c.State != ChannelState.Replaced);

        // The status, the payment texts and the filters on them are computed (the same rules the owner sees), not stored: at this row count (one row per
        // number, not per message) a full materialize-then-filter is acceptable — the cycle-22 design of this endpoint, kept.
        var all = await query.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id).ToListAsync(ct);
        var rows = await adminChannels.BuildRowsAsync(all, ct);
        var filtered = rows.AsEnumerable();
        if (paymentState.HasValue) filtered = filtered.Where(r => r.Dto.PaymentState == paymentState.Value);
        if (displayStatus.HasValue) filtered = filtered.Where(r => r.Dto.DisplayStatus == displayStatus.Value);
        if (payment.HasValue) filtered = filtered.Where(r => r.Payment == payment.Value);
        var list = filtered.ToList();

        var items = list.Skip((currentPage - 1) * currentPageSize).Take(currentPageSize).Select(r => r.Dto).ToList();
        return Ok(Pagination.Create(items, currentPage, currentPageSize, list.Count));
    }

    [HttpGet("notification-channels/summary")]
    public async Task<ActionResult<AdminChannelSummaryDto>> GetNotificationChannelsSummary(CancellationToken ct)
    {
        var channels = await db.NotificationChannels.AsNoTracking().ToListAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var in7Days = nowUtc.AddDays(7);

        // Cycle 40 (§40.13): the counters read the SAME facts as the table — pendingRequests = a request newer than the last payment (not yet paid),
        // expiringIn7Days = by the paid period of the number's transport; working/actionRequired/off = the three-state presentation of live numbers.
        var rows = await adminChannels.BuildRowsAsync(channels.Where(c => c.State != ChannelState.Replaced).ToList(), ct);
        return Ok(new AdminChannelSummaryDto(
            Connected: channels.Count(c => c.State == ChannelState.Connected),
            Connecting: channels.Count(c => c.State == ChannelState.Connecting),
            Disconnected: channels.Count(c => c.State == ChannelState.Disconnected),
            Blocked: channels.Count(c => c.State == ChannelState.Blocked),
            NeedsReconnect: channels.Count(c => c.State == ChannelState.NeedsReconnect),
            Idle: channels.Count(c => c.IdleSinceUtc is not null),
            ExpiringIn7Days: rows.Count(r => r.Facts.Paid && r.Facts.PaidUntil is { } paidUntil && paidUntil >= nowUtc && paidUntil <= in7Days),
            PendingRequests: rows.Count(r => !r.Facts.Paid && r.Facts.RequestNewerThanPayment),
            Working: rows.Count(r => r.Dto.DisplayStatus == ChannelDisplayStatus.Working),
            ActionRequired: rows.Count(r => r.Dto.DisplayStatus == ChannelDisplayStatus.ActionRequired),
            Off: rows.Count(r => r.Dto.DisplayStatus == ChannelDisplayStatus.Off)));
    }

    [HttpGet("notification-channels/{id:guid}")]
    public async Task<ActionResult<AdminChannelCardDto>> GetNotificationChannelCard(Guid id, CancellationToken ct)
    {
        var card = await adminChannels.BuildCardAsync(id, ct);
        return card is null ? NotFound() : Ok(card);
    }

    /// <summary>API_CONTRACT_CYCLE40.md §40.37 — the manual confirmation of a payment (the «Оплата» step of the owner's wizard is an application the SuperAdmin confirms).
    /// Order of refusals: 404; months not 1..12 → 400; comment &gt; 500 → 400; replaced → 409; no option in the catalog → 409; option closed → 409.</summary>
    [HttpPost("notification-channels/{id:guid}/confirm-payment")]
    public async Task<ActionResult<AdminChannelCardDto>> ConfirmNotificationChannelPayment(Guid id, [FromBody] ConfirmChannelPaymentInput input, CancellationToken ct)
    {
        var channel = await db.NotificationChannels.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (channel is null) return NotFound();
        if (input.Months is < 1 or > 12) return BadRequest("Срок — от 1 до 12 месяцев");
        if (input.Comment is { Length: > 500 }) return BadRequest("Комментарий — не длиннее 500 символов");

        var result = await adminChannels.ConfirmPaymentAsync(channel, input.Months, input.Comment, User.FindFirstValue(ClaimTypes.NameIdentifier)!, ct);
        if (!result.Ok) return StatusCode(result.Status!.Value, result.Text);

        await db.SaveChangesAsync(ct);
        return Ok(await adminChannels.BuildCardAsync(id, ct));
    }

    [HttpPost("notification-channels/{id:guid}/suspend")]
    public Task<IActionResult> SuspendChannel(Guid id, [FromBody] AdminChannelSuspendDto dto) => SetSuspendedAsync(id, true, dto.Comment);

    [HttpPost("notification-channels/{id:guid}/resume")]
    public Task<IActionResult> ResumeChannel(Guid id, [FromBody] AdminChannelSuspendDto dto) => SetSuspendedAsync(id, false, dto.Comment);

    private async Task<IActionResult> SetSuspendedAsync(Guid id, bool suspended, string? comment)
    {
        var channel = await db.NotificationChannels.FindAsync(id);
        if (channel is null) return NotFound();

        // Cycle 22 (§379, Р2): the log records the channel's funding paid-until at this moment (the
        // WhatsApp option's, else the subscription period) — suspending never changes it, so old = new.
        var paidUntil = (await fundingReader.LoadAsync([channel])).GetValueOrDefault(channel.Id)?.PaidUntil;
        channel.IsSuspendedByAdmin = suspended;
        db.ChannelPaymentLogs.Add(new ChannelPaymentLog
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            ChangedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            OldPaidUntil = paidUntil,
            NewPaidUntil = paidUntil,
            Comment = comment is null ? (suspended ? "suspended" : "resumed") : $"{(suspended ? "suspended" : "resumed")}: {comment}",
        });

        await db.SaveChangesAsync();
        return NoContent();
    }
}

// ── DTOs ───────────────────────────────────────────────────────────────────────

// ── Notification channels (API_CONTRACT_CYCLE4.md §34, T4-B11) ────────────────

// Inn/LegalEntityForm appended (code review, "заодно"): the owner-facing channel read already exposes
// both (NotificationChannelsController); SuperAdmin — who has to reconcile the same channel against
// invoicing/compliance — was the one reader who couldn't see either.
// ARCHITECTURE_CYCLE9.md §104.3/§114.3 (US-121) — Transport is additive, inserted right after Id;
// every other field keeps its name and position.
// Cycle 22 (ARCHITECTURE_CYCLE22.md §380, Р6): PaidFrom (always null since cycle 7) removed; PaidUntil is
// the channel's funding paid-until (ChannelFundingReader), no longer the dropped channel column.
// Cycle 40 (ARCHITECTURE_CYCLE40.md §40.13, API_CONTRACT_CYCLE40.md §40.36): DisplayStatus..AvailableActions appended — the SAME three-state presentation the owner sees.
public record AdminChannelDto(
    Guid Id, NotificationTransport Transport, ChannelState State, ChannelPaymentStatus PaymentState,
    string OwnerName, string? OwnerPhoneMasked,
    DateTime? PaidUntil, int CompanyCount, DateTime? IdleSince, DateTime? RequestedAt,
    string? Inn = null, LegalEntityForm? LegalEntityForm = null,
    ChannelDisplayStatus? DisplayStatus = null, string? DisplayText = null, string StateText = "", string? PhoneMasked = null,
    string PaymentText = "", bool IsSuspended = false, DateTime CreatedAt = default, IReadOnlyList<string>? AvailableActions = null);

public record AdminChannelSummaryDto(
    int Connected, int Connecting, int Disconnected, int Blocked,
    int NeedsReconnect, int Idle, int ExpiringIn7Days, int PendingRequests,
    int Working = 0, int ActionRequired = 0, int Off = 0);

public record AdminChannelStateEventDto(
    DateTime OccurredAtUtc, ChannelState? FromState, ChannelState ToState, ChannelStateReason Reason, string ReasonText, string? Detail);

public record AdminChannelPaymentEventDto(
    DateTime OccurredAtUtc, string Kind, ChannelOptionChangeSource? Source, string? ChangedByName, DateTime? OldPaidUntil, DateTime? NewPaidUntil, string? Comment);

public record AdminChannelPaymentDto(bool Paid, DateTime? PaidUntil, bool IsTrial, bool Requested, DateTime? LastPaymentAt);

public record AdminChannelCardDto(
    AdminChannelDto Channel, Guid BillingAccountId, string OwnerUserId, ChannelState State, ChannelStateReason? LastStateReason, string? LastStateReasonText,
    LegalEntityForm? LegalEntityForm, string? Inn, DateTime? RequestedAt, DateTime? RiskAcceptedAt, string? RiskAcceptedVersion, bool TermsAccepted,
    DateTime? InstanceCreatedAt, DateTime? ConnectedAt, DateTime? LastStateCheckAt, DateTime? IdleSince, DateTime? IdleDeadline, string? ProviderServerCountry,
    ServiceBooking.API.DTOs.Notifications.ChannelTestDto? LastTest, AdminChannelPaymentDto Payment, bool OptionOpen, Guid? ReplacedByChannelId, Guid? ReplacesChannelId,
    IReadOnlyList<ServiceBooking.API.DTOs.Notifications.ChannelCompanyDto> Companies, IReadOnlyList<AdminChannelStateEventDto> StateEvents,
    IReadOnlyList<AdminChannelPaymentEventDto> PaymentEvents, IReadOnlyList<string> AvailableActions);

public record ConfirmChannelPaymentInput(int Months, string? Comment);

public record AdminChannelSuspendDto(string? Comment);
