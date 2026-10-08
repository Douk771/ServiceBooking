using System.Text.RegularExpressions;
using FluentAssertions;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>Т39-14, Т39-02 — the texts of the services vertical never use the forbidden words and never promise «не меньше 0 ₽».</summary>
public class ServiceTextsTests
{
    private static readonly Regex Forbidden = new("задат|невозвратн|депозит", RegexOptions.IgnoreCase);

    private static List<string> AllTexts()
    {
        var texts = typeof(ServiceTexts).GetFields().Where(f => f.FieldType == typeof(string)).Select(f => (string)f.GetValue(null)!).ToList();
        foreach (var policy in Enum.GetValues<StayServiceCancellationPolicy>())
            texts.AddRange([ServiceTexts.CancellationSummary(policy, 12), ServiceTexts.PolicyName(policy)]);
        texts.AddRange([
            ServiceTexts.RefundFullByRule(1200), ServiceTexts.RefundFullByOwner(1200), ServiceTexts.RefundPartialAtLeast(1000, 2000), ServiceTexts.RefundCostsOnly(1200),
            ServiceTexts.HoursOutOfRange(2, 6), ServiceTexts.OutsideStay("a", "b"), ServiceTexts.TooEarly(60), ServiceTexts.TooEarly(30), ServiceTexts.TooEarly(120),
            ServiceTexts.ItemUnavailable("Веник"), ServiceTexts.ItemQuantityExceeded("Веник", 3), ServiceTexts.TooManySessions(5), ServiceTexts.PriceChanged(4600),
            ServiceTexts.SlotUnavailableInForm("Баня", "пт 15 янв, 22:00 — сб 16 янв, 01:00"), ServiceTexts.HoldExpired("+7 900"), ServiceTexts.AlreadyStarted("+7 900"),
            ServiceTexts.ProofNotAllowed("Подтверждён"), ServiceTexts.SessionsOutsideWarning(1), ServiceTexts.SessionsOutsideWarning(3), ServiceTexts.OwnerCancelRefund(500)]);
        foreach (var status in Enum.GetValues<StayBookingStatus>())
        {
            texts.Add(ServiceTexts.StatusText(status.ToString()));
            texts.Add(ServiceTexts.OutcomeText(status, "причина", "+7 900", 0));
            texts.Add(ServiceTexts.OutcomeText(status, "причина", "+7 900", 1000));
        }
        texts.AddRange(Enum.GetValues<StayServiceSessionState>().Select(ServiceTexts.SessionStateText));
        texts.AddRange(Enum.GetValues<StayServiceOrderEventKind>().Select(ServiceTexts.StaffEventText));
        texts.AddRange(Enum.GetValues<StayServiceRequestBasis>().Select(ServiceTexts.BasisText));
        texts.AddRange(Enum.GetValues<StaysServiceConflictCode>().Select(ServicePublishRules.Message));
        texts.Add(ArrivalReminderTemplate.Default);
        texts.AddRange(ArrivalReminderTemplate.Placeholders.Select(p => p.Description));
        return texts;
    }

    [Fact]
    public void No_text_uses_the_forbidden_words() => AllTexts().Where(t => Forbidden.IsMatch(t)).Should().BeEmpty();

    [Fact]
    public void No_text_promises_a_refund_of_not_less_than_zero_roubles() =>
        AllTexts().Where(t => Regex.IsMatch(t, @"не меньше\s+0\s*[₽р]")).Should().BeEmpty();

    [Fact]
    public void A_guest_text_has_no_internal_words() =>
        AllTexts().Where(t => t.Contains("бизнес-день", StringComparison.OrdinalIgnoreCase) || t.Contains("Standard")).Should().BeEmpty();

    [Theory]
    [InlineData(30, "30 минут")]
    [InlineData(60, "1 час")]
    [InlineData(120, "2 часа")]
    [InlineData(90, "1 час 30 минут")]
    [InlineData(0, "0 минут")]
    public void Lead_time_is_spoken(int minutes, string expected) => ServiceTexts.LeadText(minutes).Should().Be(expected);

    [Fact]
    public void Refusal_texts_match_the_contract()
    {
        ServiceTexts.TooEarly(60).Should().Be("Забронировать можно не позже чем за 1 час до начала");
        ServiceTexts.HoursOutOfRange(2, 6).Should().Be("Длительность — от 2 до 6 часов");
        ServiceTexts.TooManySessions(5).Should().Be("К брони можно добавить не больше 5 услуг");
        ServiceTexts.PriceChanged(4600).Should().Be("Стоимость изменилась: 4 600 ₽. Проверьте и подтвердите ещё раз");
        ServiceTexts.ItemUnavailable("Веник").Should().Be("Позиция «Веник» больше недоступна");
        ServiceTexts.ItemQuantityExceeded("Веник", 3).Should().Be("«Веник» — не больше 3 на сеанс");
        ServiceTexts.RefundCostsOnly(1200).Should().Be("Компания вправе удержать только фактические расходы на подготовку, не больше 1 200 ₽. Остальное она обязана вернуть");
    }
}
