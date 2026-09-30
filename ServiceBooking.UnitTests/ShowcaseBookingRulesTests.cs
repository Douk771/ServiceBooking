using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.Services.Retention;
using ServiceBooking.API.Services.Retention.Rules;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE28.md §577.4, API_CONTRACT_CYCLE28.md §592–§593 — refusal wording, the visitor-booking retention rule and the BookingDto mark.</summary>
public class ShowcaseBookingRulesTests
{
    [Fact]
    public void BookingClosedRefusal_IsTheContractJson()
    {
        var refusal = ShowcaseTexts.BookingClosedRefusal();

        refusal.Code.Should().Be("ShowcaseBookingClosed");
        refusal.Message.Should().Be("Это пример страницы салона: компания вымышленная, запись к ней не принимается.");
    }

    [Theory]
    [InlineData(24)]
    [InlineData(1)]
    [InlineData(72)]
    public void VisitorBookingRule_Cutoff_IsNowMinusTheConfiguredHours(int hours)
    {
        var now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        ShowcaseVisitorBookingRule.CutoffFor(now, hours).Should().Be(now.AddHours(-hours));
    }

    [Fact]
    public void RetentionPeriods_DefaultVisitorWindow_IsADay() =>
        new RetentionPeriods().ShowcaseVisitorBookingHours.Should().Be(24);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task VisitorBookingRule_WithNoConfiguredWindow_DeletesNothing_AndSaysSo(int hours)
    {
        // The skip branch never touches the database, so no context is needed.
        var rule = new ShowcaseVisitorBookingRule(null!);
        var ctx = new RetentionContext(DateTime.UtcNow, new RetentionPeriods { ShowcaseVisitorBookingHours = hours }, BatchSize: 100, DryRun: false);

        var outcome = await rule.ApplyAsync(ctx, CancellationToken.None);

        outcome.Skipped.Should().BeTrue();
        outcome.Affected.Should().Be(0);
        outcome.Summary.Should().Contain("не настроен");
    }

    [Theory]
    [InlineData(ShowcaseBookingKind.None, false)]
    [InlineData(ShowcaseBookingKind.Seeded, true)]
    [InlineData(ShowcaseBookingKind.Visitor, true)]
    public void BookingDto_CompanyIsShowcase_FollowsTheBookingMark(ShowcaseBookingKind kind, bool expected)
    {
        var serviceId = Guid.NewGuid();
        var booking = new Booking
        {
            Id = Guid.NewGuid(), CompanyId = Guid.NewGuid(), ServiceId = serviceId, MasterId = "m", ShowcaseKind = kind,
            Date = new DateOnly(2026, 10, 1), StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0), Price = 100,
        };
        var service = new Service { Id = serviceId, Name = "Стрижка", DurationMinutes = 60, Price = 100 };
        var master = new AppUser { FirstName = "Анна", LastName = "Иванова" };

        BookingEndpointHelpers.MapToDto(booking, service, master, "Гость").CompanyIsShowcase.Should().Be(expected);
    }
}
