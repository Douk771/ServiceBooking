using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.API.Services.PhoneVerification.Max;
using ServiceBooking.API.Services.Scheduling;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// QA цикл 25 — вторичный хост против БД класса, на котором «MAX для персонала» ВКЛЮЧЁН (Notifications:StaffMax:Enabled + провайдер max-bot),
/// а обе исходящие границы MAX подменены записывающими двойниками: <see cref="IMaxBotClient"/> (ответы бота при подключении и подтверждении
/// телефона) и <see cref="IMaxBotMessenger"/> (сообщения персоналу). Ни одного сетевого вызова наружу (SPEC цикла 14 §8.2 — то же правило).
/// Планировщик выключен (как у <see cref="PushDispatchTestFactory"/> с disableAutomaticTicking): проход диспетчера <c>staff-max-dispatch</c>
/// запускается тестом явно и синхронно через <see cref="RunDispatchPassAsync"/>, поэтому «второй проход» и «после отключения» проверяются
/// детерминированно, а не таймером.
/// </summary>
public sealed class StaffMaxTestFactory(string connectionString, bool platformEnabled = true) : WebApplicationFactory<Program>
{
    public const string BotUsername = "qa_cycle25_bot";
    public const string WebhookToken = "0123456789abcdef0123456789abcdef";
    public const string BotToken = "test-bot-token-for-qa-cycle-25-not-a-real-secret";

    public RecordingMaxBotClient BotClient { get; } = new();
    public RecordingMaxMessenger Messenger { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestHostSettings.Apply(builder, "api", connectionString, factoryType: GetType().Name);
        builder.UseSetting("PhoneVerification:Provider", "max-bot");
        builder.UseSetting("PhoneVerification:SessionTtlMinutes", "10");
        builder.UseSetting("PhoneVerification:VerifiedSessionUsableMinutes", "30");
        builder.UseSetting("PhoneVerification:MaxPhonesPerExternalAccount", "3");
        builder.UseSetting("PhoneVerification:MaxOpenSessionsPerPhone", "3");
        builder.UseSetting("PhoneVerification:ExternalKeyHmac", PhoneVerificationEnabledFactory.TestExternalKeyHmacBase64);
        builder.UseSetting("PhoneVerification:Max:BotUsername", BotUsername);
        builder.UseSetting("PhoneVerification:Max:BotToken", BotToken);
        builder.UseSetting("PhoneVerification:Max:WebhookToken", WebhookToken);
        builder.UseSetting("PhoneVerification:Max:PublicBaseUrl", "https://qa-cycle25.example.test");

        builder.UseSetting("Notifications:EncryptionKey", NotificationDispatchTestFactory.TestEncryptionKeyBase64);
        builder.UseSetting("Notifications:StaffMax:Enabled", platformEnabled ? "true" : "false");

        // Планировщик остаётся выключенным (Testing): проходы запускаются вручную.
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IMaxBotClient>(BotClient);
            services.AddSingleton<IMaxBotMessenger>(Messenger);
        });
    }

    /// <summary>Гарантирует, что «вебхук подписан» — без этого <c>canLink</c> ложен и ссылку не выдают (§523.1).</summary>
    public void EnsureWebhookSubscribed() =>
        Services.GetRequiredService<PhoneVerificationDiagnostics>().RecordSubscriptionAttempt(true, null);

    /// <summary>Один проход диспетчера сообщений персоналу в собственной области DI — тот же вызов, что делает раннер планировщика.</summary>
    public async Task<ScheduledTaskOutcome> RunDispatchPassAsync()
    {
        using var scope = Services.CreateScope();
        var task = scope.ServiceProvider.GetServices<IScheduledTask>().First(t => t.Name == "staff-max-dispatch");
        return await task.ExecuteAsync(CancellationToken.None);
    }
}

/// <summary>Записывающий <see cref="IMaxBotMessenger"/>: ни сети, ни ожидания. Исход по умолчанию — «доставлено»; исходы можно назначить чату.</summary>
public sealed class RecordingMaxMessenger : IMaxBotMessenger
{
    private readonly ConcurrentQueue<(string ChatId, string Text)> _calls = new();
    private readonly ConcurrentDictionary<string, MaxSendOutcome> _outcomeByChat = new();
    private volatile MaxSendOutcome? _defaultOutcome;

    public IReadOnlyList<(string ChatId, string Text)> Calls => _calls.ToArray();

    public IReadOnlyList<string> TextsTo(string chatId) => _calls.Where(c => c.ChatId == chatId).Select(c => c.Text).ToList();

    public void SetOutcomeForChat(string chatId, MaxSendOutcome outcome) => _outcomeByChat[chatId] = outcome;

    public void SetDefaultOutcome(MaxSendOutcome? outcome) => _defaultOutcome = outcome;

    /// <summary>Цикл 36, L1: хост живёт весь класс, поэтому тест начинает с чистых записей и исходов по умолчанию.</summary>
    public void Reset()
    {
        _calls.Clear();
        _outcomeByChat.Clear();
        _defaultOutcome = null;
    }

    public Task<MaxSendOutcome> SendAsync(string chatId, string text, CancellationToken ct)
    {
        _calls.Enqueue((chatId, text));
        if (_outcomeByChat.TryGetValue(chatId, out var byChat)) return Task.FromResult(byChat);
        return Task.FromResult(_defaultOutcome ?? new MaxSendOutcome.Sent());
    }
}
