using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.API.Services.Subjects;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE42.md §42.11, BE-42-6: ПДн банной компании (Т42-06) и проверка BathsBaseUrl.</summary>
public class Cycle42PrivacyUnitTests
{
    [Fact]
    public void ExportSite_BathsCompany_IsBaths() => SubjectDataExporter.ExportSite(CompanyKind.Baths).Should().Be("Baths");

    [Theory]
    [InlineData(CompanyKind.Stays)]
    [InlineData(CompanyKind.Services)]
    public void ExportSite_OtherKinds_AreStays(CompanyKind kind) => SubjectDataExporter.ExportSite(kind).Should().Be("Stays");

    [Fact]
    public void EraseOrder_KeepsGuestsCountAndClearsPersonalData()
    {
        var order = new StayServiceOrder
        {
            GuestUserId = "u1", GuestName = "Иван", GuestPhone = "+79990000000", Comment = "с вениками",
            NotifyByMessenger = true, GuestsCount = 6,
        };

        StayPersonalData.EraseOrder(order);

        order.GuestsCount.Should().Be(6);
        order.GuestUserId.Should().BeNull();
        order.GuestName.Should().BeNull();
        order.GuestPhone.Should().BeNull();
        order.Comment.Should().BeNull();
        order.NotifyByMessenger.Should().BeFalse();
        order.PersonalDataErased.Should().BeTrue();
    }

    [Fact]
    public void ExportDto_DefaultsKeepOldShape_AndCarryGuestsAndSite()
    {
        var dto = new ExportStayServiceOrderDto("c", "s", "t", 2, [], 0, 0, "Accepted", null, null, null, null, "u", [], [], 4, "Baths");
        dto.GuestsCount.Should().Be(4);
        dto.Site.Should().Be("Baths");
    }

    [Theory]
    [InlineData("http://bani.ezbook.ru")]
    [InlineData("https://bani.ezbook.ru/")]
    [InlineData("https://bani.ezbook.ru/x")]
    [InlineData("bani.ezbook.ru")]
    public void ValidatePublicSites_BadBathsBaseUrl_ThrowsInProduction(string value)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PublicSites:BathsBaseUrl"] = value }).Build();
        var act = () => DeploymentSafetyChecks.ValidatePublicSites(cfg, "Production");
        act.Should().Throw<InvalidOperationException>().WithMessage("*BathsBaseUrl*");
    }

    [Fact]
    public void ValidatePublicSites_BathsBaseUrl_HttpAllowedInDevelopment_AndBlankIsDefault()
    {
        var dev = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PublicSites:BathsBaseUrl"] = "http://localhost:5176" }).Build();
        var blank = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["PublicSites:BathsBaseUrl"] = "" }).Build();
        ((Action)(() => DeploymentSafetyChecks.ValidatePublicSites(dev, "Development"))).Should().NotThrow();
        ((Action)(() => DeploymentSafetyChecks.ValidatePublicSites(blank, "Production"))).Should().NotThrow();
    }

    [Fact]
    public void AdminCompanyDto_BathsStatsAreOmittedWhenNull_AndPresentForBaths()
    {
        var opts = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var without = System.Text.Json.JsonSerializer.Serialize(new AdminCompanyDto(Guid.NewGuid(), "n", "s", null, null, true, true, DateTime.UtcNow, 0, 0, "o", "e", null, "Free", null, true, "Baths", "u"), opts);
        var with = System.Text.Json.JsonSerializer.Serialize(new AdminCompanyDto(Guid.NewGuid(), "n", "s", null, null, true, true, DateTime.UtcNow, 0, 0, "o", "e", null, "Free", null, true, "Baths", "u", false, false, new AdminBathsStatsDto(2, 5)), opts);
        without.Should().NotContain("bathsStats");
        with.Should().Contain("\"bathsStats\":{\"resourcesCount\":2,\"ordersLast30Days\":5}");
    }
}
