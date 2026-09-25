namespace ServiceBooking.API.Services.Retention;

/// <summary>
/// One retention/redaction rule (ARCHITECTURE_CYCLE5.md §49.1). The fourth <see cref="Scheduling.IScheduledTask"/>
/// (<c>Tasks.DataRetentionTask</c>) does not know any rule's business logic — it only enumerates
/// whatever was registered, exactly like <see cref="Scheduling.ScheduledTaskRunner"/> does for tasks.
/// </summary>
public interface IRetentionRule
{
    /// <summary>Stable identifier used in the per-rule log line and summary (§49.2) — "notification-body",
    /// "client-note", etc. Not shown to end users.</summary>
    string Name { get; }

    /// <summary>
    /// Runs one full pass of this rule. Implementations MUST build exactly one selection query and reuse
    /// it for both dry-run and real mode (§49.2) — the only allowed difference between the two modes is
    /// whether <c>SaveChangesAsync</c> is actually called. See <see cref="RetentionRuleRunner"/>, which
    /// every rule in this codebase uses to guarantee that by construction rather than by discipline.
    /// </summary>
    Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct);
}

/// <summary>Everything a rule needs to run one pass, and nothing it can reach around (§49.4: "тестирование
/// без ожидания реального времени" — <see cref="NowUtc"/> is a parameter, never <c>DateTime.UtcNow</c> read
/// directly by a rule).</summary>
public readonly record struct RetentionContext(DateTime NowUtc, RetentionPeriods Periods, int BatchSize, bool DryRun);

/// <summary>Result of one rule's pass — the numbers the per-rule log line and <c>ScheduledTaskState.LastSummary</c>
/// are built from. Never carries subject content (§49.2: "телефонов, текстов, идентификаторов субъектов —
/// никогда").
///
/// <see cref="Affected"/> deliberately always equals <see cref="Scanned"/> in this codebase's rules
/// (code review, "заодно"): every one of them selects EXACTLY the rows it is about to mutate, with no
/// further filtering after the query runs — there is no rule shaped like "scanned 500, only 300 turned
/// out to need changing". That is exactly what makes a dry-run's "Affected" a true, literal answer to
/// "how many rows would this change if run for real right now" (§49.2), not an approximation. Kept as
/// two separate fields anyway, not collapsed into one: a future rule with a genuine two-step
/// select-then-filter shape (none exists today) would then have a place to report the distinction
/// without a breaking change to this struct.</summary>
public readonly record struct RetentionOutcome(string Name, int Scanned, int Affected, string Summary)
{
    /// <summary>TD-04 (ARCHITECTURE_CYCLE16.md §246.3): true when this rule is not configured (its
    /// retention period is 0) and deliberately did nothing this pass. Additive <c>init</c> property on
    /// top of the existing positional constructor — every rule's existing call site compiles unchanged
    /// and defaults to <c>false</c> (not skipped), which is exactly the pre-cycle-16 behavior.</summary>
    public bool Skipped { get; init; }
}
