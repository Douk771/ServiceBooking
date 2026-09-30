using FluentAssertions;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.UnitTests;

public class ShopTimeZoneChangePolicyTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Same_zone_is_allowed_with_orders() =>
        ShopTimeZoneChangePolicy.IsAllowed("Asia/Barnaul", "Asia/Barnaul", true, Now).Should().BeTrue();

    [Fact]
    public void Different_id_with_same_offset_is_allowed_with_orders() =>
        ShopTimeZoneChangePolicy.IsAllowed("Asia/Barnaul", "Asia/Krasnoyarsk", true, Now).Should().BeTrue();

    [Fact]
    public void Different_offset_without_orders_is_allowed() =>
        ShopTimeZoneChangePolicy.IsAllowed("Asia/Barnaul", "Europe/Moscow", false, Now).Should().BeTrue();

    [Fact]
    public void Different_offset_with_orders_is_refused() =>
        ShopTimeZoneChangePolicy.IsAllowed("Asia/Barnaul", "Europe/Moscow", true, Now).Should().BeFalse();

    [Fact]
    public void Unrecognized_zone_with_orders_is_refused() =>
        ShopTimeZoneChangePolicy.IsAllowed("Asia/Barnaul", "Not/AZone", true, Now).Should().BeFalse();

    [Theory]
    [InlineData(420, "UTC+7")]
    [InlineData(330, "UTC+5:30")]
    [InlineData(-180, "UTC-3")]
    [InlineData(0, "UTC+0")]
    public void FormatOffset_renders_sign_hours_and_minutes(int minutes, string expected) =>
        ShopTimeZoneChangePolicy.FormatOffset(minutes).Should().Be(expected);

    [Fact]
    public void LockedText_contains_current_offset_and_falls_back_to_utc()
    {
        ShopTimeZoneChangePolicy.LockedText(420).Should().EndWith("Выберите город в том же часовом поясе (UTC+7).");
        ShopTimeZoneChangePolicy.LockedText(null).Should().Contain("(UTC+0)");
    }
}
