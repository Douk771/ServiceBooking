using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.DTOs.Legal;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Bookings;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Scheduling;
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
public class AdminChannelsController(AppDbContext db, ChannelFundingReader fundingReader) : ControllerBase
{
    // ── Notification channels (ARCHITECTURE_CYCLE4.md §34, T4-B11) ───────────────

    [HttpGet("notification-channels")]
    public async Task<ActionResult<PagedResult<AdminChannelDto>>> GetNotificationChannels(
        [FromQuery] ChannelState? state, [FromQuery] ChannelPaymentStatus? paymentState,
        [FromQuery] NotificationTransport? transport,
        [FromQuery] int? page, [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var query = db.NotificationChannels.AsNoTracking().Include(c => c.Assignments).AsQueryable();
        if (state.HasValue) query = query.Where(c => c.State == state);
        // ARCHITECTURE_CYCLE9.md §114.3 (US-121) — ?transport= filter, additive.
        if (transport.HasValue) query = query.Where(c => c.Transport == transport);

        // Payment state is computed, not stored (ChannelPaymentState.Of) — filtering by it means
        // pulling candidates in state-shaped buckets rather than a single indexed WHERE. At this row
        // count (one row per channel, not per message) a full materialize-then-filter is acceptable; see
        // ChannelPaymentState's own doc comment for why this can never become a stored column.
        // Cycle 22 (§379, Р2): its source is the channel's funding, read in one batch for all candidates.
        int total;
        List<NotificationChannel> page1;
        Dictionary<Guid, ChannelFundingInfo> funding;
        if (paymentState.HasValue)
        {
            var all = await query.ToListAsync(ct);
            funding = await fundingReader.LoadAsync(all, ct);
            var filtered = all
                .Where(c => ChannelPaymentState.Of(c, funding.GetValueOrDefault(c.Id)) == paymentState.Value).ToList();
            total = filtered.Count;
            page1 = filtered.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
                .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize).ToList();
        }
        else
        {
            // §375 F12: without the computed payment filter, count and page in SQL — same order
            // (CreatedAt DESC, then Id: uuid order in Postgres equals Guid.CompareTo order).
            total = await query.CountAsync(ct);
            page1 = await query.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
                .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize).ToListAsync(ct);
            funding = await fundingReader.LoadAsync(page1, ct);
        }

        var ownerIds = page1.Select(c => c.OwnerUserId).Distinct().ToList();
        var owners = await db.Users.AsNoTracking().Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u, ct);

        var items = page1.Select(c =>
        {
            var owner = owners.GetValueOrDefault(c.OwnerUserId);
            var f = funding.GetValueOrDefault(c.Id);
            return new AdminChannelDto(
                c.Id, c.Transport, c.State, ChannelPaymentState.Of(c, f),
                owner is null ? "" : $"{owner.FirstName} {owner.LastName}",
                owner?.PhoneNumber is null ? null : PhoneDisplayMask.Mask(owner.PhoneNumber),
                f?.PaidUntil, c.Assignments.Count, c.IdleSinceUtc, c.RequestedAtUtc,
                c.Inn, c.LegalEntityForm);
        }).ToList();

        return Ok(Pagination.Create(items, currentPage, currentPageSize, total));
    }

    [HttpGet("notification-channels/summary")]
    public async Task<ActionResult<AdminChannelSummaryDto>> GetNotificationChannelsSummary(CancellationToken ct)
    {
        var channels = await db.NotificationChannels.AsNoTracking().ToListAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var in7Days = nowUtc.AddDays(7);

        // Cycle 22 (§379, Р2): both payment counters read the channel's FUNDING (one batch for every
        // channel), not the dropped NotificationChannel.PaidUntilUtc column:
        //  - ExpiringIn7Days — the channel is funded and its funding's paid-until (the WhatsApp option's
        //    PaidUntilUtc, else the subscription period) falls within [now, now + 7 days];
        //  - PendingRequests — the owner requested the channel (RequestedAtUtc set) and it is NOT funded
        //    (was: "RequestedAtUtc set and PaidUntilUtc null"). A Replaced row is terminal history, not a
        //    request — it is never funded (ChannelFunding.Rank skips it), so it is excluded explicitly,
        //    or every replacement would count once more per ban.
        var funding = await fundingReader.LoadAsync(channels, ct);
        bool IsFunded(NotificationChannel c) =>
            funding.TryGetValue(c.Id, out var f) && f.State == ChannelFundingState.Funded;

        return Ok(new AdminChannelSummaryDto(
            Connected: channels.Count(c => c.State == ChannelState.Connected),
            Connecting: channels.Count(c => c.State == ChannelState.Connecting),
            Disconnected: channels.Count(c => c.State == ChannelState.Disconnected),
            Blocked: channels.Count(c => c.State == ChannelState.Blocked),
            NeedsReconnect: channels.Count(c => c.State == ChannelState.NeedsReconnect),
            Idle: channels.Count(c => c.IdleSinceUtc is not null),
            ExpiringIn7Days: channels.Count(c => IsFunded(c)
                && funding[c.Id].PaidUntil is { } paidUntil && paidUntil >= nowUtc && paidUntil <= in7Days),
            PendingRequests: channels.Count(c => c.RequestedAtUtc is not null && c.State != ChannelState.Replaced && !IsFunded(c))));
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
public record AdminChannelDto(
    Guid Id, NotificationTransport Transport, ChannelState State, ChannelPaymentStatus PaymentState,
    string OwnerName, string? OwnerPhoneMasked,
    DateTime? PaidUntil, int CompanyCount, DateTime? IdleSince, DateTime? RequestedAt,
    string? Inn = null, LegalEntityForm? LegalEntityForm = null);

public record AdminChannelSummaryDto(
    int Connected, int Connecting, int Disconnected, int Blocked,
    int NeedsReconnect, int Idle, int ExpiringIn7Days, int PendingRequests);

public record AdminChannelSuspendDto(string? Comment);
