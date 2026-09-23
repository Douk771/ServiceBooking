using System.Text.Json.Nodes;
using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.LegalKit.Commands;

/// <summary>`legal status` — ARCHITECTURE_CYCLE11.md §105.4/§117.1. Read-only, safe on a production
/// machine: never writes, never opens a database connection. Same report shape
/// (contracts/cycle11/legal-status.schema.json) as `GET /api/admin/legal/readiness`, minus `impact`
/// (needs the database, which this command deliberately never touches), plus `drift` when `--source` is
/// given (the endpoint can't compute that — no `legal-drafts/` inside the running container).</summary>
internal static class StatusCommand
{
    public static int Run(CliArgs args)
    {
        var root = args.GetOrDefault("root", BuildCommand.DefaultOut);
        var source = args.Get("source");
        var valuesPath = args.Get("values");

        LegalSnapshot? snapshot;
        try
        {
            snapshot = LegalProviderFactory.CreateForRoot(root).LoadStrict();
        }
        catch (InvalidOperationException ex)
        {
            return ReportUnavailable(root, ex.Message, args.Has("json"));
        }

        var report = StatusReport.Build(snapshot, root, source, valuesPath);
        var ready = report["ready"]!.GetValue<bool>();

        if (args.Has("json"))
            Console.WriteLine(StatusReport.ToJsonString(report));
        else
            PrintHuman(report);

        return ready ? 0 : 4;
    }

    private static int ReportUnavailable(string root, string errorMessage, bool json)
    {
        if (json)
        {
            var report = new JsonObject
            {
                ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["root"] = root,
                ["ready"] = false,
                ["blockers"] = new JsonArray(new JsonObject { ["kind"] = "LegalUnavailable", ["detail"] = errorMessage }),
                ["documents"] = new JsonArray(),
                ["uiTexts"] = new JsonArray(),
                ["placeholders"] = new JsonArray(),
                ["links"] = new JsonObject { ["checked"] = 0, ["broken"] = new JsonArray() },
                ["anchors"] = new JsonObject { ["missing"] = new JsonArray() },
                ["disclaimer"] = LegalReadinessReportBuilder.Disclaimer,
            };
            Console.WriteLine(StatusReport.ToJsonString(report));
        }
        else
        {
            Console.Error.WriteLine($"Снимок правовых документов недоступен в '{root}': {errorMessage}");
        }
        return 4;
    }

    private static void PrintHuman(JsonObject report)
    {
        Console.WriteLine($"root: {report["root"]}");
        Console.WriteLine($"ready: {report["ready"]}");
        var blockers = report["blockers"]!.AsArray();
        if (blockers.Count == 0)
        {
            Console.WriteLine("blockers: нет");
        }
        else
        {
            Console.WriteLine("blockers:");
            foreach (var b in blockers)
                Console.WriteLine($"  - {b!["kind"]}: {b["detail"]}");
        }
        Console.WriteLine();
        Console.WriteLine(report["disclaimer"]!.GetValue<string>());
    }
}
