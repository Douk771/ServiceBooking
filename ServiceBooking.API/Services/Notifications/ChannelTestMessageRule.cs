using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>What the automatic check message of a freshly bound number does.</summary>
public enum ChannelTestDecision { Send, SkipPlatformDisabled, SkipNoOwnerPhone, SkipSameNumber }

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.8 — who gets the check message and when it is skipped. Pure. Order: the platform switch (or a demo stand,
/// which sends nothing to anybody) first, then the owner's phone, then "the bound number is the owner's own phone" (a message from a
/// number to itself proves nothing; <c>Notifications:TestMessage:AllowSameNumber</c> overrides it).
/// </summary>
public static class ChannelTestMessageRule
{
    public static ChannelTestDecision Decide(
        bool platformEnabled, bool demoStand, string? ownerPhone, string? channelPhone, bool allowSameNumber)
    {
        if (!platformEnabled || demoStand) return ChannelTestDecision.SkipPlatformDisabled;
        if (!PhoneNormalizer.TryNormalize(ownerPhone, out var owner)) return ChannelTestDecision.SkipNoOwnerPhone;
        if (!allowSameNumber && PhoneNormalizer.TryNormalize(channelPhone, out var bound) && bound == owner)
            return ChannelTestDecision.SkipSameNumber;
        return ChannelTestDecision.Send;
    }

    public static ChannelTestResult ResultOf(ChannelTestDecision decision) => decision switch
    {
        ChannelTestDecision.SkipPlatformDisabled => ChannelTestResult.SkippedPlatformDisabled,
        ChannelTestDecision.SkipNoOwnerPhone => ChannelTestResult.SkippedNoOwnerPhone,
        ChannelTestDecision.SkipSameNumber => ChannelTestResult.SkippedSameNumber,
        _ => ChannelTestResult.Sent,
    };
}

/// <summary><c>ChannelStateEvent.Detail</c> markers of a failed check message (read back to word the owner's text).</summary>
public static class ChannelTestFailure
{
    public const string SendFailed = "send-failed";
    public const string NotConnected = "not-connected";
    public const string Unconfirmed = "unconfirmed";
}

/// <summary>API_CONTRACT_CYCLE40.md §40.33.3 — <c>ChannelTestDto.text</c>.</summary>
public static class ChannelTestTexts
{
    public const string InProgress = "Отправляем проверочное сообщение на ваш номер…";
    public const string FailedBase = "Не удалось отправить проверочное сообщение";
    public const string SameNumber =
        "Проверочное сообщение не отправлено: номер совпадает с телефоном вашего аккаунта. Проверьте работу записью на другой номер";
    public const string NoOwnerPhone = "Проверочное сообщение не отправлено: у вашего аккаунта не указан номер телефона";
    public const string PlatformDisabled = "Проверочное сообщение не отправлено: рассылки временно отключены платформой";

    public static string For(ChannelTestResult result, string? ownerPhoneMasked, string? failureDetail = null) => result switch
    {
        ChannelTestResult.Pending or ChannelTestResult.Sending => InProgress,
        ChannelTestResult.Sent => ownerPhoneMasked is null
            ? "Проверочное сообщение отправлено на ваш номер"
            : $"Проверочное сообщение отправлено на ваш номер {ownerPhoneMasked}",
        ChannelTestResult.Failed => failureDetail switch
        {
            ChannelTestFailure.NotConnected => FailedBase + " — номер не был на связи",
            ChannelTestFailure.Unconfirmed => FailedBase + " — не удалось подтвердить отправку",
            _ => FailedBase,
        },
        ChannelTestResult.SkippedSameNumber => SameNumber,
        ChannelTestResult.SkippedNoOwnerPhone => NoOwnerPhone,
        ChannelTestResult.SkippedPlatformDisabled => PlatformDisabled,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, null),
    };
}
