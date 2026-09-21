using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// Covers AdminController.MapAdminPlanDto/SplitHighlights — the pure projection from
/// SubscriptionPlanConfig onto openapi-cycle5.yaml's AdminPlanDto shape (contracts/openapi-cycle5.yaml,
/// cycle-07 contract-drift fixes). No HTTP, no DB: exercises the mapping functions directly.
/// </summary>
public class AdminPlanDtoMappingTests
{
    private static SubscriptionPlanConfig NewPlan() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Standard",
        PricePerMonth = 990,
        PhotoRetention = PhotoRetention.SixMonths,
    };

    [Fact]
    public void MapAdminPlanDto_AlwaysReportsCurrencyAsRub()
    {
        var dto = AdminController.MapAdminPlanDto(NewPlan(), subscribedAccounts: 0);

        dto.Currency.Should().Be("RUB");
    }

    [Fact]
    public void MapAdminPlanDto_NullHighlights_MapsToEmptyArrayNotNull()
    {
        var plan = NewPlan();
        plan.Highlights = null;

        var dto = AdminController.MapAdminPlanDto(plan, subscribedAccounts: 0);

        dto.Highlights.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void MapAdminPlanDto_OptionsIsAlwaysEmpty_UntilTheOptionCatalogExists()
    {
        // Documents a known, deliberate gap (see the comment on MapAdminPlanDto): the schema's
        // `options` field depends on the BillingAccount/option catalog, which isn't built yet.
        var dto = AdminController.MapAdminPlanDto(NewPlan(), subscribedAccounts: 3);

        dto.Options.Should().BeEmpty();
        dto.SubscribedAccounts.Should().Be(3);
    }

    [Fact]
    public void MapAdminPlanDto_DoesNotExposeAllowNotificationChannelOrCreatedAt()
    {
        // additionalProperties: false in the schema — the projection type itself must not declare
        // these entity-only fields, or they'd be serialized and fail contract validation.
        var dtoType = typeof(ServiceBooking.API.Controllers.AdminPlanDto);

        dtoType.GetProperty("AllowNotificationChannel").Should().BeNull();
        dtoType.GetProperty("CreatedAt").Should().BeNull();
    }

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("", new string[0])]
    [InlineData("  \n \n", new string[0])]
    [InlineData("Only line", new[] { "Only line" })]
    [InlineData("First\nSecond\n\nThird", new[] { "First", "Second", "Third" })]
    [InlineData(" leading and trailing spaces \n second ", new[] { "leading and trailing spaces", "second" })]
    public void SplitHighlights_SplitsTrimsAndDropsBlankLines(string? raw, string[] expected) =>
        AdminController.SplitHighlights(raw).Should().Equal(expected);

    [Fact]
    public void SplitHighlights_CapsAtTenLines_MatchingTheSchemasMaxItems()
    {
        var raw = string.Join('\n', Enumerable.Range(1, 15).Select(i => $"Line {i}"));

        var result = AdminController.SplitHighlights(raw);

        result.Should().HaveCount(10);
        result.Should().Equal(Enumerable.Range(1, 10).Select(i => $"Line {i}"));
    }
}
