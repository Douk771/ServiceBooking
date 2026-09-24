using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.LegalKit.Commands;

/// <summary>
/// `legal check` — ARCHITECTURE_CYCLE11.md §105.2. Writes nothing. Three gates, in order:
///  1. Artifact drift: build `--source` into a temp dir and compare byte-for-byte against `--root`,
///     PLUS every `--root` file the build doesn't produce at all (§103.2: the artifact is "11 files
///     under the same names" — an exact set. A file `build` stopped producing — e.g. cycle 5's
///     `privacy.html` orphaned by cycle 11's `01-privacy-policy.html` rename — is exactly as much
///     drift as a file whose content changed; missing it here is the same class of hole this cycle
///     exists to close: the stub would ship in the image and CI would stay green). Exit 3 on any
///     difference — "этим краснеет CI".
///  2. Links/anchors on the freshly-built (i.e. source-of-truth) snapshot (exit 6).
///  3. Risk A4 (API_CONTRACT_CYCLE11.md §112): every source file is scanned for `{{…}}` tokens that
///     aren't one of the 14 recognized placeholder names — a Latin or lowercase-Cyrillic placeholder
///     that the product's own narrow regex would silently accept as filled text is failed here instead.
///     The contract's exit-code table (§117.3) has no code reserved for this specific failure; it is
///     mapped onto exit 6 (the other "structural integrity of the source" failure) rather than inventing
///     an undocumented code — see the accompanying report for why.
///  4. Cycle 12 review finding 3.1: every <c>*.html</c> file in `--source` is either a manifest entry, an
///     appendix, or listed (with a reason) in `deferredDrafts` — otherwise a new draft that nobody wired
///     in would silently ship nowhere. Also mapped onto exit 6.
/// </summary>
internal static class CheckCommand
{
    public static int Run(CliArgs args)
    {
        var source = args.GetOrDefault("source", BuildCommand.DefaultSource);
        var root = args.GetOrDefault("root", BuildCommand.DefaultOut);

        var unknownForms = ScanSourceForUnknownPlaceholderForms(source);
        var unaccountedFiles = LegalSourceSet.FindUnaccountedDraftFiles(source);

        List<string> differentFiles;
        var staging = Path.Combine(Path.GetTempPath(), "legalkit-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            LegalSourceSet.Build(source, staging);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Console.Error.WriteLine($"Сборка исходника не удалась: {ex.Message}");
            return 1;
        }

        try
        {
            differentFiles = LegalSourceSet.EnumerateManifestFiles(source)
                .Where(file => !FilesEqual(Path.Combine(staging, file), Path.Combine(root, file)))
                .ToList();

            var strayFiles = LegalSourceSet.FindStrayHtmlFiles(root, source);

            if (differentFiles.Count > 0 || strayFiles.Count > 0)
            {
                Console.Error.WriteLine($"Артефакт '{root}' разошёлся с исходником '{source}':");
                foreach (var file in differentFiles) Console.Error.WriteLine($"  - {file}");
                foreach (var file in strayFiles) Console.Error.WriteLine($"  - {file} (осиротевший файл — сборка его больше не производит)");
                return 3;
            }

            var snapshot = LegalProviderFactory.CreateForRoot(staging).LoadStrict();
            var links = LegalReadinessReportBuilder.CheckLinks(snapshot);
            var anchors = LegalReadinessReportBuilder.CheckAnchors(snapshot);

            var brokenReported = false;
            if (links.Broken.Count > 0)
            {
                Console.Error.WriteLine("Битые ссылки:");
                foreach (var b in links.Broken) Console.Error.WriteLine($"  - {b.File}: {b.Href}");
                brokenReported = true;
            }
            if (anchors.Missing.Count > 0)
            {
                Console.Error.WriteLine("Отсутствующие якоря:");
                foreach (var m in anchors.Missing) Console.Error.WriteLine($"  - {m.Route}#{m.Anchor}");
                brokenReported = true;
            }
            if (unknownForms.Count > 0)
            {
                Console.Error.WriteLine("Плейсхолдеры в нераспознанной форме (не входят в 14 известных имён):");
                foreach (var (file, token) in unknownForms) Console.Error.WriteLine($"  - {file}: {token}");
                brokenReported = true;
            }
            if (unaccountedFiles.Count > 0)
            {
                Console.Error.WriteLine("Файлы-черновики, не учтённые ни в манифесте, ни в appendices, ни в deferredDrafts:");
                foreach (var file in unaccountedFiles) Console.Error.WriteLine($"  - {file}");
                brokenReported = true;
            }

            if (brokenReported) return 6;

            Console.WriteLine($"OK: '{root}' соответствует '{source}', ссылки и якоря целы.");
            return 0;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static List<(string File, string Token)> ScanSourceForUnknownPlaceholderForms(string source)
    {
        var found = new List<(string, string)>();
        if (!Directory.Exists(source)) return found;

        foreach (var path in Directory.EnumerateFiles(source, "*.html", SearchOption.TopDirectoryOnly))
        {
            var html = File.ReadAllText(path);
            foreach (var token in PlaceholderScanner.FindUnknownForms(html))
                found.Add((Path.GetFileName(path), token));
        }
        return found;
    }

    private static bool FilesEqual(string a, string b)
    {
        if (!File.Exists(a) || !File.Exists(b)) return false;
        return File.ReadAllText(a) == File.ReadAllText(b);
    }
}
