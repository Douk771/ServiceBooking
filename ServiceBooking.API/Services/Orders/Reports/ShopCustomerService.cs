using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Reports;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders.Reports;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §504, API_CONTRACT_CYCLE25.md §529–§530 — the customer card of a shop and the shop's note about a customer. A customer is
/// <c>(shop, canonical phone)</c> over the NOT erased orders of THIS shop only (Q-25-6); the address of the card is the id of ANY order of that customer in
/// that shop (<c>customerRef</c>), so neither the phone nor a new secret appears in a URL. A ref of another shop's order, an erased one or an unknown one is
/// the same "not found" — never an oracle.
/// </summary>
public sealed class ShopCustomerService(
    AppDbContext db, OrderReportQueries queries, IConfiguration configuration)
{
    public const string NoteInvalidCharsText = "Текст заметки содержит недопустимые символы";
    public const string NoteTooLongText = "Заметка — не длиннее 1000 символов";
    public const int MaxNoteLength = 1000;
    public const string DeletedAuthorName = "Удалённый пользователь";

    public int CustomerOrdersPageSize => Math.Max(1, configuration.GetValue("Orders:CustomerOrdersPageSize", 20));

    /// <summary>The canonical phone of the customer behind <paramref name="customerRef"/> in this shop, or null (404).</summary>
    public async Task<string?> ResolvePhoneAsync(Guid shopId, Guid customerRef, CancellationToken ct) =>
        await db.Orders.AsNoTracking()
            .Where(o => o.Id == customerRef && o.CompanyId == shopId && !o.PersonalDataErased && o.CustomerPhone != null)
            .Select(o => o.CustomerPhone).FirstOrDefaultAsync(ct);

    private IQueryable<Order> OrdersOf(Guid shopId, string phone) =>
        db.Orders.AsNoTracking().Where(o => o.CompanyId == shopId && !o.PersonalDataErased && o.CustomerPhone == phone); // SUBJECT-PHONE-GATE: not-account-scoped — shop's own customer card (US-25-09)

    // ── the card ────────────────────────────────────────────────────────────────────────────────────────────

    public async Task<ShopCustomerCardDto?> CardAsync(Company shop, Guid customerRef, int page, CancellationToken ct)
    {
        var phone = await ResolvePhoneAsync(shop.Id, customerRef, ct);
        if (phone is null) return null;

        var orders = OrdersOf(shop.Id, phone);
        var aggregates = await queries.StatusAggregatesAsync(orders, ct);
        int Count(OrderStatus s) => aggregates.FirstOrDefault(a => a.Status == s)?.Count ?? 0;
        var issued = aggregates.FirstOrDefault(a => a.Status == OrderStatus.Issued);
        var total = aggregates.Sum(a => a.Count);

        var span = await orders.GroupBy(_ => 1).Select(g => new { First = g.Min(o => o.PickupDate), Last = g.Max(o => o.PickupDate) }).FirstOrDefaultAsync(ct);
        var name = await orders.OrderByDescending(o => o.CreatedAtUtc).Select(o => o.CustomerName).FirstOrDefaultAsync(ct);
        // Only the positive mark: a paid-for account order with a confirmed number (like the salon's client card).
        var verified = await orders.AnyAsync(o => o.CustomerKind == OrderActorKind.Customer && o.CustomerPhoneVerified, ct);

        var zone = ShopGateLoader.ZoneOf(shop);
        var pageSize = CustomerOrdersPageSize;
        var rows = await queries.PageAsync(orders, OrderHistorySort.PickupDesc, page, pageSize, ct);
        var note = await db.ShopCustomerNotes.AsNoTracking().FirstOrDefaultAsync(n => n.CompanyId == shop.Id && n.Phone == phone, ct); // SUBJECT-PHONE-GATE: not-account-scoped — shop's own customer note (US-25-10)

        return new ShopCustomerCardDto(
            customerRef, name, "+" + phone, PhoneDisplay(phone), verified, total, issued?.Count ?? 0, issued?.Amount ?? 0m,
            Count(OrderStatus.CancelledByCustomer), Count(OrderStatus.NotPickedUp), span?.First, span?.Last,
            StatsText(total, issued?.Count ?? 0, issued?.Amount ?? 0m, Count(OrderStatus.CancelledByCustomer), Count(OrderStatus.NotPickedUp)),
            new ShopCustomerOrdersPageDto(rows.Select(r => ShopReportService.ToRowDto(r, zone, includePhone: false)).ToList(), page, pageSize, total),
            note is null ? null : ToDto(note, zone));
    }

    /// <summary>"14 заказов · выдано 11 на 6 120 ₽ · отменено покупателем 1 · не забрано 1" — impersonal (the customer's gender is not guessed); zero counters of cancellations are left out.</summary>
    public static string StatsText(int total, int issuedCount, decimal issuedAmount, int cancelledByCustomer, int notPickedUp)
    {
        var parts = new List<string>
        {
            $"{total} {ShopTimeTexts.Plural(total, "заказ", "заказа", "заказов")}",
            $"выдано {issuedCount} на {OrderTexts.Money(issuedAmount)}"
        };
        if (cancelledByCustomer > 0) parts.Add($"отменено покупателем {cancelledByCustomer}");
        if (notPickedUp > 0) parts.Add($"не забрано {notPickedUp}");
        return string.Join(" · ", parts);
    }

    /// <summary>"+7 999 123-45-67" for a Russian number; otherwise "+" and the digits.</summary>
    public static string PhoneDisplay(string canonical) =>
        canonical.Length == 11 && canonical[0] == '7'
            ? $"+7 {canonical[1..4]} {canonical[4..7]}-{canonical[7..9]}-{canonical[9..11]}"
            : "+" + canonical;

    // ── the note ────────────────────────────────────────────────────────────────────────────────────────────

    public async Task<(bool Found, ShopCustomerNoteDto? Note)> GetNoteAsync(Company shop, Guid customerRef, CancellationToken ct)
    {
        var phone = await ResolvePhoneAsync(shop.Id, customerRef, ct);
        if (phone is null) return (false, null);
        var note = await db.ShopCustomerNotes.AsNoTracking().FirstOrDefaultAsync(n => n.CompanyId == shop.Id && n.Phone == phone, ct); // SUBJECT-PHONE-GATE: not-account-scoped — shop's own customer note (US-25-10)
        return (true, note is null ? null : ToDto(note, ShopGateLoader.ZoneOf(shop)));
    }

    /// <summary>Writes the note: trimmed; longer than 1000 → <see cref="NoteTooLongText"/>; empty deletes it; otherwise creates or replaces it (the last write wins).</summary>
    public async Task<(bool Found, string? Error, ShopCustomerNoteDto? Note)> PutNoteAsync(
        Company shop, Guid customerRef, string? text, string userId, CancellationToken ct)
    {
        var phone = await ResolvePhoneAsync(shop.Id, customerRef, ct);
        if (phone is null) return (false, null, null);

        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length > MaxNoteLength) return (true, NoteTooLongText, null);
        if (trimmed.Contains('\0')) return (true, NoteInvalidCharsText, null);

        var existing = await db.ShopCustomerNotes.FirstOrDefaultAsync(n => n.CompanyId == shop.Id && n.Phone == phone, ct); // SUBJECT-PHONE-GATE: not-account-scoped — shop's own customer note (US-25-10)
        if (trimmed.Length == 0)
        {
            if (existing is not null)
            {
                db.ShopCustomerNotes.Remove(existing);
                await db.SaveChangesAsync(ct);
            }
            return (true, null, null);
        }

        var author = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.FirstName, u.LastName }).FirstOrDefaultAsync(ct);
        var authorName = author is null ? "Сотрудник" : $"{author.FirstName} {author.LastName}".Trim();
        if (authorName.Length == 0) authorName = "Сотрудник";

        var now = DateTime.UtcNow;
        if (existing is null)
        {
            existing = new ShopCustomerNote { Id = Guid.NewGuid(), CompanyId = shop.Id, Phone = phone, CreatedAtUtc = now };
            db.ShopCustomerNotes.Add(existing);
        }
        existing.Text = trimmed;
        existing.UpdatedAtUtc = now;
        existing.UpdatedByUserId = userId;
        existing.UpdatedByName = authorName.Length > 200 ? authorName[..200] : authorName;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Two staff members created the FIRST note for the same customer at the same moment: the unique (shop, phone) index refused the second.
            // The last write wins — replace the row the other one created (or create it again if that one is already gone).
            var authorLabel = existing.UpdatedByName;
            db.ChangeTracker.Clear();
            var winner = await db.ShopCustomerNotes.FirstOrDefaultAsync(n => n.CompanyId == shop.Id && n.Phone == phone, ct); // SUBJECT-PHONE-GATE: not-account-scoped — shop's own customer note (US-25-10)
            if (winner is null)
            {
                winner = new ShopCustomerNote { Id = Guid.NewGuid(), CompanyId = shop.Id, Phone = phone, CreatedAtUtc = now };
                db.ShopCustomerNotes.Add(winner);
            }
            winner.Text = trimmed;
            winner.UpdatedAtUtc = now;
            winner.UpdatedByUserId = userId;
            winner.UpdatedByName = authorLabel;
            await db.SaveChangesAsync(ct);
            existing = winner;
        }
        return (true, null, ToDto(existing, ShopGateLoader.ZoneOf(shop)));
    }

    /// <summary>"Изменено: Иван Петров, 30 сен 11:40" — the time in the shop's zone.</summary>
    public static ShopCustomerNoteDto ToDto(ShopCustomerNote note, TimeZoneInfo zone)
    {
        var utc = note.UpdatedAtUtc.Kind == DateTimeKind.Utc ? note.UpdatedAtUtc : DateTime.SpecifyKind(note.UpdatedAtUtc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var when = $"{ShopTimeTexts.DayMonth(DateOnly.FromDateTime(local))} {ShopTimeTexts.Hhmm(local.Hour * 60 + local.Minute)}";
        return new ShopCustomerNoteDto(note.Text, utc, note.UpdatedByName, $"Изменено: {note.UpdatedByName}, {when}");
    }
}
