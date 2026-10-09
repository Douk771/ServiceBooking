using System.Text.Json;
using FluentAssertions;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>Self-checks of the OpenAPI subset validator (ARCHITECTURE_CYCLE29.md §29.7.2). No DB, no HTTP.</summary>
public class OpenApiContractValidatorTests
{
    private const string Doc = """{"openapi":"3.0.3","components":{"schemas":{"Id":{"type":"string","format":"uuid"}}}}""";
    private static readonly OpenApiContract Contract = OpenApiContract.FromJson(Doc);

    private static IReadOnlyList<string> Run(string schema, string value) =>
        Contract.Validate(JsonDocument.Parse(schema).RootElement, JsonDocument.Parse(value).RootElement);

    [Theory]
    [InlineData("""{"type":"string"}""", "\"a\"", "1")]
    [InlineData("""{"type":"integer"}""", "3", "3.5")]
    [InlineData("""{"type":"integer"}""", "3", "\"3\"")]
    [InlineData("""{"type":"number"}""", "3.5", "\"x\"")]
    [InlineData("""{"type":"boolean"}""", "true", "1")]
    [InlineData("""{"type":"array","items":{"type":"integer"}}""", "[1,2]", "[1,\"2\"]")]
    [InlineData("""{"type":"object","properties":{"a":{"type":"string"}}}""", """{"a":"x"}""", "[]")]
    [InlineData("""{"type":"array","minItems":2}""", "[1,2]", "[1]")]
    [InlineData("""{"type":"array","maxItems":1}""", "[1]", "[1,2]")]
    [InlineData("""{"type":"string","enum":["a","b"]}""", "\"a\"", "\"c\"")]
    [InlineData("""{"type":"integer","minimum":1}""", "1", "0")]
    [InlineData("""{"type":"integer","maximum":5}""", "5", "6")]
    [InlineData("""{"type":"string","maxLength":2}""", "\"ab\"", "\"abc\"")]
    [InlineData("""{"type":"string","minLength":2}""", "\"ab\"", "\"a\"")]
    [InlineData("""{"type":"string","format":"uuid"}""", "\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\"", "\"nope\"")]
    [InlineData("""{"type":"string","format":"date"}""", "\"2026-01-31\"", "\"31.01.2026\"")]
    [InlineData("""{"type":"string","format":"date-time"}""", "\"2026-01-31T10:00:00Z\"", "\"yesterday\"")]
    [InlineData("""{"type":"string","nullable":true}""", "null", "1")]
    [InlineData("""{"$ref":"#/components/schemas/Id"}""", "\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\"", "\"x\"")]
    [InlineData("""{"allOf":[{"type":"object","required":["a"],"additionalProperties":true},{"type":"object","properties":{"b":{"type":"integer"}},"additionalProperties":true}]}""",
        """{"a":1,"b":2}""", """{"b":2}""")]
    public void Keyword_accepts_valid_and_rejects_invalid(string schema, string good, string bad)
    {
        Run(schema, good).Should().BeEmpty();
        Run(schema, bad).Should().NotBeEmpty();
    }

    [Fact]
    public void Null_requires_nullable()
    {
        Run("""{"type":"string"}""", "null").Should().ContainSingle().Which.Should().Contain("null");
    }

    [Fact]
    public void Required_property_missing_is_reported_and_null_value_follows_nullable()
    {
        const string schema = """{"type":"object","required":["a"],"properties":{"a":{"type":"string","nullable":true}}}""";
        Run(schema, "{}").Should().ContainSingle().Which.Should().Contain("$.a");
        Run(schema, """{"a":null}""").Should().BeEmpty();
    }

    [Fact]
    public void Extra_property_is_an_error_unless_additionalProperties_true()
    {
        const string strict = """{"type":"object","properties":{"a":{"type":"string"}}}""";
        const string open = """{"type":"object","properties":{"a":{"type":"string"}},"additionalProperties":true}""";
        Run(strict, """{"a":"x","extra":1}""").Should().ContainSingle().Which.Should().Contain("$.extra");
        Run(open, """{"a":"x","extra":1}""").Should().BeEmpty();
    }

    [Fact]
    public void Violation_path_points_to_the_nested_element()
    {
        Run("""{"type":"object","properties":{"photos":{"type":"array","items":{"type":"object","properties":{"width":{"type":"integer"}}}}}}""",
                """{"photos":[{"width":1},{"width":"x"}]}""")
            .Should().ContainSingle().Which.Should().StartWith("$.photos[1].width");
    }

    [Fact]
    public void Unknown_keyword_throws_instead_of_passing_silently()
    {
        var act = () => Run("""{"type":"string","oneOf":[{"type":"string"}]}""", "\"a\"");
        act.Should().Throw<NotSupportedException>().WithMessage("*oneOf*");
    }

    [Fact]
    public void Pattern_is_checked()
    {
        Run("""{"type":"string","pattern":"^\\d{2}:\\d{2}$"}""", "\"04:00\"").Should().BeEmpty();
        Run("""{"type":"string","pattern":"^\\d{2}:\\d{2}$"}""", "\"4:00\"").Should().ContainSingle().Which.Should().Contain("pattern");
    }

    [Fact]
    public void Undeclared_status_and_operation_are_violations()
    {
        var contract = OpenApiContract.FromJson("""
            {"paths":{"/x":{"get":{"responses":{"200":{"content":{"application/json":{"schema":{"type":"object","additionalProperties":true}}}}}}}}}
            """);
        var body = JsonDocument.Parse("{}").RootElement;
        contract.Collect("get", "/x", 200, body).Should().BeEmpty();
        contract.Collect("get", "/x", 404, body).Should().ContainSingle();
        contract.Collect("post", "/x", 200, body).Should().ContainSingle();
    }

    [Theory]
    [InlineData("cycle26")]
    [InlineData("cycle29")]
    [InlineData("cycle35")]
    [InlineData("cycle37")]
    [InlineData("cycle39")]
    public void Bundled_contracts_load_and_unknown_path_is_reported(string cycle)
    {
        var contract = OpenApiContract.Load(cycle);
        var schemaProbe = JsonDocument.Parse("{}").RootElement;
        contract.Collect("get", "/definitely/not/there", 200, schemaProbe).Should().ContainSingle();
    }
}
