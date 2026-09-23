using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.LegalKit.Commands;

/// <summary>`legal links` — ARCHITECTURE_CYCLE11.md §105/§117.1. Checks every `href="/…"` in the
/// currently-loaded snapshot at `--root` against contracts/cycle11/legal-routes.json's routes/aliases
/// (via <see cref="LegalRoutes"/>, the backend's typed copy of that same file) and every required anchor.
/// Writes nothing.</summary>
internal static class LinksCommand
{
    public static int Run(CliArgs args)
    {
        var root = args.GetOrDefault("root", BuildCommand.DefaultOut);

        LegalSnapshot snapshot;
        try
        {
            snapshot = LegalProviderFactory.CreateForRoot(root).LoadStrict();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"Не удалось загрузить снимок из '{root}': {ex.Message}");
            return 1;
        }

        var links = LegalReadinessReportBuilder.CheckLinks(snapshot);
        var anchors = LegalReadinessReportBuilder.CheckAnchors(snapshot);

        var broken = false;
        if (links.Broken.Count > 0)
        {
            Console.Error.WriteLine("Битые ссылки:");
            foreach (var b in links.Broken) Console.Error.WriteLine($"  - {b.File}: {b.Href}");
            broken = true;
        }
        if (anchors.Missing.Count > 0)
        {
            Console.Error.WriteLine("Отсутствующие якоря:");
            foreach (var m in anchors.Missing) Console.Error.WriteLine($"  - {m.Route}#{m.Anchor}");
            broken = true;
        }

        if (broken) return 6;

        Console.WriteLine($"OK: {links.Checked} ссылок проверено, якоря на месте.");
        return 0;
    }
}
