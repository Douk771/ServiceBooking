using FluentAssertions;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

public class StaysCompanyKindTests
{
    [Fact]
    public void Guard_refuses_each_non_salon_kind_with_its_own_text_and_lets_a_salon_through()
    {
        CompanyKindGuard.RejectNonSalon(CompanyKind.Services).Should().BeNull();
        CompanyKindGuard.RejectNonSalon(CompanyKind.Orders)!.Value.Should().Be("Это магазин: записи, услуги и расписание для него недоступны.");
        CompanyKindGuard.RejectNonSalon(CompanyKind.Stays)!.Value.Should().Be("Это компания «Дома»: записи, услуги и расписание для неё недоступны.");
    }

    private static PublicSiteLinks Links(string? stays = null) => new(Options.Create(new PublicSitesOptions { StaysBaseUrl = stays! }));

    [Fact]
    public void Links_for_stays_use_the_dom_site_and_default_when_blank()
    {
        var links = Links("");
        links.SiteBaseUrl(CompanyKind.Stays).Should().Be("https://dom.ezbook.ru");
        links.CompanyPageUrl(CompanyKind.Stays, "kedr").Should().Be("https://dom.ezbook.ru/kedr");
        links.HousePageUrl("kedr", "dom-1").Should().Be("https://dom.ezbook.ru/kedr/dom-1");
        links.StayBookingPageUrl("tok").Should().Be("https://dom.ezbook.ru/b/tok");
        var id = Guid.NewGuid();
        var bid = Guid.NewGuid();
        links.StaysCabinetBookingUrl(id, bid).Should().Be($"https://dom.ezbook.ru/cabinet/{id}/bookings/{bid}");
        Links("http://localhost:5175/").SiteBaseUrl(CompanyKind.Stays).Should().Be("http://localhost:5175");
        // the other two sites are untouched
        links.CompanyPageUrl(CompanyKind.Services, "salon").Should().Be("https://ezbook.ru/company/salon");
        links.CompanyPageUrl(CompanyKind.Orders, "shop").Should().Be("https://goods.ezbook.ru/shop");
    }
}
