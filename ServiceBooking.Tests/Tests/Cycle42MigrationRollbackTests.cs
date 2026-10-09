using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 42: накат → откат → накат миграции <c>Cycle42Baths</c> поверх <c>Cycle40ChannelOptions</c> на отдельной пустой базе (по образцу CY39-140). Накат добавляет
/// 2 таблицы, 5 столбцов, 3 CHECK и 4 тарифа линейки «Бани» с правилом whatsapp; откат убирает всё это и ничего не трогает в данных цикла 40 и тарифах других линеек.
/// </summary>
public class Cycle42MigrationRollbackTests(TestDatabaseFixture fixture) : Cycle42TestBase(fixture)
{
    private const string PreviousMigration = "20261009100000_Cycle40ChannelOptions";
    private static readonly string[] BathsTables = ["BathsSubscriptions", "StayServiceItemConfirmations"];

    private static readonly (string Table, string Column)[] NewColumns =
    [
        ("SubscriptionPlanConfigs", "MaxResources"), ("StaysSettings", "ServiceReminderHours"), ("StayServices", "Capacity"),
        ("StayServiceOrders", "GuestsCount"), ("StayServiceOrders", "SessionReminderAtUtc"),
    ];

    private static readonly string[] BathsPlanIds =
    [
        "0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b01", "0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b02", "0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b03", "0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b04",
    ];

