using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle43;

/// <summary>API_CONTRACT_CYCLE43.md §43.33 — the coverage text of a tariff when no catalog option depends on the tariff.</summary>
public class AdminPlanCoverageTextTests
{
    private static SubscriptionPlanConfig Plan() => new() { Id = Guid.NewGuid(), Name = "Стандарт", PricePerMonth = 990, PhotoRetention = PhotoRetention.SixMonths };

    [Fact]
    public void NoTariffDependentOptions_SaysSo()
    {
        var dto = AdminPlansController.MapAdminPlanDto(Plan(), 0, [], totalOptionsInCatalog: 0);

        dto.OptionCoverage!.Total.Should().Be(0);
        dto.OptionCoverage.Text.Should().Be("Опций, зависящих от тарифа, нет");
    }

    [Fact]
    public void WithOptions_KeepsTheOldText()
    {
        var dto = AdminPlansController.MapAdminPlanDto(Plan(), 0, [], totalOptionsInCatalog: 4);

        dto.OptionCoverage!.Text.Should().Be("В тариф включено 0 из 4 опций каталога");
    }
}
