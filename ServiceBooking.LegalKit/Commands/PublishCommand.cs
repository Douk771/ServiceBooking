using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.LegalKit.Commands;

/// <summary>
/// `legal publish` — ARCHITECTURE_CYCLE11.md §105.3. One obviously-obeys-the-doc step order:
///  1. Read source (`--source`, defaults to the baked-in artifact) + values (`--values`) + version/date.
///  2. Refuse ENTIRELY, before a single byte is written, if anything is missing/invalid/left over.
///  3. Write substituted content under `&lt;base&gt;.&lt;version&gt;.html`.
///  4. Write `legal.json.tmp`, then rename() to `legal.json`.
///  5. Append to `legal.published.json`.
/// </summary>
internal static class PublishCommand
{
    public static int Run(CliArgs args)
    {
        try
        {
            return RunCore(args);
        }
        catch (CliUsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunCore(CliArgs args)
    {
        var source = args.GetOrDefault("source", BuildCommand.DefaultOut);
        var outDir = args.Require("out");
        var valuesPath = args.Require("values");
        var version = args.Require("version");
        var effectiveFromRaw = args.Require("effective-from");
        var dryRun = args.Has("dry-run");

        var problems = new List<string>();

        if (version.EndsWith("-draft", StringComparison.Ordinal))
            problems.Add($"--version '{version}' содержит суффикс '-draft' — версия публикации обязана быть финальной.");

        if (!DateOnly.TryParseExact(effectiveFromRaw, "yyyy-MM-dd", out var effectiveFrom))
            problems.Add($"--effective-from '{effectiveFromRaw}' не в формате ГГГГ-ММ-ДД.");

        PlaceholderValues? values = null;
        try
        {
            values = PlaceholderValues.Load(valuesPath);
        }
        catch (PlaceholderValuesException ex)
        {
            problems.AddRange(ex.Problems);
        }

        LegalSnapshot? snapshot = null;
        try
        {
            snapshot = LegalProviderFactory.CreateForRoot(source).LoadStrict();
        }
        catch (InvalidOperationException ex)
        {
            problems.Add($"Источник '{source}' не загружается: {ex.Message}");
        }

        if (problems.Count > 0 || values is null || snapshot is null)
        {
            PrintRejection(problems);
            return 5;
        }

        var effectiveFromFormatted = effectiveFrom.ToString("dd.MM.yyyy");
        var substitutions = new Dictionary<string, string>(values.Values, StringComparer.Ordinal)
        {
            ["ВЕРСИЯ_ДОКУМЕНТА"] = version,
            ["ДАТА_ВСТУПЛЕНИЯ_В_СИЛУ"] = effectiveFromFormatted,
        };

        var substituted = new List<(string BaseFile, string NewFile, string Content, bool IsDocument, string ManifestKey)>();
        var leftoverProblems = new List<string>();

        foreach (var doc in snapshot.Documents.Values.OrderBy(d => d.Type))
        {
            var content = Substitute(doc.ContentHtml, substitutions);
            CollectLeftovers(doc.File, content, leftoverProblems);
            substituted.Add((doc.File, VersionedFileName(doc.File, version), content, true, doc.Type.ToString()));
        }
        foreach (var text in snapshot.UiTexts.Values.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var content = Substitute(text.ContentHtml, substitutions);
            CollectLeftovers(text.File, content, leftoverProblems);
            substituted.Add((text.File, VersionedFileName(text.File, version), content, false, text.Key));
        }

        if (leftoverProblems.Count > 0)
        {
            // §105.3 step 2: ANY {{...}} surviving substitution refuses publication — the broad token
            // scan (not just the known Cyrillic-uppercase pattern) is exactly the fix for risk A4
            // (API_CONTRACT_CYCLE11.md §112): a Latin/lowercase placeholder that was never a real
            // substitution key still counts as "a hole left in the output".
            PrintRejection(leftoverProblems);
            return 5;
        }

        // Links/anchors are checked against the SUBSTITUTED content, in a throwaway staging snapshot —
        // §105.3 step 2 also refuses on a dead link or a lost offer-channel anchor.
        var staging = Path.Combine(Path.GetTempPath(), "legalkit-publish-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            var stagingManifest = BuildManifestJson(snapshot, substituted, version, effectiveFromRaw);
            File.WriteAllText(Path.Combine(staging, "legal.json"), stagingManifest);
            foreach (var (_, newFile, content, _, _) in substituted)
                File.WriteAllText(Path.Combine(staging, newFile), content);

            LegalSnapshot stagedSnapshot;
            try
            {
                stagedSnapshot = LegalProviderFactory.CreateForRoot(staging).LoadStrict();
            }
            catch (InvalidOperationException ex)
            {
                PrintRejection([$"Собранный к публикации комплект не проходит проверку: {ex.Message}"]);
                return 5;
            }

            var links = LegalReadinessReportBuilder.CheckLinks(stagedSnapshot);
            var anchors = LegalReadinessReportBuilder.CheckAnchors(stagedSnapshot);
            var linkProblems = new List<string>();
            linkProblems.AddRange(links.Broken.Select(b => $"Битая ссылка: {b.File}: {b.Href}"));
            linkProblems.AddRange(anchors.Missing.Select(m => $"Отсутствует якорь: {m.Route}#{m.Anchor}"));
            if (linkProblems.Count > 0)
            {
                PrintRejection(linkProblems);
                return 5;
            }

            if (dryRun)
            {
                Console.WriteLine($"[dry-run] Публикация версии '{version}' в '{outDir}' прошла бы проверку. На диск ничего не записано.");
                return 0;
            }

            // Steps 3-5: write files under new names, then atomically swap the manifest, then log.
            Directory.CreateDirectory(outDir);
            var previousManifestPath = Path.Combine(outDir, "legal.json");
            var previousManifest = File.Exists(previousManifestPath) ? File.ReadAllText(previousManifestPath) : null;

            foreach (var (_, newFile, content, _, _) in substituted)
                File.WriteAllText(Path.Combine(outDir, newFile), content);

            var finalManifest = BuildManifestJson(snapshot, substituted, version, effectiveFromRaw);
            var tmpManifestPath = Path.Combine(outDir, "legal.json.tmp");
            File.WriteAllText(tmpManifestPath, finalManifest);
            File.Move(tmpManifestPath, previousManifestPath, overwrite: true);

            AppendPublishedLog(outDir, version, effectiveFromRaw, previousManifest, valuesPath);

            Console.WriteLine($"Опубликовано: версия '{version}', вступает в силу {effectiveFromRaw}, каталог '{outDir}'.");
            return 0;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static void CollectLeftovers(string file, string content, List<string> problems)
    {
        foreach (var token in PlaceholderScanner.FindAllTokens(content))
            problems.Add($"{file}: после подстановки остался {token}.");
    }

    private static string Substitute(string html, IReadOnlyDictionary<string, string> values) =>
        Regex.Replace(html, PlaceholderScanner.KnownPattern, m =>
        {
            var name = m.Value.Trim('{', '}');
            return values.TryGetValue(name, out var value) ? value : m.Value;
        });

    private static string VersionedFileName(string originalFileName, string version)
    {
        var extension = Path.GetExtension(originalFileName);
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        return $"{baseName}.{version}{extension}";
    }

    private static string BuildManifestJson(
        LegalSnapshot snapshot,
        List<(string BaseFile, string NewFile, string Content, bool IsDocument, string ManifestKey)> substituted,
        string version, string effectiveFromRaw)
    {
        var documents = new JsonArray();
        var uiTexts = new JsonArray();

        foreach (var (baseFile, newFile, _, isDocument, key) in substituted)
        {
            if (isDocument)
            {
                var doc = snapshot.Documents.Values.Single(d => d.File == baseFile);
                var entry = new JsonObject
                {
                    ["type"] = doc.Type.ToString(),
                    ["version"] = version,
                    ["effectiveFrom"] = effectiveFromRaw,
                    ["isDraft"] = false,
                    ["changeKind"] = doc.ChangeKind.ToString(),
                    ["gate"] = doc.Gate.ToString(),
                    ["title"] = doc.Title,
                    ["file"] = newFile,
                };
                if (doc.Purposes.Count > 0)
                {
                    entry["purposes"] = new JsonArray(doc.Purposes
                        .Select(p => (JsonNode)new JsonObject { ["key"] = p.Key.ToString(), ["title"] = p.Title })
                        .ToArray());
                }
                documents.Add(entry);
            }
            else
            {
                uiTexts.Add(new JsonObject
                {
                    ["key"] = key,
                    ["version"] = version,
                    ["isDraft"] = false,
                    ["file"] = newFile,
                });
            }
        }

        var manifest = new JsonObject { ["documents"] = documents, ["uiTexts"] = uiTexts };
        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void AppendPublishedLog(string outDir, string version, string effectiveFromRaw, string? previousManifest, string valuesPath)
    {
        var logPath = Path.Combine(outDir, "legal.published.json");
        var entries = File.Exists(logPath)
            ? (JsonNode.Parse(File.ReadAllText(logPath)) as JsonArray ?? [])
            : [];

        entries.Add(new JsonObject
        {
            ["publishedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["version"] = version,
            ["effectiveFrom"] = effectiveFromRaw,
            ["valuesFile"] = Path.GetFileName(valuesPath),
            ["previousManifest"] = previousManifest is null ? null : JsonNode.Parse(previousManifest),
        });

        File.WriteAllText(logPath, entries.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void PrintRejection(List<string> problems)
    {
        Console.Error.WriteLine("Публикация отклонена:");
        foreach (var p in problems) Console.Error.WriteLine($"  - {p}");
    }
}
