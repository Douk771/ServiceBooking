using System.Text.Json;
using ServiceBooking.API.Services;

namespace ServiceBooking.API.Services.Notifications.GreenApi;

/// <summary>
/// Parses a GREEN-API webhook POST body (ARCHITECTURE_CYCLE4.md §32) — the one place the provider's
/// webhook JSON shape AND its raw status/state strings are known, so <c>NotificationsController</c>
/// (via <see cref="ServiceBooking.API.Services.IProviderWebhookParser"/>, the DI seam the other backend
/// developer defined in <c>Services/ProviderWebhookParsing.cs</c> for T4-B13) never sees a
/// provider-specific field name or string value (US-27 p.4). Stateless, so a singleton registration is fine.
/// </summary>
public sealed class GreenApiWebhookParser : ServiceBooking.API.Services.IProviderWebhookParser
{
    public ProviderCallback? Parse(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody)) return null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawBody);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var typeWebhook = GetString(root, "typeWebhook");
            var instanceId = GetInstanceId(root);
            var occurredAtUtc = GetTimestamp(root);

            return typeWebhook switch
            {
                "outgoingMessageStatus" => ParseDeliveryStatus(root, instanceId, occurredAtUtc),
                "stateInstanceChanged" => ParseChannelState(root, instanceId, occurredAtUtc),
                _ => null,
            };
        }
    }

    private static ProviderCallback? ParseDeliveryStatus(JsonElement root, string? instanceId, DateTime occurredAtUtc)
    {
        var idMessage = GetString(root, "idMessage");
        var status = ParseMessageStatus(GetString(root, "status"));
        if (status is null) return null; // an event type we don't recognise the status of — nothing to act on

        return new ProviderCallback(
            ProviderCallbackKind.DeliveryStatus, idMessage, instanceId, status, ChannelState: null, occurredAtUtc);
    }

    private static ProviderCallback ParseChannelState(JsonElement root, string? instanceId, DateTime occurredAtUtc)
    {
        var state = GreenApiStateInstanceParser.Parse(GetString(root, "stateInstance"));
        return new ProviderCallback(
            ProviderCallbackKind.ChannelState, ProviderMessageId: null, instanceId, MessageStatus: null, state, occurredAtUtc);
    }

    // GREEN-API's own outgoingMessageStatus values: "sent", "delivered", "read", "noAccount". Anything
    // else (a status value this cycle doesn't know about yet) is deliberately unmapped rather than
    // guessed at.
    private static ProviderMessageStatus? ParseMessageStatus(string? raw) => raw switch
    {
        "sent" => ProviderMessageStatus.Sent,
        "delivered" => ProviderMessageStatus.Delivered,
        "read" => ProviderMessageStatus.Read,
        "noAccount" => ProviderMessageStatus.Failed,
        _ => null,
    };

    private static string? GetInstanceId(JsonElement root)
    {
        if (!root.TryGetProperty("instanceData", out var instanceData) || instanceData.ValueKind != JsonValueKind.Object)
            return null;
        if (!instanceData.TryGetProperty("idInstance", out var idInstance)) return null;

        return idInstance.ValueKind switch
        {
            JsonValueKind.String => idInstance.GetString(),
            JsonValueKind.Number => idInstance.GetRawText(),
            _ => null,
        };
    }

    private static DateTime GetTimestamp(JsonElement root) =>
        root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.Number && ts.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : DateTime.UtcNow;

    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
