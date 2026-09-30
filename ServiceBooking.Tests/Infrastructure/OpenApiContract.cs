using System.Globalization;
using System.Text.Json;
using FluentAssertions;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Minimal OpenAPI 3.0 response validator (ARCHITECTURE_CYCLE29.md §29.7.2). Reads the bundled JSON
/// (`contracts/cycleNN/openapi.json`, produced by `npm run contracts:json`). Supports only the keywords the
/// project contracts use; any other schema keyword throws NotSupportedException so a silent "pass" is impossible.
/// Strict by default: a property absent from `properties` is a violation unless `additionalProperties: true`.
/// </summary>
public sealed class OpenApiContract
{
    private static readonly HashSet<string> Annotations = ["description", "example", "default", "title"];

    private static readonly HashSet<string> Supported =
    [
        "$ref", "type", "nullable", "required", "properties", "additionalProperties", "items", "minItems", "maxItems",
        "enum", "allOf", "format", "minimum", "maximum", "maxLength", "minLength", "pattern",
    ];

    private readonly JsonElement _root;

    private OpenApiContract(JsonElement root) => _root = root;

    public static OpenApiContract Load(string cycle)
    {
        var path = FindRepoFile(Path.Combine("contracts", cycle, "openapi.json"));
        using var stream = File.OpenRead(path);
        return new OpenApiContract(JsonDocument.Parse(stream).RootElement.Clone());
    }

