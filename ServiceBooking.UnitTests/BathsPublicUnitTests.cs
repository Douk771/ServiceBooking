using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Baths;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.API.Startup;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>BE-42-4 — the pure parts of the public side of «Бани»: the catalog query rules, «Мои брони» arrangement, the log mask, the wording and the shapes of the DTOs (ARCHITECTURE_CYCLE42.md §42.10).</summary>
public class BathsPublicUnitTests
{
    private static readonly DateTime Now = new(2027, 2, 10, 12, 0, 0, DateTimeKind.Utc);

    private static CatalogResource Resource(string city, string company, int position, string name, Guid? companyId = null)
    {
        var c = new Company { Id = companyId ?? Guid.NewGuid(), Name = company, Slug = company.ToLowerInvariant() };
        var s = new StayService { Id = Guid.NewGuid(), CompanyId = c.Id, Name = name, Slug = name.ToLowerInvariant(), Position = position };
        return new CatalogResource(new ServiceScope(s, c, new StaysSettings()), 1, city, null, null, null, null);
    }

    // ── catalog rules ──

    [Theory]
    [InlineData(null, 12)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(7, 7)]
    [InlineData(50, 50)]
    [InlineData(500, 50)]
    public void Page_size_defaults_to_12_and_is_clamped_to_1_50(int? requested, int expected) =>
        BathsCatalogRules.ClampPageSize(requested).Should().Be(expected);

    [Theory]
    [InlineData("2027-02-10", null)]
    [InlineData("2027-03-01", null)]
    [InlineData("2027-02-09", "Эта дата уже прошла")]
    [InlineData("10.02.2027", "Неверный формат даты")]
    [InlineData("2027-13-01", "Неверный формат даты")]
    [InlineData("", "Неверный формат даты")]
    public void The_date_filter_rejects_a_bad_format_and_the_past(string raw, string? error) =>
        BathsCatalogRules.ParseDate(raw, new DateOnly(2027, 2, 10), out _).Should().Be(error);

    [Fact]
    public void A_parsed_date_is_returned()
    {
        BathsCatalogRules.ParseDate(" 2027-02-15 ", new DateOnly(2027, 2, 10), out var date).Should().BeNull();
        date.Should().Be(new DateOnly(2027, 2, 15));
    }

    [Fact]
    public void The_catalog_is_ordered_by_city_then_company_then_position()
    {
        var shared = Guid.NewGuid();
        var a = Resource("Шерегеш", "Баня Б", 1, "Чан", shared);
        var b = Resource("Шерегеш", "Баня Б", 0, "Сауна", shared);
        var c = Resource("Шерегеш", "Баня А", 5, "Фурако");
        var d = Resource("Абакан", "Баня Я", 0, "Баня");
        BathsCatalogRules.Order([a, b, c, d]).Select(r => r.Service.Name).Should().Equal("Баня", "Фурако", "Сауна", "Чан");
    }

    [Fact]
    public void A_card_url_is_company_slash_resource_and_carries_the_capacity()
    {
        var r = Resource("Шерегеш", "Sauna", 0, "Chan");
        r.Service.Capacity = 6;
        var card = BathsCatalogService.ToCard(r);
        card.Url.Should().Be("/sauna/chan");
        card.Capacity.Should().Be(6);
        card.CityName.Should().Be("Шерегеш");
    }

    // ── «Мои брони» ──

    [Theory]
    [InlineData(StayBookingStatus.Held, -1, true)]
    [InlineData(StayBookingStatus.AwaitingPaymentCheck, 1, true)]
    [InlineData(StayBookingStatus.Confirmed, 1, true)]
    [InlineData(StayBookingStatus.Confirmed, -1, false)]
    [InlineData(StayBookingStatus.CancelledByGuest, 1, false)]
    [InlineData(StayBookingStatus.ExpiredUnpaid, 1, false)]
    public void A_booking_is_active_while_it_holds_the_time(StayBookingStatus status, int endsInHours, bool expected) =>
        BathsMyOrdersService.IsActive(status, Now.AddHours(endsInHours), Now).Should().Be(expected);

    private static (MyBathOrderDto, DateTime) Row(string id, bool active, int startDaysFromNow) =>
        (new MyBathOrderDto("/s/" + id, "К", "Р", "т", null, StayBookingStatus.Confirmed, "Confirmed", "т", 1, active), Now.AddDays(startDaysFromNow));

    [Fact]
    public void Active_bookings_come_first_by_start_ascending_then_the_rest_by_start_descending()
    {
        var rows = new[] { Row("past-old", false, -30), Row("soon", true, 1), Row("past-new", false, -2), Row("later", true, 9) };
        BathsMyOrdersService.Arrange(rows).Select(r => r.Dto.OrderUrl).Should().Equal("/s/soon", "/s/later", "/s/past-new", "/s/past-old");
    }

