namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>ARCHITECTURE_CYCLE25.md §499.3 — the result of ONE outgoing MAX message, classified so the dispatcher can decide what happens to the queue row.</summary>
public abstract record MaxSendOutcome
{
    public sealed record Sent : MaxSendOutcome;

    /// <summary>403 / 404 — the bot was stopped or blocked, the chat does not exist.</summary>
    public sealed record ChatUnavailable(int StatusCode) : MaxSendOutcome;

    /// <summary>429, or the local limiter did not grant a lease.</summary>
    public sealed record RateLimited : MaxSendOutcome;

    /// <summary>5xx, timeout, network failure — retried later.</summary>
    public sealed record Transient(string Detail) : MaxSendOutcome;

    /// <summary>400 / 401 / other 4xx — a configuration or shape error, not retried.</summary>
    public sealed record Rejected(int StatusCode, string Detail) : MaxSendOutcome;
}
