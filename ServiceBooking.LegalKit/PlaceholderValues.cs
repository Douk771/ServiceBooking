using System.Text.Json;
using System.Text.RegularExpressions;

namespace ServiceBooking.LegalKit;

/// <summary>
/// Reads and validates <c>legal.values.json</c> against the 12 keys (10 required, 2 optional) of
/// contracts/cycle11/legal-values.schema.json (ARCHITECTURE_CYCLE11.md §103.3). Deliberately hand-rolled
/// rather than a generic ajv-in-.NET dependency: the rule set is small, fixed, and needs to produce the
/// "чего нет и откуда брать" list ARCHITECTURE_CYCLE11.md §117.3 requires on every non-zero exit code,
/// which a generic schema validator's error format doesn't give for free.
/// </summary>
internal sealed class PlaceholderValues
{
    /// <summary>The 10 required keys, in the exact order legal-values.schema.json lists them —
    /// preserved here so error/status output enumerates them in the same order a human reading the
    /// schema would expect.</summary>
    public static readonly IReadOnlyList<string> RequiredKeys =
    [
        "НАИМЕНОВАНИЕ_ОПЕРАТОРА", "ИНН_ОПЕРАТОРА", "ЮРИДИЧЕСКИЙ_АДРЕС",
        "ПОЧТОВЫЙ_АДРЕС", "ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ", "ТЕЛЕФОН_ОПЕРАТОРА", "ОТВЕТСТВЕННЫЙ_ЗА_ОБРАБОТКУ",
        "ПОЧТА_ОТВЕТСТВЕННОГО", "СРОК_ОТВЕТА_НА_ОБРАЩЕНИЕ",
        "НДС_ОГОВОРКА",
    ];

    /// <summary>The 2 keys legal-values.schema.json allows but does not require — the Roskomnadzor
    /// registry notice number/date (ARCHITECTURE_CYCLE11.md §103.3). Publishing the registration number
    /// is not a legal requirement (it is a good-faith marker, not an obligation under 152-ФЗ), while the
    /// notice-then-registration process has a ~30-day gap during which the number genuinely doesn't
    /// exist yet. When one of these is absent, `legal publish` removes the whole containing element
    /// rather than leaving a half-filled sentence — see <c>PublishCommand.SubstituteWithOptionalRemoval</c>.</summary>
    public static readonly IReadOnlyList<string> OptionalKeys =
    [
        "НОМЕР_УВЕДОМЛЕНИЯ_РКН", "ДАТА_УВЕДОМЛЕНИЯ_РКН",
    ];

    /// <summary>All 12 keys legal-values.schema.json's `values` object accepts — required ∪ optional.</summary>
    public static readonly IReadOnlyList<string> AllKnownKeys = [.. RequiredKeys, .. OptionalKeys];

    /// <summary>Extra shape checks contracts/cycle11/legal-values.schema.json imposes on top of "present
    /// and non-empty" — ARCHITECTURE_CYCLE11.md §111: "прав контракт, расхождение чинится кодом". Keyed
    /// by the same 12 names as <see cref="RequiredKeys"/>; a key absent from this dictionary has no
    /// pattern beyond non-empty.</summary>
    private static readonly IReadOnlyDictionary<string, (Regex Pattern, string Description)> PatternedKeys =
        new Dictionary<string, (Regex, string)>(StringComparer.Ordinal)
        {
            ["ИНН_ОПЕРАТОРА"] = (new Regex(@"^(?:[0-9]{10}|[0-9]{12})$"),
                "10 цифр у юрлица или 12 у ИП/самозанятого"),
            ["ДАТА_УВЕДОМЛЕНИЯ_РКН"] = (new Regex(@"^[0-9]{2}\.[0-9]{2}\.[0-9]{4}$"),
                "формат ДД.ММ.ГГГГ"),
        };

