using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceBooking.API.DTOs.Common;

/// <summary>
/// Distinguishes "this JSON property was omitted from the request" from "this JSON property was present
/// and explicitly null". Plain <c>T?</c> deserialization collapses both to the same C# default, which
/// isn't enough for rules that behave differently in the two cases — cycle 4's example is
/// API_CONTRACT_CYCLE4.md §31.3 (<c>UpdateCompanyDto.TimeZoneId</c>): omitting the field must leave the
/// company's time zone untouched, while sending it as an explicit <c>null</c> must revert the zone to
/// the one derived from the company's city (see <c>CompanyTimeZoneResolver.ForUpdate</c>).
///
/// <see cref="IsSpecified"/> defaults to <see langword="false"/> — exactly what a property keeps when
/// <see cref="OptionalJsonConverterFactory"/> never runs on it because the key was absent from the
/// payload. It runs, and sets <see cref="IsSpecified"/> to <see langword="true"/>, for every property
/// that DID appear in the JSON, whether its value was null or not.
/// </summary>
public readonly struct Optional<T>
{
    public bool IsSpecified { get; }
    public T? Value { get; }

    public Optional(T? value)
    {
        IsSpecified = true;
        Value = value;
    }
}

/// <summary>Registered once in Program.cs alongside <see cref="JsonStringEnumConverter"/> — required for
/// <see cref="Optional{T}"/> to work as a model-bound DTO property at all.</summary>
public class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var valueType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OptionalJsonConverter<>).MakeGenericType(valueType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
    {
        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = JsonSerializer.Deserialize<T>(ref reader, options);
            return new Optional<T>(value);
        }

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}
