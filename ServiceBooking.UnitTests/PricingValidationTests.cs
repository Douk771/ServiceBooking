using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Billing;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Cycle-07 backend report, item 3: POST/PUT /api/admin/options and /api/admin/plans used to let a
/// denormalized pricePerMonth reach SaveChangesAsync and blow up as an unhandled 500 (SubscriptionOption
/// price overflowing its `numeric(10,2)` column; plan price overflowing the shared decimal arithmetic it's
/// later summed into) instead of a clean 400. Exercises the validation functions directly, no HTTP/DB.
/// </summary>
public class PricingValidationTests
{
    private static Billing_AdminOptionInput NewOptionInput(decimal? price = 100, int? maxQuantity = 10) => new(
        Code: "test.option", Name: "Test option", Description: null, Kind: "Toggle", CapabilityKey: null,
        PricePerMonth: price, UnitName: null, MaxQuantity: maxQuantity);

    [Fact]
    public void ValidateOptionInput_PriceAtTheColumnCeiling_IsAccepted() =>
        AdminBillingController.ValidateOptionInput(NewOptionInput(AdminBillingController.MaxOptionPricePerMonth), existingCode: null)
            .Should().BeNull();

    [Fact]
    public void ValidateOptionInput_PriceAboveTheColumnCeiling_Is400NotAnUnhandledOverflow()
    {
        var result = AdminBillingController.ValidateOptionInput(
            NewOptionInput(AdminBillingController.MaxOptionPricePerMonth + 0.01m), existingCode: null);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void ValidateOptionInput_MaxQuantityAboveItsCeiling_Is400()
    {
        var result = AdminBillingController.ValidateOptionInput(
            NewOptionInput(maxQuantity: AdminBillingController.MaxOptionMaxQuantity + 1), existingCode: null);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void ValidateOptionInput_NullPrice_IsStillAccepted_NotForSaleIsAValidState() =>
        AdminBillingController.ValidateOptionInput(NewOptionInput(price: null), existingCode: null).Should().BeNull();

    private static AdminPlanInput NewPlanInput(decimal price = 990) => new(
        Name: "Standard", Description: null, Highlights: null, PricePerMonth: price,
        MaxEmployees: 5, MaxCompanies: 1);

    [Fact]
    public void ValidatePlanInput_PriceAtTheCeiling_IsAccepted() =>
        AdminController.ValidatePlanInput(NewPlanInput(AdminController.MaxPlanPricePerMonth)).Should().BeNull();

    [Fact]
    public void ValidatePlanInput_PriceAboveTheCeiling_Is400() =>
        AdminController.ValidatePlanInput(NewPlanInput(AdminController.MaxPlanPricePerMonth + 0.01m))
            .Should().BeOfType<BadRequestObjectResult>();

    [Fact]
    public void ValidatePlanInput_NegativeMaxEmployees_Is400() =>
        AdminController.ValidatePlanInput(NewPlanInput() with { MaxEmployees = -1 })
            .Should().BeOfType<BadRequestObjectResult>();

    [Fact]
    public void ValidatePlanInput_NegativeMaxCompanies_Is400() =>
        AdminController.ValidatePlanInput(NewPlanInput() with { MaxCompanies = -1 })
            .Should().BeOfType<BadRequestObjectResult>();
}
