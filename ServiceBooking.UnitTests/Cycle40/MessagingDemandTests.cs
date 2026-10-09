using FluentAssertions;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle40;

/// <summary>ARCHITECTURE_CYCLE40.md §40.4.4.</summary>
public class MessagingDemandTests
{
    private static CompanyDemandFacts Salon(int? mask = null, bool active = true, bool showcase = false) =>
        new(CompanyKind.Services, active, showcase, mask, false, false);

    [Fact] public void Salon_NoSettingsRow_DefaultMaskMeansDemand() => MessagingDemand.HasDemand([Salon()]).Should().BeTrue();

    [Fact]
    public void Salon_OnlyStaffTypesEnabled_NoDemand()
    {
        var staffOnly = 1 << (int)NotificationType.StaffBookingCreated;
        MessagingDemand.HasDemand([Salon(staffOnly)]).Should().BeFalse();
    }

    [Fact]
    public void Salon_ReminderOnly_HasDemand() =>
        MessagingDemand.HasDemand([Salon(1 << (int)NotificationType.Reminder)]).Should().BeTrue();

    [Fact]
    public void Salon_ZeroMask_NoDemand() => MessagingDemand.HasDemand([Salon(0)]).Should().BeFalse();

    [Fact]
    public void InactiveOrShowcaseCompany_NeverCounts() =>
        MessagingDemand.HasDemand([Salon(active: false), Salon(showcase: true)]).Should().BeFalse();

    [Fact]
    public void Shop_FollowsCustomerMessengerFlag()
    {
        MessagingDemand.HasDemand([new(CompanyKind.Orders, true, false, null, true, false)]).Should().BeTrue();
        MessagingDemand.HasDemand([new(CompanyKind.Orders, true, false, null, false, true)]).Should().BeFalse();
    }

    [Fact]
    public void Stays_FollowsGuestMessengerFlag()
    {
        MessagingDemand.HasDemand([new(CompanyKind.Stays, true, false, null, false, true)]).Should().BeTrue();
        MessagingDemand.HasDemand([new(CompanyKind.Stays, true, false, null, true, false)]).Should().BeFalse();
    }

    [Fact] public void NoCompanies_NoDemand() => MessagingDemand.HasDemand([]).Should().BeFalse();
}
