using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

public sealed record ScheduleWriteResult(string? Error, ScheduleSaveResultDto? Result = null);

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.2.3, §39.3.3, API_CONTRACT_CYCLE39.md §39.28 — the ONLY writer of the weekly template and of the manual dates of a service. Under the lock of the service
/// (a schedule edit and a booking of a session never interleave); every change is a journal row (before/after) and a bump of the board revision. A save NEVER cancels a session:
/// the sessions that fall outside the new windows are returned as a warning and stay valid.
/// </summary>
public class ServiceScheduleWriter(AppDbContext db, ServiceSlotService slots, ServiceSessionWriter sessionWriter, StayBookingEventLog bookingLog, IStaysClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<WeeklyScheduleDto> WeeklyAsync(Guid serviceId, CancellationToken ct)
    {
        var rows = (await db.StayServiceWeeklyWindows.AsNoTracking().Where(w => w.ServiceId == serviceId).ToListAsync(ct)).ToLookup(w => w.DayOfWeek);
        return new WeeklyScheduleDto(Enumerable.Range(1, 7).Select(d => new WeeklyDayDto(d, ServiceCatalogService.DayLabel(d),
            rows[d].OrderBy(w => w.StartMinute).Select(w => WindowDto(w.StartMinute, w.EndMinute)).ToList())).ToList());
    }

    public static ServiceWindowDto WindowDto(int start, int end) => new(start, end, ServiceTimeFormat.Window(start, end));

    public async Task<ScheduleWriteResult> SaveWeeklyAsync(ServiceScope scope, WeeklyScheduleInput input, StayActor actor, CancellationToken ct)
    {
        var days = input.Days ?? [];
        if (days.Count != 7 || days.Select(d => d.DayOfWeek).OrderBy(x => x).Zip(Enumerable.Range(1, 7)).Any(p => p.First != p.Second)) return new("Передайте расписание на все 7 дней");
        var b = slots.BusinessDayStart;
        foreach (var d in days)
        {
            var windows = (d.Windows ?? []).Select(w => new WindowSpec(w.StartMinute, w.EndMinute)).ToList();
            var check = ServiceScheduleRules.Validate(windows, b);
            if (!check.Ok) return new(ServiceScheduleRules.Message(check, ServiceCatalogService.DayLabel(d.DayOfWeek), b));
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await sessionWriter.LockServiceAsync(scope.Service.Id);
        var before = await WeeklyAsync(scope.Service.Id, ct);
        await db.StayServiceWeeklyWindows.Where(w => w.ServiceId == scope.Service.Id).ExecuteDeleteAsync(ct);
        foreach (var d in days)
            foreach (var w in d.Windows ?? [])
                db.StayServiceWeeklyWindows.Add(new StayServiceWeeklyWindow { Id = Guid.NewGuid(), ServiceId = scope.Service.Id, DayOfWeek = d.DayOfWeek, StartMinute = w.StartMinute, EndMinute = w.EndMinute });
        await db.SaveChangesAsync(ct);
        var after = await WeeklyAsync(scope.Service.Id, ct);
        await AppendAsync(scope, StayServiceScheduleEventKind.WeeklyTemplateChanged, null, before, after, actor);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(null, await ResultAsync(scope, after, null, ct));
    }

    public async Task<ScheduleWriteResult> SetOverrideAsync(ServiceScope scope, DateOnly date, DateOverrideInput input, StayActor actor, CancellationToken ct)
    {
        if (date < slots.TodayOf(scope.Company)) return new(ServiceTexts.PastDate);
        var comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        if (comment is { Length: > 300 }) return new("Комментарий — не длиннее 300 символов");
        var windows = input.Closed ? [] : (input.Windows ?? []).Select(w => new WindowSpec(w.StartMinute, w.EndMinute)).ToList();
        if (!input.Closed && windows.Count == 0) return new("Укажите окна или закройте день");
        var b = slots.BusinessDayStart;
        var check = ServiceScheduleRules.Validate(windows, b);
        if (!check.Ok) return new(ServiceScheduleRules.Message(check, ServiceTimeFormat.BusinessDateLabel(date), b));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await sessionWriter.LockServiceAsync(scope.Service.Id);
        var existing = await db.StayServiceDateOverrides.FirstOrDefaultAsync(o => o.ServiceId == scope.Service.Id && o.BusinessDate == date, ct);
        var before = existing is null ? null : OverrideSnapshot(existing);
        if (existing is null)
        {
            existing = new StayServiceDateOverride { Id = Guid.NewGuid(), ServiceId = scope.Service.Id, BusinessDate = date };
            db.StayServiceDateOverrides.Add(existing);
        }
        existing.IsClosed = input.Closed;
        existing.WindowsJson = ServiceJson.Windows(ServiceScheduleRules.Sorted(windows));
        existing.Comment = comment;
        existing.UpdatedAtUtc = clock.UtcNow;
        existing.UpdatedByUserId = actor.UserId;
        await AppendAsync(scope, StayServiceScheduleEventKind.DateOverrideSet, date, before, OverrideSnapshot(existing), actor);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(null, await ResultAsync(scope, null, date, ct));
    }

    public async Task<ScheduleWriteResult> RemoveOverrideAsync(ServiceScope scope, DateOnly date, StayActor actor, CancellationToken ct)
    {
        if (date < slots.TodayOf(scope.Company)) return new(ServiceTexts.PastDate);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await sessionWriter.LockServiceAsync(scope.Service.Id);
        var existing = await db.StayServiceDateOverrides.FirstOrDefaultAsync(o => o.ServiceId == scope.Service.Id && o.BusinessDate == date, ct);
        if (existing is not null)
        {
            var before = OverrideSnapshot(existing);
            db.StayServiceDateOverrides.Remove(existing);
            await AppendAsync(scope, StayServiceScheduleEventKind.DateOverrideRemoved, date, before, null, actor);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new(null, await ResultAsync(scope, null, date, ct));
    }

    private static object OverrideSnapshot(StayServiceDateOverride o) =>
        new { closed = o.IsClosed, windows = ServiceJson.ReadWindows(o.WindowsJson), comment = o.Comment };

    private async Task AppendAsync(ServiceScope scope, StayServiceScheduleEventKind kind, DateOnly? date, object? before, object? after, StayActor actor)
    {
        db.StayServiceScheduleEvents.Add(new StayServiceScheduleEvent
        {
            Id = Guid.NewGuid(), ServiceId = scope.Service.Id, CompanyId = scope.Company.Id, Kind = kind, BusinessDate = date, OccurredAtUtc = clock.UtcNow, ActorUserId = actor.UserId,
            ActorNameSnapshot = actor.NameSnapshot, BeforeJson = JsonSerializer.Serialize(before, Json), AfterJson = JsonSerializer.Serialize(after, Json),
        });
        await bookingLog.BumpRevisionAsync(scope.Company.Id);
    }

    // ── result, month, sessions outside the windows ──

    private async Task<ScheduleSaveResultDto> ResultAsync(ServiceScope scope, WeeklyScheduleDto? weekly, DateOnly? day, CancellationToken ct)
    {
        var outside = await OutsideSessionsAsync(scope, ct);
        ServiceMonthDayDto? dayDto = null;
        if (day is { } d) dayDto = (await MonthAsync(scope, new DateOnly(d.Year, d.Month, 1), ct)).Days.First(x => x.BusinessDate == d);
        return new ScheduleSaveResultDto(weekly, dayDto, outside, outside.Count > 0 ? ServiceTexts.SessionsOutsideWarning(outside.Count) : null);
    }

    /// <summary>Active future sessions the new schedule no longer covers: they stay valid, the owner is only warned.</summary>
    public async Task<List<OutsideSessionDto>> OutsideSessionsAsync(ServiceScope scope, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var future = await db.StayServiceSessions.AsNoTracking().Where(s => s.ServiceId == scope.Service.Id && s.ReleasedAtUtc == null && s.StartUtc > now).OrderBy(s => s.StartUtc).ToListAsync(ct);
        if (future.Count == 0) return [];
        var from = future.Min(s => s.BusinessDate);
        var to = future.Max(s => s.BusinessDate);
        var windows = await slots.WindowsAsync(scope.Service.Id, from, to, ct);
        var bookingIds = future.Where(s => s.StayBookingId != null).Select(s => s.StayBookingId!.Value).Distinct().ToList();
        var houses = await (from b in db.StayBookings.AsNoTracking() join h in db.Houses.AsNoTracking() on b.HouseId equals h.Id where bookingIds.Contains(b.Id) select new { b.Id, h.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return future.Where(s => !ServiceScheduleRules.Covers(windows[s.BusinessDate], s.StartMinute, s.StartMinute + 60 * s.Hours))
            .Select(s => new OutsideSessionDto(s.Id, ServiceTimeFormat.Staff(s.BusinessDate, s.StartMinute, s.Hours), s.StayBookingId is { } b && houses.TryGetValue(b, out var n) ? n : null)).ToList();
    }

    public static bool TryMonth(string? raw, out DateOnly first) =>
        DateOnly.TryParseExact((raw ?? string.Empty) + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out first);

    public async Task<ServiceMonthDto> MonthAsync(ServiceScope scope, DateOnly first, CancellationToken ct)
    {
        var last = first.AddMonths(1).AddDays(-1);
        var overrides = await db.StayServiceDateOverrides.AsNoTracking().Where(o => o.ServiceId == scope.Service.Id && o.BusinessDate >= first && o.BusinessDate <= last).ToDictionaryAsync(o => o.BusinessDate, ct);
        var windows = await slots.WindowsAsync(scope.Service.Id, first, last, ct);
        var userIds = overrides.Values.Select(o => o.UpdatedByUserId).Where(u => u != null).Distinct().ToList();
        var names = (await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FirstName, u.LastName }).ToListAsync(ct)).ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());
        var days = new List<ServiceMonthDayDto>();
        for (var d = first; d <= last; d = d.AddDays(1))
        {
            var o = overrides.GetValueOrDefault(d);
            var source = o is null ? DateSource.Template : o.IsClosed ? DateSource.Closed : DateSource.Override;
            days.Add(new ServiceMonthDayDto(d, source, windows[d].Select(w => WindowDto(w.StartMinute, w.EndMinute)).ToList(), o?.Comment,
                o?.UpdatedByUserId is { } u && names.TryGetValue(u, out var n) ? n : null, o?.UpdatedAtUtc));
        }
        return new ServiceMonthDto(scope.Service.Id, $"{first:yyyy-MM}", slots.TodayOf(scope.Company), days);
    }
}
