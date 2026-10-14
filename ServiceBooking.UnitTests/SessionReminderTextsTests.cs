using FluentAssertions;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>BE-42-5: the reminder texts (API_CONTRACT_CYCLE42.md §42.36.4), the journal row of the reminder, the plan of the event, the day-bar label and the declension in the admin's 409.</summary>
public class SessionReminderTextsTests
{
    private static ServiceTextFacts Facts() =>
        new("Баня у реки", "Баня №1", null, new DateOnly(2026, 11, 20), 20 * 60, 3, "+7 900 000-00-00", null, 0, false, null, null, null, "ул. Парковая, 1", "Барнаул");

    [Fact]
    public void Messenger_text_names_the_booking_marks_local_time_and_carries_the_link_and_unsubscribe()
    {
        var text = ServiceNotificationTexts.Messenger(NotificationType.ServiceGuestSessionReminder, Facts(), "https://bani.ezbook.ru/s/tok", "https://x/u/1", ServiceWording.Baths);
        text.Should().StartWith("Баня у реки: напоминаем о брони — «Баня №1», ").And.Contain("(время местное, Барнаул). Бронь: https://bani.ezbook.ru/s/tok")
            .And.EndWith("Отписаться от сообщений: https://x/u/1");
    }

    [Fact]
    public void Page_text_has_no_link_no_address_no_phone()
    {
        var text = ServiceNotificationTexts.BathsSessionReminderPageText("Баня у реки", "Баня №1", new DateOnly(2026, 11, 20), 20 * 60, 3, "Барнаул");
        text.Should().StartWith("Баня у реки: напоминаем о брони — «Баня №1», ").And.EndWith("(время местное, Барнаул).");
        text.Should().NotContain("http").And.NotContain("+7").And.NotContain("Парковая");
    }

    [Fact]
    public void Guest_push_is_signed_ezbook_bani_and_has_no_personal_data()
    {
        var push = ServiceNotificationTexts.GuestPush(NotificationType.ServiceGuestSessionReminder, Guid.NewGuid(), "/s/tok", forOrder: true, ServiceWording.Baths);
        push.Title.Should().Be("EZBOOK Бани");
        push.Body.Should().Be("Скоро ваш сеанс — откройте бронь");
        push.Url.Should().Be("/s/tok");
    }

    [Fact]
    public void Event_plan_queues_only_the_guest_reminder() =>
        StayNotificationPlan.ForOrderEvent(StayServiceOrderEventKind.SessionReminderSent).Should().ContainSingle()
            .Which.Should().Be(new PlannedNotification(NotificationType.ServiceGuestSessionReminder, StayAudience.Guest));

    [Fact]
    public void Staff_event_text_of_the_reminder() => ServiceWording.Baths.StaffEventText(StayServiceOrderEventKind.SessionReminderSent).Should().Be("Отправлено напоминание");

    [Theory]
    [InlineData("Анна", 4, false, "Анна, 4 чел.")]
    [InlineData("Анна", null, false, "Анна")]
    [InlineData("  Анна ", 2, false, "Анна, 2 чел.")]
    [InlineData("Анна", 4, true, "Бронь")]
    [InlineData(null, 4, false, "Бронь")]
    [InlineData("", 4, false, "Бронь")]
    public void Day_bar_label(string? name, int? guests, bool erased, string expected) => ServiceDayService.BathsBarLabel(name, guests, erased).Should().Be(expected);

    [Theory]
    [InlineData(GateUnit.Resource, 1, "На новом тарифе доступно 1 ресурс, опубликовано 3.")]
    [InlineData(GateUnit.Resource, 2, "На новом тарифе доступно 2 ресурса, опубликовано 3.")]
    [InlineData(GateUnit.Resource, 5, "На новом тарифе доступно 5 ресурсов, опубликовано 3.")]
    [InlineData(GateUnit.Resource, 11, "На новом тарифе доступно 11 ресурсов, опубликовано 3.")]
    [InlineData(GateUnit.Resource, 21, "На новом тарифе доступно 21 ресурс, опубликовано 3.")]
    [InlineData(GateUnit.House, 1, "На новом тарифе доступно 1 дом, опубликовано 3.")]
    [InlineData(GateUnit.House, 3, "На новом тарифе доступно 3 дома, опубликовано 3.")]
    [InlineData(GateUnit.House, 0, "На новом тарифе доступно 0 домов, опубликовано 3.")]
    public void Plan_limit_overflow_declines_the_unit(GateUnit unit, int limit, string start) =>
        StaysTexts.PlanLimitOverflow(unit, limit, 3).Should().StartWith(start).And.EndWith("Подтвердите превышение лимита, чтобы продолжить.");
}
