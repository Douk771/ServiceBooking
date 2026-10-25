using FluentAssertions;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests.Cycle48;

/// <summary>CY48-B3 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.8.3): the messenger tax remark is a constant, not the price-list notice.</summary>
public class MessengerTaxNoteTests
{
    [Fact]
    public void MessengerTaxNote_EqualsTheJsonString()
    {
        using var doc = ContractFiles.Load("cycle48", "storefront-pages.json");

        PricingCatalogBuilder.MessengerTaxNote.Should().Be(doc.RootElement.GetProperty("pricing").GetProperty("messengerTaxNote").GetString());
    }

    [Fact]
    public void MessengerTaxNote_IsNotTheNotice_AndHasNoNoticeWording()
    {
        PricingCatalogBuilder.MessengerTaxNote.Should().NotBe(PricingCatalogBuilder.DefaultNotice);
        PricingCatalogBuilder.MessengerTaxNote.Should().NotContain("счёт").And.NotContain("заявк");
    }
}
