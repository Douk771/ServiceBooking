using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Slots;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.8.1, API_CONTRACT_CYCLE42.md §42.30 — the only writer of service positions (both «Дома» and «Бани») and of the journal of "this is not alcohol or tobacco"
/// confirmations (Т42-05). The form checks, the words of §42.8.2 (400), the soft alcohol/tobacco filter (409 + confirmation) and the journal row go in one transaction.
/// </summary>
public class ServiceItemWriter(AppDbContext db, LegalDocumentProvider legalProvider, IOptions<StaysOptions> options, IStaysClock clock)
{
    public const string NoticeKey = LegalTextKey.BathPositionsOwnerNotice;
    public const string NoticeFallback =
        "Через сервис нельзя продавать алкоголь и табачные изделия (в том числе кальяны). Добавляйте только то, что вы вправе продавать без лицензии: веники, чай, полотенца, простыни.";

    public sealed record Result(ServiceItemDto? Item, string? BadRequest, StaysServiceConflictDto? Conflict, bool NotFound = false);

    public static string? FormError(ServiceItemInput input, out string name)
    {
        name = (input.Name ?? string.Empty).Trim();
        if (name.Length is < 1 or > 100) return "Название позиции — от 1 до 100 символов";
        if (input.PriceRub is < 0 or > 100_000) return "Цена — от 0 до 100 000 ₽";
        if (input.MaxPerSession is < 1 or > 50) return "Максимум на сеанс — от 1 до 50";
        return null;
    }

    public static ServiceItemDto ToDto(StayServiceItem i) =>
        new(i.Id, i.Name, i.PriceRub, i.MaxPerSession, i.IsActive, i.Position, OwnerTextChecks.Check(i.Name).Warnings.ToList());

    /// <summary>The name for comparison of "the same name that was already confirmed": lower case, ё = е, whitespace collapsed.</summary>
    public static string NormalizeName(string name) =>
        string.Join(' ', name.Trim().ToLowerInvariant().Replace('ё', 'е').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public string NoticeVersion() =>
        legalProvider.Current?.GetText(NoticeKey)?.Version
        ?? "fallback:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(NoticeFallback))).ToLowerInvariant();

    public string NoticeText() => legalProvider.Current?.GetText(NoticeKey)?.ContentHtml ?? NoticeFallback;

    public async Task<Result> AddAsync(StayService service, ServiceItemInput input, StayActor actor, string? ip, CancellationToken ct)
    {
        var (error, name, markers) = Precheck(input);
        if (error is not null) return new(null, error, null);
        if (markers.Length > 0 && input.ConfirmRestricted != true) return new(null, null, RestrictedConflict(markers));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"service-items:{service.Id}");
        var count = await db.StayServiceItems.CountAsync(i => i.ServiceId == service.Id, ct);
        var max = options.Value.Services.MaxItemsPerService;
        if (count >= max)
            return new(null, null, new StaysServiceConflictDto(nameof(StaysServiceConflictCode.ItemLimitReached), $"У услуги может быть не больше {max} позиций"));
        var position = (await db.StayServiceItems.Where(i => i.ServiceId == service.Id).MaxAsync(i => (int?)i.Position, ct) ?? -1) + 1;
        var item = new StayServiceItem { Id = Guid.NewGuid(), ServiceId = service.Id, Name = name, PriceRub = input.PriceRub, MaxPerSession = input.MaxPerSession, IsActive = input.IsActive, Position = position };
        db.StayServiceItems.Add(item);
        if (markers.Length > 0) AddConfirmation(service, item, markers, actor, ip);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(ToDto(item), null, null);
    }

    public async Task<Result> UpdateAsync(StayService service, Guid itemId, ServiceItemInput input, StayActor actor, string? ip, CancellationToken ct)
    {
        var (error, name, markers) = Precheck(input);
        if (error is not null) return new(null, error, null);
        var item = await db.StayServiceItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ServiceId == service.Id, ct);
        if (item is null) return new(null, null, null, NotFound: true);

        var needsJournal = false;
        if (markers.Length > 0)
        {
            // The same (normalized) name that this position already has confirmed does not ask again (§42.30.1 п. 4).
            var alreadyConfirmed = await AlreadyConfirmedAsync(itemId, name, ct);
            if (!alreadyConfirmed && input.ConfirmRestricted != true) return new(null, null, RestrictedConflict(markers));
            needsJournal = !alreadyConfirmed;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        item.Name = name;
        item.PriceRub = input.PriceRub;
        item.MaxPerSession = input.MaxPerSession;
        item.IsActive = input.IsActive;
        if (needsJournal) AddConfirmation(service, item, markers, actor, ip);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(ToDto(item), null, null);
    }

    /// <summary>Always allowed: a session keeps the snapshot of its positions; the journal keeps its rows (ItemId becomes NULL).</summary>
    public async Task<bool> DeleteAsync(Guid serviceId, Guid itemId, CancellationToken ct)
    {
        var item = await db.StayServiceItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ServiceId == serviceId, ct);
        if (item is null) return false;
        db.StayServiceItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return true;
    }

    // The order of the contract: form → forbidden words (400) → the filter (the caller decides about the confirmation).
    private static (string? Error, string Name, string[] Markers) Precheck(ServiceItemInput input)
    {
        var error = FormError(input, out var name);
        if (error is not null) return (error, name, []);
        if (OwnerTextChecks.Check(name).HasErrors) return (OwnerTextChecks.ErrorText, name, []);
        return (null, name, RestrictedItemFilter.Match(name));
    }

    private StaysServiceConflictDto RestrictedConflict(string[] markers) =>
        new(nameof(StaysServiceConflictCode.ItemRestrictedConfirmationRequired),
            $"Похоже на алкоголь или табак: {string.Join(", ", markers)}. Через сервис их продавать нельзя. Если это другая позиция — подтвердите",
            null, markers.ToList(), NoticeText());

    private async Task<bool> AlreadyConfirmedAsync(Guid itemId, string name, CancellationToken ct)
    {
        var normalized = NormalizeName(name);
        var snapshots = await db.StayServiceItemConfirmations.AsNoTracking().Where(c => c.ItemId == itemId).Select(c => c.ItemNameSnapshot).ToListAsync(ct);
        return snapshots.Any(s => NormalizeName(s) == normalized);
    }

    private void AddConfirmation(StayService service, StayServiceItem item, string[] markers, StayActor actor, string? ip) =>
        db.StayServiceItemConfirmations.Add(new StayServiceItemConfirmation
        {
            Id = Guid.NewGuid(), CompanyId = service.CompanyId, ServiceId = service.Id, ServiceNameSnapshot = service.Name, ItemId = item.Id, ItemNameSnapshot = item.Name,
            MarkersHit = string.Join(", ", markers), NoticeKey = NoticeKey, NoticeVersion = NoticeVersion(),
            ConfirmedByUserId = actor.UserId ?? string.Empty, ConfirmedByNameSnapshot = actor.NameSnapshot ?? "Сотрудник",
            ConfirmedAtUtc = clock.UtcNow, IpAddress = ip is { Length: > 64 } ? ip[..64] : ip,
        });
}
