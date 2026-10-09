using FluentAssertions;
using ServiceBooking.API.Services.Baths;

namespace ServiceBooking.UnitTests;

public class BathsLocationChangePolicyTests
{
    [Fact]
    public void Without_future_bookings_any_change_is_allowed() =>
        BathsLocationChangePolicy.IsAllowed(true, "Asia/Barnaul", "Europe/Moscow", false).Should().BeTrue();

    [Fact]
    public void With_future_bookings_a_city_change_is_refused_even_within_the_same_zone() =>
        BathsLocationChangePolicy.IsAllowed(true, "Asia/Barnaul", "Asia/Barnaul", true).Should().BeFalse();

    [Fact]
    public void With_future_bookings_a_zone_change_is_refused() =>
        BathsLocationChangePolicy.IsAllowed(false, "Asia/Barnaul", "Europe/Moscow", true).Should().BeFalse();

    [Fact]
    public void With_future_bookings_no_change_is_allowed() =>
        BathsLocationChangePolicy.IsAllowed(false, "Asia/Barnaul", "Asia/Barnaul", true).Should().BeTrue();

    [Fact]
    public void Locked_text_is_russian_names_the_booking_and_has_no_forbidden_words()
    {
        BathsLocationChangePolicy.LockedText.Should().StartWith("Нельзя сменить город или часовой пояс, пока есть будущие брони");
        BathsLocationChangePolicy.LockedText.ToLowerInvariant().Should().NotContain("заказ").And.NotContain("дом");
    }
}
