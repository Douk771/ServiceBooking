using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE24.md §458 — the split of NotificationType into booking types and order types, and its effect on the gate.</summary>
public class NotificationTypeCatalogTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EveryMember_IsExactlyOneOfBookingOrOrder()
    {
        foreach (var type in Enum.GetValues<NotificationType>())
            (NotificationTypeCatalog.IsBookingType(type) ^ NotificationTypeCatalog.IsOrderType(type))
                .Should().BeTrue($"{type} must be a booking type or an order type, never both or neither");
    }

    [Fact]
    public void BookingTypes_AreTheSevenCycle23Ones_InEnumOrder_SoTheSalonScreenIsUnchanged()
    {
        NotificationTypeCatalog.BookingTypes.Should().Equal(
            NotificationType.BookingConfirmed, NotificationType.Reminder, NotificationType.BookingCancelled, NotificationType.BookingRescheduled,
            NotificationType.StaffBookingCreated, NotificationType.StaffBookingCancelled, NotificationType.StaffBookingRescheduled);
        // The salon mask screen used to enumerate the whole enum; this is what it enumerates now — the same list.
        NotificationTypeCatalog.BookingTypes.Should().Equal(Enum.GetValues<NotificationType>().Where(t => (int)t <= 6));
    }

    [Fact]
    public void OrderTypes_AreAppended_WithFixedNumbers()
    {
        ((int)NotificationType.StaffOrderCreated).Should().Be(7);
        ((int)NotificationType.StaffOrderCancelledByCustomer).Should().Be(8);
        ((int)NotificationType.OrderAccepted).Should().Be(9);
        ((int)NotificationType.OrderReady).Should().Be(10);
        ((int)NotificationType.OrderRejected).Should().Be(11);
        ((int)NotificationType.OrderCancelledByShop).Should().Be(12);
        ((int)NotificationType.OrderEditedByShop).Should().Be(13);
        ((int)NotificationType.OrderPickupChanged).Should().Be(14);
        ((int)NotificationType.OwnerOrderLimitWarning).Should().Be(15);
    }

    [Fact]
    public void CustomerTypes_AreTheSixTheCustomerIsTold()
    {
        NotificationTypeCatalog.OrderTypes.Where(NotificationTypeCatalog.IsCustomerOrderType).Should().Equal(
            NotificationType.OrderAccepted, NotificationType.OrderReady, NotificationType.OrderRejected,
            NotificationType.OrderCancelledByShop, NotificationType.OrderEditedByShop, NotificationType.OrderPickupChanged);
    }

    [Fact]
    public void EveryNewType_HasATypeText_ForTheDeliveryLog()
    {
        foreach (var type in NotificationTypeCatalog.OrderTypes)
            NotificationTexts.TypeText(type).Should().NotBe("Уведомление", $"{type} must have its own log text");
    }

    // ── the gate: the salon mask concerns booking types only ────────────────────────────────────────

    private static NotificationGateResult Gate(NotificationType type, int mask) => NotificationGate.Evaluate(
        EffectivePlan.Free with { AllowNotificationChannel = true, PaidNotificationNumbers = 1 }, type, true, new NotificationChannel(),
        new CompanyNotificationSettings { EnabledTypeMask = mask }, false, Now, Now.AddHours(2), channelIsFunded: true);

    [Fact]
    public void Gate_OrderType_IgnoresTheSalonMask_EvenAnEmptyOne()
    {
        // A salon form saved with no type ticked (mask 0) must not silently switch off the messages of a shop.
        foreach (var type in NotificationTypeCatalog.OrderTypes)
            Gate(type, mask: 0).Outcome.Should().Be(NotificationGateOutcome.Allowed, type.ToString());
    }

    [Fact]
    public void Gate_BookingType_StillHonorsTheMask()
    {
        Gate(NotificationType.BookingConfirmed, mask: 0).Reason.Should().Be(NotificationReason.TypeDisabledByCompany);
        Gate(NotificationType.BookingConfirmed, mask: 1).Outcome.Should().Be(NotificationGateOutcome.Allowed);
        Gate(NotificationType.Reminder, mask: ~(1 << (int)NotificationType.Reminder)).Reason.Should().Be(NotificationReason.TypeDisabledByCompany);
    }

    [Fact]
    public void Gate_OrderType_IsNotStoppedByTheReminderLeadThreshold()
    {
        // MinLeadMinutes (default 120) concerns reminders only; an order message queued a minute before its "deadline" still goes.
        var result = NotificationGate.Evaluate(
            EffectivePlan.Free with { AllowNotificationChannel = true, PaidNotificationNumbers = 1 }, NotificationType.OrderReady, true,
            new NotificationChannel(), new CompanyNotificationSettings { MinLeadMinutes = 720 }, false, Now, Now.AddMinutes(1), channelIsFunded: true);
        result.Outcome.Should().Be(NotificationGateOutcome.Allowed);
    }

    [Fact]
    public void Gate_OrderType_StillBlockedByOptOutAndByNoPaidNumber()
    {
        NotificationGate.Evaluate(EffectivePlan.Free with { PaidNotificationNumbers = 1 }, NotificationType.OrderReady, true, new NotificationChannel(),
            null, recipientOptedOut: true, Now, Now.AddHours(2), true).Reason.Should().Be(NotificationReason.RecipientOptedOut);
        NotificationGate.Evaluate(EffectivePlan.Free, NotificationType.OrderReady, true, new NotificationChannel(),
            null, false, Now, Now.AddHours(2), true).Reason.Should().Be(NotificationReason.NotOnPaidPlan);
    }
}
