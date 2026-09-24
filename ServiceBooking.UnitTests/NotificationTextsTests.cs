using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>API_CONTRACT_CYCLE4.md §30.1's catalog of delivery-log Russian texts, collected server-side.</summary>
public class NotificationTextsTests
{
    [Fact]
    public void StatusText_Delivered_WithoutReadAt_IsDelivered()
    {
        NotificationTexts.StatusText(NotificationStatus.Delivered, null, Guid.NewGuid(), readAtUtc: null, attemptCount: 1)
            .Should().Be("доставлено");
    }

    [Fact]
    public void StatusText_Delivered_WithReadAt_IsRead()
    {
        NotificationTexts.StatusText(NotificationStatus.Delivered, null, Guid.NewGuid(), readAtUtc: DateTime.UtcNow, attemptCount: 1)
            .Should().Be("прочитано");
    }

    [Fact]
    public void StatusText_Sent_IsUndeliveredConfirmation()
    {
        NotificationTexts.StatusText(NotificationStatus.Sent, null, Guid.NewGuid(), null, 1)
            .Should().Be("отправлено, доставка не подтверждена");
    }

    [Fact]
    public void StatusText_Failed_NoWhatsApp()
    {
        NotificationTexts.StatusText(NotificationStatus.Failed, NotificationReason.RecipientHasNoWhatsApp, Guid.NewGuid(), null, 1)
            .Should().Be("не доставлено: у клиента нет WhatsApp");
    }

    [Fact]
    public void StatusText_Skipped_OptedOut()
    {
        NotificationTexts.StatusText(NotificationStatus.Skipped, NotificationReason.RecipientOptedOut, null, null, 0)
            .Should().Be("клиент отказался от уведомлений");
    }

    [Fact]
    public void StatusText_Expired_VisitAlreadyStarted()
    {
        NotificationTexts.StatusText(NotificationStatus.Expired, NotificationReason.VisitAlreadyStarted, Guid.NewGuid(), null, 0)
            .Should().Be("визит уже начался — не отправлено");
    }

    [Fact]
    public void StatusText_BelowMinimumLeadTime()
    {
        NotificationTexts.StatusText(NotificationStatus.Skipped, NotificationReason.BelowMinimumLeadTime, Guid.NewGuid(), null, 0)
            .Should().Be("до визита осталось слишком мало времени");
    }

    // NoUsableChannel is disambiguated by whether a channel was ever assigned at all (ChannelId null) vs
    // assigned but unpaid/inactive (ChannelId set) — API_CONTRACT_CYCLE4.md §30.1's "канал недоступен"
    // vs "срок действия канала истёк".
    [Fact]
    public void StatusText_NoUsableChannel_NoChannelAssigned_IsChannelUnavailable()
    {
        NotificationTexts.StatusText(NotificationStatus.Skipped, NotificationReason.NoUsableChannel, channelId: null, null, 0)
            .Should().Be("канал недоступен");
    }

    [Fact]
    public void StatusText_NoUsableChannel_ChannelAssignedButUnpaid_IsExpired()
    {
        NotificationTexts.StatusText(NotificationStatus.Skipped, NotificationReason.NoUsableChannel, channelId: Guid.NewGuid(), null, 0)
            .Should().Be("срок действия канала истёк");
    }

    [Fact]
    public void StatusText_PendingWithNoAttempts_IsWaiting()
    {
        NotificationTexts.StatusText(NotificationStatus.Pending, null, Guid.NewGuid(), null, attemptCount: 0)
            .Should().Be("ожидает отправки");
    }

    [Fact]
    public void StatusText_PendingWithAttempts_IsTransientRetry()
    {
        NotificationTexts.StatusText(NotificationStatus.Pending, null, Guid.NewGuid(), null, attemptCount: 2)
            .Should().Be("временный сбой, повторим");
    }

    [Fact]
    public void TypeText_EveryTypeProducesNonEmptyRussianText()
    {
        foreach (var type in Enum.GetValues<NotificationType>())
            NotificationTexts.TypeText(type).Should().NotBeNullOrWhiteSpace();
    }

    // ── ARCHITECTURE_CYCLE9.md §105.8 (US-124) — Web Push reasons ──────────────────────────────────

    [Theory]
    [InlineData(NotificationReason.PushSubscriptionGone)]
    [InlineData(NotificationReason.PushAuthRejected)]
    [InlineData(NotificationReason.PushPayloadTooLarge)]
    [InlineData(NotificationReason.PushTtlExhausted)]
    [InlineData(NotificationReason.StaffPushDisabledByCompany)]
    [InlineData(NotificationReason.MasterNoLongerInCompany)]
    public void StatusText_EveryPushReason_ProducesItsOwnNonGenericRussianText(NotificationReason reason)
    {
        var text = NotificationTexts.StatusText(NotificationStatus.Failed, reason, null, null, attemptCount: 1);

        text.Should().NotBeNullOrWhiteSpace();
        // §6 convention: "русский текст статусов и причин собирает сервер, в одном месте" — the whole
        // point of giving each reason its own switch branch is that it must NOT fall through to the
        // generic "пропущено"/"не удалось отправить" catch-all.
        text.Should().NotBe("пропущено").And.NotBe("не удалось отправить");
    }
}
