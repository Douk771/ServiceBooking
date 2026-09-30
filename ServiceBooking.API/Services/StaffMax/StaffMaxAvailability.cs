using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.PhoneVerification;

namespace ServiceBooking.API.Services.StaffMax;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §498.1 — the ONE place that says whether "MAX for staff" works on this platform. <see cref="Enabled"/> (sending) needs the
/// platform switch, the real bot provider and a bot username; <see cref="CanLink"/> (issuing a link) additionally needs the webhook to be subscribed —
/// without it "Начать" never reaches the server.
/// </summary>
public sealed class StaffMaxAvailability(
    IOptions<StaffMaxOptions> options, IOptions<PhoneVerificationOptions> phoneOptions, PhoneVerificationDiagnostics diagnostics)
{
    public const string NotEnabledText = "Сообщения в MAX пока не включены на платформе";
    public const string LinkUnavailableText = "Подключение к MAX временно недоступно, попробуйте позже";

    public bool Enabled =>
        options.Value.Enabled &&
        string.Equals(phoneOptions.Value.Provider, "max-bot", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(phoneOptions.Value.Max.BotUsername);

    public bool CanLink => Enabled && diagnostics.WebhookSubscribed;

    /// <summary>The text for a screen that cannot offer the feature (null when it can).</summary>
    public string? UnavailableText => !Enabled ? NotEnabledText : !CanLink ? LinkUnavailableText : null;
}
