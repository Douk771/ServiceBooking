using System.Text.Json;

namespace ServiceBooking.LegalKit;

/// <summary>
/// Reads and validates <c>legal.values.json</c> against the 13 required keys of
/// contracts/cycle11/legal-values.schema.json (ARCHITECTURE_CYCLE11.md §103.3). Deliberately hand-rolled
/// rather than a generic ajv-in-.NET dependency: the rule set is small, fixed, and needs to produce the
/// "чего нет и откуда брать" list ARCHITECTURE_CYCLE11.md §117.3 requires on every non-zero exit code,
/// which a generic schema validator's error format doesn't give for free.
/// </summary>
internal sealed class PlaceholderValues
{
    /// <summary>The 13 required keys, in the exact order legal-values.schema.json lists them —
    /// preserved here so error/status output enumerates them in the same order a human reading the
    /// schema would expect.</summary>
    public static readonly IReadOnlyList<string> RequiredKeys =
    [
        "НАИМЕНОВАНИЕ_ОПЕРАТОРА", "ИНН_ОПЕРАТОРА", "ОГРН_ОПЕРАТОРА", "ЮРИДИЧЕСКИЙ_АДРЕС",
        "ПОЧТОВЫЙ_АДРЕС", "ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ", "ТЕЛЕФОН_ОПЕРАТОРА", "ОТВЕТСТВЕННЫЙ_ЗА_ОБРАБОТКУ",
        "ПОЧТА_ОТВЕТСТВЕННОГО", "НОМЕР_УВЕДОМЛЕНИЯ_РКН", "ДАТА_УВЕДОМЛЕНИЯ_РКН", "СРОК_ОТВЕТА_НА_ОБРАЩЕНИЕ",
        "НДС_ОГОВОРКА",
    ];

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
                if (!RequiredKeys.Contains(property.Name))
                {
                    problems.Add($"{property.Name}: неизвестный ключ, не входит в 13 обязательных реквизитов.");
                    continue;
                }
                result[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : "";
            }

            foreach (var key in RequiredKeys)
            {
                if (!result.TryGetValue(key, out var value))
                    problems.Add($"{key}: значение отсутствует.");
                else if (string.IsNullOrWhiteSpace(value))
                    problems.Add($"{key}: значение пустое.");
            }

            if (problems.Count > 0)
                throw new PlaceholderValuesException(problems);

            return new PlaceholderValues(result);
        }
    }
}

/// <summary>Carries the full list of validation problems — see <see cref="PlaceholderValues.Load"/>.</summary>
internal sealed class PlaceholderValuesException(IReadOnlyList<string> problems)
    : Exception($"legal.values.json не прошёл проверку: {problems.Count} проблем(а).")
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
