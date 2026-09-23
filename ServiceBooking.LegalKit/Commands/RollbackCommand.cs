using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServiceBooking.LegalKit.Commands;

/// <summary>`legal rollback` — ARCHITECTURE_CYCLE11.md §105.3: restores the manifest that was current
/// immediately before the most recent `legal publish` recorded in `legal.published.json`. The versioned
/// content files of every earlier redaction are untouched (they were written under unique
/// `&lt;base&gt;.&lt;version&gt;.html` names, so nothing needed deleting) — this only swaps which manifest
/// `legal.json` points at, atomically, the same way `publish` does.
///
/// The journal is APPENDED to, never mutated: a rollback records its own entry rather than deleting the
/// publish entry it undoes. The journal exists as an operational record of what happened — a rollback
/// silently erasing the very entry that proves a publish took place would make the journal lie about the
/// past. It also makes "rollback twice in a row" detectable: the second call sees the first rollback's own
/// entry as the most recent action and refuses, instead of quietly walking one generation further back
/// than the operator asked for.</summary>
internal static class RollbackCommand
{
    public static int Run(CliArgs args)
    {
        try
        {
            return RunCore(args);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Risk A8 (ARCHITECTURE_CYCLE11.md §112): the same bind-mount permission risk applies to
            // rollback as to publish — turn it into a listed rejection, not a stack trace.
            Console.Error.WriteLine($"Откат прерван ошибкой ввода-вывода: {ex.Message}");
            return 1;
        }
    }

    private static int RunCore(CliArgs args)
    {
        var outDir = args.Require("out");
        var logPath = Path.Combine(outDir, "legal.published.json");
        var manifestPath = Path.Combine(outDir, "legal.json");

        if (!File.Exists(logPath))
        {
            Console.Error.WriteLine($"Журнал публикаций не найден: {logPath}. Откатывать нечего.");
            return 1;
        }

        if (JsonNode.Parse(File.ReadAllText(logPath)) is not JsonArray entries || entries.Count == 0)
        {
            Console.Error.WriteLine("Журнал публикаций пуст. Откатывать нечего.");
            return 1;
        }

        var last = entries[^1]!.AsObject();
        var lastAction = last["action"]?.GetValue<string>() ?? "publish";
        if (lastAction == "rollback")
        {
            Console.Error.WriteLine(
                "Последнее действие в журнале — уже откат (без новой публикации после него). " +
                "Откатывать больше нечего: повторный откат ушёл бы на поколение раньше, чем просили, " +
                "поэтому отклонён явно.");
            return 1;
        }

        var previousManifest = last["previousManifest"];
        if (previousManifest is null)
        {
            Console.Error.WriteLine("Последняя запись в журнале была первой публикацией — предыдущего манифеста не существует.");
            return 1;
        }

        var undoneVersion = last["version"]?.GetValue<string>();

        if (args.Has("dry-run"))
        {
            Console.WriteLine($"[dry-run] Откатил бы публикацию версии '{undoneVersion}' в '{outDir}'. На диск ничего не записано.");
            return 0;
        }

        var previousManifestJson = previousManifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        // Same order as `publish` (see PublishCommand): the journal entry documenting the rollback is
        // written BEFORE the manifest swap becomes visible, so an abort between the two leaves the
        // journal over-claiming (says rolled back, manifest is still the pre-rollback one) rather than
        // under-claiming (manifest already swapped, nothing on record) — the harmless direction.
        entries.Add(new JsonObject
        {
            ["action"] = "rollback",
            ["rolledBackAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["undidVersion"] = undoneVersion,
            ["restoredManifest"] = JsonNode.Parse(previousManifestJson),
        });
        File.WriteAllText(logPath, entries.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var tmpPath = Path.Combine(outDir, "legal.json.tmp");
        File.WriteAllText(tmpPath, previousManifestJson);
        File.Move(tmpPath, manifestPath, overwrite: true);

        Console.WriteLine($"Откат выполнен: манифест в '{outDir}' возвращён к состоянию до публикации версии '{undoneVersion}'.");
        return 0;
    }
}
