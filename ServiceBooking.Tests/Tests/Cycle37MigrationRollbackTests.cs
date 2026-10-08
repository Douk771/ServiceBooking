using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 37, «Вызов 3»: откат миграции <c>Cycle37Stays</c> (SQL в <c>Down()</c>, DEPLOY.md DO-37-03). Накат -> откат -> накат на отдельной пустой базе того же
/// тестового сервера — боевая схема и базы других тестов не затрагиваются.
/// </summary>
public class Cycle37MigrationRollbackTests(TestDatabaseFixture fixture) : Cycle37TestBase(fixture)
{
    private const string PreviousMigration = "20260930132324_Cycle28ShowcaseMarks";

    private static readonly string[] StaysTables =
    [
        "Houses", "HouseBlocks", "HouseBlockEvents", "HouseOccupancies", "HousePhotos", "HousePricePeriods", "HouseRegistryAttestations", "StayBookings",
        "StayBookingCharges", "StayBookingEvents", "StayPaymentProofs", "StayGuestPushSubscriptions", "StayGuestPushNotifications", "StaysSettings", "StaysSubscriptions",
    ];

    [Fact, TestCase("CY37-140")]
    public async Task Cycle37StaysMigration_UpDownUp_OnScratchDatabase_RemovesAndRestoresSchemaAndSeed()
    {
        var admin = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres", Pooling = false };
        var dbName = "sbtest_cy37mig_" + Guid.NewGuid().ToString("N")[..12];
        await using (var conn = new NpgsqlConnection(admin.ConnectionString))
        {
            await conn.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
            await create.ExecuteNonQueryAsync();
        }
        var scratch = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = dbName, Pooling = false }.ConnectionString;
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(scratch).Options;
            await using var db = new AppDbContext(options);
            var migrator = ((IInfrastructure<IServiceProvider>)db.Database).Instance.GetService(typeof(IMigrator)) as IMigrator;
            migrator.Should().NotBeNull();

            await db.Database.MigrateAsync();
            (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(m => m.EndsWith("_Cycle37Stays"));
            foreach (var t in StaysTables) (await TableExists(db, t)).Should().BeTrue($"после наката есть таблица {t}");
            var seededPlans = await Scalar(db, "SELECT COUNT(*) FROM \"SubscriptionPlanConfigs\" WHERE \"Line\" = 2");
            seededPlans.Should().BeGreaterThan(0, "накат засевает тарифы линейки «Дома»");
            var seededRules = await Scalar(db, "SELECT COUNT(*) FROM \"PlanOptionRules\" r JOIN \"SubscriptionPlanConfigs\" p ON p.\"Id\" = r.\"PlanConfigId\" WHERE p.\"Line\" = 2");

            // один и тот же телефон с пробным периодом «Записей» (Line 0) и «Домов» (Line 2): Down() обязан убрать вторую запись ДО восстановления
            // прежнего уникального индекса по ключу телефона, иначе откат упадёт на дубликате (DEPLOY.md DO-37-03)
            var hash = new string('a', 64);
            await db.Database.ExecuteSqlRawAsync($"""
                INSERT INTO "TrialPhoneRegistrations" ("Id", "Line", "PhoneKeyHash", "RegisteredAtUtc", "KeyId")
                VALUES (gen_random_uuid(), 0, '{hash}', now(), 'qa'), (gen_random_uuid(), 2, '{hash}', now(), 'qa');
                """);

            // ── откат ──
            await migrator!.MigrateAsync(PreviousMigration);
            (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(m => m.EndsWith("_Cycle37Stays"));
            foreach (var t in StaysTables) (await TableExists(db, t)).Should().BeFalse($"после отката таблицы {t} нет");
            foreach (var (table, column) in new[] { ("OutboundNotifications", "StayBookingId"), ("StaffPushNotifications", "StayBookingId"), ("StaffMaxMessages", "StayBookingId"),
                         ("CompanyMembers", "StaffPosition"), ("SubscriptionPlanConfigs", "MaxHouses"), ("TrialGrants", "Line"), ("TrialPhoneRegistrations", "Line") })
                (await ColumnExists(db, table, column)).Should().BeFalse($"колонка {table}.{column} убрана");
            (await IndexExists(db, "UX_TrialPhoneRegistrations_Key")).Should().BeTrue("прежний уникальный индекс по ключу телефона восстановлен");
            (await IndexExists(db, "UX_TrialGrants_OnePerAccount")).Should().BeTrue("прежний уникальный индекс одного триала на аккаунт восстановлен");

            // ── повторный накат ──
            await db.Database.MigrateAsync();
            foreach (var t in StaysTables) (await TableExists(db, t)).Should().BeTrue($"после повторного наката есть таблица {t}");
            (await Scalar(db, "SELECT COUNT(*) FROM \"SubscriptionPlanConfigs\" WHERE \"Line\" = 2")).Should().Be(seededPlans, "тарифы «Домов» засеяны заново в том же числе");
            (await Scalar(db, "SELECT COUNT(*) FROM \"PlanOptionRules\" r JOIN \"SubscriptionPlanConfigs\" p ON p.\"Id\" = r.\"PlanConfigId\" WHERE p.\"Line\" = 2")).Should().Be(seededRules);
            (await Scalar(db, $"SELECT COUNT(*) FROM \"TrialPhoneRegistrations\" WHERE \"PhoneKeyHash\" = '{hash}'")).Should().Be(1, "запись «Домов» удалена откатом, запись «Записей» уцелела");
            (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var conn = new NpgsqlConnection(admin.ConnectionString);
            await conn.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", conn);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task<long> Scalar(AppDbContext db, string sql)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open) await cmd.Connection.OpenAsync();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<bool> TableExists(AppDbContext db, string table) =>
        await Scalar(db, $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '{table}'") > 0;

    private static async Task<bool> ColumnExists(AppDbContext db, string table, string column) =>
        await Scalar(db, $"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{table}' AND column_name = '{column}'") > 0;

    private static async Task<bool> IndexExists(AppDbContext db, string index) =>
        await Scalar(db, $"SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'public' AND indexname = '{index}'") > 0;
}
