namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §499.3, R-3 — turns an HTTP status of <c>POST messages</c> into a <see cref="MaxSendOutcome"/>. One pure file on purpose: the
/// form of MAX's answers for a blocked bot was not confirmed live, and if it differs only this class changes. The detail carries the status code only —
/// never the body, the token or the chat id.
/// </summary>
public static class MaxSendResponseClassifier
{
    public static MaxSendOutcome Classify(int statusCode) => statusCode switch
    {
        >= 200 and < 300 => new MaxSendOutcome.Sent(),
        403 or 404 => new MaxSendOutcome.ChatUnavailable(statusCode),
        429 => new MaxSendOutcome.RateLimited(),
        408 or >= 500 => new MaxSendOutcome.Transient($"HTTP {statusCode}"),
        _ => new MaxSendOutcome.Rejected(statusCode, $"HTTP {statusCode}")
    };
}
