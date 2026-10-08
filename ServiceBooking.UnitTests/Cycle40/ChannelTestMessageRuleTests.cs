using FluentAssertions;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>ARCHITECTURE_CYCLE40.md §40.8 — who receives the automatic check message and when it is skipped; §40.33.3 texts.</summary>
public class ChannelTestMessageRuleTests
{
    private const string Owner = "79991112233";

    [Fact]
    public void DifferentNumber_IsSent() =>
        ChannelTestMessageRule.Decide(true, false, Owner, "79994445566", false).Should().Be(ChannelTestDecision.Send);

    [Fact]
    public void SameNumber_IsSkipped_EvenWhenWrittenInAnotherFormat() =>
        ChannelTestMessageRule.Decide(true, false, "+7 (999) 111-22-33", Owner, false).Should().Be(ChannelTestDecision.SkipSameNumber);

    [Fact]
    public void SameNumber_IsSent_WhenTheFlagAllowsIt() =>
        ChannelTestMessageRule.Decide(true, false, Owner, Owner, true).Should().Be(ChannelTestDecision.Send);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void NoOwnerPhone_IsSkipped(string? phone) =>
        ChannelTestMessageRule.Decide(true, false, phone, "79994445566", false).Should().Be(ChannelTestDecision.SkipNoOwnerPhone);

    [Fact]
    public void PlatformOffOrDemoStand_WinsOverEverythingElse()
    {
        ChannelTestMessageRule.Decide(false, false, null, Owner, false).Should().Be(ChannelTestDecision.SkipPlatformDisabled);
        ChannelTestMessageRule.Decide(true, true, Owner, "79994445566", false).Should().Be(ChannelTestDecision.SkipPlatformDisabled);
    }

    [Theory]
    [InlineData(ChannelTestDecision.Send, ChannelTestResult.Sent)]
    [InlineData(ChannelTestDecision.SkipSameNumber, ChannelTestResult.SkippedSameNumber)]
    [InlineData(ChannelTestDecision.SkipNoOwnerPhone, ChannelTestResult.SkippedNoOwnerPhone)]
    [InlineData(ChannelTestDecision.SkipPlatformDisabled, ChannelTestResult.SkippedPlatformDisabled)]
    public void ResultOf_MapsEveryDecision(ChannelTestDecision decision, ChannelTestResult expected) =>
        ChannelTestMessageRule.ResultOf(decision).Should().Be(expected);

    [Theory]
    [InlineData(ChannelTestResult.Pending, null, null, "Отправляем проверочное сообщение на ваш номер…")]
    [InlineData(ChannelTestResult.Sending, null, null, "Отправляем проверочное сообщение на ваш номер…")]
    [InlineData(ChannelTestResult.Sent, "+7 999 ***-**-33", null, "Проверочное сообщение отправлено на ваш номер +7 999 ***-**-33")]
    [InlineData(ChannelTestResult.Failed, null, null, "Не удалось отправить проверочное сообщение")]
    [InlineData(ChannelTestResult.Failed, null, ChannelTestFailure.NotConnected, "Не удалось отправить проверочное сообщение — номер не был на связи")]
    [InlineData(ChannelTestResult.Failed, null, ChannelTestFailure.Unconfirmed, "Не удалось отправить проверочное сообщение — не удалось подтвердить отправку")]
    [InlineData(ChannelTestResult.Failed, null, ChannelTestFailure.SendFailed, "Не удалось отправить проверочное сообщение")]
    public void Texts_MatchTheContract(ChannelTestResult result, string? masked, string? detail, string expected) =>
        ChannelTestTexts.For(result, masked, detail).Should().Be(expected);

    [Fact]
    public void SkipTexts_MatchTheContract()
    {
        ChannelTestTexts.For(ChannelTestResult.SkippedSameNumber, null).Should().Be(
            "Проверочное сообщение не отправлено: номер совпадает с телефоном вашего аккаунта. Проверьте работу записью на другой номер");
        ChannelTestTexts.For(ChannelTestResult.SkippedNoOwnerPhone, null).Should().Be(
            "Проверочное сообщение не отправлено: у вашего аккаунта не указан номер телефона");
        ChannelTestTexts.For(ChannelTestResult.SkippedPlatformDisabled, null).Should().Be(
            "Проверочное сообщение не отправлено: рассылки временно отключены платформой");
    }

    [Fact]
    public void NoTestText_MentionsCircumventionTools()
    {
        var forbidden = new[] { "vpn", "впн", "прокси", "proxy", "обход", "обойт" };
        foreach (var result in Enum.GetValues<ChannelTestResult>())
        foreach (var detail in new[] { null, ChannelTestFailure.NotConnected, ChannelTestFailure.Unconfirmed, ChannelTestFailure.SendFailed })
        foreach (var word in forbidden)
            ChannelTestTexts.For(result, "+7 999 ***-**-33", detail).ToLowerInvariant().Should().NotContain(word);
    }
}
