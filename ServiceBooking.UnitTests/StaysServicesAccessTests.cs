using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE39.md §39.12 (A39-10) — the three rights of services, every right × every position; ARCHITECTURE_CYCLE39.md §39.9.1 — the salon mask and the services types.</summary>
public class StaysServicesAccessTests
{
    [Theory]
    [InlineData(StaysMyRole.Owner, StaysPermission.ManageServices, true)]
    [InlineData(StaysMyRole.Owner, StaysPermission.EditServiceContent, true)]
    [InlineData(StaysMyRole.Owner, StaysPermission.ManageServiceDates, true)]
    [InlineData(StaysMyRole.SuperAdmin, StaysPermission.ManageServices, true)]
    [InlineData(StaysMyRole.Manager, StaysPermission.ManageServices, false)]
    [InlineData(StaysMyRole.Manager, StaysPermission.EditServiceContent, true)]
    [InlineData(StaysMyRole.Manager, StaysPermission.ManageServiceDates, true)]
    [InlineData(StaysMyRole.Manager, StaysPermission.ManageCompany, false)]
    [InlineData(StaysMyRole.Manager, StaysPermission.ManageBookings, true)]
    [InlineData(StaysMyRole.Housekeeper, StaysPermission.ManageServices, false)]
    [InlineData(StaysMyRole.Housekeeper, StaysPermission.EditServiceContent, false)]
    [InlineData(StaysMyRole.Housekeeper, StaysPermission.ManageServiceDates, false)]
    [InlineData(StaysMyRole.Housekeeper, StaysPermission.ViewSchedule, true)]
    [InlineData(StaysMyRole.Housekeeper, StaysPermission.ViewBookings, false)]
    public void Right_by_position(StaysMyRole role, StaysPermission permission, bool expected) => StaysAccess.Has(role, permission).Should().Be(expected);

    [Fact]
    public void The_new_rights_are_appended_after_the_old_ones_and_named_as_in_the_contract()
    {
        Enum.GetNames<StaysPermission>().Should().Equal(
            "ManageCompany", "ManageHouses", "EditHouseContent", "ViewBookings", "ManageBookings", "ManageBlocks", "ViewSchedule", "ViewCabinet",
            "ManageServices", "EditServiceContent", "ManageServiceDates");
        StaysAccess.For(StaysMyRole.Manager).Should().HaveCount(8);
        StaysAccess.For(StaysMyRole.Housekeeper).Should().ContainSingle().Which.Should().Be(StaysPermission.ViewSchedule);
    }

    // ── the salon mask never sees the new types ──

    [Fact]
    public void Salon_mask_is_built_from_the_salon_types_only()
    {
        // `1 << 32 == 1` in C#: a service type from the request body must not flip the bit of BookingConfirmed, and the old stay types must not set a bit either.
        CompanyNotificationsController.BuildMask([NotificationType.StaffServiceOrderCreated, NotificationType.StayGuestSessionCancelledByOwner]).Should().Be(0);
        CompanyNotificationsController.BuildMask([NotificationType.StaffStayCreated, NotificationType.StaffOrderCreated]).Should().Be(0);
        CompanyNotificationsController.BuildMask([NotificationType.Reminder, NotificationType.StaffServiceOrderCreated]).Should().Be(1 << (int)NotificationType.Reminder);
        CompanyNotificationsController.BuildMask(NotificationTypeCatalog.BookingTypes).Should().Be(0b111_1111);
    }

    [Fact]
    public void Every_type_is_exactly_one_of_salon_order_or_stay()
    {
        foreach (var type in Enum.GetValues<NotificationType>())
            new[] { NotificationTypeCatalog.IsBookingType(type), NotificationTypeCatalog.IsOrderType(type), NotificationTypeCatalog.IsStayType(type) }
                .Count(x => x).Should().Be(1, type.ToString());
        NotificationTypeCatalog.ServiceTypes.Select(t => (int)t).Should().Equal(Enumerable.Range(27, 12));
        NotificationTypeCatalog.ServiceTypes.Should().OnlyContain(t => NotificationTypeCatalog.IsStayType(t) && !NotificationTypeCatalog.IsBookingType(t));
        Enum.GetValues<NotificationType>().Select(t => (int)t).Should().NotContain([39, 40], "39 and 40 are reserved for cycle 40 (iCal)");
    }

    [Fact]
    public void Types_outside_the_salon_do_not_use_the_mask_in_the_gate()
    {
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        foreach (var type in NotificationTypeCatalog.ServiceTypes)
        {
            var result = NotificationGate.Evaluate(
                ServiceBooking.API.Services.EffectivePlan.Free with { AllowNotificationChannel = true, PaidNotificationNumbers = 1 }, type, true,
                new ServiceBooking.Core.Entities.NotificationChannel(), new ServiceBooking.Core.Entities.CompanyNotificationSettings { EnabledTypeMask = 0 },
                false, now, now.AddHours(2), channelIsFunded: true);
            result.Outcome.Should().Be(NotificationGateOutcome.Allowed, type.ToString());
        }
    }

    [Fact]
    public void Every_new_type_has_its_own_log_text()
    {
        foreach (var type in NotificationTypeCatalog.ServiceTypes)
            ServiceBooking.API.Services.NotificationTexts.TypeText(type).Should().NotBe("Уведомление", type.ToString());
    }

    [Fact]
    public void Gate_requires_the_executor_with_zero_prepay()
    {
        var empty = new StayProviderFacts(null, null, null, null, null);
        StaysBookingGate.Evaluate(true, true, 1, 1, 0, null, empty).ReasonCode.Should().Be(NotAcceptingReason.NoProviderInfo);
        StaysBookingGate.Evaluate(true, true, 1, 1, 30, null, empty).ReasonCode.Should().Be(NotAcceptingReason.NoPaymentDetails, "the requisites are still asked only with a prepayment");
    }
}
