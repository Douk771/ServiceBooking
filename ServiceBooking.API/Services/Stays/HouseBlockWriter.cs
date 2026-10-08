using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

public sealed record BlockResult(ActionResult? Error, HouseBlock? Block = null);

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.2.5, §37.5 — the ONLY writer of blocks: the block, its occupancy row (source OwnerBlock — the EXCLUDE constraint is the net
/// against a simultaneous booking), the journal event and the board revision, in one transaction under the house lock. Blocks may touch each other (the end
/// is the first free date) but not overlap (deviation 11 of §37.18): 409 BlockOverlapsBlock.
/// </summary>
public class HouseBlockWriter(AppDbContext db, HouseOccupancyWriter occupancy, StayBookingEventLog eventLog, StayHoldExpirer expirer, IStaysClock clock)
{
    public const int MaxNights = 366;

    public static string? Validate(DateOnly? start, DateOnly? end, string? comment, DateOnly today, DateOnly? previousStart)
    {
        if (start is null || end is null || end <= start) return "Дата окончания должна быть позже начала";
        if (comment is { Length: > 300 }) return "Комментарий — не длиннее 300 символов";
        if (end.Value.DayNumber - start.Value.DayNumber > MaxNights) return "Блокировка — не длиннее 366 ночей";
        // On an edit only the NEW nights are checked: a block that started in the past may keep its start.
        if (start < today && start != previousStart) return "Нельзя блокировать прошедшие даты";
        return null;
    }

    public async Task<BlockResult> CreateAsync(Company company, Guid houseId, DateOnly start, DateOnly end, HouseBlockKind kind, string? comment, StayActor actor, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await occupancy.LockHouseAsync(houseId);
        await expirer.ExpireOverlappingAsync(houseId, start, end, now, ct);
        if (await ConflictAsync(houseId, start, end, exceptBlockId: null, ct) is { } conflict) return new BlockResult(conflict);

        var block = new HouseBlock
        {
            Id = Guid.NewGuid(), CompanyId = company.Id, HouseId = houseId, StartDate = start, EndDate = end, Kind = kind, Comment = comment,
            CreatedAtUtc = now, CreatedByUserId = actor.UserId ?? string.Empty,
        };
        db.HouseBlocks.Add(block);
        occupancy.AddBlock(block, now);
        AddEvent(block, HouseBlockEventKind.Created, actor, null, Snapshot(block), now);
        await eventLog.BumpRevisionAsync(company.Id);
        return await CommitAsync(tx, block, ct);
    }

    public async Task<BlockResult> UpdateAsync(Company company, HouseBlock block, DateOnly start, DateOnly end, HouseBlockKind kind, string? comment, StayActor actor, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await occupancy.LockHouseAsync(block.HouseId);
        await expirer.ExpireOverlappingAsync(block.HouseId, start, end, now, ct);
        if (await ConflictAsync(block.HouseId, start, end, block.Id, ct) is { } conflict) return new BlockResult(conflict);

        var before = Snapshot(block);
        block.StartDate = start;
        block.EndDate = end;
        block.Kind = kind;
        block.Comment = comment;
        block.UpdatedAtUtc = now;
        await db.HouseOccupancies.Where(o => o.HouseBlockId == block.Id && o.ReleasedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.StartDate, start).SetProperty(o => o.EndDate, end), ct);
        AddEvent(block, HouseBlockEventKind.Updated, actor, before, Snapshot(block), now);
        await eventLog.BumpRevisionAsync(company.Id);
        return await CommitAsync(tx, block, ct);
    }

    public async Task DeleteAsync(Company company, HouseBlock block, StayActor actor, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await occupancy.LockHouseAsync(block.HouseId);
        block.DeletedAtUtc = now;
        await occupancy.ReleaseBlockAsync(block.Id, now);
        AddEvent(block, HouseBlockEventKind.Deleted, actor, Snapshot(block), null, now);
        await eventLog.BumpRevisionAsync(company.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<ConflictObjectResult?> ConflictAsync(Guid houseId, DateOnly start, DateOnly end, Guid? exceptBlockId, CancellationToken ct)
    {
        var overlapping = await db.HouseOccupancies.AsNoTracking()
            .Where(o => o.HouseId == houseId && o.ReleasedAtUtc == null && o.StartDate < end && o.EndDate > start && (exceptBlockId == null || o.HouseBlockId != exceptBlockId))
            .ToListAsync(ct);
        if (overlapping.Count == 0) return null;

        var bookingIds = overlapping.Where(o => o.StayBookingId != null).Select(o => o.StayBookingId!.Value).ToList();
        if (bookingIds.Count > 0)
        {
            var bookings = await db.StayBookings.AsNoTracking().Where(b => bookingIds.Contains(b.Id)).OrderBy(b => b.CheckInDate).ToListAsync(ct);
            var list = bookings.Select(b => new BlockBookingConflictDto(b.Id, b.CheckInDate, b.CheckOutDate, b.GuestName, b.Status)).ToList();
            var first = bookings[0];
            var who = string.IsNullOrWhiteSpace(first.GuestName) ? "гостя" : first.GuestName;
            return new ConflictObjectResult(new StaysConflictDto("BlockConflictsWithBooking",
                $"Даты заняты бронью {who}, {first.CheckInDate:dd.MM}–{first.CheckOutDate:dd.MM}", Conflicts: list));
        }
        return new ConflictObjectResult(new StaysConflictDto("BlockOverlapsBlock", "Даты пересекаются с другой блокировкой"));
    }

    private async Task<BlockResult> CommitAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, HouseBlock block, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (HouseOccupancyWriter.IsOverlapViolation(ex))
        {
            // A booking slipped in between the check and the write (it cannot under the lock — this is the database's own verdict).
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new BlockResult(new ConflictObjectResult(new StaysConflictDto("BlockConflictsWithBooking", "Даты уже заняты")));
        }
        return new BlockResult(null, block);
    }

    private void AddEvent(HouseBlock block, HouseBlockEventKind kind, StayActor actor, string? before, string? after, DateTime now) =>
        db.HouseBlockEvents.Add(new HouseBlockEvent
        {
            Id = Guid.NewGuid(), HouseBlockId = block.Id, CompanyId = block.CompanyId, Kind = kind, OccurredAtUtc = now,
            ActorUserId = actor.UserId ?? string.Empty, ActorNameSnapshot = actor.NameSnapshot ?? string.Empty, BeforeJson = before, AfterJson = after,
        });

    private static string Snapshot(HouseBlock b) =>
        JsonSerializer.Serialize(new { b.StartDate, b.EndDate, kind = b.Kind.ToString(), b.Comment }, StaysCompanyService.SnapshotJson);
}
