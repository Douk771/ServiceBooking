using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// TD-03-bis — срок ответа по ВИДУ обращения (LEGAL_REVIEW_CYCLE16.md §2.4, требование О5).
/// До цикла 16 все пять видов получали единый срок в 10 рабочих дней, из-за чего уточнение и
/// уничтожение по построению выходили за норму ч. 3 ст. 20 152-ФЗ.
/// </summary>
public class SubjectRequestDeadlineTests
{
    // 2026-09-21 — понедельник, та же опорная дата, что и в WorkingDaysTests.
    private static readonly DateTime Monday = new(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);

    private static readonly SubjectRequestOptions Options = new() { ResponseWorkingDays = 10 };

    [Fact]
    public void Access_keeps_ten_working_days()
    {
        var (due, disclosed) = SubjectRequestDeadline.For(SubjectRequestKind.Access, Monday, Options);

        disclosed.Should().Be(10, "ч. 1 ст. 20 152-ФЗ — доступ к своим данным");
        due.Should().Be(WorkingDays.Add(Monday, 10));
    }

    [Theory]
    [InlineData(SubjectRequestKind.Rectification)]
    [InlineData(SubjectRequestKind.Erasure)]
    public void Rectification_and_erasure_get_seven_working_days(SubjectRequestKind kind)
    {
        var (due, disclosed) = SubjectRequestDeadline.For(kind, Monday, Options);

        disclosed.Should().Be(7, "ч. 3 ст. 20 152-ФЗ — уточнение и уничтожение");
        due.Should().Be(WorkingDays.Add(Monday, 7));
        due.Should().BeBefore(WorkingDays.Add(Monday, 10),
            "срок обязан быть СТРОЖЕ прежнего единого, иначе правка бессмысленна");
    }

    [Fact]
    public void Consent_withdrawal_gets_thirty_calendar_days_not_working_days()
    {
        var (due, disclosed) = SubjectRequestDeadline.For(SubjectRequestKind.ConsentWithdrawal, Monday, Options);

        due.Should().Be(Monday.AddDays(30), "ч. 5 ст. 21 152-ФЗ — срок календарный, не рабочий");
        due.Should().NotBe(WorkingDays.Add(Monday, 30),
            "30 рабочих дней — это шесть недель, а норма даёт тридцать календарных");
        disclosed.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Complaint_falls_back_to_configured_default()
    {
        var (due, disclosed) = SubjectRequestDeadline.For(SubjectRequestKind.Complaint, Monday, Options);

        disclosed.Should().Be(Options.ResponseWorkingDays, "специального срока для жалобы норма не задаёт");
        due.Should().Be(WorkingDays.Add(Monday, Options.ResponseWorkingDays));
    }

    /// <summary>🔴 Главное свойство: названный заявителю срок обязан СОВПАДАТЬ с фактически записанным
    /// <c>DueAtUtc</c>. Расхождение между обещанием и записью само по себе вводит в заблуждение (§2.4) —
    /// и именно оно возникло бы, если бы отзыв согласия считали календарно, а называли как раньше.</summary>
    [Theory]
    [InlineData(SubjectRequestKind.Access)]
    [InlineData(SubjectRequestKind.Rectification)]
    [InlineData(SubjectRequestKind.Erasure)]
    [InlineData(SubjectRequestKind.ConsentWithdrawal)]
    [InlineData(SubjectRequestKind.Complaint)]
    public void Disclosed_working_days_never_promise_sooner_than_the_recorded_due_date(SubjectRequestKind kind)
    {
        var (due, disclosed) = SubjectRequestDeadline.For(kind, Monday, Options);

        WorkingDays.Add(Monday, disclosed).Should().BeOnOrBefore(due,
            "заявителю нельзя называть срок ПОЗЖЕ того, который записан как крайний");
    }
}
