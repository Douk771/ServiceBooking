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
                var wouldRemove = LegalSourceSet.FindStrayHtmlFiles(outDir, source);
                Console.WriteLine(wouldRemove.Count == 0
                    ? $"[dry-run] Собрано и провалидировано из '{source}'. В '{outDir}' ничего не записано."
                    : $"[dry-run] Собрано и провалидировано из '{source}'. В '{outDir}' ничего не записано. " +
                      $"Были бы удалены осиротевшие файлы: {string.Join(", ", wouldRemove)}.");
                return 0;
            }

            Directory.CreateDirectory(outDir);
            foreach (var file in LegalSourceSet.EnumerateManifestFiles(source))
                File.Copy(Path.Combine(staging, file), Path.Combine(outDir, file), overwrite: true);

            // ARCHITECTURE_CYCLE11.md §103.2: the artifact is "11 files under the same names" plus
            // legal.json — an EXACT set, not a floor. A file the manifest used to reference but no
            // longer does (e.g. cycle 5's privacy.html/terms.html, orphaned by cycle 11 renaming to
            // 01-privacy-policy.html etc.) has to be removed here, or it just sits in the artifact
            // directory forever, unreferenced by anything but still shipping in the Docker image.
            // Scoped to *.html at the top level only (LegalSourceSet.FindStrayHtmlFiles) — this never
            // touches a non-.html file, a subdirectory, or anything `build` itself didn't just produce,
            // so something unrelated that happens to live in --out survives untouched.
            var stray = LegalSourceSet.FindStrayHtmlFiles(outDir, source);
            foreach (var file in stray)
                File.Delete(Path.Combine(outDir, file));

            Console.WriteLine(stray.Count == 0
                ? $"Собрано '{source}' -> '{outDir}'."
                : $"Собрано '{source}' -> '{outDir}'. Удалены осиротевшие файлы: {string.Join(", ", stray)}.");
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