    public static OpenApiContract FromJson(string json) => new(JsonDocument.Parse(json).RootElement.Clone());

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ServiceBooking.sln")))
            {
                var candidate = Path.Combine(dir.FullName, relative);
                return File.Exists(candidate)
                    ? candidate
                    : throw new FileNotFoundException($"{relative} not found; run `npm run contracts:json` in frontend/.", candidate);
            }
        }

        throw new DirectoryNotFoundException("ServiceBooking.sln not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Validates a real response body against paths[pathTemplate][method].responses[status] JSON schema.</summary>
    public void AssertResponse(string method, string pathTemplate, int status, JsonElement body)
    {
        var errors = Collect(method, pathTemplate, status, body);
        errors.Should().BeEmpty("the response of {0} {1} -> {2} must conform to the contract", method, pathTemplate, status);
    }

    public IReadOnlyList<string> Collect(string method, string pathTemplate, int status, JsonElement body)
    {
        if (!_root.TryGetProperty("paths", out var paths) || !paths.TryGetProperty(pathTemplate, out var pathItem)
            || !pathItem.TryGetProperty(method.ToLowerInvariant(), out var operation))
        {
            return [$"operation {method} {pathTemplate} is not in the contract"];
        }

        if (!operation.TryGetProperty("responses", out var responses)
            || !responses.TryGetProperty(status.ToString(CultureInfo.InvariantCulture), out var response))
        {
            return [$"status {status} is not declared for {method} {pathTemplate}"];
        }

        if (!response.TryGetProperty("content", out var content) || !content.TryGetProperty("application/json", out var media)
            || !media.TryGetProperty("schema", out var schema))
        {
            return [$"{method} {pathTemplate} {status} has no application/json schema"];
        }

        return Validate(schema, body);
    }

    public IReadOnlyList<string> Validate(JsonElement schema, JsonElement value)
    {
        var errors = new List<string>();
        Check(schema, value, "$", errors);
        return errors;
    }

    private JsonElement Resolve(string reference)
    {
        const string prefix = "#/components/schemas/";
        if (!reference.StartsWith(prefix, StringComparison.Ordinal))
            throw new NotSupportedException($"$ref {reference}: only {prefix}* is supported");
        return _root.GetProperty("components").GetProperty("schemas").GetProperty(reference[prefix.Length..]);
    }

    private void Check(JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        foreach (var keyword in schema.EnumerateObject())
        {
            if (!Supported.Contains(keyword.Name) && !Annotations.Contains(keyword.Name))
                throw new NotSupportedException($"OpenAPI keyword '{keyword.Name}' is not supported by the validator");
        }

        if (schema.TryGetProperty("$ref", out var reference))
        {
            Check(Resolve(reference.GetString()!), value, path, errors);
            return;
        }

        var nullable = schema.TryGetProperty("nullable", out var n) && n.ValueKind == JsonValueKind.True;
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!nullable) errors.Add($"{path}: null is not allowed (nullable is not set)");
            return;
        }

        if (schema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var part in allOf.EnumerateArray()) Check(part, value, path, errors);
        }

        if (schema.TryGetProperty("enum", out var allowed)
            && !allowed.EnumerateArray().Any(a => a.GetRawText() == value.GetRawText()))
        {
            errors.Add($"{path}: value {value.GetRawText()} is not in enum");
        }

        if (schema.TryGetProperty("type", out var typeEl))
        {
            var type = typeEl.GetString();
            if (!TypeMatches(type, value))
            {
                errors.Add($"{path}: expected {type}, got {Describe(value)}");
                return;
            }

            switch (type)
            {
                case "object": CheckObject(schema, value, path, errors); break;
                case "array": CheckArray(schema, value, path, errors); break;
                case "string": CheckString(schema, value, path, errors); break;
                case "integer" or "number": CheckNumber(schema, value, path, errors); break;
            }
        }
    }

    private void CheckObject(JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        var props = schema.TryGetProperty("properties", out var p) ? p : default;
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
            {
                if (!value.TryGetProperty(name, out _)) errors.Add($"{path}.{name}: required property is missing");
            }
        }

        var additionalAllowed = schema.TryGetProperty("additionalProperties", out var ap) && ap.ValueKind == JsonValueKind.True;
        var additionalSchema = schema.TryGetProperty("additionalProperties", out var aps) && aps.ValueKind == JsonValueKind.Object ? aps : (JsonElement?)null;
        foreach (var member in value.EnumerateObject())
        {
            if (props.ValueKind == JsonValueKind.Object && props.TryGetProperty(member.Name, out var propSchema))
                Check(propSchema, member.Value, $"{path}.{member.Name}", errors);
            else if (additionalSchema is { } extraSchema)
                Check(extraSchema, member.Value, $"{path}.{member.Name}", errors);
            else if (!additionalAllowed)
                errors.Add($"{path}.{member.Name}: property is not described by the schema");
        }
    }

    private void CheckArray(JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        var count = value.GetArrayLength();
        if (schema.TryGetProperty("minItems", out var min) && count < min.GetInt32())
            errors.Add($"{path}: expected at least {min.GetInt32()} items, got {count}");
        if (schema.TryGetProperty("maxItems", out var max) && count > max.GetInt32())
            errors.Add($"{path}: expected at most {max.GetInt32()} items, got {count}");
        if (!schema.TryGetProperty("items", out var items)) return;
        var i = 0;
        foreach (var item in value.EnumerateArray()) Check(items, item, $"{path}[{i++}]", errors);
    }

    private static void CheckString(JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        var s = value.GetString()!;
        if (schema.TryGetProperty("minLength", out var min) && s.Length < min.GetInt32())
            errors.Add($"{path}: string shorter than {min.GetInt32()}");
        if (schema.TryGetProperty("maxLength", out var max) && s.Length > max.GetInt32())
            errors.Add($"{path}: string longer than {max.GetInt32()}");
        if (schema.TryGetProperty("pattern", out var pattern) && !System.Text.RegularExpressions.Regex.IsMatch(s, pattern.GetString()!))
            errors.Add($"{path}: string does not match pattern {pattern.GetString()}");
        if (!schema.TryGetProperty("format", out var f)) return;
        var ok = f.GetString() switch
        {
            "uuid" => Guid.TryParse(s, out _),
            "date" => DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "date-time" => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            _ => true,
        };
        if (!ok) errors.Add($"{path}: '{s}' is not a valid {f.GetString()}");
    }

    private static void CheckNumber(JsonElement schema, JsonElement value, string path, List<string> errors)
    {
        var d = value.GetDouble();
        if (schema.TryGetProperty("minimum", out var min) && d < min.GetDouble())
            errors.Add($"{path}: {d} is less than minimum {min.GetDouble()}");
        if (schema.TryGetProperty("maximum", out var max) && d > max.GetDouble())
            errors.Add($"{path}: {d} is greater than maximum {max.GetDouble()}");
    }

    private static bool TypeMatches(string? type, JsonElement v) => type switch
    {
        "object" => v.ValueKind == JsonValueKind.Object,
        "array" => v.ValueKind == JsonValueKind.Array,
        "string" => v.ValueKind == JsonValueKind.String,
        "boolean" => v.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "number" => v.ValueKind == JsonValueKind.Number,
        "integer" => v.ValueKind == JsonValueKind.Number && (v.TryGetInt64(out _) || Math.Floor(v.GetDouble()) == v.GetDouble()),
        _ => throw new NotSupportedException($"type '{type}' is not supported"),
    };

    private static string Describe(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        _ => v.ValueKind.ToString().ToLowerInvariant(),
    };
}
