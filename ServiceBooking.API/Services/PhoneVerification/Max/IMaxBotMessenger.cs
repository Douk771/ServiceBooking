namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §499.3 — the outgoing side of "MAX for staff": one text message to one chat, with a classified result. A separate interface
/// from <see cref="IMaxBotClient"/> (cycle 14) so that client and its test doubles stay untouched; implemented by the same <c>MaxBotClient</c> singleton
/// (shared rate limiters) — or by <see cref="StubMaxBotMessenger"/> when the provider is "stub".
/// </summary>
public interface IMaxBotMessenger
{
    /// <summary>Sends <paramref name="text"/> to <paramref name="chatId"/>. Never throws for a network or HTTP failure — it returns the outcome.</summary>
    Task<MaxSendOutcome> SendAsync(string chatId, string text, CancellationToken ct);
}
