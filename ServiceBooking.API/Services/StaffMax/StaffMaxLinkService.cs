using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.StaffMax;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.StaffMax;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §498.2, §498.4, API_CONTRACT_CYCLE25.md §523 — the account side of "MAX for staff": the status the cabinet shows, the
/// one-time link session and the disconnect. No network calls: MAX only learns about a session when the person opens the link.
/// </summary>
public sealed class StaffMaxLinkService(
    AppDbContext db, StaffMaxAvailability availability, IOptions<StaffMaxOptions> options, IOptions<PhoneVerificationOptions> phoneOptions)
{
    public const string NotEligibleText = "Подключить MAX могут владельцы и сотрудники магазинов и компаний «Дома»";

    /// <summary>Kinds whose staff have positions («Дома», «Бани»): there the owner / manager gets booking messages, a housekeeper / банщик does not (ARCHITECTURE_CYCLE42.md §42.3.4).</summary>
    private static readonly CompanyKind[] PositionKinds =
        Enum.GetValues<CompanyKind>().Where(k => CompanyKindTraits.For(k).UsesStaffPositions).ToArray();

    public const string NotLinkedText = "Не подключено";
    public const string PendingText = "Ждём подтверждения в MAX…";
    public const string StoppedText = "Отключено: бот остановлен в MAX";

    /// <summary>The active shops (goods) of which the user is owner or staff, with the shop's own MAX flag.</summary>
    public async Task<List<StaffMaxShopDto>> ShopsOfAsync(string userId, CancellationToken ct)
    {
        var rows = await db.CompanyMembers.AsNoTracking().Where(CompanyMembership.IsStaffRole)
            .Where(cm => cm.UserId == userId)
            .Join(db.Companies.Where(c => c.Kind == CompanyKind.Orders && c.IsActive), cm => cm.CompanyId, c => c.Id, (cm, c) => new { c.Id, c.Name })
            .Distinct().OrderBy(c => c.Name).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var flags = await db.ShopSettings.AsNoTracking().Where(s => ids.Contains(s.CompanyId))
            .ToDictionaryAsync(s => s.CompanyId, s => s.StaffMaxEnabled, ct);
        return rows.Select(r => new StaffMaxShopDto(r.Id, r.Name, flags.GetValueOrDefault(r.Id, true))).ToList();
    }

    public async Task<StaffMaxStatusDto> GetStatusAsync(string userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var shops = await ShopsOfAsync(userId, ct);
        var link = await db.StaffMaxLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == userId, ct);
        var pending = await db.StaffMaxLinkSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.CompletedAtUtc == null && s.ExpiresAtUtc > now)
            .OrderByDescending(s => s.CreatedAtUtc).FirstOrDefaultAsync(ct);

        // A pending session wins over an existing link: while the person re-links, the cabinet must keep polling until the new chat is in.
        StaffMaxStatus status;
        string text;
        if (pending is not null) { status = StaffMaxStatus.Pending; text = PendingText; }
        else if (link is { Status: StaffMaxLinkStatus.Active }) { status = StaffMaxStatus.Linked; text = LinkedText(link.LinkedAtUtc); }
        else if (link is { Status: StaffMaxLinkStatus.StoppedInMax }) { status = StaffMaxStatus.StoppedInMax; text = StoppedText; }
        else { status = StaffMaxStatus.NotLinked; text = NotLinkedText; }

        // Cycle 39: an owner or a manager of a «Дома» company is eligible too (a housekeeper is not — the same rule as LinkAsync).
        var eligible = shops.Count > 0 || await db.CompanyMembers.AsNoTracking().Where(CompanyMembership.IsStaffRole)
            .AnyAsync(cm => cm.UserId == userId && cm.Company.IsActive && PositionKinds.Contains(cm.Company.Kind) && cm.StaffPosition != StaffPosition.Housekeeper, ct);
        return new StaffMaxStatusDto(
            availability.Enabled, availability.CanLink, availability.UnavailableText, eligible, status, text,
            link?.LinkedAtUtc, link?.StoppedAtUtc,
            pending is null ? null : new StaffMaxPendingSessionDto(pending.Id, DateTime.SpecifyKind(pending.ExpiresAtUtc, DateTimeKind.Utc)),
            shops, Math.Max(1, options.Value.PollIntervalSeconds));
    }

    /// <summary>"Подключено 30.09.2026" — the date in Moscow time (the platform's working zone).</summary>
    public static string LinkedText(DateTime linkedAtUtc)
    {
        var utc = linkedAtUtc.Kind == DateTimeKind.Utc ? linkedAtUtc : DateTime.SpecifyKind(linkedAtUtc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"));
        return "Подключено " + local.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    }

    /// <summary>Checks §523.2 in order (3 → 4 → 5) and, when all pass, replaces the user's unfinished sessions with a fresh one.</summary>
    public async Task<(string? Conflict, StaffMaxLinkSessionDto? Session)> CreateSessionAsync(string userId, CancellationToken ct)
    {
        if (!await db.CompanyMembers.Where(CompanyMembership.IsStaffRole)
                .AnyAsync(cm => cm.UserId == userId && db.Companies.Any(c => c.Id == cm.CompanyId && c.IsActive &&
                    // ARCHITECTURE_CYCLE37.md §37.3.2: a shop's staff, or the owner / a manager of a «Дома» company (a housekeeper gets no booking messages).
                    (c.Kind == CompanyKind.Orders || (PositionKinds.Contains(c.Kind) && cm.StaffPosition != StaffPosition.Housekeeper))), ct))
            return (NotEligibleText, null);
        if (!availability.Enabled) return (StaffMaxAvailability.NotEnabledText, null);
        if (!availability.CanLink) return (StaffMaxAvailability.LinkUnavailableText, null);

        var now = DateTime.UtcNow;
        var ttl = TimeSpan.FromMinutes(Math.Max(1, options.Value.LinkSessionTtlMinutes));
        var payload = StaffMaxPayload.Generate();
        var session = new StaffMaxLinkSession
        {
            Id = Guid.NewGuid(), UserId = userId, PayloadHash = StaffMaxPayload.Hash(payload), CreatedAtUtc = now, ExpiresAtUtc = now.Add(ttl)
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.StaffMaxLinkSessions.Where(s => s.UserId == userId && s.CompletedAtUtc == null).ExecuteDeleteAsync(ct);
        db.StaffMaxLinkSessions.Add(session);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var botUsername = phoneOptions.Value.Max.BotUsername ?? string.Empty;
        var deepLink = $"https://max.ru/{botUsername}?start={payload}";
        var webLink = $"https://web.max.ru/{botUsername}?start={payload}";
        string? qr;
        try { qr = Convert.ToBase64String(QrImage.EncodePng(deepLink)); }
        catch (Exception) { qr = null; } // a QR failure never blocks the link (§141 of cycle 14)

        return (null, new StaffMaxLinkSessionDto(
            session.Id, deepLink, webLink, qr, DateTime.SpecifyKind(session.ExpiresAtUtc, DateTimeKind.Utc), (int)ttl.TotalSeconds,
            Math.Max(1, options.Value.PollIntervalSeconds)));
    }

    /// <summary>Idempotent: removes the binding and the unfinished sessions; messages stop at once (the dispatcher re-checks the link before every send).</summary>
    public async Task DisconnectAsync(string userId, CancellationToken ct)
    {
        await db.StaffMaxLinks.Where(l => l.UserId == userId).ExecuteDeleteAsync(ct);
        await db.StaffMaxLinkSessions.Where(s => s.UserId == userId && s.CompletedAtUtc == null).ExecuteDeleteAsync(ct);
    }
}
