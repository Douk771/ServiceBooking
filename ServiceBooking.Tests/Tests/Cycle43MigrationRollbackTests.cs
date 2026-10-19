using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 43, ARCHITECTURE_CYCLE43.md §43.2.4–§43.2.5 и SPEC §5: миграция только добавляет (таблица журнала цены, три nullable-колонки журнала опций), данные не меняет,
/// накат → откат → накат на отдельной пустой базе проходит, прежние строки переживают оба направления (CY43-81).
/// </summary>
public class Cycle43MigrationRollbackTests(TestDatabaseFixture fixture) : Cycle43TestBase(fixture)
{
    private const string PreviousMigration = "20261009120658_Cycle42Baths";
    private static readonly string[] NewColumns = ["PricePerMonth", "Months", "Amount"];

    [Fact, TestCase("CY43-81")]
    public async Task Cycle43Migration_UpDownUp_OnScratchDatabase_AddsOnly_AndKeepsOldRows()
    {
        var admin = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres", Pooling = false };
        var dbName = "sbtest_cy43mig_" + Guid.NewGuid().ToString("N")[..12];
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

            // ── state of cycle 42 with rows of the old shape ──
            await migrator!.MigrateAsync(PreviousMigration);
            (await TableExists(db, "SubscriptionOptionPriceChangeLogs")).Should().BeFalse("no price journal before cycle 43");
            foreach (var c in NewColumns) (await ColumnExists(db, "ChannelOptionChangeLogs", c)).Should().BeFalse($"no column {c} before cycle 43");
            var oldLogId = Guid.NewGuid();
            await Exec(db, "INSERT INTO \"ChannelOptionChangeLogs\" (\"Id\", \"BillingAccountId\", \"OptionCode\", \"Source\", \"ChangedAtUtc\") " +
                           $"VALUES ('{oldLogId}', '{Guid.NewGuid()}', 'notifications.max', 0, now()) ", replicaFk: true);
            var dataFingerprint = await FingerprintAsync(db);
            dataFingerprint.Options.Should().BeGreaterThan(0, "the catalog options were seeded by the earlier migrations");

            // ── up ──
            await db.Database.MigrateAsync();
            (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(m => m.EndsWith("_Cycle43MessengerOptionPricing"));
            (await TableExists(db, "SubscriptionOptionPriceChangeLogs")).Should().BeTrue();
            foreach (var c in NewColumns) (await ColumnExists(db, "ChannelOptionChangeLogs", c)).Should().BeTrue($"column {c} added");
            (await Scalar(db, $"SELECT COUNT(*) FROM \"ChannelOptionChangeLogs\" WHERE \"Id\" = '{oldLogId}' AND \"PricePerMonth\" IS NULL AND \"Months\" IS NULL AND \"Amount\" IS NULL")).Should().Be(1,
                "the old row is intact, the new columns are NULL");
            (await FingerprintAsync(db)).Should().Be(dataFingerprint, "the migration changes no data: neither options, nor their prices, nor rows of accounts, nor tariff rules");
            (await Scalar(db, "SELECT COUNT(*) FROM \"SubscriptionOptionPriceChangeLogs\"")).Should().Be(0, "no journal row is invented for the existing prices");

            // the new table: a row of the journal; the option cannot be deleted while the journal refers to it (RESTRICT)
            var optionId = Guid.Parse((await ScalarText(db, "SELECT \"Id\"::text FROM \"SubscriptionOptions\" WHERE \"Code\" = 'notifications.max'"))!);
            await Exec(db, "INSERT INTO \"SubscriptionOptionPriceChangeLogs\" (\"Id\", \"OptionId\", \"OptionCode\", \"OldPricePerMonth\", \"NewPricePerMonth\", \"OldIsActive\", \"NewIsActive\", \"Source\", \"ChangedAtUtc\") " +
                           $"VALUES ('{Guid.NewGuid()}', '{optionId}', 'notifications.max', NULL, 490.00, true, true, 0, now())");
            (await Record.ExceptionAsync(() => Exec(db, $"DELETE FROM \"SubscriptionOptions\" WHERE \"Id\" = '{optionId}'"))).Should().NotBeNull("the journal blocks a physical delete of the option");
            (await Record.ExceptionAsync(() => Exec(db, "INSERT INTO \"SubscriptionOptionPriceChangeLogs\" (\"Id\", \"OptionId\", \"OptionCode\", \"OldIsActive\", \"NewIsActive\", \"Source\", \"ChangedAtUtc\") " +
                                                         $"VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', 'x', true, true, 0, now())"))).Should().NotBeNull("a journal row without an option is refused");
            await Exec(db, $"UPDATE \"ChannelOptionChangeLogs\" SET \"PricePerMonth\" = 490, \"Months\" = 3, \"Amount\" = 1470 WHERE \"Id\" = '{oldLogId}'");

            // ── down: the journal of prices and the amount columns go, everything else stays ──
            await Exec(db, "DELETE FROM \"SubscriptionOptionPriceChangeLogs\"");
            await migrator.MigrateAsync(PreviousMigration);
            (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(m => m.EndsWith("_Cycle43MessengerOptionPricing"));
            (await TableExists(db, "SubscriptionOptionPriceChangeLogs")).Should().BeFalse();
            foreach (var c in NewColumns) (await ColumnExists(db, "ChannelOptionChangeLogs", c)).Should().BeFalse($"column {c} removed");
            (await Scalar(db, $"SELECT COUNT(*) FROM \"ChannelOptionChangeLogs\" WHERE \"Id\" = '{oldLogId}'")).Should().Be(1, "the row of the older shape survives the rollback");
            (await FingerprintAsync(db)).Should().Be(dataFingerprint, "the rollback does not touch the options either");

            // ── up again ──
            await db.Database.MigrateAsync();
            (await TableExists(db, "SubscriptionOptionPriceChangeLogs")).Should().BeTrue();
            foreach (var c in NewColumns) (await ColumnExists(db, "ChannelOptionChangeLogs", c)).Should().BeTrue();
            (await Scalar(db, $"SELECT COUNT(*) FROM \"ChannelOptionChangeLogs\" WHERE \"Id\" = '{oldLogId}'")).Should().Be(1);
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

    private sealed record Fingerprint(long Options, string OptionsHash, long AccountRows, long Rules);

    /// <summary>Хэши данных, которые миграция трогать не должна: каталог опций с ценами, строки опций аккаунтов, правила тарифов.</summary>
    private static async Task<Fingerprint> FingerprintAsync(AppDbContext db) => new(
        await Scalar(db, "SELECT COUNT(*) FROM \"SubscriptionOptions\""),
        (await ScalarText(db, "SELECT md5(COALESCE(string_agg(\"Id\"::text || \"Code\" || COALESCE(\"PricePerMonth\"::text, '-') || \"IsActive\"::text, ',' ORDER BY \"Id\"), '')) FROM \"SubscriptionOptions\""))!,
        await Scalar(db, "SELECT COUNT(*) FROM \"AccountSubscriptionOptions\""),
        await Scalar(db, "SELECT COUNT(*) FROM \"PlanOptionRules\""));

    private static async Task Exec(AppDbContext db, string sql, bool replicaFk = false)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        if (replicaFk)
        {
            await using var off = new NpgsqlCommand("SET LOCAL session_replication_role = replica", connection, tx);
            await off.ExecuteNonQueryAsync();
        }
        await using var cmd = new NpgsqlCommand(sql, connection, tx);
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }

    private static async Task<long> Scalar(AppDbContext db, string sql)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open) await cmd.Connection.OpenAsync();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<string?> ScalarText(AppDbContext db, string sql)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open) await cmd.Connection.OpenAsync();
        cmd.CommandText = sql;
        return Convert.ToString(await cmd.ExecuteScalarAsync());
    }

    private static async Task<bool> TableExists(AppDbContext db, string table) =>
        await Scalar(db, $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '{table}'") > 0;

    private static async Task<bool> ColumnExists(AppDbContext db, string table, string column) =>
        await Scalar(db, $"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{table}' AND column_name = '{column}'") > 0;
}

