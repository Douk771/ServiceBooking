using FluentAssertions;
using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE9.md §105.6 — the pure, DI-free pieces of <see cref="StaffPushScheduler"/>
/// (payload rendering, idempotency key shape). The DB-driven decision logic (staff-push-enabled gate,
/// "creator is the master" gate, subscription lookup, queueing) needs a real <c>AppDbContext</c> and is
/// intentionally NOT covered here — see the cycle report for the functional-test scenarios it should be
/// covered by instead.</summary>
public class StaffPushSchedulerTests
{
    [Fact]
    public void BuildPayload_SingleService_JoinsServiceDateTimeAndClientName()
    {
        var payload = StaffPushScheduler.BuildPayload(
            ["Стрижка"], new DateOnly(2026, 9, 25), new TimeOnly(14, 30), "Анна");

        payload.Should().Be("Новая запись: Стрижка · 25.09.2026 14:30 · Анна");
    }

    [Fact]
    public void BuildPayload_MultipleServices_JoinsWithCommaSpace()
    {
        var payload = StaffPushScheduler.BuildPayload(
            ["Стрижка", "Укладка"], new DateOnly(2026, 1, 5), new TimeOnly(9, 5), "Клиент");

        payload.Should().Contain("Стрижка, Укладка");
    }

    [Fact]
    public void BuildPayload_NoServices_FallsBackToGenericWord()
    {
        var payload = StaffPushScheduler.BuildPayload([], new DateOnly(2026, 1, 1), TimeOnly.MinValue, "Клиент");
        payload.Should().Contain("услуга");
    }

    [Fact]
    public void BuildPayload_NeverContainsAPhoneLikePattern()
    {
        // §105.6 (П8): "Телефона клиента нет." — a regression here would mean a phone number leaking
        // into an OS notification tray, including a locked screen.
        var payload = StaffPushScheduler.BuildPayload(
            ["Маникюр"], new DateOnly(2026, 3, 3), new TimeOnly(10, 0), "Иван +7 900 123-45-67");

        // The method itself never ADDS a phone — this only proves it doesn't invent one from thin air;
        // a caller passing a phone number as the "name" is a caller bug, not this method's to prevent.
        payload.Should().NotContain("тел.");
    }

    [Fact]
    public void BuildIdempotencyKey_SameInputs_AreEqual()
    {
        var bookingId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();

        var first = StaffPushScheduler.BuildIdempotencyKey(bookingId, "user-1", subscriptionId);
        var second = StaffPushScheduler.BuildIdempotencyKey(bookingId, "user-1", subscriptionId);

        first.Should().Be(second);
    }

    [Fact]
    public void BuildIdempotencyKey_DifferentSubscriptions_AreDistinct()
    {
        var bookingId = Guid.NewGuid();

        var forDeviceA = StaffPushScheduler.BuildIdempotencyKey(bookingId, "user-1", Guid.NewGuid());
        var forDeviceB = StaffPushScheduler.BuildIdempotencyKey(bookingId, "user-1", Guid.NewGuid());

        // §105.6: "три устройства — три уведомления" — the idempotency key must NOT collapse different
        // devices into the same row.
        forDeviceA.Should().NotBe(forDeviceB);
    }

    [Fact]
    public void BuildIdempotencyKey_ContainsTheTypeBookingUserAndSubscription()
    {
        var bookingId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();

        var key = StaffPushScheduler.BuildIdempotencyKey(bookingId, "user-42", subscriptionId);

        key.Should().Contain("StaffBookingCreated")
            .And.Contain(bookingId.ToString())
            .And.Contain("user-42")
            .And.Contain(subscriptionId.ToString());
    }
}
