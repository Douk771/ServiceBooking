using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>How many <c>Pending</c> rows an event moved to another number and how many it cancelled.</summary>
public readonly record struct PendingRebindSummary(int Rebound, int Cancelled)
{
    public int Total => Rebound + Cancelled;
}

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.9 (Р40-Ю3) — the database side of <see cref="PendingRebind"/>: one helper for the three events that
/// change which number a message may go through. Only <c>Pending</c> rows are touched (an <c>Expired</c>/<c>Sent</c> row is never
/// revived). Like the schedulers this only changes tracked entities — the caller's <c>SaveChangesAsync</c> commits them together with
/// the event itself.
/// <list type="bullet">
/// <item><b>Unbind</b> a number: its <c>Pending</c> rows are cancelled; moved to another routable transport only when
/// <c>Notifications:RebindPendingToOtherTransport</c> is on (off by default).</item>
/// <item><b>Replace</b> a number: rows follow to the new row of the same transport.</item>
/// <item><b>Company transfer</b>: rows move to the routable number of the same transport of the receiving account, else cancelled.</item>
/// </list>
/// </summary>
public sealed class PendingRebinder(AppDbContext db, AccountMessagingReader messagingReader, IOptions<NotificationOptions> options)
{
    public async Task<PendingRebindSummary> OnUnbindAsync(NotificationChannel channel, CancellationToken ct = default)
    {
        var rows = await db.OutboundNotifications
            .Where(n => n.ChannelId == channel.Id && n.Status == NotificationStatus.Pending).ToListAsync(ct);
        if (rows.Count == 0) return default;

        var candidates = new List<RebindTargetChannel>();
        var allowOtherTransport = options.Value.RebindPendingToOtherTransport;
        if (allowOtherTransport && channel.BillingAccountId is { } accountId &&
            await messagingReader.ForAccountAsync(accountId, ct: ct) is { } account)
        {
            candidates.AddRange(account.Transports
                .Where(t => t.Routable && t.Primary!.Id != channel.Id)
                .Select(t => new RebindTargetChannel(t.Primary!.Id, t.Transport)));
        }
        return await ApplyAsync(PendingRebindEvent.Unbind, rows, candidates, allowOtherTransport, ct);
    }

    public async Task<PendingRebindSummary> OnReplaceAsync(NotificationChannel oldChannel, NotificationChannel newChannel, CancellationToken ct = default)
    {
        var rows = await db.OutboundNotifications
            .Where(n => n.ChannelId == oldChannel.Id && n.Status == NotificationStatus.Pending).ToListAsync(ct);
        if (rows.Count == 0) return default;
        return await ApplyAsync(PendingRebindEvent.Replace, rows,
            [new RebindTargetChannel(newChannel.Id, newChannel.Transport)], false, ct);
    }

    /// <summary>Call BEFORE the company's billing account changes. <paramref name="targetAccountId"/> is the receiving account.</summary>
    public async Task<PendingRebindSummary> OnCompanyTransferAsync(Guid companyId, Guid targetAccountId, CancellationToken ct = default)
    {
        var rows = await db.OutboundNotifications
            .Where(n => n.CompanyId == companyId && n.Status == NotificationStatus.Pending).ToListAsync(ct);
        if (rows.Count == 0) return default;
        return await ApplyAsync(PendingRebindEvent.CompanyTransfer, rows, await TargetCandidatesAsync(targetAccountId, ct), false, ct);
    }

    /// <summary>How many `Pending` rows of the company find NO routable number of the same transport on the receiving account (and so
    /// would be cancelled). Read-only: the transfer preview.</summary>
    public async Task<int> CountCancelledOnTransferAsync(Guid companyId, Guid targetAccountId, CancellationToken ct = default)
    {
        var pendingTransports = await db.OutboundNotifications.AsNoTracking()
            .Where(n => n.CompanyId == companyId && n.Status == NotificationStatus.Pending)
            .GroupBy(n => n.Transport).Select(g => new { Transport = g.Key, Count = g.Count() }).ToListAsync(ct);
        if (pendingTransports.Count == 0) return 0;
        var routable = (await TargetCandidatesAsync(targetAccountId, ct)).Select(c => c.Transport).ToHashSet();
        return pendingTransports.Where(p => !routable.Contains(p.Transport)).Sum(p => p.Count);
    }

    private async Task<List<RebindTargetChannel>> TargetCandidatesAsync(Guid targetAccountId, CancellationToken ct) =>
        await messagingReader.ForAccountAsync(targetAccountId, ct: ct) is { } target
            ? target.Transports.Where(t => t.Routable).Select(t => new RebindTargetChannel(t.Primary!.Id, t.Transport)).ToList()
            : [];

    private async Task<PendingRebindSummary> ApplyAsync(
        PendingRebindEvent evt, List<OutboundNotification> rows, IReadOnlyList<RebindTargetChannel> candidates,
        bool allowOtherTransport, CancellationToken ct)
    {
        // Keys a row would get after a move to ANOTHER transport — one lookup for the whole batch.
        var possibleKeys = rows
            .SelectMany(r => candidates.Where(c => c.Transport != r.Transport).Select(c => PendingRebind.RebindKey(r.IdempotencyKey, c.Transport)))
            .Distinct().ToList();
        var existingKeys = possibleKeys.Count == 0
            ? new HashSet<string>()
            : (await db.OutboundNotifications.AsNoTracking().Where(n => possibleKeys.Contains(n.IdempotencyKey))
                .Select(n => n.IdempotencyKey).ToListAsync(ct)).ToHashSet();

        int rebound = 0, cancelled = 0;
        foreach (var row in rows)
        {
            var decision = PendingRebind.Decide(evt, row.Transport, row.IdempotencyKey, candidates, allowOtherTransport, existingKeys);
            if (decision.Cancel)
            {
                row.Status = NotificationStatus.Cancelled;
                row.Reason = NotificationReason.BookingOrAssignmentCancelled;
                cancelled++;
                continue;
            }
            row.ChannelId = decision.ChannelId;
            row.Transport = decision.Transport!.Value;
            row.IdempotencyKey = decision.IdempotencyKey!;
            existingKeys.Add(row.IdempotencyKey);
            rebound++;
        }
        return new PendingRebindSummary(rebound, cancelled);
    }
}
