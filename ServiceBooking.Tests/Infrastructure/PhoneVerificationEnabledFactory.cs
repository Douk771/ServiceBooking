using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Services.PhoneVerification.Max;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// QA cycle 14 — a secondary host against the SAME class database an <see cref="ApiTestBase"/> subclass
/// already has, same shape/reasoning as <see cref="PushEnabledFactory"/> and
/// <see cref="NotificationTestFactory"/>: the shared "Api" collection factory
/// (<see cref="CustomWebApplicationFactory"/>) leaves <c>PhoneVerification:Provider</c> at its Testing
/// default (<c>"stub"</c>, <c>appsettings.Testing.json</c>) — under which the whole subsystem is
/// deliberately unreachable (§0.5's "невыпущенность"). This host flips <c>PhoneVerification:Provider</c>
/// to <c>"max-bot"</c> so <c>MaxBotVerificationAdapter.Enabled</c> is true, while replacing
/// <see cref="IMaxBotClient"/> with <see cref="RecordingMaxBotClient"/> — registered AFTER Program.cs's
/// own registration, so DI resolves the LAST one added (same trick <c>NotificationDispatchTestFactory</c>
/// documents for <c>INotificationTransport</c>). This is the ONLY way to exercise the enabled path at all
/// without a functional test ever making a real outbound call to <c>platform-api2.max.ru</c> — exactly the
/// property SPEC.md §8.2 requires ("сетевых вызовов наружу функциональный набор не делает").
/// </summary>
public sealed class PhoneVerificationEnabledFactory(string connectionString) : WebApplicationFactory<Program>
{
    /// <summary>Fixed 32-byte key, base64-encoded — deterministic so a test can independently compute
    /// <c>ExternalAccountKey.Compute</c> for the same MAX sender id it feeds into a webhook body.</summary>
    public const string TestExternalKeyHmacBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    /// <summary>Fixed bot token — also the HMAC key <see cref="MaxContactSignature"/> signs
    /// <c>vcf_info</c> with, so a test can compute a VALID signature for a forged/legitimate contact.</summary>
    public const string TestBotToken = "test-bot-token-for-qa-cycle-12-not-a-real-secret";

    public const string TestWebhookToken = "0123456789abcdef0123456789abcdef";

    public RecordingMaxBotClient RecordingClient { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestHostSettings.Apply(builder, "api", connectionString);
        builder.UseSetting("PhoneVerification:Provider", "max-bot");
        builder.UseSetting("PhoneVerification:SessionTtlMinutes", "10");
        builder.UseSetting("PhoneVerification:VerifiedSessionUsableMinutes", "30");
        builder.UseSetting("PhoneVerification:MaxPhonesPerExternalAccount", "3");
        builder.UseSetting("PhoneVerification:MaxOpenSessionsPerPhone", "3");
        builder.UseSetting("PhoneVerification:ExternalKeyHmac", TestExternalKeyHmacBase64);
        builder.UseSetting("PhoneVerification:Max:BotUsername", "qa_cycle14_bot");
        builder.UseSetting("PhoneVerification:Max:BotToken", TestBotToken);
        builder.UseSetting("PhoneVerification:Max:WebhookToken", TestWebhookToken);
        builder.UseSetting("PhoneVerification:Max:PublicBaseUrl", "https://qa-cycle14.example.test");

        builder.ConfigureServices(services =>
        {
            // Registered AFTER Program.cs's own AddSingleton<IMaxBotClient> — DI resolves the LAST
            // registration, so this wins without needing to remove anything first.
            services.AddSingleton<IMaxBotClient>(RecordingClient);
        });
    }
}

/// <summary>A fake <see cref="IMaxBotClient"/> that never touches the network — records every
/// <c>SubscribeAsync</c>/<c>SendMessageAsync</c> call so a test can assert on what the webhook handler
/// tried to say back to the bot chat, without any of it leaving the process.</summary>
public sealed class RecordingMaxBotClient : IMaxBotClient
{
    public ConcurrentBag<(string ChatId, string Text)> SentMessages { get; } = [];
    public int SubscribeCallCount;

    public Task<bool> SubscribeAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref SubscribeCallCount);
        return Task.FromResult(true);
    }

    public Task SendMessageAsync(string chatId, string text, CancellationToken ct)
    {
        SentMessages.Add((chatId, text));
        return Task.CompletedTask;
    }
}
