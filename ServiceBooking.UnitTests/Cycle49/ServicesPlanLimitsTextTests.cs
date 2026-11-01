using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using Xunit;

namespace ServiceBooking.UnitTests.Cycle49;

/// <summary>CY49-B8 — the one-line limits of a "Записи" tariff.</summary>
public class ServicesPlanLimitsTextTests
{
    [Theory]
    [InlineData(1, "до 1 точки · до 1 сотрудника")]
    [InlineData(2, "до 2 точек · до 2 сотрудников")]
    [InlineData(5, "до 5 точек · до 5 сотрудников")]
    [InlineData(11, "до 11 точек · до 11 сотрудников")]
    [InlineData(21, "до 21 точки · до 21 сотрудника")]
    public void Plural_ForCompaniesAndEmployees(int n, string expected) =>
        BillingTexts.ServicesPlanLimitsText(n, n).Should().Be(expected);

    [Fact]
    public void Null_MeansNoLimit() =>
        BillingTexts.ServicesPlanLimitsText(null, null).Should().Be("точки без ограничения · сотрудники без ограничения");

    [Fact]
    public void Mixed_KeepsSeparator() =>
        BillingTexts.ServicesPlanLimitsText(3, null).Should().Be("до 3 точек · сотрудники без ограничения");
}
