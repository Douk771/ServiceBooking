using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// T5-B8/B9 (LEGAL_REVIEW.md §13.5: "аккаунт клиента без активности" → анонимизация, ст. 196 ГК).
///
/// 🟡 Scope decision, recorded rather than silently made. The lawyer's reference point is "с последнего
/// входа/визита" — this product tracks neither a last-login timestamp nor, on <see cref="AppUser"/> itself,
/// any activity marker at all. Rather than invent one, this rule is restricted to accounts that have
/// ALREADY been reduced to nothing but a login shell by the OTHER rules and by ordinary product use:
/// no company (owned or as staff/member), no notification channel owned, no booking ever made as a
/// client, no client note or review attached to them. For such an account, <c>CreatedAt</c> is not just
/// the best available signal — with zero footprint anywhere else, it is the ONLY signal, and scrubbing it
/// needs no cascade at all (unlike <c>ProfileController.DeleteAccount</c>, which this rule deliberately
/// does not call into — see below). A client with an old, not-yet-anonymized booking is picked up by
/// <see cref="BookingPersonalizationRule"/> first and becomes eligible here on a LATER pass once that
/// booking no longer references them — normal, restart-safe eventual consistency (§49.4), not a bug.
///
/// Does not reuse <c>UserManager</c> (unlike <c>DeleteAccount</c>): <c>UserManager</c>'s high-level
/// setters (<c>SetEmailAsync</c>, <c>UpdateSecurityStampAsync</c>, ...) call <c>Store.UpdateAsync</c>,
/// which persists via its OWN <c>SaveChangesAsync</c> immediately — incompatible with this rule's
/// dry-run guarantee, which depends on nothing reaching the database until the SHARED batch's
/// <c>SaveChangesAsync</c> is called (§49.2). The fields set below are the same ones <c>DeleteAccount</c>
/// sets, done directly through the tracked entity instead.
/// </summary>
public sealed class InactiveAccountRule(AppDbContext db, FileStorage storage) : IRetentionRule
{
    public string Name => "inactive-account";

    public async Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).InactiveAccount;

        IQueryable<AppUser> Query(string cursor) => db.Users
            .Where(u => string.Compare(u.Id, cursor) > 0
                        && u.DeletedAtUtc == null
                        && u.CreatedAt < cutoff
                        && !db.Companies.Any(c => c.OwnerUserId == u.Id)
                        && !db.CompanyMembers.Any(m => m.UserId == u.Id)
                        && !db.NotificationChannels.Any(c => c.OwnerUserId == u.Id)
                        && !db.Bookings.Any(b => b.ClientId == u.Id)
                        && !db.ClientNotes.Any(n => n.ClientId == u.Id)
                        && !db.Reviews.Any(r => r.ClientId == u.Id)
                        && !db.UserRoles.Any(ur => ur.UserId == u.Id &&
                            db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "SuperAdmin")))
            .OrderBy(u => u.Id);

        var cursorId = string.Empty;
        var scanned = 0;
        var affected = 0;
        DateTime? oldest = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var batch = await Query(cursorId).Take(ctx.BatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;

            scanned += batch.Count;
            var avatarsToDelete = new List<string>();

            foreach (var user in batch)
            {
                if (oldest is null || user.CreatedAt < oldest) oldest = user.CreatedAt;

                if (user.AvatarUrl is not null) avatarsToDelete.Add(user.AvatarUrl);

                user.FirstName = "Удалённый";
                user.LastName = "пользователь";
                user.AvatarUrl = null;
                user.PasswordHash = null;
                user.LockoutEnabled = true;
                user.LockoutEnd = DateTimeOffset.MaxValue;
                user.DeletedAtUtc = ctx.NowUtc;
                user.Email = null;
                user.NormalizedEmail = null;
                user.PhoneNumber = null;
                user.UserName = $"deleted-{user.Id}";
                user.NormalizedUserName = $"deleted-{user.Id}".ToUpperInvariant();
                user.SecurityStamp = Guid.NewGuid().ToString();
            }

            if (!ctx.DryRun)
            {
                await db.SaveChangesAsync(ct);
                foreach (var avatarUrl in avatarsToDelete) storage.DeletePublic(avatarUrl);
            }
            else
            {
                db.ChangeTracker.Clear();
            }

            affected += batch.Count;
            cursorId = batch[^1].Id;

            if (batch.Count < ctx.BatchSize) break;
        }

        var mode = ctx.DryRun ? "dry" : "live";
        var summary = oldest is null
            ? $"retention[{mode}] {Name}: scanned={scanned} affected={affected}"
            : $"retention[{mode}] {Name}: scanned={scanned} affected={affected} oldest={oldest:yyyy-MM-dd}";
        return new RetentionOutcome(Name, scanned, affected, summary);
    }
}
