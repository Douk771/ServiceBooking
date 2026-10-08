using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>Who did it, as written to the journal (a SNAPSHOT of the name — the journal outlives renames and deletions).</summary>
public sealed record StayActor(StayActorKind Kind, string? UserId, string? NameSnapshot)
{
    public static StayActor System { get; } = new(StayActorKind.System, null, "Система");
    public static StayActor AnonymousGuest { get; } = new(StayActorKind.Guest, null, "Гость");
}

public class StayActorResolver(AppDbContext db)
{
    public async Task<StayActor> ResolveStaffAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = userId is null ? null : await NameOfAsync(userId, ct);
        return new StayActor(user.IsInRole("SuperAdmin") ? StayActorKind.SuperAdmin : StayActorKind.Staff, userId, name ?? "Сотрудник");
    }

    /// <summary>A guest: a signed-in customer is named, an anonymous one is just "Гость".</summary>
    public async Task<StayActor> ResolveGuestAsync(ClaimsPrincipal user, string? guestName, CancellationToken ct = default)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return new StayActor(StayActorKind.Guest, null, guestName ?? "Гость");
        return new StayActor(StayActorKind.Customer, userId, guestName ?? await NameOfAsync(userId, ct) ?? "Гость");
    }

    public async Task<string?> NameOfAsync(string userId, CancellationToken ct = default)
    {
        var u = await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => new { x.FirstName, x.LastName }).FirstOrDefaultAsync(ct);
        return u is null ? null : $"{u.FirstName} {u.LastName}".Trim();
    }
}

public static class StayPhone
{
    /// <summary>`+7 (9••) •••-••-12` — the phone as a guest sees it on the booking page (the full number only goes to staff with ViewBookings).</summary>
    public static string? Mask(string? canonicalPhone)
    {
        if (string.IsNullOrEmpty(canonicalPhone)) return null;
        var digits = new string(canonicalPhone.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 11 && digits[0] is '7' or '8') return $"+7 ({digits[1]}••) •••-••-{digits[9..11]}";
        return digits.Length <= 2 ? "••" : "••••" + digits[^2..];
    }
}
