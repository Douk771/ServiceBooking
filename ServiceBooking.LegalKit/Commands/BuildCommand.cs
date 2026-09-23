namespace ServiceBooking.LegalKit.Commands;

/// <summary>`legal build` — ARCHITECTURE_CYCLE11.md §105.1.</summary>
internal static class BuildCommand
{
    public const string DefaultSource = "legal-drafts";
    public const string DefaultOut = "ServiceBooking.API/App_Data/legal";

    public static int Run(CliArgs args)
    {
        var source = args.GetOrDefault("source", DefaultSource);
        var outDir = args.GetOrDefault("out", DefaultOut);

        // Build into a staging directory first, then validate with the SAME loader the product uses
        // (LoadStrict) before anything lands in --out — ARCHITECTURE_CYCLE11.md §105.1: "собрать
        // невалидный артефакт нельзя".
        var staging = Path.Combine(Path.GetTempPath(), "legalkit-build-" + Guid.NewGuid().ToString("N"));
        try
        {
            LegalSourceSet.Build(source, staging);
            LegalProviderFactory.CreateForRoot(staging).LoadStrict();

            if (args.Has("dry-run"))
            {
                Console.WriteLine($"[dry-run] Собрано и провалидировано из '{source}'. В '{outDir}' ничего не записано.");
                return 0;
            }

            Directory.CreateDirectory(outDir);
            foreach (var file in LegalSourceSet.EnumerateManifestFiles(source))
                File.Copy(Path.Combine(staging, file), Path.Combine(outDir, file), overwrite: true);

            Console.WriteLine($"Собрано '{source}' -> '{outDir}'.");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Console.Error.WriteLine($"Сборка не удалась: {ex.Message}");
            return 1;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}
