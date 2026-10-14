using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 42, I-2 (ARCHITECTURE_CYCLE42.md §42.3.4): банной компании нельзя сменить город или часовой пояс, пока есть будущие брони.
/// CY42-106…109 по заданию заказчика цикла; «Дома» и остальные виды не меняются.
/// </summary>
public class Cycle42LocationChangeTests(TestDatabaseFixture fixture) : Cycle42TestBase(fixture)
{
    private const string LockedPrefix = "Нельзя сменить город или часовой пояс, пока есть будущие брони";

    private Task<int> NewCityAsync(string timeZoneId) => WithDbAsync(async db =>
    {
        var name = Unique("Город ");
        var city = new City { Name = name, Region = "Тест", TimeZoneId = timeZoneId, IsActive = true, SearchName = name.ToLowerInvariant() };
        db.Cities.Add(city);
        await db.SaveChangesAsync();
        return city.Id;
    });

    private async Task<Guid> AddBookingAsync(Guid companyId, StayBookingStatus status, int daysAhead, DateTime? holdExpiresAtUtc = null)
    {
        var svc = await WithDbAsync(async db =>
        {
            var s = new StayService { Id = Guid.NewGuid(), CompanyId = companyId, Name = "Русская баня", Slug = Unique("r-").ToLowerInvariant(), IsPublished = true, Capacity = 6, AvailableForHouseBookings = false };
            db.StayServices.Add(s);
            await db.SaveChangesAsync();
            return s.Id;
        });
        return await WithDbAsync(async db =>
        {
            var tz = await db.Companies.Where(c => c.Id == companyId).Select(c => c.TimeZoneId).FirstAsync();
            var date = InDays(daysAhead);
            var start = BusinessClock.ToUtc(tz, date, 600);
            var order = new StayServiceOrder
            {
                Id = Guid.NewGuid(), CompanyId = companyId, ServiceId = svc, PublicToken = Guid.NewGuid().ToString("N"), IdempotencyKey = Guid.NewGuid(), Status = status,
                HoldExpiresAtUtc = holdExpiresAtUtc, GuestName = "Анна Гость", GuestPhone = "+79005554433", GuestsCount = 2, TotalRub = 4000, ServiceAmountRub = 4000, PrepayRub = 1200,
                DueOnSiteRub = 2800, TimeZoneIdSnapshot = tz, GuestKind = StayActorKind.Guest
            };
            db.StayServiceOrders.Add(order);
            db.StayServiceSessions.Add(new StayServiceSession
            {
                Id = Guid.NewGuid(), CompanyId = companyId, ServiceId = svc, StayServiceOrderId = order.Id, BusinessDate = date, StartMinute = 600, Hours = 2, StartUtc = start,
                EndUtc = start.AddHours(2), BufferMinutesSnapshot = 30, OccupiedUntilUtc = start.AddHours(2).AddMinutes(30), ServiceNameSnapshot = "Русская баня",
                ServiceAmountRub = 4000, TotalRub = 4000, AddedByKind = StayActorKind.Guest
            });
            await db.SaveChangesAsync();
            return order.Id;
        });
    }

    private Task<HttpResponseMessage> PutCityAsync(BathCtx c, int cityId) => AuthedClient(c.Token).PutJsonAsync($"/api/companies/{c.CompanyId}", new { cityId });

    private Task<(int? CityId, string Zone)> StoredAsync(Guid companyId) =>
        WithDbAsync(async db => { var x = await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => new { c.CityId, c.TimeZoneId }).FirstAsync(); return (x.CityId, x.TimeZoneId); });

    [Fact, TestCase("CY42-106")]
    public async Task Baths_WithoutBookings_CityAndZoneChange()
    {
        var c = await CreateBathAsync();
        var city = await NewCityAsync("Asia/Vladivostok");
        var r = await PutCityAsync(c, city);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        (await StoredAsync(c.CompanyId)).Should().Be((city, "Asia/Vladivostok"));
    }

    [Theory, TestCase("CY42-107")]
    [InlineData(StayBookingStatus.Held)]
    [InlineData(StayBookingStatus.AwaitingPaymentCheck)]
    [InlineData(StayBookingStatus.Confirmed)]
    public async Task Baths_WithFutureBooking_CityChangeIs409_AndNothingIsSaved(StayBookingStatus status)
    {
        var c = await CreateBathAsync();
        var before = await StoredAsync(c.CompanyId);
        await AddBookingAsync(c.CompanyId, status, 3, status == StayBookingStatus.Held ? DateTime.UtcNow.AddMinutes(30) : null);
        var city = await NewCityAsync("Asia/Vladivostok");

        var r = await PutCityAsync(c, city);

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var text = await r.Content.ReadAsStringAsync();
        text.Should().StartWith(LockedPrefix);
        text.ToLowerInvariant().Should().NotContain("заказ").And.NotContain("дом");
        (await StoredAsync(c.CompanyId)).Should().Be(before);

        var sameZoneCity = await NewCityAsync(before.Zone);
        (await PutCityAsync(c, sameZoneCity)).StatusCode.Should().Be(HttpStatusCode.Conflict, "смена города без смены пояса тоже запрещена");
        var zone = await AuthedClient(c.Token).PutJsonAsync($"/api/companies/{c.CompanyId}", new { timeZoneId = "Asia/Vladivostok" });
        zone.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact, TestCase("CY42-108")]
    public async Task Baths_WithOnlyPastCancelledOrExpiredBookings_CityChanges()
    {
        var c = await CreateBathAsync();
        await AddBookingAsync(c.CompanyId, StayBookingStatus.Confirmed, -3);
        await AddBookingAsync(c.CompanyId, StayBookingStatus.CancelledByGuest, 3);
        await AddBookingAsync(c.CompanyId, StayBookingStatus.Held, 3, DateTime.UtcNow.AddMinutes(-10));
        var city = await NewCityAsync("Asia/Vladivostok");

        var r = await PutCityAsync(c, city);

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        (await StoredAsync(c.CompanyId)).Should().Be((city, "Asia/Vladivostok"));
    }

    [Fact, TestCase("CY42-109")]
    public async Task OtherKinds_AreUnchanged_SalonWithFutureOrderLikeSessionStillChangesCity()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var city = await NewCityAsync("Asia/Vladivostok");
        await AddBookingAsync(company.Id, StayBookingStatus.Confirmed, 3);

        var r = await AuthedClient(owner.Token).PutJsonAsync($"/api/companies/{company.Id}", new { cityId = city });

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        (await StoredAsync(company.Id)).Should().Be((city, "Asia/Vladivostok"));
    }
}