/// <summary>
/// Цикл 43, ARCHITECTURE_CYCLE43.md §43.2.6 (инварианты 1, 2, 4): правила тарифов для мессенджеров не меняет ни один путь денег; журналы только пополняются; каждое
/// изменение цены даёт ровно одну строку журнала цены.
/// </summary>
public class Cycle43InvariantTests(TestDatabaseFixture fixture) : Cycle43TestBase(fixture)
{
    [Fact, TestCase("CY43-82")]
    public async Task NoMoneyPath_TouchesTheTariffRulesOfTheMessengers_AndJournalsOnlyGrow()
    {
        await SetCatalogAsync("WhatsApp", 300);
        await SetCatalogAsync("Max", 490);
        var admin = await AdminAsync();
        var a = await NewAccountAsync();
        async Task<string> RulesFingerprintAsync() => await DbAsync(async db => string.Join(";",
            (await db.PlanOptionRules.AsNoTracking().Where(r => r.Option.Code == WhatsAppCode || r.Option.Code == MaxCode).OrderBy(r => r.Id).ToListAsync())
            .Select(r => $"{r.Id}|{r.PlanConfigId}|{r.OptionId}|{r.Availability}|{r.IncludedQuantity}")));
        var rulesBefore = await RulesFingerprintAsync();
        rulesBefore.Should().NotBeEmpty("the test host seeded the old rules (the previous model), they must stay");

        var optionLogsBefore = await DbAsync(db => db.ChannelOptionChangeLogs.CountAsync());
        var priceLogsBefore = await DbAsync(db => db.SubscriptionOptionPriceChangeLogs.CountAsync());

        await SetCatalogAsync("Max", 500);
        await Expect(await ExtendAsync(admin, a.AccountId, "Max", 2, 500), HttpStatusCode.OK);
        await Expect(await ExtendAsync(admin, a.AccountId, "WhatsApp", 1, 300), HttpStatusCode.OK);
        await Expect(await DisableAsync(admin, a.AccountId, "Max", "PeriodEnd"), HttpStatusCode.OK);
        await Expect(await DisableAsync(admin, a.AccountId, "WhatsApp", "Now"), HttpStatusCode.OK);
        var channel = await RequestChannelAsync(a, "Max");
        await SetCatalogAsync("Max", 520);
        await Expect(await admin.PostAsJsonAsync($"/api/admin/notification-channels/{channel}/confirm-payment", new { months = 1 }), HttpStatusCode.OK);

        (await RulesFingerprintAsync()).Should().Be(rulesBefore, "no path of money writes, changes or deletes the tariff rules of the messengers");
        (await DbAsync(db => db.ChannelOptionChangeLogs.CountAsync())).Should().BeGreaterThan(optionLogsBefore);
        (await DbAsync(db => db.SubscriptionOptionPriceChangeLogs.CountAsync())).Should().Be(priceLogsBefore + 2, "two price changes (500 and 520), one journal row each");
    }
}
