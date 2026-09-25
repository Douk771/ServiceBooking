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
        // N1 (code review) — string.IsNullOrWhiteSpace, matching every other consumer of this same
        // setting (TrialActivationService.GrantAsync, DeploymentSafetyChecks.ValidateTrialSecrets). A
        // bare `!= null` reads Trial:PhoneKeyId = "" as "configured", which would then treat every row's
        // KeyId as stale and mass-delete the whole uniqueness registry via the К3 clause below — the
        // exact opposite of the "no known-good key → leave К3 inert" fallback this comment documents.
        var currentKeyId = string.IsNullOrWhiteSpace(trialOptions.Value.PhoneKeyId) ? null : trialOptions.Value.PhoneKeyId;

        IQueryable<Core.Entities.TrialPhoneRegistration> Query(Guid cursor) => db.TrialPhoneRegistrations
            .Where(r => r.Id > cursor &&
                (r.RegisteredAtUtc < cutoff || (currentKeyId != null && r.KeyId != currentKeyId)))
            .OrderBy(r => r.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, r => r.Id,
            mutate: r => db.TrialPhoneRegistrations.Remove(r),
            ctx, db, ct,
            dateOf: r => r.RegisteredAtUtc,
            // §343.2 п.2 / §351 п.18 — a row can match both clauses (old AND rotated); "removed for
            // rotation" is reported whenever the key-rotation clause is what makes it stale, regardless
            // of whether the age cutoff also would have caught it independently.
            isRotationRemoval: r => currentKeyId != null && r.KeyId != currentKeyId);
    }
}
