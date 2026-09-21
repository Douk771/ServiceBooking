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
/// никогда").</summary>
public readonly record struct RetentionOutcome(string Name, int Scanned, int Affected, string Summary);
