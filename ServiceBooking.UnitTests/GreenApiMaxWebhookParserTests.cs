using FluentAssertions;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.GreenApiMax;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE9.md §104.7/§104.9 (B1/B4/US-120) — the MAX counterpart of
/// GreenApiWebhookParserTests. The interesting difference from WhatsApp's parser: this one also sets
/// ProviderCallback.TerminalReason, distinguishing "noAccount" (RecipientNotInMax) from a generic
/// "failed" (RejectedByProvider) instead of collapsing both into the same reason.</summary>
public class GreenApiMaxWebhookParserTests
{
    private readonly GreenApiMaxWebhookParser _parser = new();

    [Fact]
    public void Parse_EmptyBody_ReturnsNull()
    {
        _parser.Parse("").Should().BeNull();
    }

    [Fact]
    public void Parse_MalformedJson_ReturnsNull_DoesNotThrow()
    {
        _parser.Parse("not json").Should().BeNull();
    }

    [Fact]
    public void Parse_UnrecognisedTypeWebhook_ReturnsNull()
    {
        _parser.Parse("""{"typeWebhook":"incomingMessageReceived"}""").Should().BeNull();
    }

    [Theory]
    [InlineData("sent", ProviderMessageStatus.Sent)]
    [InlineData("delivered", ProviderMessageStatus.Delivered)]
    [InlineData("read", ProviderMessageStatus.Read)]
    [InlineData("noAccount", ProviderMessageStatus.Failed)]
    [InlineData("failed", ProviderMessageStatus.Failed)]
    public void Parse_OutgoingMessageStatus_MapsEveryKnownStatus(string rawStatus, ProviderMessageStatus expected)
    {
        var json = $$"""
        {
            "typeWebhook": "outgoingMessageStatus",
            "instanceData": { "idInstance": 3100000000, "wid": "79991234567@c.us", "typeInstance": "v3" },
            "idMessage": "ABC123",
            "status": "{{rawStatus}}",
            "timestamp": 1700000000
        }
        """;

        var callback = _parser.Parse(json);

        callback.Should().NotBeNull();
        callback!.Kind.Should().Be(ProviderCallbackKind.DeliveryStatus);
        callback.ProviderMessageId.Should().Be("ABC123");
        callback.InstanceId.Should().Be("3100000000");
        callback.MessageStatus.Should().Be(expected);
        callback.OccurredAtUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1700000000).UtcDateTime);
    }

    [Fact]
    public void Parse_NoAccountStatus_SetsTerminalReasonToRecipientNotInMax()
    {
        var json = """{"typeWebhook":"outgoingMessageStatus","idMessage":"ABC123","status":"noAccount"}""";

        var callback = _parser.Parse(json);

        callback.Should().NotBeNull();
        callback!.TerminalReason.Should().Be(NotificationReason.RecipientNotInMax);
    }

    [Fact]
    public void Parse_FailedStatus_SetsTerminalReasonToRejectedByProvider_NotWhatsAppReason()
    {
        var json = """{"typeWebhook":"outgoingMessageStatus","idMessage":"ABC123","status":"failed"}""";

        var callback = _parser.Parse(json);

        callback.Should().NotBeNull();
        callback!.TerminalReason.Should().Be(NotificationReason.RejectedByProvider);
        callback.TerminalReason.Should().NotBe(NotificationReason.RecipientHasNoWhatsApp);
    }

    [Fact]
    public void Parse_DeliveredStatus_LeavesTerminalReasonNull()
    {
        var json = """{"typeWebhook":"outgoingMessageStatus","idMessage":"ABC123","status":"delivered"}""";

        var callback = _parser.Parse(json);

        callback.Should().NotBeNull();
        callback!.TerminalReason.Should().BeNull();
    }

    [Fact]
    public void Parse_OutgoingMessageStatus_UnknownStatusValue_ReturnsNull()
    {
        var json = """
        {
            "typeWebhook": "outgoingMessageStatus",
            "idMessage": "ABC123",
            "status": "notInGroup"
        }
        """;

        _parser.Parse(json).Should().BeNull();
    }

    [Fact]
    public void Parse_StateInstanceChanged_MapsToProviderChannelState()
    {
        var json = """
        {
            "typeWebhook": "stateInstanceChanged",
            "instanceData": { "idInstance": 42 },
            "stateInstance": "authorized"
        }
        """;

        var callback = _parser.Parse(json);

        callback.Should().NotBeNull();
        callback!.Kind.Should().Be(ProviderCallbackKind.ChannelState);
        callback.InstanceId.Should().Be("42");
        callback.ChannelState.Should().Be(ProviderChannelState.Authorized);
    }

    [Fact]
    public void Parse_StateInstanceChanged_PendingPassword_MapsToNotAuthorized()
    {
        var json = """{"typeWebhook":"stateInstanceChanged","instanceData":{"idInstance":42},"stateInstance":"pendingPassword"}""";

        var callback = _parser.Parse(json);

        callback.Should().NotBeNull();
        callback!.ChannelState.Should().Be(ProviderChannelState.NotAuthorized);
    }

    [Fact]
    public void Parse_NoTimestamp_FallsBackToNow()
    {
        var json = """{"typeWebhook":"stateInstanceChanged","stateInstance":"blocked"}""";
        var before = DateTime.UtcNow;

        var callback = _parser.Parse(json);

        callback.Should().NotBeNull();
        callback!.OccurredAtUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public void Parse_InstanceIdAsString_IsAlsoRead()
    {
        var json = """{"typeWebhook":"stateInstanceChanged","instanceData":{"idInstance":"abc-def"},"stateInstance":"authorized"}""";
        _parser.Parse(json)!.InstanceId.Should().Be("abc-def");
    }
}
