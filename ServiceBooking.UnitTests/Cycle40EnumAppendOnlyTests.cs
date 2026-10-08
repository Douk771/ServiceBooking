using FluentAssertions;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.2.2 — enums are stored as int; members may only be appended at the end, and the stored values of
/// the new members are fixed. Existing members are pinned by their ordinal so a reorder or insertion in the middle fails here.
/// </summary>
public class Cycle40EnumAppendOnlyTests
{
    private static void Pin<T>(params (string Name, int Value)[] expected) where T : struct, Enum
    {
        var actual = Enum.GetValues<T>().Select(v => (v.ToString(), Convert.ToInt32(v))).ToList();
        actual.Should().Equal(expected.Select(e => (e.Name, e.Value)),
            $"{typeof(T).Name} is append-only: new members only at the end with the stored values from ARCHITECTURE_CYCLE40.md §40.2.2");
    }

    [Fact]
    public void ConsentSource_IsAppendOnly() => Pin<ConsentSource>(
        ("Registration", 0), ("ReAcceptance", 1), ("Profile", 2), ("CompanyCreation", 3), ("ChannelRequest", 4),
        ("ChannelLink", 5), ("PhotoForm", 6), ("HealthForm", 7), ("Booking", 8), ("Migrated", 9), ("AddressForm", 10),
        ("PaperForm", 11), ("MessengerOptInBooking", 12), ("MessengerOptInOrder", 13), ("MessengerOptInStay", 14));

    [Fact]
    public void ChannelStateReason_IsAppendOnly() => Pin<ChannelStateReason>(
        ("Authorized", 0), ("ProviderReportsUnauthorized", 1), ("ProviderReportsBlocked", 2),
        ("ConsecutiveSendFailuresExceeded", 3), ("DisconnectedByOwner", 4), ("SuspendedByAdmin", 5),
        ("UnauthorizedInstanceTimedOut", 6), ("IdleInstanceDeleted", 7), ("ReplacedAfterBan", 8), ("SecretUnavailable", 9),
        ("ServerCountryMismatch", 10), ("ReplacedByOwner", 11), ("RebindStarted", 12), ("TestMessageSent", 13),
        ("TestMessageFailed", 14), ("TestMessageSkipped", 15));

    [Fact]
    public void NotificationReason_NewMembersAreLastWithFixedValues()
    {
        var all = Enum.GetValues<NotificationReason>().Select(v => (Name: v.ToString(), Value: Convert.ToInt32(v))).ToList();
        all.Select(a => a.Value).Should().Equal(Enumerable.Range(0, 37), "values are contiguous 0..36, nothing inserted in the middle");
        all.TakeLast(3).Should().Equal(
            ("ClientDeclinedMessenger", 34), ("PlatformMessagingDisabled", 35), ("ChannelAccountMismatch", 36));
        all[33].Name.Should().Be("StayMessengerDisabled");
    }

    [Fact]
    public void ChannelTestResult_HasFixedValues() => Pin<ChannelTestResult>(
        ("Pending", 0), ("Sending", 1), ("Sent", 2), ("Failed", 3), ("SkippedSameNumber", 4),
        ("SkippedNoOwnerPhone", 5), ("SkippedPlatformDisabled", 6));

    [Fact]
    public void ChannelOptionChangeSource_HasFixedValues() => Pin<ChannelOptionChangeSource>(
        ("AdminBillingAccount", 0), ("AdminChannelCard", 1), ("TrialGrant", 2), ("TrialWindowStart", 3),
        ("TrialExpiry", 4), ("AdminOptionEnded", 5));

    [Fact]
    public void ChannelOptionCodes_AreFixed()
    {
        ChannelOptionCodes.WhatsApp.Should().Be("notifications.whatsapp");
        ChannelOptionCodes.Max.Should().Be("notifications.max");
        ChannelOptionCodes.MaxOptionSeedId.Should().Be(Guid.Parse("c4000000-0000-4000-8000-000000000040"));
    }
}
