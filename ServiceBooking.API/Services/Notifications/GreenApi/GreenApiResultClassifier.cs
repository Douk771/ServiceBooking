using System.Net;
using System.Text.Json;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications.GreenApi;

/// <summary>
/// Turns a raw <c>sendMessage</c> HTTP outcome into one of the four <see cref="SendOutcome"/> cases
/// (ARCHITECTURE_CYCLE4.md §28, US-27 p.11). Pure and network-free by construction: every input is a
/// value already extracted from the response (or the fact that there wasn't one), so this is testable
/// without a socket. This is also the boundary <see cref="GreenApiTransport"/> uses so a raw exception —
/// which could echo request/response content, i.e. the token URL — never crosses it (§24.3 rung 3).
/// </summary>
public static class GreenApiResultClassifier
{
    /// <param name="networkFailure">True when the HTTP call itself never produced a response (DNS
    /// failure, connection refused, <see cref="HttpRequestException"/>, or the client's own
    /// <c>HttpClient.Timeout</c> elapsing) — as opposed to a cancellation of the caller's own token, which
    /// must never reach this method at all (see <see cref="GreenApiTransport"/>'s catch filter).</param>
    /// <param name="statusCode">HTTP status code, when a response was received.</param>
    /// <param name="responseBody">Raw response body, when a response was received.</param>
    public static SendOutcome Classify(bool networkFailure, HttpStatusCode? statusCode, string? responseBody)
    {
        if (networkFailure || statusCode is null)
            return new SendOutcome.TransientFailure("network failure or timeout");

        var code = (int)statusCode;

        // 429 is a pause, not a provider-side rejection (US-27 p.8) — same bucket as a network hiccup.
        if (code == 429)
            return new SendOutcome.TransientFailure("rate limited (429)");

        if (code >= 500)
            return new SendOutcome.TransientFailure($"provider error {code}");

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new SendOutcome.ChannelInvalid($"provider returned {code}");

        if (statusCode == HttpStatusCode.OK)
        {
            var idMessage = ExtractString(responseBody, "idMessage");
            if (idMessage is not null)
                return new SendOutcome.Sent(idMessage);

            if (IndicatesNoAccount(responseBody))
                return new SendOutcome.PermanentlyRejected(NotificationReason.RecipientHasNoWhatsApp, "noAccount");

            // A 200 with neither an idMessage nor a recognisable rejection is not something this cycle
            // can classify with confidence — treat as transient so it retries rather than silently
            // failing a message the provider may have actually accepted.
            return new SendOutcome.TransientFailure("200 response without idMessage");
        }

        // Remaining 4xx. Some GREEN-API 400 bodies mean the INSTANCE session itself is unusable (needs
        // re-authorization) rather than rejecting this one message — that is a channel problem, not a
        // per-message one, and must not be retried as if it were (§26.4).
        if (IndicatesUnauthorizedInstance(responseBody))
            return new SendOutcome.ChannelInvalid($"provider returned {code}: instance not authorized");

        if (IndicatesNoAccount(responseBody))
            return new SendOutcome.PermanentlyRejected(NotificationReason.RecipientHasNoWhatsApp, "noAccount");

        return new SendOutcome.PermanentlyRejected(NotificationReason.RejectedByProvider, $"{code}: {Truncate(responseBody)}");
    }

    private static bool IndicatesNoAccount(string? body) =>
        body is not null && body.Contains("noAccount", StringComparison.OrdinalIgnoreCase);

    private static bool IndicatesUnauthorizedInstance(string? body) =>
        body is not null && (
            body.Contains("notAuthorized", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("not authorized", StringComparison.OrdinalIgnoreCase));

    private static string? ExtractString(string? json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(fieldName, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A provider error body is diagnostic text, not a secret — but it is still bounded before it reaches
    // ReasonDetail (Core.Entities.OutboundNotification.ReasonDetail is string(300)) and, eventually, a log.
    private static string Truncate(string? body)
    {
        var text = body ?? string.Empty;
        return text.Length > 200 ? text[..200] : text;
    }
}
