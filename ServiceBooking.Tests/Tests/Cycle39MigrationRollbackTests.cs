using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 39: накат → откат → накат миграции <c>Cycle39StaysServices</c> на отдельной пустой базе (по образцу CY37-140). Откат убирает 11 таблиц услуг, колонки настроек напоминания
/// и привязки к заказам; строки цикла 37 (настройки компании, бронь) переживают оба перехода, а компаниям цикла 37 время напоминания после наката — 18:00 (US-39-19).
/// Down() сознательно падает громко, если уже есть подтверждения оплаты или push-подписки отдельных заказов (DEPLOY.md §28а) — это не проверяется данными здесь.
/// </summary>
public class Cycle39MigrationRollbackTests(TestDatabaseFixture fixture) : Cycle39TestBase(fixture)
{
    private const string PreviousMigration = "20261008070135_Cycle37Stays";

    private static readonly string[] ServiceTables =
    [
        "StayServices", "StayServicePhotos", "StayServiceWeeklyWindows", "StayServiceDateOverrides", "StayServiceScheduleEvents", "StayServicePriceRules", "StayServiceItems",
        "StayServiceSessions", "StayServiceOrders", "StayServiceOrderEvents", "StaysReminderTemplateChanges",
    ];

    [Fact, TestCase("CY39-140")]
    public async Task Cycle39StaysServicesMigration_UpDownUp_OnScratchDatabase_KeepsCycle37Rows()
    {
        var admin = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = "postgres", Pooling = false };
        var dbName = "sbtest_cy39mig_" + Guid.NewGuid().ToString("N")[..12];
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

            // ── состояние цикла 37 с данными ──
            await migrator!.MigrateAsync(PreviousMigration);
            foreach (var t in ServiceTables) (await TableExists(db, t)).Should().BeFalse($"до цикла 39 таблицы {t} нет");
            var company = Guid.NewGuid();
            await InsertMinimalRowAsync(db, "StaysSettings", ("CompanyId", company), ("MinNights", 1), ("MaxNights", 30));
            (await ColumnExists(db, "StaysSettings", "ArrivalReminderTime")).Should().BeFalse();

            // ── накат ──
            await db.Database.MigrateAsync();
            (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(m => m.EndsWith("_Cycle39StaysServices"));
            foreach (var t in ServiceTables) (await TableExists(db, t)).Should().BeTrue($"после наката есть таблица {t}");
            (await ConstraintExists(db, "EX_StayServiceSessions_NoOverlap")).Should().BeTrue("исключающее ограничение от двойной брони сеанса");
            (await Scalar(db, $"SELECT COUNT(*) FROM \"StaysSettings\" WHERE \"CompanyId\" = '{company}' AND \"ArrivalReminderTime\" = TIME '18:00' AND \"ArrivalReminderTemplate\" IS NULL " +
                              "AND \"ArrivalReminderPushText\" = false AND \"AcceptServiceOrdersWithoutStay\" = false")).Should().Be(1, "компании цикла 37: напоминание в 18:00, шаблон не менялся, push-текст выключен, заказ без проживания выключен");

            // двойная бронь времени отбивается самой БД (23P01) уже на скретч-базе
            var service = Guid.NewGuid();
            var booking = Guid.NewGuid();
            await InsertMinimalRowAsync(db, "StayServices", ("Id", service), ("CompanyId", company), ("Slug", "banya"), ("Name", "Баня"), ("MinHours", 2), ("MaxHours", 6), ("StepMinutes", 60),
                ("BufferMinutes", 30), ("MinLeadMinutes", 0), ("CancellationBoundaryHours", 12));
            await InsertMinimalRowAsync(db, "StayBookings", ("Id", booking), ("CompanyId", company), ("CheckInDate", new DateOnly(2027, 1, 10)), ("CheckOutDate", new DateOnly(2027, 1, 12)), ("Nights", 2));
            await InsertSessionAsync(db, service, company, booking, "2027-01-15 15:00+00", "2027-01-15 18:00+00", "2027-01-15 18:30+00");
            var overlap = await Record.ExceptionAsync(() => InsertSessionAsync(db, service, company, booking, "2027-01-15 18:15+00", "2027-01-15 19:00+00", "2027-01-15 19:30+00"));
            overlap.Should().NotBeNull().And.Subject.ToString().Should().Contain("23P01");
            (await Record.ExceptionAsync(() => InsertSessionAsync(db, service, company, booking, "2027-01-15 18:30+00", "2027-01-15 19:30+00", "2027-01-15 20:00+00"))).Should().BeNull("вплотную после зазора — можно");

            // у сеанса ровно один родитель (CHECK)
            var noParent = await Record.ExceptionAsync(() => InsertSessionAsync(db, service, company, null, "2027-02-15 15:00+00", "2027-02-15 16:00+00", "2027-02-15 16:30+00"));
            noParent.Should().NotBeNull("сеанс без брони и без заказа недопустим");

            // ── откат ──
            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"StayServiceSessions\"");
            await migrator.MigrateAsync(PreviousMigration);
            (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(m => m.EndsWith("_Cycle39StaysServices"));
            foreach (var t in ServiceTables) (await TableExists(db, t)).Should().BeFalse($"после отката таблицы {t} нет");
            foreach (var (table, column) in new[] { ("StaysSettings", "ArrivalReminderTime"), ("StaysSettings", "ArrivalReminderTemplate"), ("StaysSettings", "ArrivalReminderPushText"),
                         ("StaysSettings", "AcceptServiceOrdersWithoutStay"), ("StayBookings", "ArrivalReminderPageText"), ("StayBookings", "ArrivalReminderSentAtUtc"),
                         ("StayPaymentProofs", "StayServiceOrderId"), ("StayBookingCharges", "ServiceSessionId"), ("OutboundNotifications", "StayServiceOrderId") })
                (await ColumnExists(db, table, column)).Should().BeFalse($"колонка {table}.{column} убрана");
            (await Scalar(db, $"SELECT COUNT(*) FROM \"StaysSettings\" WHERE \"CompanyId\" = '{company}'")).Should().Be(1, "строка настроек цикла 37 пережила откат");
            (await Scalar(db, $"SELECT COUNT(*) FROM \"StayBookings\" WHERE \"Id\" = '{booking}'")).Should().Be(1, "бронь цикла 37 пережила откат");

            // ── повторный накат ──
            await db.Database.MigrateAsync();
            foreach (var t in ServiceTables) (await TableExists(db, t)).Should().BeTrue($"после повторного наката есть таблица {t}");
            (await ConstraintExists(db, "EX_StayServiceSessions_NoOverlap")).Should().BeTrue();
            (await Scalar(db, $"SELECT COUNT(*) FROM \"StaysSettings\" WHERE \"CompanyId\" = '{company}' AND \"ArrivalReminderTime\" = TIME '18:00'")).Should().Be(1);
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

    private static async Task InsertSessionAsync(AppDbContext db, Guid service, Guid company, Guid? booking, string start, string end, string occupiedUntil) =>
        await InsertMinimalRowAsync(db, "StayServiceSessions", ("Id", Guid.NewGuid()), ("ServiceId", service), ("CompanyId", company), ("StayBookingId", booking),
            ("StartUtc", DateTime.Parse(start, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal)),
            ("EndUtc", DateTime.Parse(end, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal)),
            ("OccupiedUntilUtc", DateTime.Parse(occupiedUntil, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal)),
            ("ReleasedAtUtc", null), ("State", 0), ("Hours", 1), ("StartMinute", 0), ("ServiceNameSnapshot", "Баня"));

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
