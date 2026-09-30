using FluentAssertions;
using ServiceBooking.API.Services.Shops;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE25.md §506 — the three new permissions: reports and notes for staff, the summary for the owner only.</summary>
public class ShopAccessCycle25Tests
{
    [Theory]
    [InlineData(ShopRole.Owner, ShopPermission.ViewOrderReports, true)]
    [InlineData(ShopRole.Staff, ShopPermission.ViewOrderReports, true)]
    [InlineData(ShopRole.SuperAdmin, ShopPermission.ViewOrderReports, true)]
    [InlineData(ShopRole.Owner, ShopPermission.EditCustomerNotes, true)]
    [InlineData(ShopRole.Staff, ShopPermission.EditCustomerNotes, true)]
    [InlineData(ShopRole.SuperAdmin, ShopPermission.EditCustomerNotes, true)]
    [InlineData(ShopRole.Owner, ShopPermission.ViewSummary, true)]
    [InlineData(ShopRole.Staff, ShopPermission.ViewSummary, false)]
    [InlineData(ShopRole.SuperAdmin, ShopPermission.ViewSummary, true)]
    public void Allows(ShopRole role, ShopPermission permission, bool expected) => ShopAccess.Allows(role, permission).Should().Be(expected);

    [Fact]
    public void StaffStillCannotDoOwnerThings() =>
        new[] { ShopPermission.ManageShop, ShopPermission.ManageStaff, ShopPermission.EditSettings }
            .Should().OnlyContain(p => !ShopAccess.Allows(ShopRole.Staff, p));
}
