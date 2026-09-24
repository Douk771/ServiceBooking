using System.Net;
using System.Text.Json;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications.GreenApiMax;

/// <summary>
/// Turns a raw MAX <c>sendMessage</c> HTTP outcome into one of the four <see cref="SendOutcome"/> cases —
/// the MAX counterpart of <see cref="GreenApi.GreenApiResultClassifier"/>. Pure and network-free, same
/// reasoning as the WhatsApp classifier (testable without a socket, and the boundary that keeps a raw
/// exception echoing a token URL from ever crossing into a log).
///
/// B1 (ARCHITECTURE_CYCLE9.md §104.1/§104.9): MAX's <c>sendMessage</c> response shape is IDENTICAL to
/// WhatsApp's — a 200 body with <c>idMessage</c> on acceptance. The rejection this cycle's abstraction
/// needs, "recipient has no account on this messenger", is asynchronous for MAX — the provider's own docs
/// for the <c>outgoingMessageStatus</c> webhook are explicit that <c>noAccount</c> arrives THERE, not in
/// the synchronous <c>sendMessage</c> response (the same as WhatsApp's own primary path) — so the
/// classifier's job for a 200 response is unchanged: <c>idMessage</c> present → <see cref="SendOutcome.Sent"/>,
/// same defensive "200 with neither an idMessage nor a recognisable rejection → transient" fallback as
/// WhatsApp for a response shape this cycle hasn't specifically seen.
/// </summary>
public static class GreenApiMaxResultClassifier
{
    public static SendOutcome Classify(bool networkFailure, HttpStatusCode? statusCode, string? responseBody)
    {
        if (networkFailure || statusCode is null)
            return new SendOutcome.TransientFailure("network failure or timeout");

        var code = (int)statusCode;

        if (code == 429)
            return new SendOutcome.TransientFailure("rate limited (429)");

        if (code >= 500)
            return new SendOutcome.TransientFailure($"provider error {code}");

        // B1: 403 "Your account is suspended" (MAX's temporary, partial-restriction state, distinct from
        // a full ban) lands here too — same bucket as an unauthorized instance. Both mean "the CHANNEL is
        // the problem, not this one message", and both are things the owner (suspended: wait it out;
        // unauthorized: reconnect) has to act on, not something a retry of THIS message fixes.
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new SendOutcome.ChannelInvalid($"provider returned {code}");

        if (statusCode == HttpStatusCode.OK)
        {
            var idMessage = ExtractString(responseBody, "idMessage");
            if (idMessage is not null)
                return new SendOutcome.Sent(idMessage);

            if (IndicatesNoAccount(responseBody))
                return new SendOutcome.PermanentlyRejected(NotificationReason.RecipientNotInMax, "noAccount");

            return new SendOutcome.TransientFailure("200 response without idMessage");
        }

        if (IndicatesUnauthorizedInstance(responseBody))
            return new SendOutcome.ChannelInvalid($"provider returned {code}: instance not authorized");

        if (IndicatesNoAccount(responseBody))
            return new SendOutcome.PermanentlyRejected(NotificationReason.RecipientNotInMax, "noAccount");

        return new SendOutcome.PermanentlyRejected(NotificationReason.RejectedByProvider, $"{code}: {Truncate(responseBody)}");
    }

    private static bool IndicatesNoAccount(string? body) =>
        body is not null && body.Contains("noAccount", StringComparison.OrdinalIgnoreCase);

    private static bool IndicatesUnauthorizedInstance(string? body) =>
        body is not null && (
            body.Contains("notAuthorized", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("not authorized", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("pendingPassword", StringComparison.OrdinalIgnoreCase));

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

    private static string Truncate(string? body)
    {
        var text = body ?? string.Empty;
        return text.Length > 200 ? text[..200] : text;
    }
}
