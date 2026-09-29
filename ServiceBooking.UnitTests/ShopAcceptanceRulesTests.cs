using FluentAssertions;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE24.md §449.3 — pause / stop as pure functions; the resume of a pause is computed, not scheduled.</summary>
public class ShopAcceptanceRulesTests
{
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
    private static readonly DateTime Now = new(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc); // 12:00 local, Wednesday

    private static PickupSchedule Hours(int start = 540, int end = 1260) => new(
        new ShopScheduleSnapshot(Moscow, new WeeklyHours(new Dictionary<DayOfWeek, IReadOnlyList<TimeInterval>>
        {
            [DayOfWeek.Wednesday] = [new(start, end)], [DayOfWeek.Thursday] = [new(start, end)],
        }), new Dictionary<DateOnly, SpecialDayHours>()),
        new PickupSettings(true, true, 15, 1, 15));

    [Fact]
    public void Mode_StoppedWinsOverPause_ExpiredPauseIsAccepting()
    {
        ShopAcceptanceRules.Mode(new ShopSettings(), Now).Should().Be(ShopAcceptanceMode.Accepting);
        ShopAcceptanceRules.Mode(new ShopSettings { PausedUntilUtc = Now.AddMinutes(1) }, Now).Should().Be(ShopAcceptanceMode.Paused);
        ShopAcceptanceRules.Mode(new ShopSettings { PausedUntilUtc = Now }, Now).Should().Be(ShopAcceptanceMode.Accepting);
        ShopAcceptanceRules.Mode(new ShopSettings { OrdersStopped = true, PausedUntilUtc = Now.AddHours(1) }, Now).Should().Be(ShopAcceptanceMode.Stopped);
    }

    [Fact]
    public void State_TextsAndWhoWhen()
    {
        var paused = ShopAcceptanceRules.State(new ShopSettings
        {
            PausedUntilUtc = Now.AddMinutes(90), AcceptanceChangedAtUtc = Now.AddMinutes(-2), AcceptanceChangedByName = "Анна"
        }, Now, Moscow);
        paused.Mode.Should().Be(ShopAcceptanceMode.Paused);
        paused.StatusText.Should().Be("Пауза до 13:30");
        paused.ChangedText.Should().Be("Изменено: Анна, 11:58");
        paused.PausedUntilUtc.Should().Be(Now.AddMinutes(90));

        ShopAcceptanceRules.State(new ShopSettings(), Now, Moscow).StatusText.Should().Be("Принимаем заказы");
        ShopAcceptanceRules.State(new ShopSettings { OrdersStopped = true }, Now, Moscow).StatusText.Should().Be("Не принимаем, пока не включите");
        ShopAcceptanceRules.State(new ShopSettings { PausedUntilUtc = Now.AddMinutes(-5) }, Now, Moscow).PausedUntilUtc.Should().BeNull();
    }

    [Fact]
    public void PausedUntilText_OtherDayNamesTheDate()
    {
        ShopAcceptanceRules.PausedUntilText(Now.AddHours(1), Now, Moscow).Should().Be("13:00");
        ShopAcceptanceRules.PausedUntilText(new DateTime(2026, 10, 2, 6, 0, 0, DateTimeKind.Utc), Now, Moscow).Should().Be("2 окт 9:00");
    }

    [Fact]
    public void ChangedText_IsNeutral_AndOptional()
    {
        ShopAcceptanceRules.ChangedText(null, "Анна", Moscow).Should().BeNull();
        ShopAcceptanceRules.ChangedText(Now, null, Moscow).Should().Be("Изменено: 12:00");
        ShopAcceptanceRules.ChangedText(Now, "  ", Moscow).Should().Be("Изменено: 12:00");
    }

    [Theory]
    [InlineData(PauseDuration.Minutes15, 15)]
    [InlineData(PauseDuration.Minutes30, 30)]
    [InlineData(PauseDuration.Hour1, 60)]
    public void Apply_FixedPauses(PauseDuration pause, int minutes) =>
        ShopAcceptanceRules.Apply(ShopAcceptanceMode.Paused, pause, Now, Hours()).Should().Be((false, (DateTime?)Now.AddMinutes(minutes)));

    [Fact]
    public void Apply_EndOfDay_IsTheEndOfTheLastInterval_OrTheNearestMidnight()
    {
        ShopAcceptanceRules.Apply(ShopAcceptanceMode.Paused, PauseDuration.EndOfDay, Now, Hours())
            .Should().Be((false, (DateTime?)new DateTime(2026, 9, 30, 18, 0, 0, DateTimeKind.Utc)));
        // After the last interval: the nearest local midnight (00:00 Oct 1 Moscow = 21:00 UTC).
        ShopAcceptanceRules.Apply(ShopAcceptanceMode.Paused, PauseDuration.EndOfDay, new DateTime(2026, 9, 30, 18, 30, 0, DateTimeKind.Utc), Hours())
            .Should().Be((false, (DateTime?)new DateTime(2026, 9, 30, 21, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Apply_PausedWithoutDuration_IsRefused_OthersClearThePause()
    {
        ShopAcceptanceRules.Apply(ShopAcceptanceMode.Paused, null, Now, Hours()).Should().BeNull();
        ShopAcceptanceRules.Apply(ShopAcceptanceMode.Accepting, PauseDuration.Hour1, Now, Hours()).Should().Be((false, (DateTime?)null));
        ShopAcceptanceRules.Apply(ShopAcceptanceMode.Stopped, null, Now, Hours()).Should().Be((true, (DateTime?)null));
    }
}
