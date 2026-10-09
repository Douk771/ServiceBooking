using System.Text.RegularExpressions;
using FluentAssertions;
using ServiceBooking.API.Services.Orders.Notifications;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>«дом» is matched at a word start (otherwise «уведомления» trips it). ARCHITECTURE_CYCLE42.md §42.4.4, LEGAL_REVIEW_CYCLE42.md §10 — the strings of «Бани» carry none of the words of houses and stays.</summary>
public class BathsWordingGuardTests
{
    private static readonly Regex Forbidden = new("заказ|(?<![а-яё])дом|прожив|заселен|заезд|бизнес-день|задаток|невозвратн|депозит|туристическ", RegexOptions.IgnoreCase);
    private static readonly ServiceWording W = ServiceWording.Baths;

    private static ServiceTextFacts Facts(int prepay, bool paid) =>
        new("Баня у реки", "Баня №1", null, new DateOnly(2026, 11, 20), 20 * 60, 3, "+7 900 000-00-00", "причина", prepay, paid, "р/с 1", "Бронь", new DateTime(2026, 11, 20, 12, 0, 0), "ул. Парковая, 1", "Барнаул");

    private static List<string> AllTexts()
    {
        var t = new List<string> { W.GuestPushTitle, W.RefundTerminal, W.OrderAlreadyCancelled, W.ServiceOrderDone, W.TooManyHeldOrders, W.TooManyOrdersPerDay,
            W.HoldExpired("+7 900"), W.HoldExpired(null), W.ProofNotAllowed("Подтверждена"), W.GuestTimeSuffix("Барнаул"), W.GuestTimeSuffix(null) };
        foreach (var s in new[] { "Held", "AwaitingPaymentCheck", "Confirmed", "Completed", "ExpiredUnpaid", "PaymentRejected", "CancelledByGuest", "CancelledByOwner" })
            t.Add(W.StatusText(s));
        foreach (var status in Enum.GetValues<StayBookingStatus>())
            foreach (var prepaid in new[] { false, true })
                t.Add(W.OutcomeText(status, "причина", "+7 900", prepaid));
        t.AddRange(Enum.GetValues<StayServiceOrderEventKind>().Select(W.StaffEventText));

        var id = Guid.NewGuid();
        var types = new[]
        {
            NotificationType.StaffServiceOrderCreated, NotificationType.StaffServiceOrderPaymentProofUploaded, NotificationType.StaffServiceSessionCancelledByGuest,
            NotificationType.ServiceGuestOrderCreated, NotificationType.ServiceGuestHoldExpiring, NotificationType.ServiceGuestHoldExpired, NotificationType.ServiceGuestConfirmed,
            NotificationType.ServiceGuestPaymentRejected, NotificationType.ServiceGuestCancelledByOwner, NotificationType.ServiceGuestPaymentRejected,
        };
        foreach (var type in types)
        {
            foreach (var f in new[] { Facts(0, false), Facts(1500, true) })
            {
                var staff = ServiceNotificationTexts.StaffPush(type, f, id, "https://bani.ezbook.ru/cabinet/x", W);
                t.Add(staff.Body);
                t.Add(ServiceNotificationTexts.Messenger(type, f, "https://bani.ezbook.ru/s/t", null, W));
            }
            var guest = ServiceNotificationTexts.GuestPush(type, id, "/s/t", forOrder: true, W);
            t.Add(guest.Title);
            t.Add(guest.Body);
        }
        return t;
    }

    [Fact]
    public void No_baths_string_uses_the_words_of_houses_and_stays() =>
        AllTexts().Where(s => Forbidden.IsMatch(s)).Should().BeEmpty();

    [Fact]
    public void Title_and_time_mark_follow_the_decisions()
    {
        W.GuestPushTitle.Should().Be("EZBOOK Бани");
        W.GuestTimeSuffix("Барнаул").Should().Be(" (время местное, Барнаул)");
    }

    [Fact]
    public void Guest_messenger_marks_local_time_with_the_city() =>
        ServiceNotificationTexts.Messenger(NotificationType.ServiceGuestConfirmed, Facts(0, true), "u", null, W).Should().Contain("(время местное, Барнаул)");

    [Fact]
    public void Staff_push_names_a_booking_not_an_order() =>
        ServiceNotificationTexts.StaffPush(NotificationType.StaffServiceOrderCreated, Facts(0, false), Guid.NewGuid(), "u", W).Body.Should().StartWith("Новая бронь");
}
