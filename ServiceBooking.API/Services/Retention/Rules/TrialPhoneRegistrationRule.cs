using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// Cycle 18, B8 (ARCHITECTURE_CYCLE18.md §343, Д14/Д16, К3). Two independent, unconditionally-combined
/// reasons a <see cref="Core.Entities.TrialPhoneRegistration"/> row is destroyed:
///
/// 1. Age since <see cref="Core.Entities.TrialPhoneRegistration.RegisteredAtUtc"/> (the date of GRANT,
///    Д16) exceeds <see cref="RetentionPeriods.TrialPhoneRegistrationDays"/> (3 years, matching
///    п. 5.6.7 Политики конфиденциальности / п. 6.16.7 Соглашения).
/// 2. К3: the row was computed under a <see cref="Core.Entities.TrialPhoneRegistration.KeyId"/> that is
///    no longer the platform's current key (<see cref="TrialOptions.PhoneKeyId"/>). A rotated-away key
///    can never again be used to compute a comparable hash, so such a row is held with no purpose left
///    (ч. 7 ст. 5 152-ФЗ) regardless of how recently it was written.
///
/// This is NOT anonymized data (Д14 — HMAC is not on RKN order № 140's closed list of anonymization
/// methods), so every ordinary operator retention obligation applies to it, unlike most of the other
/// rules in this directory which handle already-anonymized or purely technical rows.
/// </summary>
public sealed class TrialPhoneRegistrationRule(AppDbContext db, IOptions<TrialOptions> trialOptions) : IRetentionRule
{
    public string Name => "trial-phone-registration";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).TrialPhoneRegistration;
        // No current key id configured (misconfiguration, or an environment that never set Trial:PhoneKeyId)
        // must NOT be read as "every row's key is stale" — that would mass-delete the uniqueness registry
        // outright and defeat Д6 for every phone that was ever registered. Absent a known-good current
        // key, this rule falls back to the age-only cutoff and leaves К3's key-rotation clause inert
        // until the platform actually has a key to compare against.
        var currentKeyId = trialOptions.Value.PhoneKeyId;

        IQueryable<Core.Entities.TrialPhoneRegistration> Query(Guid cursor) => db.TrialPhoneRegistrations
            .Where(r => r.Id > cursor &&
                (r.RegisteredAtUtc < cutoff || (currentKeyId != null && r.KeyId != currentKeyId)))
            .OrderBy(r => r.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, r => r.Id,
            mutate: r => db.TrialPhoneRegistrations.Remove(r),
            ctx, db, ct,
            dateOf: r => r.RegisteredAtUtc);
    }
}