    // ── the log mask ──

    [Theory]
    [InlineData("/api/baths/service-orders/public/AbC_123-token", "/api/baths/service-orders/public/***")]
    [InlineData("/api/baths/service-orders/public/AbC_123-token/cancel", "/api/baths/service-orders/public/***/cancel")]
    [InlineData("/api/baths/service-orders/public/AbC/payment-proofs/00000000-0000-0000-0000-000000000001", "/api/baths/service-orders/public/***/payment-proofs/00000000-0000-0000-0000-000000000001")]
    [InlineData("/api/baths/service-orders/public/", null)]
    [InlineData("/api/baths/service-orders/my", null)]
    [InlineData("/api/baths/catalog", null)]
    public void The_token_of_a_baths_booking_is_masked_in_the_request_path(string path, string? expected) =>
        LoggingExtensions.MaskSensitiveRequestPath(path).Should().Be(expected);

    [Fact]
    public void The_token_of_a_dom_booking_is_still_masked() =>
        LoggingExtensions.MaskSensitiveRequestPath("/api/stays/service-orders/public/secret").Should().Be("/api/stays/service-orders/public/***");

    // ── wording and shapes ──

    [Theory]
    [InlineData("Шерегеш", "Время местное, Шерегеш")]
    [InlineData("  Шерегеш ", "Время местное, Шерегеш")]
    [InlineData("", "Время местное")]
    [InlineData(null, "Время местное")]
    public void The_local_time_note_names_the_city(string? city, string expected) =>
        ServiceWording.LocalTimeNote(city).Should().Be(expected);

    [Fact]
    public void The_resource_path_of_the_baths_vertical_has_no_uslugi_segment_and_the_dom_one_has()
    {
        SlotVerticals.Baths.ResourcePagePath("sauna", "chan").Should().Be("/sauna/chan");
        SlotVerticals.Stays.ResourcePagePath("dom", "banya").Should().Be("/dom/uslugi/banya");
    }

    [Fact]
    public void The_order_input_reads_guestsCount_and_an_absent_one_is_null()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonSerializer.Deserialize<CreateServiceOrderInput>("""{"guestsCount":4,"guestName":"А"}""", options)!.GuestsCount.Should().Be(4);
        JsonSerializer.Deserialize<CreateServiceOrderInput>("""{"guestName":"А"}""", options)!.GuestsCount.Should().BeNull();
    }

    [Fact]
    public void Public_cycle42_fields_are_omitted_for_dom_and_written_for_baths()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var dom = JsonSerializer.Serialize(new PublicServiceDto(
            Guid.NewGuid(), "s", "n", null, [], 1, 2, 60, [], [], null, new PublicServiceStandaloneDto(true, null, null, null, null, 30, null),
            new PublicServiceCompanyDto("c", "C", null, null, "/c"), null, true, null, true, null, new DateOnly(2027, 2, 10), "Europe/Moscow"), options);
        dom.Should().NotContain("capacity").And.NotContain("cityName").And.NotContain("localTimeNote");

        var baths = JsonSerializer.Serialize(new PublicServiceDto(
            Guid.NewGuid(), "s", "n", null, [], 1, 2, 60, [], [], null, new PublicServiceStandaloneDto(true, null, null, null, null, 30, null),
            new PublicServiceCompanyDto("c", "C", null, null, "/c"), null, true, null, true, null, new DateOnly(2027, 2, 10), "Asia/Novokuznetsk", 6, "Шерегеш", "Время местное, Шерегеш"), options);
        using var json = JsonDocument.Parse(baths);
        json.RootElement.GetProperty("capacity").GetInt32().Should().Be(6);
        json.RootElement.GetProperty("cityName").GetString().Should().Be("Шерегеш");
        json.RootElement.GetProperty("localTimeNote").GetString().Should().Be("Время местное, Шерегеш");
    }

    [Fact]
    public void The_guests_count_rules_answer_with_the_contracts_sentences()
    {
        GuestsCountRules.Validate(null, 6).Error.Should().Be("Укажите, сколько человек придёт");
        GuestsCountRules.Validate(7, 6).Error.Should().Be("Число гостей — от 1 до 6");
        GuestsCountRules.Validate(0, 6).Error.Should().Be("Число гостей — от 1 до 6");
        GuestsCountRules.Validate(6, 6).Stored.Should().Be(6);
        GuestsCountRules.Validate(5, null).Stored.Should().BeNull();
    }
}
