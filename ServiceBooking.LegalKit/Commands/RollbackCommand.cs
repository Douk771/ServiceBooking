using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServiceBooking.LegalKit.Commands;

/// <summary>`legal rollback` — ARCHITECTURE_CYCLE11.md §105.3: restores the manifest that was current
/// immediately before the most recent `legal publish` recorded in `legal.published.json`. The versioned
/// content files of every earlier redaction are untouched (they were written under unique
/// `&lt;base&gt;.&lt;version&gt;.html` names, so nothing needed deleting) — this only swaps which manifest
/// `legal.json` points at, atomically, the same way `publish` does.</summary>
internal static class RollbackCommand
{
    public static int Run(CliArgs args)
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
        var previousManifest = last["previousManifest"];
        if (previousManifest is null)
        {
            Console.Error.WriteLine("Последняя запись в журнале была первой публикацией — предыдущего манифеста не существует.");
            return 1;
        }

        if (args.Has("dry-run"))
        {
            Console.WriteLine($"[dry-run] Откатил бы публикацию версии '{last["version"]}' в '{outDir}'. На диск ничего не записано.");
            return 0;
        }

        var tmpPath = Path.Combine(outDir, "legal.json.tmp");
        File.WriteAllText(tmpPath, previousManifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmpPath, manifestPath, overwrite: true);

        entries.RemoveAt(entries.Count - 1);
        File.WriteAllText(logPath, entries.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"Откат выполнен: манифест в '{outDir}' возвращён к состоянию до публикации версии '{last["version"]}'.");
        return 0;
    }
}
