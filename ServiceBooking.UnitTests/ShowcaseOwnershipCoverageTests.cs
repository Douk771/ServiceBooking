using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §572.3 — a table added in a future cycle must not silently escape the showcase eraser. Reads the EF model only (no connection, no database):
/// every table that references a company, a user, a billing account or a booking is either in <see cref="ShowcaseOwnership.DeleteSteps"/> or explicitly declared
/// "never written for a showcase". A new table without a decision fails here.
/// </summary>
public class ShowcaseOwnershipCoverageTests
{
    private static AppDbContext NewModelOnlyContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=model-only").Options);

    private static readonly Type[] Roots = [typeof(Company), typeof(AppUser), typeof(BillingAccount), typeof(Booking)];

    [Fact]
    public void EveryTableReferencingARoot_IsErasedOrDeclaredNeverWritten()
    {
        using var db = NewModelOnlyContext();
        var referencing = db.Model.GetEntityTypes()
            .Where(e => e.GetForeignKeys().Any(fk => Roots.Contains(fk.PrincipalEntityType.ClrType)))
            .Select(e => e.GetTableName()!)
            .Distinct()
            .ToList();

        var known = ShowcaseOwnership.Tables.Concat(ShowcaseOwnership.NeverWritten.Keys).ToHashSet(StringComparer.Ordinal);

        referencing.Where(t => !known.Contains(t)).Should().BeEmpty(
            "a new table that references Company/AppUser/BillingAccount/Booking needs a decision in ShowcaseOwnership: an erase step, or NeverWritten with the reason");
    }

    [Fact]
    public void EveryStepAndDeclaration_NamesARealTable()
    {
        using var db = NewModelOnlyContext();
        var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).ToHashSet();

