using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ServiceBooking.API.Services.Demo;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §580 step 3 — which tables the demo reset empties. The list is built from the EF model, not written by hand: every table of the
/// model EXCEPT the short allow-list below, so a table added in a later cycle is wiped by the reset on its own (and cannot leak a visitor's data into the next day).
///
/// The allow-list is what must survive: the migration history, the city directory, the Identity roles, the tariff catalog (plans, options and their rules),
/// and the platform settings (which carry the demo's own mark <c>instance.kind</c> and the time of the last reset). One table is added to the architecture's list:
/// <c>ScheduledTaskStates</c> — the reset runs AS a scheduled task, which has already written its own state row, and wiping the state of every other task
/// would make all of them fire at once after each reset.
///
/// A kept table must never reference a wiped one (the wipe would fail on the foreign key); <see cref="KeptTablesReferencingWiped"/> makes that checkable from
/// the model alone (a unit test does).
/// </summary>
public static class DemoResetTables
{
    public static readonly IReadOnlySet<string> Kept = new HashSet<string>(StringComparer.Ordinal)
    {
        "__EFMigrationsHistory",
        "Cities",
        "AspNetRoles",
        "SubscriptionPlanConfigs",
        "SubscriptionOptions",
        "PlanOptionRules",
        "PlatformSettings",
        "ScheduledTaskStates",
    };

    /// <summary>Every table of the model that is not on the allow-list, schema-qualified when the model names a schema, in a stable order.</summary>
    public static IReadOnlyList<string> TablesToWipe(IModel model) =>
        model.GetEntityTypes()
            .Where(e => e.GetTableName() is not null && !Kept.Contains(e.GetTableName()!))
            .Select(e => Qualified(e.GetSchema(), e.GetTableName()!))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// One statement for all tables: a foreign key between two tables of the same TRUNCATE is fine, so no CASCADE is needed (and deliberately not used: a kept table
    /// that referenced a wiped one would fail the statement — and roll the whole reset back — instead of being wiped silently).
    /// </summary>
    public static string TruncateSql(IReadOnlyList<string> tables)
    {
        if (tables.Count == 0) throw new ArgumentException("Nothing to wipe.", nameof(tables));
        return $"TRUNCATE TABLE {string.Join(", ", tables)} RESTART IDENTITY";
    }

    /// <summary>The (kept table, wiped table) pairs where a kept table has a foreign key to a wiped one. Must be empty.</summary>
    public static IReadOnlyList<(string Kept, string Wiped)> KeptTablesReferencingWiped(IModel model)
    {
        var result = new List<(string, string)>();
        foreach (var entity in model.GetEntityTypes().Where(e => e.GetTableName() is { } name && Kept.Contains(name)))
            foreach (var fk in entity.GetForeignKeys())
                if (fk.PrincipalEntityType.GetTableName() is { } principal && !Kept.Contains(principal))
                    result.Add((entity.GetTableName()!, principal));
        return result;
    }

    private static string Qualified(string? schema, string table) =>
        schema is null ? Quote(table) : $"{Quote(schema)}.{Quote(table)}";

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
