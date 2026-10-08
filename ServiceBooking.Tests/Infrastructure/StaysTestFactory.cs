using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceBooking.API.Services.Notifications.WebPush;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.API.Services.Stays;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// QA цикл 37: хост вертикали «Дома» с подменяемыми часами (<see cref="IStaysClock"/>, ARCHITECTURE_CYCLE37.md §37.5.3 — единственный источник «сейчас»
/// для удержаний), включённым Web Push (записывающий отправитель — настоящих сетевых вызовов нет) и ручным прогоном задач вертикали (фоновый тик
/// выключен — задача запускается явно, чтобы тест сам решал, когда она «срабатывает»). Лимиты по номеру телефона и политики частоты по умолчанию
/// такие же, как у остальных тестовых хостов (подняты до 10000); тесты лимитов поднимают хост с боевыми значениями через <paramref name="phoneLimits"/>
/// и <paramref name="stayCreateAnonymousPermits"/>.
/// </summary>
public sealed class StaysTestFactory(
    string connectionString, bool phoneLimits = false, int? stayCreateAnonymousPermits = null, int? stayProofPermitsPerToken = null)
    : WebApplicationFactory<Program>
{
    public FakeStaysClock StaysClock { get; } = new();
    public FakeWebPushSender Sender { get; } = new();
    public TestHostIdentity Identity { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Identity = TestHostSettings.Apply(builder, "api", connectionString, factoryType: GetType().Name);
        builder.UseSetting("Notifications:Provider", "logging");
        builder.UseSetting("Notifications:EncryptionKey", NotificationDispatchTestFactory.TestEncryptionKeyBase64);
        builder.UseSetting("Notifications:StaffPush:Provider", "web-push");
        builder.UseSetting("ScheduledTasks:Enabled", "false");
        // как у CustomWebApplicationFactory: без ключа однократность пробного периода «закрыта» (TrialUniquenessCheckUnavailable)
        builder.UseSetting("Trial:PhoneKeyHmac", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
        builder.UseSetting("Trial:PhoneKeyId", "qa-test-key");
        if (phoneLimits)
        {
            builder.UseSetting("Stays:PhoneLimits:MaxHeldPerPhone", "2");
            builder.UseSetting("Stays:PhoneLimits:MaxHeldPerPhonePerCompany", "1");
            builder.UseSetting("Stays:PhoneLimits:MaxCreatedPerPhonePerDay", "10");
        }
        if (stayCreateAnonymousPermits is { } n)
            builder.UseSetting("RateLimits:stay-create:AnonymousPermitLimit", n.ToString());
        if (stayProofPermitsPerToken is { } p)
            builder.UseSetting("RateLimits:stay-proof:PermitLimit", p.ToString());

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IStaysClock>();
            services.AddSingleton<IStaysClock>(StaysClock);
            services.AddSingleton<IWebPushSender>(Sender);
        });
    }

    public HttpClient Client(string? token = null)
    {
        var client = CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Один проход задачи по имени (<c>stays-hold-expiry</c>, <c>stays-scheduled-messages</c>, <c>staff-push-dispatch</c>, …) в своей области DI.</summary>
    public async Task<ScheduledTaskOutcome> RunTaskAsync(string name)
    {
        using var scope = Services.CreateScope();
        var task = scope.ServiceProvider.GetServices<IScheduledTask>().First(t => t.Name == name);
        return await task.ExecuteAsync(CancellationToken.None);
    }
}

/// <summary>Часы, которые тест двигает вручную. Стартуют с настоящего «сейчас», чтобы строки, записанные не через эти часы, выглядели свежими.</summary>
public sealed class FakeStaysClock : IStaysClock
{
    private long _ticks = DateTime.UtcNow.Ticks;

    public DateTime UtcNow => new(Interlocked.Read(ref _ticks), DateTimeKind.Utc);

    public void Set(DateTime utc) => Interlocked.Exchange(ref _ticks, DateTime.SpecifyKind(utc, DateTimeKind.Utc).Ticks);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
