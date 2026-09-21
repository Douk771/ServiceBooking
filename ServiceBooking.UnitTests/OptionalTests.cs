using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Common;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Cheap reviewer note: <see cref="Optional{T}"/>/<see cref="OptionalJsonConverterFactory"/> back a
/// real contract rule (API_CONTRACT_CYCLE4.md §31.3, <c>UpdateCompanyDto.TimeZoneId</c> — field omitted
/// leaves the zone untouched, field sent as explicit <c>null</c> reverts it) but had no unit coverage of
/// its own. Exercises <see cref="JsonSerializer.Deserialize{TValue}(string,JsonSerializerOptions?)"/>
/// directly against the converter — pure, no HTTP, no server.
/// </summary>
public class OptionalTests
{
    private sealed record TestDto(Optional<string?> Name);

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new OptionalJsonConverterFactory() },
        PropertyNameCaseInsensitive = true, // matches Program.cs's own controller JSON options
    };

    [Fact]
    public void Deserialize_PropertyOmitted_IsNotSpecified()
    {
        var dto = JsonSerializer.Deserialize<TestDto>("{}", Options);

        dto!.Name.IsSpecified.Should().BeFalse();
        dto.Name.Value.Should().BeNull();
    }

    [Fact]
    public void Deserialize_PropertyExplicitNull_IsSpecifiedWithNullValue()
    {
        var dto = JsonSerializer.Deserialize<TestDto>("""{"name": null}""", Options);

        dto!.Name.IsSpecified.Should().BeTrue();
        dto.Name.Value.Should().BeNull();
    }

    [Fact]
    public void Deserialize_PropertyWithValue_IsSpecifiedWithThatValue()
    {
        var dto = JsonSerializer.Deserialize<TestDto>("""{"name": "Asia/Barnaul"}""", Options);

        dto!.Name.IsSpecified.Should().BeTrue();
        dto.Name.Value.Should().Be("Asia/Barnaul");
    }
}
