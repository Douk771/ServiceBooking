namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>
/// Default <see cref="IMaxBotClient"/> when <c>PhoneVerification:Provider = "stub"</c>
/// (ARCHITECTURE_CYCLE12.md §150.3) — makes NO network call, ever. Not "a real client that decided not
/// to call": there is no <see cref="System.Net.Http.HttpClient"/> reference inside this class at all,
/// which is what makes "zero outbound calls to platform-api2.max.ru while disabled" a structural
/// guarantee rather than a runtime decision that could regress.
/// </summary>
public sealed class StubMaxBotClient : IMaxBotClient
{
    public Task<bool> SubscribeAsync(CancellationToken ct) => Task.FromResult(false);

    public Task SendMessageAsync(string chatId, string text, CancellationToken ct) => Task.CompletedTask;
}
