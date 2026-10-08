using System.Text.RegularExpressions;
using FluentAssertions;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

public class StayNotificationTextsTests
{
    private static StayTextFacts Facts(string? full = null) => new(
        "Кедр", "Берёза", new DateOnly(2026, 12, 30), new DateOnly(2027, 1, 2), 3, 4500, 10500, "Шерегеш, ул. Гора, 1", "+7 913 000-00-00",
        "авария", "Сбербанк 2202 0000", "Бронь", new DateTime(2026, 12, 1, 15, 30, 0), "14:00", full);

    [Fact]
    public void Staff_push_has_no_guest_data_and_an_absolute_url()
    {
        var id = Guid.NewGuid();
        var p = StayNotificationTexts.StaffPush(NotificationType.StaffStayCreated, Facts(), id, "https://dom.ezbook.ru/cabinet/x/bookings/y");
        p.Title.Should().Be("Кедр");
        p.Body.Should().Be("Новая бронь · «Берёза» · 30 дек – 2 янв · 3 ночи");
        p.Tag.Should().Be($"s-{id}");
        p.Url.Should().StartWith("https://");
    }

    [Fact]
    public void Guest_push_carries_nothing_personal()
    {
        var p = StayNotificationTexts.GuestPush(NotificationType.StayGuestConfirmed, Guid.NewGuid(), "TOKEN");
        p.Body.Should().Be("Статус вашей брони изменился");
        p.Url.Should().Be("/b/TOKEN");
        (p.Title + p.Body).Should().NotContain("Кедр").And.NotContain("Берёза");
    }

    [Fact]
    public void Requisites_go_only_into_the_created_messenger_message()
    {
        var created = StayNotificationTexts.Messenger(NotificationType.StayGuestCreated, Facts(), "https://dom/b/t", null);
        created.Should().Contain("Сбербанк 2202 0000").And.Contain("4 500 ₽").And.Contain("15:30 01.12");
        foreach (var t in Enum.GetValues<NotificationType>().Where(t => (int)t is >= 19 and <= 26 && t != NotificationType.StayGuestCreated))
            StayNotificationTexts.Messenger(t, Facts(), "https://dom/b/t", null).Should().NotContain("Сбербанк");
    }

    [Fact]
    public void Check_in_info_is_a_link_unless_the_owner_chose_the_full_text()
    {
        StayNotificationTexts.Messenger(NotificationType.StayGuestCheckInInfo, Facts(), "https://dom/b/t", null).Should().Contain("готова").And.NotContain("код 1234");
        StayNotificationTexts.Messenger(NotificationType.StayGuestCheckInInfo, Facts("код 1234"), "https://dom/b/t", null).Should().Contain("код 1234");
    }

    [Fact]
    public void No_message_uses_the_forbidden_words_and_each_ends_with_unsubscribe_when_given()
    {
        var rx = new Regex("задат|невозвратн|депозит", RegexOptions.IgnoreCase);
        foreach (var t in Enum.GetValues<NotificationType>().Where(t => (int)t >= 19))
        {
            var text = StayNotificationTexts.Messenger(t, Facts(), "https://dom/b/t", "https://ezbook.ru/u/x");
            rx.IsMatch(text).Should().BeFalse(t.ToString());
            text.Should().EndWith("https://ezbook.ru/u/x");
        }
    }
}
