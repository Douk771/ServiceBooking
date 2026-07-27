using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// The API serializes enums as strings (Program.cs registers a global JsonStringEnumConverter),
/// but System.Net.Http.Json's default options don't know that. Use these helpers instead of the
/// bare ReadFromJsonAsync/PostAsJsonAsync extensions whenever a DTO contains an enum
/// (BookingStatus, PaymentStatus, ...).
/// </summary>
public static class JsonHelpers
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static Task<T?> ReadJsonAsync<T>(this HttpContent content) =>
        content.ReadFromJsonAsync<T>(Options);

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T payload) =>
        client.PostAsJsonAsync(url, payload, Options);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T payload) =>
        client.PutAsJsonAsync(url, payload, Options);

    public static Task<HttpResponseMessage> PatchJsonAsync<T>(this HttpClient client, string url, T payload) =>
        client.PatchAsJsonAsync(url, payload, Options);
}
