using FluentAssertions;
using ServiceBooking.API.Services.Billing;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE19.md §384.1/§391.1 — the one place the account limit is computed:
/// tariff field + grandfathered bonus, nothing else.</summary>
public class AccountLimitFormulaTests
{
    [Fact]
    public void Compute_AddsBonusToPlanEmployees()
    {
        var (employees, _) = AccountLimitFormula.Compute(planMaxEmployees: 5, planMaxCompanies: 2, grandfatheredEmployeeBonus: 2);

        employees.Should().Be(7);
    }

    [Fact]
    public void Compute_NullPlanEmployees_StaysUnlimited_BonusNeverAdded()
    {
        var (employees, _) = AccountLimitFormula.Compute(planMaxEmployees: null, planMaxCompanies: 2, grandfatheredEmployeeBonus: 2);

        employees.Should().BeNull();
    }

    [Fact]
    public void Compute_NegativeBonus_TreatedAsZero()
    {
        var (employees, _) = AccountLimitFormula.Compute(planMaxEmployees: 5, planMaxCompanies: 2, grandfatheredEmployeeBonus: -3);

        employees.Should().Be(5);
    }

    [Fact]
    public void Compute_Companies_NeverGetsABonus_OnlyThePlanField()
    {
        var (_, companies) = AccountLimitFormula.Compute(planMaxEmployees: 5, planMaxCompanies: 2, grandfatheredEmployeeBonus: 10);

        companies.Should().Be(2);
    }

    [Fact]
    public void Compute_NullPlanCompanies_StaysUnlimited()
    {
        var (_, companies) = AccountLimitFormula.Compute(planMaxEmployees: 5, planMaxCompanies: null, grandfatheredEmployeeBonus: 10);

        companies.Should().BeNull();
    }
}
