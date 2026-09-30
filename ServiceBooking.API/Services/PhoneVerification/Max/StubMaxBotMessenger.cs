namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>Default <see cref="IMaxBotMessenger"/> for <c>PhoneVerification:Provider = "stub"</c>: no <see cref="HttpClient"/> at all, so "no outbound call" is structural (like <see cref="StubMaxBotClient"/>).</summary>
public sealed class StubMaxBotMessenger : IMaxBotMessenger
{
    public Task<MaxSendOutcome> SendAsync(string chatId, string text, CancellationToken ct) =>
        Task.FromResult<MaxSendOutcome>(new MaxSendOutcome.Sent());
}
