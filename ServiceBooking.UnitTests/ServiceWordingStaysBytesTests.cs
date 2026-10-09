using FluentAssertions;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE42.md §42.4.4 — the «Дома» wording is the strings of cycles 39–41 byte for byte (literals pinned here).</summary>
public class ServiceWordingStaysBytesTests
{
    private static readonly ServiceWording W = ServiceWording.Stays;

    [Fact]
    public void Fixed_strings_are_the_old_ones()
    {
        W.GuestPushTitle.Should().Be("ezbook · Дома");
        W.RefundTerminal.Should().Be("Заказ уже завершён — отменять нечего");
        W.OrderAlreadyCancelled.Should().Be("Заказ уже отменён");
        W.ServiceOrderDone.Should().Be("Заказ завершён — уведомления не нужны");
        W.TooManyHeldOrders.Should().Be("Слишком много неоплаченных заказов. Оплатите или отмените текущий заказ");
        W.TooManyOrdersPerDay.Should().Be("Слишком много заказов с этого номера. Попробуйте позже");
    }

    [Fact]
    public void Functions_give_the_old_strings()
    {
        W.HoldExpired("+7 900").Should().Be("Время на оплату истекло, заказ снят. Если вы уже оплатили — свяжитесь с компанией: +7 900");
        W.HoldExpired(null).Should().Be("Время на оплату истекло, заказ снят. Если вы уже оплатили — свяжитесь с компанией.");
        W.ProofNotAllowed("Подтверждён").Should().Be("Заказ уже подтверждён — подтверждение оплаты не нужно");
        W.StatusText("Confirmed").Should().Be("Подтверждён");
        W.StatusText("CancelledByOwner").Should().Be("Отменён компанией");
        W.StaffEventText(StayServiceOrderEventKind.Created).Should().Be("Заказ создан");
        W.OutcomeText(StayBookingStatus.CancelledByGuest, null, null, false).Should().Be("Вы отменили заказ.");
        W.OutcomeText(StayBookingStatus.ExpiredUnpaid, null, "+7 900", false)
            .Should().Be("Время на оплату истекло, заказ снят. Если вы успели оплатить — свяжитесь с компанией. Контакт компании: +7 900.");
        W.GuestTimeSuffix("Барнаул").Should().BeEmpty();
    }

    [Fact]
    public void Wording_matches_legacy_service_texts_for_every_status_and_event()
    {
        foreach (var status in Enum.GetValues<StayBookingStatus>())
        {
            W.StatusText(status.ToString()).Should().Be(ServiceTexts.StatusText(status.ToString()));
            W.OutcomeText(status, "р", "+7", true).Should().Be(ServiceTexts.OutcomeText(status, "р", "+7", true));
        }
        foreach (var kind in Enum.GetValues<StayServiceOrderEventKind>())
            W.StaffEventText(kind).Should().Be(ServiceTexts.StaffEventText(kind));
    }

    [Fact]
    public void Non_bath_kinds_use_the_stays_wording() =>
        new[] { CompanyKind.Stays, CompanyKind.Services, CompanyKind.Orders }.Select(ServiceWording.For).Should().OnlyContain(w => ReferenceEquals(w, ServiceWording.Stays));
}
