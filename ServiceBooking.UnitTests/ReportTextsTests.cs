using FluentAssertions;
using ServiceBooking.API.Services.Orders.Reports;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>API_CONTRACT_CYCLE25.md §526–§530 — the texts and small rules of history rows, the customer card, the note and the pick list interval.</summary>
public class ReportTextsTests
{
    [Theory]
    [InlineData(0, "Найдено 0 заказов, выдано на 0 ₽")]
    [InlineData(1, "Найдено 1 заказ, выдано на 0 ₽")]
    [InlineData(3, "Найдено 3 заказа, выдано на 0 ₽")]
    [InlineData(128, "Найдено 128 заказов, выдано на 0 ₽")]
    public void HistorySummaryText_Plural(int count, string expected) => ShopReportService.SummaryText(count, 0m).Should().Be(expected);

    [Fact]
    public void HistorySummaryText_Money() => ShopReportService.SummaryText(128, 54300m).Should().MatchRegex(@"^Найдено 128 заказов, выдано на 54\s300 ₽$");

    private static HistoryRow Row(OrderStatus status = OrderStatus.Issued, PickupKind kind = PickupKind.Slot, bool erased = false, bool weight = false) => new(
        Guid.NewGuid(), 27, new DateOnly(2026, 9, 30), new DateTime(2026, 9, 30, 9, 30, 0, DateTimeKind.Utc), kind, status, "Анна", "79991231234", erased, weight, 540m, 3);

    [Fact]
    public void Row_SlotAndAsapPickupText_InTheShopZone()
    {
        var moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
        ShopReportService.ToRowDto(Row(), moscow, true).PickupText.Should().Be("30 сен, к 12:30");
        ShopReportService.ToRowDto(Row(kind: PickupKind.Asap), moscow, true).PickupText.Should().Be("30 сен, ≈ 12:30");
    }

    [Fact]
    public void Row_PhoneIsMasked_AndAbsentInTheCustomerCard()
    {
        var utc = TimeZoneInfo.Utc;
        ShopReportService.ToRowDto(Row(), utc, includePhone: true).CustomerPhoneMasked.Should().Be("+7 (···) ···-12-34");
        ShopReportService.ToRowDto(Row(), utc, includePhone: false).CustomerPhoneMasked.Should().BeNull();
    }

    [Fact]
    public void Row_Erased_HasNoPersonalData()
    {
        var dto = ShopReportService.ToRowDto(Row(erased: true), TimeZoneInfo.Utc, true);
        (dto.CustomerName, dto.CustomerPhoneMasked, dto.PersonalDataErased).Should().Be((null, null, true));
    }

    [Theory]
    [InlineData(OrderStatus.Ready, true, true)]
    [InlineData(OrderStatus.Issued, true, false)]
    [InlineData(OrderStatus.Ready, false, false)]
    public void Row_TotalIsApproximate_OnlyForUnissuedWeightOrders(OrderStatus status, bool weight, bool expected) =>
        ShopReportService.ToRowDto(Row(status, weight: weight), TimeZoneInfo.Utc, true).TotalIsApproximate.Should().Be(expected);

    [Fact]
    public void CustomerStats_OmitsZeroCancellationCounters()
    {
        ShopCustomerService.StatsText(14, 11, 6120m, 1, 1).Should().MatchRegex(@"^14 заказов · выдано 11 на 6\s120 ₽ · отменено покупателем 1 · не забрано 1$");
        ShopCustomerService.StatsText(1, 1, 500m, 0, 0).Should().Be("1 заказ · выдано 1 на 500 ₽");
        ShopCustomerService.StatsText(2, 1, 500m, 1, 0).Should().Be("2 заказа · выдано 1 на 500 ₽ · отменено покупателем 1");
    }

    [Theory]
    [InlineData("79991234567", "+7 999 123-45-67")]
    [InlineData("375291234567", "+375291234567")]
    public void CustomerPhoneDisplay(string canonical, string expected) => ShopCustomerService.PhoneDisplay(canonical).Should().Be(expected);

    [Fact]
    public void NoteText_UsesTheShopZone_AndTheStoredAuthorName()
    {
        var note = new ShopCustomerNote { Text = "без лука", UpdatedByName = "Иван Петров", UpdatedAtUtc = new DateTime(2026, 9, 30, 8, 40, 0, DateTimeKind.Utc) };
        var dto = ShopCustomerService.ToDto(note, TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"));
        dto.UpdatedText.Should().Be("Изменено: Иван Петров, 30 сен 11:40");
        dto.UpdatedByName.Should().Be("Иван Петров");
        ShopCustomerService.MaxNoteLength.Should().Be(1000);
    }

    [Theory]
    [InlineData("12:00", 720)]
    [InlineData("9:05", 545)]
    [InlineData("00:00", 0)]
    [InlineData("23:59", 1439)]
    public void PickListClock_Parses(string text, int minutes)
    {
        PickListService.TryParseClock(text, out var parsed).Should().BeTrue();
        parsed.Should().Be(minutes);
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("12:60")]
    [InlineData("12")]
    [InlineData("12:5")]
    [InlineData("ab:cd")]
    [InlineData("-1:00")]
    [InlineData("")]
    public void PickListClock_RejectsMalformed(string text) => PickListService.TryParseClock(text, out _).Should().BeFalse();
}