    /// <summary>Keys the schema marks <c>format: email</c>. Deliberately a pragmatic "looks like an
    /// email" check (one <c>@</c>, something on each side, a dot in the domain part) rather than
    /// RFC-5322 parsing — the schema's own intent is to catch "ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ": "уточнить у
    /// бухгалтера", not to reject every technically-unusual-but-real address.</summary>
    private static readonly HashSet<string> EmailKeys = new(StringComparer.Ordinal)
    {
        "ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ", "ПОЧТА_ОТВЕТСТВЕННОГО",
    };

    private static readonly Regex LooksLikeEmail = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    public IReadOnlyDictionary<string, string> Values { get; }

    private PlaceholderValues(IReadOnlyDictionary<string, string> values) => Values = values;

    /// <summary>Parses and validates a values file. Throws <see cref="PlaceholderValuesException"/> with
    /// a full list of problems (never just the first one found) — ARCHITECTURE_CYCLE11.md §117.3: "каждый
    /// ненулевой код обязан печатать список, а не одну строку".</summary>
    public static PlaceholderValues Load(string path)
    {
        if (!File.Exists(path))
            throw new PlaceholderValuesException([$"Файл значений не найден: {path}"]);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new PlaceholderValuesException([$"{path}: не удалось разобрать JSON — {ex.Message}"]);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("values", out var valuesElement) || valuesElement.ValueKind != JsonValueKind.Object)
                throw new PlaceholderValuesException([$"{path}: отсутствует объект \"values\"."]);

            var problems = new List<string>();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var property in valuesElement.EnumerateObject())
            {
                if (!AllKnownKeys.Contains(property.Name))
                {
                    problems.Add($"{property.Name}: неизвестный ключ, не входит в 12 допустимых реквизитов.");
                    continue;
                }
                result[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : "";
            }

            foreach (var key in RequiredKeys)
            {
                if (!result.TryGetValue(key, out var value))
                {
                    problems.Add($"{key}: значение отсутствует.");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(value))
                {
                    problems.Add($"{key}: значение пустое.");
                    continue;
                }

                ValidateFormat(key, value, problems);
            }

            // §103.3: НОМЕР_УВЕДОМЛЕНИЯ_РКН / ДАТА_УВЕДОМЛЕНИЯ_РКН are optional — the key may be absent
            // entirely (the containing element is then dropped whole at publish time, see PublishCommand),
            // but if the operator DID write the key, it has to be a real value, not a placeholder-empty
            // string: an explicit empty string is a mistake to report, not a silent "treat as absent".
            foreach (var key in OptionalKeys)
            {
                if (!result.TryGetValue(key, out var value))
                    continue;
                if (string.IsNullOrWhiteSpace(value))
                {
                    problems.Add($"{key}: значение пустое. Необязательный ключ можно не указывать вовсе, но если он указан, пустая строка недопустима.");
                    continue;
                }

                ValidateFormat(key, value, problems);
            }

            if (problems.Count > 0)
                throw new PlaceholderValuesException(problems);

            return new PlaceholderValues(result);
        }
    }

    /// <summary>Shape checks common to required and (present) optional keys — a non-empty value must
    /// still match the shape the schema demands for that key, regardless of whether the key itself is
    /// required.</summary>
    private static void ValidateFormat(string key, string value, List<string> problems)
    {
        if (PatternedKeys.TryGetValue(key, out var check) && !check.Pattern.IsMatch(value))
            problems.Add($"{key}: значение '{value}' не соответствует формату ({check.Description}).");

        if (EmailKeys.Contains(key) && !LooksLikeEmail.IsMatch(value))
            problems.Add($"{key}: значение '{value}' не похоже на адрес электронной почты.");
    }
}

/// <summary>Carries the full list of validation problems — see <see cref="PlaceholderValues.Load"/>.</summary>
internal sealed class PlaceholderValuesException(IReadOnlyList<string> problems)
    : Exception($"legal.values.json не прошёл проверку: {problems.Count} проблем(а).")
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
