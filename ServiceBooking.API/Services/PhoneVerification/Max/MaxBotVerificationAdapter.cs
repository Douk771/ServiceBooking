using Microsoft.Extensions.Options;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>
/// The single <see cref="IPhoneVerificationMethodAdapter"/> this cycle ships (ARCHITECTURE_CYCLE14.md
/// §144.2, §154). Always registered in DI regardless of <c>PhoneVerification:Provider</c> — what actually
/// changes with the provider switch is which <see cref="IMaxBotClient"/> it was built with
/// (<see cref="StubMaxBotClient"/> vs. <see cref="MaxBotClient"/>, §150.3), never whether the registry
/// knows about <see cref="PhoneVerificationMethod.MaxBot"/> at all.
/// </summary>
public sealed class MaxBotVerificationAdapter(IOptions<PhoneVerificationOptions> options) : IPhoneVerificationMethodAdapter
{
    public PhoneVerificationMethod Method => PhoneVerificationMethod.MaxBot;

    public bool Enabled => string.Equals(options.Value.Provider, "max-bot", StringComparison.OrdinalIgnoreCase);

    /// <summary>§145.1 step 1: generates the one-time payload, builds the deep link and (best-effort) its
    /// QR, and returns them WITHOUT making any outbound call — MAX only learns about this session once
    /// the person opens the link themselves (О2).</summary>
    public Task<VerificationChallenge> StartAsync(PhoneVerificationSession session, CancellationToken ct)
    {
        var payload = PayloadGenerator.Generate();
        var payloadHash = PayloadGenerator.Hash(payload);
        var botUsername = options.Value.Max.BotUsername ?? string.Empty;
        var deepLink = $"https://max.ru/{botUsername}?start={payload}";
        // Вторая ссылка — для тех, кто пользуется MAX в браузере, а не в приложении. Найдено при
        // первом живом проходе 24.09.2026: страница https://max.ru/<бот> даёт единственную кнопку
        // «Запустить бота», и ведёт она на СХЕМУ ПРИЛОЖЕНИЯ `max://…`. В Safari у человека без
        // установленного MAX это просто ошибка «не удаётся открыть страницу», и дальше пройти нельзя.
        // Веб-клиент тот же payload принимает: https://web.max.ru/<бот>?start=<payload> сохраняет и
        // путь, и параметр. QR остаётся на max.ru намеренно — его сканируют телефоном, там приложение.
        var webLink = $"https://web.max.ru/{botUsername}?start={payload}";

        byte[]? qrPng;
        try
        {
            qrPng = QrImage.EncodePng(deepLink);
        }
        catch (Exception)
        {
            // §141's own documented fallback: a QR encoding failure never blocks starting a session —
            // the deep link itself (always shown alongside the QR, §163) is enough on its own.
            qrPng = null;
        }

        return Task.FromResult(new VerificationChallenge(payloadHash, deepLink, webLink, qrPng));
    }

    /// <summary>§166: cancelling before the person ever opened the bot has nothing to undo at the
    /// provider — the session simply stops accepting further updates (enforced by
    /// <c>PhoneVerificationStateMachine</c>, not by this adapter).</summary>
    public Task CancelAsync(PhoneVerificationSession session, CancellationToken ct) => Task.CompletedTask;
}
