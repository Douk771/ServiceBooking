using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE23.md §391 — the single builder of absolute links, and the fail-closed config check.</summary>
public class PublicSiteLinksTests
{
    private static PublicSiteLinks Links(string services = "https://ezbook.ru", string orders = "https://goods.ezbook.ru") =>
        new(Options.Create(new PublicSitesOptions { ServicesBaseUrl = services, OrdersBaseUrl = orders }));

    [Fact]
    public void CompanyPageUrl_Salon_UsesServicesSiteAndCompanyPrefix() =>
        Links().CompanyPageUrl(new Company { Kind = CompanyKind.Services, Slug = "barber" })
            .Should().Be("https://ezbook.ru/company/barber");

    [Fact]
    public void CompanyPageUrl_Shop_UsesOrdersSiteRootSlug() =>
        Links().CompanyPageUrl(new Company { Kind = CompanyKind.Orders, Slug = "shaurma" })
            .Should().Be("https://goods.ezbook.ru/shaurma");

    [Fact]
    public void OrderPageUrl_IsOrdersSiteOrderToken() =>
        Links().OrderPageUrl("abc").Should().Be("https://goods.ezbook.ru/o/abc");

    [Fact]
    public void SiteBaseUrl_TrimsTrailingSlash() =>
        Links(orders: "http://localhost:5174/").SiteBaseUrl(CompanyKind.Orders).Should().Be("http://localhost:5174");

    private static IConfiguration Config(string? services, string? orders) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PublicSites:ServicesBaseUrl"] = services,
            ["PublicSites:OrdersBaseUrl"] = orders,
        }).Build();

    [Fact]
    public void ValidatePublicSites_ProductionDefaults_DoNotThrow() =>
        ((Action)(() => DeploymentSafetyChecks.ValidatePublicSites(Config("https://ezbook.ru", "https://goods.ezbook.ru"), "Production")))
            .Should().NotThrow();

    [Theory]
    [InlineData("http://ezbook.ru", "https://goods.ezbook.ru")]        // not https outside developer envs
    [InlineData("https://ezbook.ru/", "https://goods.ezbook.ru")]      // trailing slash
    [InlineData("https://ezbook.ru", "https://goods.ezbook.ru/shop")]  // path
    [InlineData("https://ezbook.ru", "goods.ezbook.ru")]               // not absolute
    public void ValidatePublicSites_BadValue_ThrowsOutsideDeveloperEnvironment(string services, string orders) =>
        ((Action)(() => DeploymentSafetyChecks.ValidatePublicSites(Config(services, orders), "Production")))
            .Should().Throw<InvalidOperationException>();

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "  ")]
    public void ValidatePublicSites_NotConfigured_MeansDefaults_AndDoesNotThrow(string? services, string? orders) =>
        ((Action)(() => DeploymentSafetyChecks.ValidatePublicSites(Config(services, orders), "Production"))).Should().NotThrow();

    [Fact]
    public void SiteBaseUrl_BlankConfigured_FallsBackToProductionDefaults()
    {
        var links = Links(services: "", orders: " ");
        links.SiteBaseUrl(CompanyKind.Services).Should().Be("https://ezbook.ru");
        links.SiteBaseUrl(CompanyKind.Orders).Should().Be("https://goods.ezbook.ru");
    }

    [Fact]
    public void ServiceLinksByKind_Stays_EqualLegacyStayMethods()
    {
        var l = Links();
        var c = Guid.NewGuid();
        var s = Guid.NewGuid();
        l.ServiceOrderPageUrl(CompanyKind.Stays, "tok").Should().Be(l.StayServiceOrderPageUrl("tok"));
        l.CabinetServiceSessionUrl(CompanyKind.Stays, c, s).Should().Be(l.StaysCabinetServiceSessionUrl(c, s));
        l.ServiceOrderRelativePath(CompanyKind.Stays, "tok").Should().Be("/s/tok");
    }

    [Fact]
    public void ServiceLinksByKind_Baths_UseBathsSite()
    {
        var l = new PublicSiteLinks(Options.Create(new PublicSitesOptions { BathsBaseUrl = "https://bani.ezbook.ru" }));
        l.ServiceOrderPageUrl(CompanyKind.Baths, "tok").Should().Be("https://bani.ezbook.ru/s/tok");
        l.ServiceOrderRelativePath(CompanyKind.Baths, "tok").Should().Be("/s/tok");
    }

    [Fact]
    public void ServiceOrderRelativePath_NonSlotKind_Throws() =>
        ((Action)(() => Links().ServiceOrderRelativePath(CompanyKind.Orders, "t"))).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void ValidatePublicSites_LocalhostHttp_AllowedInDevelopment() =>
        ((Action)(() => DeploymentSafetyChecks.ValidatePublicSites(Config("http://localhost:5173", "http://localhost:5174"), "Development")))
            .Should().NotThrow();
}
