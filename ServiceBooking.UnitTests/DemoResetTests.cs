using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §580 — what the demo reset wipes (read off the EF model, no connection) and when the nightly reset is due.
/// </summary>
public class DemoResetTests
{
    private static AppDbContext ModelOnlyContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=model-only").Options);

    // ── the wipe list ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoKeptTable_HasAForeignKeyToAWipedOne()
    {
        // Otherwise the TRUNCATE would fail on that key (or, with CASCADE, silently wipe the catalog). The fix for a new kept table is on the allow-list, not here.
        using var db = ModelOnlyContext();

        DemoResetTables.KeptTablesReferencingWiped(db.Model).Should().BeEmpty();
    }

    [Fact]
    public void EveryKeptTable_ExistsInTheModel_ExceptTheMigrationHistory()
    {
        using var db = ModelOnlyContext();
        var tables = db.Model.GetEntityTypes().Select(e => e.GetTableName()).ToHashSet();

        DemoResetTables.Kept.Where(t => t != "__EFMigrationsHistory" && !tables.Contains(t)).Should().BeEmpty(
            "a renamed or removed table left on the allow-list would hide a mistake");
    }

    [Fact]
    public void TheWipeListIsEveryTableOfTheModel_ExceptTheAllowList()
    {
        using var db = ModelOnlyContext();
        var all = db.Model.GetEntityTypes().Select(e => e.GetTableName()).OfType<string>().Distinct().ToList();

        var wiped = DemoResetTables.TablesToWipe(db.Model);

        wiped.Should().HaveCount(all.Count - all.Count(DemoResetTables.Kept.Contains));
        foreach (var kept in new[] { "Cities", "AspNetRoles", "SubscriptionPlanConfigs", "SubscriptionOptions", "PlanOptionRules", "PlatformSettings", "ScheduledTaskStates" })
            wiped.Should().NotContain(t => t.Contains($"\"{kept}\""));
        // What must go: the users, the companies, bookings, visitors' registrations, the consent journal and the outbound queues.
        foreach (var gone in new[] { "AspNetUsers", "AspNetUserRoles", "Companies", "Bookings", "BookingEvents", "Reviews", "ClientNotes", "ConsentRecords",
                     "OutboundNotifications", "StaffPushNotifications", "PhoneVerificationSessions", "VerifiedPhones", "AccountSubscriptions", "BillingAccounts" })
            wiped.Should().Contain($"\"{gone}\"");
    }

    [Fact]
    public void TheWipeListIsStable_SameModelSameOrder()
    {
        using var db = ModelOnlyContext();

        DemoResetTables.TablesToWipe(db.Model).Should().Equal(DemoResetTables.TablesToWipe(db.Model));
    }

    [Fact]
    public void TruncateSql_IsOneStatement_WithRestartIdentity_AndNoCascade()
    {
        var sql = DemoResetTables.TruncateSql(["\"Bookings\"", "\"Companies\""]);

        sql.Should().Be("TRUNCATE TABLE \"Bookings\", \"Companies\" RESTART IDENTITY");
        sql.Should().NotContain("CASCADE");
    }

    [Fact]
    public void TruncateSql_RefusesAnEmptyList() =>
        ((Action)(() => DemoResetTables.TruncateSql([]))).Should().Throw<ArgumentException>();

    // ── the nightly schedule ────────────────────────────────────────────────────────────────────────

    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
    private static readonly TimeOnly Four = new(4, 0);

    private static DateTime MoscowToUtc(int day, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Unspecified), Moscow);

    [Fact]
    public void NeverReset_IsNotDue_TheFirstResetIsTheOperators() =>
        DemoResetSchedule.IsDue(MoscowToUtc(2, 12, 0), lastResetUtc: null, Four, Moscow).Should().BeFalse();

    [Fact]
    public void BeforeTonightsSlot_TheLastResetOfYesterdayIsCurrent() =>
        DemoResetSchedule.IsDue(MoscowToUtc(2, 3, 59), MoscowToUtc(1, 4, 1), Four, Moscow).Should().BeFalse();

    [Fact]
    public void AfterTonightsSlot_AResetFromYesterdayIsDue() =>
        DemoResetSchedule.IsDue(MoscowToUtc(2, 4, 0), MoscowToUtc(1, 4, 1), Four, Moscow).Should().BeTrue();

    [Fact]
    public void AfterAResetAtTheSlot_TheNextTickFindsNothingDue() =>
        DemoResetSchedule.IsDue(MoscowToUtc(2, 4, 10), MoscowToUtc(2, 4, 0), Four, Moscow).Should().BeFalse();

    [Fact]
    public void AMissedSlot_IsMadeUpAtTheNextTick_EvenHoursLate() =>
        DemoResetSchedule.IsDue(MoscowToUtc(2, 15, 30), MoscowToUtc(1, 4, 1), Four, Moscow).Should().BeTrue();

    [Fact]
    public void AManualResetAfterTheSlot_CountsForTonight() =>
        DemoResetSchedule.IsDue(MoscowToUtc(2, 18, 0), MoscowToUtc(2, 9, 30), Four, Moscow).Should().BeFalse();

    [Fact]
    public void LatestSlot_BeforeTheTime_IsYesterdays_AfterIt_IsTodays()
    {
        DemoResetSchedule.LatestSlotUtc(MoscowToUtc(2, 3, 0), Four, Moscow).Should().Be(MoscowToUtc(1, 4, 0));
        DemoResetSchedule.LatestSlotUtc(MoscowToUtc(2, 5, 0), Four, Moscow).Should().Be(MoscowToUtc(2, 4, 0));
    }

    [Fact]
    public void Options_ParseTheResetTime()
    {
        new DemoModeOptions { ResetLocalTime = "04:00" }.TryGetResetLocalTime(out var t).Should().BeTrue();
        t.Should().Be(Four);
        new DemoModeOptions { ResetLocalTime = "4 утра" }.TryGetResetLocalTime(out _).Should().BeFalse();
    }
}
