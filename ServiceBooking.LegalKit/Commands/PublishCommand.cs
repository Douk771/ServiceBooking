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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Risk A8 (ARCHITECTURE_CYCLE11.md §112): a bind-mount permission problem on the operator
            // machine has to surface as a listed rejection, not a raw stack trace — this is the
            // catch-all net; RunCore's own try/finally around the staging directory still runs first.
            PrintRejection([$"Публикация прервана ошибкой ввода-вывода: {ex.Message}"]);
            return 5;
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

        // The version string is concatenated into every published file name (<база>.<версия>.html,
        // §105.3 step 3) AND into the manifest's `file` field, which LegalDocumentProvider only accepts
        // as a bare filename. Validating the token here, before anything is read or written, keeps a
        // path separator or a stray filesystem character from turning "publish" into a write outside
        // --out, and keeps the failure a listed rejection (exit 5) instead of an unhandled IOException.
        if (!VersionTokenRegex.IsMatch(version))
            problems.Add($"--version '{version}': допустимы только латинские буквы, цифры, '.', '-' и '_' " +
                         "(версия попадает в имя публикуемого файла и в поле file манифеста).");
        if (version.Length > 64)
            problems.Add($"--version '{version}': длиннее 64 символов — столько манифест не принимает.");

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

        // The two Roskomnadzor-registry keys are the only ones that may legitimately be missing from
        // `values.Values` (ARCHITECTURE_CYCLE11.md §103.3 / PlaceholderValues.OptionalKeys — publishing
        // the registration number is not a legal requirement, and the ~30-day gap between notice and
        // registration would otherwise block publication for a month for no legal reason). A missing
        // optional key must not turn into a substituted-empty string in the middle of a sentence
        // ("регистрационный номер , уведомление направлено ") — see RemoveElementsForMissingOptionalKeys.
        var missingOptionalKeys = PlaceholderValues.OptionalKeys
            .Where(key => !substitutions.ContainsKey(key))
            .ToList();

        // Each entry carries its own manifest fields directly rather than the source object being looked
        // back up later by BaseFile — two manifest entries sharing a source file name (a malformed but
        // not impossible manifest) would otherwise make that later lookup ambiguous.
        var substituted = new List<PublishedFile>();
        var leftoverProblems = new List<string>();
        var removalBlockingProblems = new List<string>();
        var removedBlockDescriptions = new List<string>();
        var usedNewFileNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var doc in snapshot.Documents.Values.OrderBy(d => d.Type))
        {
            var withRemovals = RemoveElementsForMissingOptionalKeys(
                doc.ContentHtml, doc.File, missingOptionalKeys, removalBlockingProblems, removedBlockDescriptions);
            var content = Substitute(withRemovals, substitutions);
            CollectLeftovers(doc.File, content, leftoverProblems);
            var newFile = VersionedFileName(doc.File, version, usedNewFileNames);
            substituted.Add(new PublishedFile(doc.File, newFile, content, IsDocument: true, doc.Type.ToString(), doc));
        }
        foreach (var text in snapshot.UiTexts.Values.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var withRemovals = RemoveElementsForMissingOptionalKeys(
                text.ContentHtml, text.File, missingOptionalKeys, removalBlockingProblems, removedBlockDescriptions);
            var content = Substitute(withRemovals, substitutions);
            CollectLeftovers(text.File, content, leftoverProblems);
            var newFile = VersionedFileName(text.File, version, usedNewFileNames);
            substituted.Add(new PublishedFile(text.File, newFile, content, IsDocument: false, text.Key, UiText: text));
        }

        // Review finding 5.1: a block dropped for a missing OPTIONAL key must not silently take an
        // unrelated REQUIRED placeholder down with it (e.g. a future draft writing "ИНН {{ИНН_ОПЕРАТОРА}};
        // сведения внесены в реестр: {{НОМЕР_УВЕДОМЛЕНИЯ_РКН}}" in one <li> would otherwise lose the ИНН
        // from the published text with nothing reporting it). Refuse instead of guessing — §105.3's
        // "отказываемся целиком, а не догадываемся" applies here exactly as much as to a leftover token.
        if (removalBlockingProblems.Count > 0)
        {
            PrintRejection(removalBlockingProblems);
            return 5;
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
            var stagingManifest = BuildManifestJson(substituted, version, effectiveFromRaw);
            File.WriteAllText(Path.Combine(staging, "legal.json"), stagingManifest);
            foreach (var file in substituted)
                File.WriteAllText(Path.Combine(staging, file.NewFile), file.Content);

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

            // §105.3/risk A5 rest on "files are written under NEW names, so the live manifest never points
            // at a file that's still being written". That holds only while the names really are new.
            // Re-publishing an already-published --version (e.g. fixing one value and re-running the same
            // command) produces the SAME file names, so File.WriteAllText would truncate-and-rewrite, in
            // place, the very files the currently-loaded manifest references — and LegalDocumentProvider
            // polls max(mtime) over exactly those files, so it can re-read one mid-write and serve (or
            // hash into a ConsentRecord) a half-written document. Refuse instead, before writing anything;
            // a byte-identical file is left untouched so re-running the same publish stays idempotent.
            var occupied = substituted
                .Where(file => File.Exists(Path.Combine(outDir, file.NewFile))
                            && File.ReadAllText(Path.Combine(outDir, file.NewFile)) != file.Content)
                .Select(file => file.NewFile)
                .ToList();
            if (occupied.Count > 0)
            {
                PrintRejection(occupied
                    .Select(f => $"{f}: файл этой версии уже есть в '{outDir}' и его содержимое другое — " +
                                 $"версия '{version}' уже опубликована. Укажите новую --version: перезапись файла, " +
                                 "на который ссылается действующий манифест, не атомарна.")
                    .ToList());
                return 5;
            }

            PrintRemovedBlocks(removedBlockDescriptions);

            if (dryRun)
            {
                Console.WriteLine($"[dry-run] Публикация версии '{version}' в '{outDir}' прошла бы проверку. На диск ничего не записано.");
                return 0;
            }

            // Steps 3-5: write files under new names, THEN record the journal entry, THEN atomically swap
            // the manifest. The journal write happens BEFORE the manifest is replaced — deliberately the
            // opposite order from an earlier revision of this command, which appended the log entry
            // AFTER the rename. That ordering meant a process abort between the rename and the log
            // append left the new manifest live with no journal entry recording what to roll back to —
            // published, but unrollbackable, contradicting §105.3's "one obviously reversible action".
            // With the log written first: if the process aborts between the log write and the rename,
            // `legal.json` on disk is untouched (still the previous manifest) while the log now claims a
            // publish happened. That is over-claiming, not under-claiming — and it is harmless, because
            // `rollback` reacting to that entry just rewrites `legal.json` with `previousManifest`, which
            // is already what's on disk. Worst case is a no-op, not data loss.
            Directory.CreateDirectory(outDir);
            var previousManifestPath = Path.Combine(outDir, "legal.json");
            var previousManifest = File.Exists(previousManifestPath) ? File.ReadAllText(previousManifestPath) : null;

            foreach (var file in substituted)
            {
                var targetPath = Path.Combine(outDir, file.NewFile);
                if (File.Exists(targetPath) && File.ReadAllText(targetPath) == file.Content) continue;
                File.WriteAllText(targetPath, file.Content);
            }

            AppendPublishedLog(outDir, version, effectiveFromRaw, previousManifest, valuesPath);

            var finalManifest = BuildManifestJson(substituted, version, effectiveFromRaw);
            var tmpManifestPath = Path.Combine(outDir, "legal.json.tmp");
            File.WriteAllText(tmpManifestPath, finalManifest);
            File.Move(tmpManifestPath, previousManifestPath, overwrite: true);

            Console.WriteLine($"Опубликовано: версия '{version}', вступает в силу {effectiveFromRaw}, каталог '{outDir}'.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Risk A8 (ARCHITECTURE_CYCLE11.md §112): a bind-mount permission problem on the operator
            // machine surfaces here as a raw I/O failure — turn it into the same "list of problems, exit
            // 5" shape every other publish rejection uses instead of an unhandled-exception stack trace.
            PrintRejection([$"Публикация прервана ошибкой ввода-вывода: {ex.Message}"]);
            return 5;
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>What a published version string may look like — it becomes part of a file name and of
    /// the manifest's `file` field (see the call site). Deliberately ASCII-only: these names are typed by
    /// hand on an operator machine and read back out of a bind-mount.</summary>
    private static readonly Regex VersionTokenRegex = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*$");

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

    /// <summary>The nearest containing block elements a placeholder may live in — the ones legal-drafts
    /// actually uses for a self-contained sentence/bullet (a single &lt;li&gt; item or a standalone
    /// &lt;p&gt;). Matched non-greedily against the SAME tag name via a backreference, and the body is
    /// forbidden from containing an OPENING tag of that same name (<c>(?!&lt;\1\b)</c>) so that "nearest
    /// containing block" stays literally true: without that guard, an outer &lt;li&gt; wrapping a nested
    /// list would match from the outer opening tag to the INNER &lt;/li&gt;, and removing that span would
    /// delete a placeholder together with half of two elements — leaving malformed HTML that the leftover
    /// scan can no longer see, because the token it would have rejected went away with the text. With the
    /// guard, the outer element simply doesn't match and the inner one (the real nearest block) does.
    /// Verified to select exactly the same spans as the unguarded pattern on every file in legal-drafts/
    /// and App_Data/legal/ today — this only changes behaviour on same-tag nesting, which the document set
    /// does not currently contain.</summary>
    private static readonly Regex BlockElementRegex =
        new(@"<(li|p)\b[^>]*>(?:(?!<\1\b).)*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Drops the whole nearest &lt;li&gt;/&lt;p&gt; block for every occurrence of a missing OPTIONAL
    /// placeholder, instead of substituting it with an empty string. Simply leaving the value blank would
    /// produce a grammatically broken sentence ("регистрационный номер , уведомление направлено ") —
    /// worse than not mentioning the registry entry at all, and exactly the failure mode this method
    /// exists to prevent.
    ///
    /// Design choice, spelled out because it is not the only defensible one: a block is removed as soon
    /// as it contains AT LEAST ONE missing-optional token, regardless of what else (other optional
    /// values, plain text) shares that same block. The alternative — "keep the block and blank only the
    /// missing token" — was rejected because a block mixing an optional registry fact with unrelated
    /// content is a drafting decision made in legal-drafts/*.html, not something this tool can safely
    /// second-guess: once one sentence in a bullet becomes unverifiable, the tool cannot tell whether the
    /// REST of that bullet still reads correctly without the missing clause, so refusing to guess and
    /// dropping the whole unit is the safer default.
    ///
    /// Review finding 5.1: if the block ALSO contains a known placeholder that is NOT itself one of the
    /// currently-missing optional keys — i.e. a required requisite, or a manifest-derived one — removing
    /// the block would silently take that unrelated, filled-in requisite down with it (a future draft
    /// combining an optional registry fact with ИНН_ОПЕРАТОРА in one &lt;li&gt; is exactly this
    /// scenario). That case is refused outright via <paramref name="blockingProblems"/> instead of
    /// guessed at — consistent with §105.3's "отказываемся целиком, а не догадываемся". A block that only
    /// ever combines missing-optional keys with EACH OTHER (both Roskomnadzor registry placeholders in
    /// one bullet — the document set's actual current shape) is still removed silently, exactly as
    /// before, and is recorded in <paramref name="removedBlockDescriptions"/> so the operator can see
    /// what disappeared from the published text.
    ///
    /// This runs BEFORE <see cref="Substitute"/> and BEFORE the leftover scan: a missing optional token
    /// that is not inside a recognized &lt;li&gt;/&lt;p&gt; is left untouched here and therefore still
    /// survives to <see cref="CollectLeftovers"/>, which rejects publication exactly as strictly as before
    /// — this method only ever REMOVES text, it never causes a token that should be caught to be missed.
    /// </summary>
    private static string RemoveElementsForMissingOptionalKeys(
        string html, string file, IReadOnlyList<string> missingOptionalKeys,
        List<string> blockingProblems, List<string> removedBlockDescriptions)
    {
        if (missingOptionalKeys.Count == 0)
            return html;

        return BlockElementRegex.Replace(html, m =>
        {
            var missingInBlock = missingOptionalKeys
                .Where(key => m.Value.Contains("{{" + key + "}}", StringComparison.Ordinal))
                .ToList();
            if (missingInBlock.Count == 0)
                return m.Value;

            // Any known placeholder in this block that is NOT one of the optional keys it's being removed
            // for — a required requisite, or a manifest-derived one — would be lost silently if the whole
            // block is dropped.
            var otherRequiredTokens = PlaceholderScanner.KnownNames
                .Where(name => !PlaceholderValues.OptionalKeys.Contains(name))
                .Where(name => m.Value.Contains("{{" + name + "}}", StringComparison.Ordinal))
                .ToList();

            if (otherRequiredTokens.Count > 0)
            {
                blockingProblems.Add(
                    $"{file}: в блоке, который пришлось бы удалить из-за отсутствующего " +
                    $"{string.Join(", ", missingInBlock.Select(k => "{{" + k + "}}"))}, есть также " +
                    $"{string.Join(", ", otherRequiredTokens.Select(k => "{{" + k + "}}"))} — разнесите предложение на отдельные элементы.");
                return m.Value;
            }

            removedBlockDescriptions.Add($"{file}: удалён блок ({string.Join(", ", missingInBlock.Select(k => "{{" + k + "}}"))}).");
            return string.Empty;
        });
    }

    /// <summary>One document or uiText being published, carrying everything <see cref="BuildManifestJson"/>
    /// needs directly — no later lookup by file name back into the snapshot, which would be ambiguous if
    /// two manifest entries ever shared a source file name.</summary>
    private sealed record PublishedFile(
        string BaseFile, string NewFile, string Content, bool IsDocument, string ManifestKey,
        LegalDocument? Document = null, LegalUiText? UiText = null);

    /// <summary>Builds <c>&lt;base&gt;.&lt;version&gt;.html</c>, disambiguating against every name already
    /// handed out in this publish run. Two source files that happen to share a base name (e.g. a document
    /// and a uiText both sourced from a file literally named the same) would otherwise collide on the same
    /// published name and one would silently overwrite the other in the loop that writes them to disk.</summary>
    private static string VersionedFileName(string originalFileName, string version, HashSet<string> usedNames)
    {
        var extension = Path.GetExtension(originalFileName);
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var candidate = $"{baseName}.{version}{extension}";
        var suffix = 2;
        while (!usedNames.Add(candidate))
            candidate = $"{baseName}.{version}.{suffix++}{extension}";
        return candidate;
    }

    private static string BuildManifestJson(List<PublishedFile> substituted, string version, string effectiveFromRaw)
    {
        var documents = new JsonArray();
        var uiTexts = new JsonArray();

        foreach (var file in substituted)
        {
            if (file.IsDocument)
            {
                var doc = file.Document!;
                var entry = new JsonObject
                {
                    ["type"] = doc.Type.ToString(),
                    ["version"] = version,
                    ["effectiveFrom"] = effectiveFromRaw,
                    ["isDraft"] = false,
                    ["changeKind"] = doc.ChangeKind.ToString(),
                    ["gate"] = doc.Gate.ToString(),
                    ["title"] = doc.Title,
                    ["file"] = file.NewFile,
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
                    ["key"] = file.ManifestKey,
                    ["version"] = version,
                    ["isDraft"] = false,
                    ["file"] = file.NewFile,
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
            ["action"] = "publish",
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

    /// <summary>Review finding 5.1: publish must surface every block it silently dropped for a missing
    /// optional key, so the operator can see that something disappeared from the published text instead
    /// of finding out by reading the final HTML line by line.</summary>
    private static void PrintRemovedBlocks(List<string> removedBlockDescriptions)
    {
        if (removedBlockDescriptions.Count == 0) return;
        Console.WriteLine("Удалены блоки с отсутствующими необязательными реквизитами:");
        foreach (var description in removedBlockDescriptions) Console.WriteLine($"  - {description}");
    }
}
