using System.Text.RegularExpressions;
using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.UnitTests.Cycle48;

/// <summary>CY48-B2 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.8.1): the notice above the price grids is built from the shared template.</summary>
public class PricingNoticeTests
{
    private static string Template(string key)
    {
        using var doc = ContractFiles.Load("cycle48", "storefront-pages.json");
        return doc.RootElement.GetProperty("pricing").GetProperty("notice").GetProperty(key).GetString()!;
    }

    private static string SchemaPattern(string schema)
    {
        using var doc = ContractFiles.Load("cycle48", "openapi.json");
        return doc.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(schema).GetProperty("pattern").GetString()!;
    }

    [Fact]
    public void Templates_AreByteForByteTheJsonStrings()
    {
        PricingCatalogBuilder.NoticeTemplate.Should().Be(Template("zapis"));
        OrdersPricingCatalogBuilder.NoticeTemplate.Should().Be(Template("derivedOrders"));
    }

    // LEGAL_REVIEW_CYCLE48.md «Поправка 31.10.2026 (2)»: «Заказы» и «Запись» показывают один и тот же короткий текст.
    [Fact]
    public void OrdersTemplate_IsTheZapisOne() =>
        OrdersPricingCatalogBuilder.NoticeTemplate.Should().Be(PricingCatalogBuilder.NoticeTemplate);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultNotice_HasNoPlaceholders_AndPointsToPaymentInfo_WithoutTheInvoicePath(bool orders)
    {
        var notice = orders ? OrdersPricingCatalogBuilder.DefaultNotice : PricingCatalogBuilder.DefaultNotice;

        notice.Should().NotContain("{{").And.NotContain("}}");
        notice.Should().Contain("в кабинете").And.Contain("«Оплата и возврат»");
        // The detailed "request → invoice → bank transfer" path is only on /payment-info now.
        notice.Should().NotContain("заявку").And.NotContain("счёт").And.NotContain("банковским переводом").And.NotContain("→");
        notice.Should().NotContain(OperatorPublicRequisites.Get("СРОК_СЧЁТА"));
    }

    [Theory]
    [InlineData(false, "PricingNoticeZapis")]
    [InlineData(true, "PricingNoticeOrders")]
    public void DefaultNotice_MatchesThePatternOfTheContract(bool orders, string schema)
    {
        var notice = orders ? OrdersPricingCatalogBuilder.DefaultNotice : PricingCatalogBuilder.DefaultNotice;

        Regex.IsMatch(notice, SchemaPattern(schema)).Should().BeTrue();
    }

    [Fact]
    public void Build_UsesTheDefaultNotice_UnlessOneIsGiven()
    {
        PricingCatalogBuilder.Build("v1", [], [], legalNotice: null).Notice.Should().Be(PricingCatalogBuilder.DefaultNotice);
        PricingCatalogBuilder.Build("v1", [], [], legalNotice: null, notice: "свой").Notice.Should().Be("свой");
        OrdersPricingCatalogBuilder.Build("v1", [], 10, legalNotice: null).Notice.Should().Be(OrdersPricingCatalogBuilder.DefaultNotice);
        OrdersPricingCatalogBuilder.Build("v1", [], 10, legalNotice: null, notice: "свой").Notice.Should().Be("свой");
    }
}