        ShowcaseOwnership.Tables.Where(t => !tables.Contains(t)).Should().BeEmpty();
        ShowcaseOwnership.NeverWritten.Keys.Where(t => !tables.Contains(t)).Should().BeEmpty();
        ShowcaseOwnership.NeverWritten.Keys.Should().NotIntersectWith(ShowcaseOwnership.Tables, "a table is either erased or never written, not both");
    }

    [Fact]
    public void EveryStepQuotesOnlyColumnsThatExist()
    {
        using var db = NewModelOnlyContext();
        var columns = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties().Select(p => p.GetColumnName()))
            .ToHashSet();
        var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).ToHashSet();

        foreach (var step in ShowcaseOwnership.DeleteSteps)
        {
            var quoted = System.Text.RegularExpressions.Regex.Matches(step.Where, "\"([A-Za-z]+)\"").Select(m => m.Groups[1].Value).Distinct();
            foreach (var name in quoted)
                (columns.Contains(name) || tables.Contains(name)).Should().BeTrue($"step '{step.Report}' quotes \"{name}\", which is neither a column nor a table of the model");
        }
    }

    [Fact]
    public void StepsRunChildrenBeforeParents_ForTheRestrictForeignKeys()
    {
        var order = ShowcaseOwnership.DeleteSteps.Select(s => s.Table).ToList();
        int At(string table) => order.IndexOf(table);

        At("Bookings").Should().BeLessThan(At("Companies"));
        At("Reviews").Should().BeLessThan(At("Bookings"));
        At("BookingEvents").Should().BeLessThan(At("Bookings"));
        At("BookingServices").Should().BeLessThan(At("Bookings"));
        At("Services").Should().BeGreaterThan(At("MasterServices"));
        At("Bookings").Should().BeLessThan(At("AspNetUsers"), "Bookings.MasterId is Restrict");
        At("Companies").Should().BeLessThan(At("AspNetUsers"), "Companies.OwnerUserId is Restrict");
        At("Companies").Should().BeLessThan(At("BillingAccounts"), "Companies.BillingAccountId is Restrict");
        At("ScheduleBreaks").Should().BeLessThan(At("WorkingHours"));
        At("CompanyMembers").Should().BeLessThan(At("Companies"));
        At("AspNetUserRoles").Should().BeLessThan(At("AspNetUsers"));
    }

    [Fact]
    public void StepsOfTheShops_RunChildrenBeforeParents_ARCHITECTURE_CYCLE35_9_7()
    {
        var order = ShowcaseOwnership.DeleteSteps.Select(s => s.Table).ToList();
        int At(string table) => order.IndexOf(table);

        foreach (var table in new[]
                 {
                     "OrderEvents", "OrderItems", "OrderPushSubscriptions", "CustomerOrderPushNotifications", "StaffMaxMessages", "Orders", "OrderDailyCounters",
                     "ShopCustomerNotes", "ShopDailyMenuItems", "ShopDailyMenus", "ShopSpecialDays", "Products", "ProductCategories", "ShopSettings",
                 })
        {
            At(table).Should().BeGreaterThanOrEqualTo(0, $"{table} must be erased with the showcase (the demo profile writes shops)");
            At(table).Should().BeLessThan(At("Companies"), $"{table}.CompanyId is Restrict");
        }

        At("OrderItems").Should().BeLessThan(At("Products"), "OrderItems.ProductId is Restrict");
        At("ShopDailyMenuItems").Should().BeLessThan(At("Products"), "ShopDailyMenuItems.ProductId is Restrict");
        At("ShopDailyMenuItems").Should().BeLessThan(At("ShopDailyMenus"));
        At("Products").Should().BeLessThan(At("ProductCategories"), "Products.CategoryId is Restrict");
        At("OrderItems").Should().BeLessThan(At("Orders"));
        At("OrderEvents").Should().BeLessThan(At("Orders"));
    }

    [Fact]
    public void OnlyTheNotificationChannelsAndTheStaysTablesAreDeclaredNeverWritten_TheShopTablesAreWrittenByTheDemoProfile()
    {
        // Cycle 37 (ARCHITECTURE_CYCLE37.md §37.3.2): the 15 tables of the "Дома" vertical are declared never written (no showcase for it).
        string[] stays = ["StaysSettings", "Houses", "HousePhotos", "HousePricePeriods", "HouseRegistryAttestations", "HouseBlocks", "HouseBlockEvents",
            "HouseOccupancies", "StayBookings", "StayBookingCharges", "StayBookingEvents", "StayPaymentProofs",
            "StayGuestPushSubscriptions", "StayGuestPushNotifications", "StaysSubscriptions"];
        // Cycle 39 (ARCHITECTURE_CYCLE39.md §39.2.6): the 11 tables of the time-slot services are declared never written too.
        string[] services = ["StayServices", "StayServicePhotos", "StayServiceWeeklyWindows", "StayServiceDateOverrides", "StayServiceScheduleEvents",
            "StayServicePriceRules", "StayServiceItems", "StayServiceSessions", "StayServiceOrders", "StayServiceOrderEvents", "StaysReminderTemplateChanges"];
        // Cycle 42 (ARCHITECTURE_CYCLE42.md §42.2.6): the 2 tables of the «Бани» vertical.
        string[] baths = ["BathsSubscriptions", "StayServiceItemConfirmations"];
        // Cycle 40 (§40.2.3): ChannelOptionChangeLogs — journal of channel options, showcase accounts own no numbers.
        ShowcaseOwnership.NeverWritten.Keys.Where(k => k != "NotificationChannels" && k != "ChannelOptionChangeLogs").Should().BeEquivalentTo(stays.Concat(services).Concat(baths));
        ShowcaseOwnership.NeverWritten.Keys.Should().Contain("NotificationChannels").And.Contain("ChannelOptionChangeLogs").And.HaveCount(30);
    }

    [Fact]
    public void ErasingNeverTouchesAnUnmarkedRow_EveryStepIsScopedByAMark()
    {
        foreach (var step in ShowcaseOwnership.DeleteSteps)
            step.Where.Should().Contain("IsShowcase", $"step '{step.Report}' must be scoped through a showcase mark");
    }
}
