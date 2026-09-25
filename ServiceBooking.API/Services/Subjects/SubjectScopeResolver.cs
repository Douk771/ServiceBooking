using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Subjects;

/// <summary>
/// TD-03's single gate (ARCHITECTURE_CYCLE16.md §245.4, R1). Every account-scoped endpoint that used to
/// build its own <c>canonicalPhone</c> local variable calls <see cref="ForAccountAsync"/> instead — one
/// indexed <c>EXISTS</c> against <see cref="AppDbContext.VerifiedPhones"/>, the source of truth
/// (§243.1: the mirror <c>AppUser.PhoneNumberConfirmed</c> has drifted from it once already, so it is
/// never read here). Scoped service (needs the request's <see cref="AppDbContext"/>), registered in
/// Program.cs alongside the controller's other scoped dependencies.
/// </summary>
public class SubjectScopeResolver(AppDbContext db)
{
    /// <summary>
    /// Resolves the calling account's scope. Safe to call with any <see cref="AppUser"/> — an account
    /// with no phone at all gets <c>OwnPhone: null, GuestMatchPhone: null</c>, which every predicate
    /// downstream already treats as "phone-matching branch never applies" (it always guarded on
    /// <c>canonicalPhone != null</c> before this resolver existed).
    /// </summary>
    public async Task<SubjectScope> ForAccountAsync(AppUser user, CancellationToken ct = default)
    {
        var ownPhone = user.PhoneNumber;
        if (ownPhone is null)
            return new SubjectScope(user.Id, OwnPhone: null, GuestMatchPhone: null);

        var isVerifiedByThisAccount = await db.VerifiedPhones.AsNoTracking()
            .AnyAsync(v => v.UserId == user.Id && v.Phone == ownPhone, ct);  // SUBJECT-PHONE-GATE: gated — this IS the resolver computing the gate itself (ARCHITECTURE_CYCLE16.md §245.4)

        return new SubjectScope(user.Id, ownPhone, GuestMatchPhone: isVerifiedByThisAccount ? ownPhone : null);
    }
}
