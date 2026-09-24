namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>
/// The bot's own outbound surface toward the MAX platform (ARCHITECTURE_CYCLE14.md §146). Deliberately
/// exactly the two operations R1 names as the license-safe set — nothing here ever sends
/// platform-generated text, only fixed sentences from <see cref="MaxBotTexts"/>. Swapped for
/// <see cref="StubMaxBotClient"/> in DI when <c>PhoneVerification:Provider</c> is <c>"stub"</c> (§150.3
/// — "структурно заменён", not "a real client that decided not to call").
/// </summary>
public interface IMaxBotClient
{
    /// <summary>Registers/renews this deployment's webhook URL with the platform (§146.3). Never throws
    /// on a network failure — returns false and lets the caller decide what to log/record; a subscribe
    /// failure must not crash the host at startup nor abort the scheduled renewal task.</summary>
    Task<bool> SubscribeAsync(CancellationToken ct);

    /// <summary>Sends one fixed-text message to a chat (bot replies during the verification
    /// conversation). Bounded by a 5s timeout and by <c>MaxBotClient</c>'s own rate limiters (§146.4);
    /// failure is swallowed by the CALLER (the webhook handler), never allowed to turn a handled update
    /// into a non-2xx response (§146.2).</summary>
    /// <param name="requestContact">Приложить к сообщению клавиатуру с кнопкой
    /// <c>request_contact</c> — единственный способ, которым человек отдаёт боту свой номер.
    /// Без неё текст «нажмите кнопку ниже» приглашает нажать то, чего нет.</param>
    Task SendMessageAsync(string chatId, string text, bool requestContact, CancellationToken ct);
}