    [Fact, TestCase("CY42-140")]
    public async Task Cycle42BathsMigration_UpDownUp_OnScratchDatabase_KeepsCycle40Rows_AndSeedsFourPlans()
    {
        var admin = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres", Pooling = false };
        var dbName = "sbtest_cy42mig_" + Guid.NewGuid().ToString("N")[..12];
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

            // ── состояние цикла 40 с данными ──
            await migrator!.MigrateAsync(PreviousMigration);
            foreach (var t in BathsTables) (await TableExists(db, t)).Should().BeFalse($"до цикла 42 таблицы {t} нет");
            foreach (var (table, column) in NewColumns) (await ColumnExists(db, table, column)).Should().BeFalse($"до цикла 42 столбца {table}.{column} нет");
            var company = Guid.NewGuid();
            var service = Guid.NewGuid();
            var order = Guid.NewGuid();
            await InsertMinimalRowAsync(db, "StaysSettings", ("CompanyId", company), ("MinNights", 1), ("MaxNights", 30));
            await InsertMinimalRowAsync(db, "StayServices", ("Id", service), ("CompanyId", company), ("Slug", "banya"), ("Name", "Баня"), ("MinHours", 2), ("MaxHours", 6), ("StepMinutes", 60),
                ("BufferMinutes", 30), ("MinLeadMinutes", 0), ("CancellationBoundaryHours", 12));
            await InsertMinimalRowAsync(db, "StayServiceOrders", ("Id", order), ("CompanyId", company), ("ServiceId", service));
            var foreignPlans = await Scalar(db, "SELECT COUNT(*) FROM \"SubscriptionPlanConfigs\"");
            (await Scalar(db, "SELECT COUNT(*) FROM \"SubscriptionPlanConfigs\" WHERE \"Line\" = 3")).Should().Be(0, "тарифов «Бань» до цикла 42 нет");

            // ── накат ──
            await db.Database.MigrateAsync();
            (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(m => m.EndsWith("_Cycle42Baths"));
            foreach (var t in BathsTables) (await TableExists(db, t)).Should().BeTrue($"после наката есть таблица {t}");
            foreach (var (table, column) in NewColumns) (await ColumnExists(db, table, column)).Should().BeTrue($"после наката есть столбец {table}.{column}");
            (await Scalar(db, $"SELECT COUNT(*) FROM \"StayServices\" WHERE \"Id\" = '{service}' AND \"Capacity\" IS NULL")).Should().Be(1, "вместимость существующих услуг — NULL");
            (await Scalar(db, $"SELECT COUNT(*) FROM \"StayServiceOrders\" WHERE \"Id\" = '{order}' AND \"GuestsCount\" IS NULL AND \"SessionReminderAtUtc\" IS NULL")).Should().Be(1);
            (await Scalar(db, $"SELECT COUNT(*) FROM \"StaysSettings\" WHERE \"CompanyId\" = '{company}' AND \"ServiceReminderHours\" IS NULL")).Should().Be(1);
            await AssertPlansSeededAsync(db);
            (await Scalar(db, $"SELECT COUNT(*) FROM \"SubscriptionPlanConfigs\" WHERE \"Line\" <> 3")).Should().Be(foreignPlans, "тарифы других линеек не тронуты");

            // CHECK-ограничения отбивают значения вне диапазона (в обход API)
            (await Record.ExceptionAsync(() => Exec(db, $"UPDATE \"StayServices\" SET \"Capacity\" = 31 WHERE \"Id\" = '{service}'"))).Should().NotBeNull("вместимость 1…30");
            (await Record.ExceptionAsync(() => Exec(db, $"UPDATE \"StayServices\" SET \"Capacity\" = 0 WHERE \"Id\" = '{service}'"))).Should().NotBeNull();
            (await Record.ExceptionAsync(() => Exec(db, $"UPDATE \"StayServices\" SET \"Capacity\" = 30 WHERE \"Id\" = '{service}'"))).Should().BeNull();
            (await Record.ExceptionAsync(() => Exec(db, $"UPDATE \"StayServiceOrders\" SET \"GuestsCount\" = 31 WHERE \"Id\" = '{order}'"))).Should().NotBeNull("гостей 1…30");
            (await Record.ExceptionAsync(() => Exec(db, $"UPDATE \"StaysSettings\" SET \"ServiceReminderHours\" = 25 WHERE \"CompanyId\" = '{company}'"))).Should().NotBeNull("напоминание 1…24 часа");

            // журнал подтверждений: ресурс удалён — строка остаётся с ServiceId = NULL (SetNull), снимок имени цел
            var bare = Guid.NewGuid();
            var journal = Guid.NewGuid();
            await InsertMinimalRowAsync(db, "StayServices", ("Id", bare), ("CompanyId", company), ("Slug", "chan"), ("Name", "Чан"), ("MinHours", 1), ("MaxHours", 3), ("StepMinutes", 60),
                ("BufferMinutes", 0), ("MinLeadMinutes", 0), ("CancellationBoundaryHours", 12));
            await InsertMinimalRowAsync(db, "StayServiceItemConfirmations", ("Id", journal), ("CompanyId", company), ("ServiceId", bare), ("ServiceNameSnapshot", "Чан"),
                ("ItemNameSnapshot", "Пиво"), ("MarkersHit", "пив"), ("NoticeKey", "k"), ("NoticeVersion", "v"), ("ConfirmedByUserId", "u"), ("ConfirmedByNameSnapshot", "Иван"));
            await Exec(db, $"DELETE FROM \"StayServices\" WHERE \"Id\" = '{bare}'");
            (await Scalar(db, $"SELECT COUNT(*) FROM \"StayServiceItemConfirmations\" WHERE \"Id\" = '{journal}' AND \"ServiceId\" IS NULL AND \"ServiceNameSnapshot\" = 'Чан'")).Should().Be(1,
                "удаление ресурса не удаляет журнал (SetNull)");

            // ── откат ──
            await Exec(db, "DELETE FROM \"StayServiceItemConfirmations\"");
            await migrator.MigrateAsync(PreviousMigration);
            (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(m => m.EndsWith("_Cycle42Baths"));
            foreach (var t in BathsTables) (await TableExists(db, t)).Should().BeFalse($"после отката таблицы {t} нет");
            foreach (var (table, column) in NewColumns) (await ColumnExists(db, table, column)).Should().BeFalse($"после отката столбца {table}.{column} нет");
            (await Scalar(db, "SELECT COUNT(*) FROM \"SubscriptionPlanConfigs\" WHERE \"Line\" = 3 OR \"Id\"::text LIKE '0c42ba70-%'")).Should().Be(0, "тарифы «Бань» убраны");
            (await Scalar(db, "SELECT COUNT(*) FROM \"SubscriptionPlanConfigs\"")).Should().Be(foreignPlans, "число прочих тарифов прежнее");
            (await Scalar(db, $"SELECT COUNT(*) FROM \"StayServices\" WHERE \"Id\" = '{service}'")).Should().Be(1, "услуга цикла 40 пережила откат");
            (await Scalar(db, $"SELECT COUNT(*) FROM \"StayServiceOrders\" WHERE \"Id\" = '{order}'")).Should().Be(1, "заказ цикла 40 пережил откат");
            (await Scalar(db, $"SELECT COUNT(*) FROM \"StaysSettings\" WHERE \"CompanyId\" = '{company}'")).Should().Be(1);

            // ── повторный накат ──
            await db.Database.MigrateAsync();
            foreach (var t in BathsTables) (await TableExists(db, t)).Should().BeTrue($"после повторного наката есть таблица {t}");
            foreach (var (table, column) in NewColumns) (await ColumnExists(db, table, column)).Should().BeTrue();
            await AssertPlansSeededAsync(db);
            (await Scalar(db, $"SELECT COUNT(*) FROM \"StayServices\" WHERE \"Id\" = '{service}'")).Should().Be(1);
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

    /// <summary>Четыре тарифа линейки 3 с фиксированными Id: лимиты бань 1 / 3 / без ограничения / пробный; правило whatsapp у платных и пробного; триал единственный системный у линейки.</summary>
    private static async Task AssertPlansSeededAsync(AppDbContext db)
    {
        (await Scalar(db, "SELECT COUNT(*) FROM \"SubscriptionPlanConfigs\" WHERE \"Line\" = 3")).Should().Be(4);
        foreach (var id in BathsPlanIds) (await Scalar(db, $"SELECT COUNT(*) FROM \"SubscriptionPlanConfigs\" WHERE \"Id\" = '{id}' AND \"Line\" = 3")).Should().Be(1, $"тариф {id}");
        (await Scalar(db, $"SELECT \"MaxResources\" FROM \"SubscriptionPlanConfigs\" WHERE \"Id\" = '{BathsPlanIds[0]}'")).Should().Be(1);
        (await Scalar(db, $"SELECT \"MaxResources\" FROM \"SubscriptionPlanConfigs\" WHERE \"Id\" = '{BathsPlanIds[1]}'")).Should().Be(3);
        (await Scalar(db, $"SELECT COUNT(*) FROM \"SubscriptionPlanConfigs\" WHERE \"Id\" IN ('{BathsPlanIds[2]}', '{BathsPlanIds[3]}') AND \"MaxResources\" IS NULL")).Should().Be(2, "«Без ограничения» и пробный — без лимита");
        (await Scalar(db, "SELECT COUNT(*) FROM \"SubscriptionPlanConfigs\" WHERE \"Line\" = 3 AND \"PricePerMonth\" = 0")).Should().Be(1, "пробный бесплатный");
        (await Scalar(db, "SELECT COUNT(*) FROM \"SubscriptionPlanConfigs\" WHERE \"Line\" = 3 AND \"IsSystemTrial\"")).Should().Be(0, "флаг системного триала уникален по всем линейкам и занят «Записью»");
        (await Scalar(db, "SELECT COUNT(*) FROM \"PlanOptionRules\" r JOIN \"SubscriptionPlanConfigs\" p ON p.\"Id\" = r.\"PlanConfigId\" JOIN \"SubscriptionOptions\" o ON o.\"Id\" = r.\"OptionId\" " +
                              "WHERE p.\"Line\" = 3 AND o.\"Code\" = 'notifications.whatsapp'")).Should().Be(4, "правило whatsapp на каждом тарифе линейки");
    }

    private static async Task Exec(AppDbContext db, string sql)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        if (cmd.Connection!.State != System.Data.ConnectionState.Open) await cmd.Connection.OpenAsync();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Строка без внешних ключей: NOT NULL колонки без значения по умолчанию заполняются нейтральным значением по типу, заданные — как переданы.</summary>
    private static async Task InsertMinimalRowAsync(AppDbContext db, string table, params (string Column, object? Value)[] given)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        var columns = new List<(string Name, string Type, bool Required)>();
        await using (var q = new NpgsqlCommand(
            "SELECT column_name, data_type, (is_nullable = 'NO' AND column_default IS NULL AND is_generated = 'NEVER' AND is_identity = 'NO') FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name = @t", connection))
        {
            q.Parameters.AddWithValue("t", table);
            await using var r = await q.ExecuteReaderAsync();
            while (await r.ReadAsync()) columns.Add((r.GetString(0), r.GetString(1), r.GetBoolean(2)));
        }
        var names = new List<string>();
        var values = new List<object?>();
        var types = new Dictionary<string, string>();
        foreach (var (name, type, required) in columns)
        {
            var hit = given.FirstOrDefault(g => g.Column == name);
            if (hit.Column is not null) { names.Add(name); values.Add(hit.Value); types[name] = type; continue; }
            if (!required) continue;
            names.Add(name);
            types[name] = type;
            values.Add(type switch
            {
                "uuid" => Guid.NewGuid(),
                "boolean" => false,
                "integer" or "smallint" or "bigint" => 0,
                "timestamp with time zone" => DateTime.UtcNow,
                "timestamp without time zone" => DateTime.UtcNow,
                "date" => DateOnly.FromDateTime(DateTime.UtcNow),
                "time without time zone" => new TimeOnly(0, 0),
                "jsonb" or "json" => "[]",
                _ => string.Empty,
            });
        }
        await using var tx = await connection.BeginTransactionAsync();
        await using (var off = new NpgsqlCommand("SET LOCAL session_replication_role = replica", connection, tx)) await off.ExecuteNonQueryAsync();
        var sql = $"INSERT INTO \"{table}\" ({string.Join(", ", names.Select(n => $"\"{n}\""))}) VALUES ({string.Join(", ", names.Select((n, i) => types[n] is "jsonb" or "json" ? $"CAST(@p{i} AS {types[n]})" : $"@p{i}"))})";
        await using var cmd = new NpgsqlCommand(sql, connection, tx);
        for (var i = 0; i < values.Count; i++)
        {
            var v = values[i];
            cmd.Parameters.Add(v switch
            {
                DateTime dt => new NpgsqlParameter($"p{i}", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = DateTime.SpecifyKind(dt, DateTimeKind.Utc) },
                DateOnly d => new NpgsqlParameter($"p{i}", NpgsqlTypes.NpgsqlDbType.Date) { Value = d },
                TimeOnly t => new NpgsqlParameter($"p{i}", NpgsqlTypes.NpgsqlDbType.Time) { Value = t },
                null => new NpgsqlParameter($"p{i}", DBNull.Value),
                _ => new NpgsqlParameter($"p{i}", v),
            });
        }
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

    private static async Task<bool> TableExists(AppDbContext db, string table) =>
        await Scalar(db, $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '{table}'") > 0;

    private static async Task<bool> ColumnExists(AppDbContext db, string table, string column) =>
        await Scalar(db, $"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = '{table}' AND column_name = '{column}'") > 0;

    private static async Task<bool> ConstraintExists(AppDbContext db, string name) =>
        await Scalar(db, $"SELECT COUNT(*) FROM pg_constraint WHERE conname = '{name}'") > 0;
}
