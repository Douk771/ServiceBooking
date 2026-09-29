using System.Data.Common;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// LIM19-020 — ARCHITECTURE_CYCLE19.md §385.2/§391. The deploy gate's "unfinished purchase of a retired
/// limit option" exists in two forms that must never drift apart: the SQL file
/// <c>deploy/checks/cycle19-retired-limit-options-live.sql</c> (what deploy-remote.sh runs against the
/// live DB) and the LINQ twin <see cref="RetiredLimitOptions.LiveRetiredRows"/> (what the startup report
/// uses). Both are run here against the same data in a real Postgres. Neither file is modified by the test.
///
/// Time handling: the SQL reads the database's now(), which is fixed for the whole transaction. The seed,
/// the SQL and the LINQ query therefore all run inside ONE transaction (rolled back at the end); the LINQ
/// side is handed exactly that now() as <c>nowUtc</c>. This makes the "exactly now" boundary
/// (<c>PaidUntilUtc == now</c> counts, <c>EndsAtUtc == now</c> does not) deterministic instead of racy.
/// </summary>
public class Cycle19RetiredLimitGateParityTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    private sealed record Seed(string Label, string? CapabilityKey, Func<DateTime, DateTime?> Ends, Func<DateTime, DateTime?> Paid,
        bool ExpectedLive, bool OnExpiredSubscriptionAccount = false);

    private static readonly Seed[] Seeds =
    [
        new("emp-null-null", "employees", _ => null, _ => null, true),
        new("comp-mixed-case-spaces-future", " Companies ", n => n.AddDays(10), n => n.AddDays(10), true),
        new("EMP-UPPER-paid-past", "EMPLOYEES", _ => null, n => n.AddDays(-1), false),
        new("emp-ended-past", "employees", n => n.AddDays(-1), _ => null, false),
        new("comp-ends-exactly-now", "companies", n => n, _ => null, false),
        new("emp-paid-exactly-now", "employees", _ => null, n => n, true),
        new("emp-ends-future-paid-past", "employees", n => n.AddDays(5), n => n.AddDays(-1), false),
        new("emp-ends-past-paid-future", "employees", n => n.AddDays(-1), n => n.AddDays(5), false),
        new("emp-paid-future", "employees", _ => null, n => n.AddDays(5), true),
        new("whatsapp-not-limit", "notifications.whatsapp", _ => null, _ => null, false),
        new("null-capability", null, _ => null, _ => null, false),
        new("near-miss-employee", "employee", _ => null, _ => null, false),
        new("comp-on-expired-subscription", "companies", _ => null, _ => null, true, OnExpiredSubscriptionAccount: true),
        new("emp-on-expired-subscription-paid-past", "employees", _ => null, n => n.AddDays(-2), false, OnExpiredSubscriptionAccount: true),
    ];

    [Fact, TestCase("LIM19-020")]
    public async Task GateSql_And_LiveRetiredRows_ReturnTheSameRows_OnBoundarySet()
    {
        var sql = ReadGateSql();
        sql.Split('\n').Where(l => l.TrimStart().StartsWith('\\')).Should().BeEmpty("the gate file must stay Npgsql-executable (no psql meta-commands)");

        var activeOwner = await RegisterAsync();
        var expiredOwner = await RegisterAsync();

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var dbTx = tx.GetDbTransaction();

        var now = await DbNowAsync(db, dbTx);

        // Empty set: both forms empty.
        (await RunGateSqlAsync(db, dbTx, sql)).Should().BeEmpty("no purchase rows exist yet");
        (await RetiredLimitOptions.LiveRetiredRows(db, now).AsNoTracking().ToListAsync()).Should().BeEmpty();

        var (activeAccount, expiredAccount) = await SeedAccountsAsync(db, activeOwner.UserId, expiredOwner.UserId, now);
        var expectedCodes = new HashSet<string>();
        var codeByLabel = new Dictionary<string, string>();
        foreach (var s in Seeds)
        {
            var option = new SubscriptionOption
            {
                Id = Guid.NewGuid(), Code = Unique("lim19-020-" + s.Label + "-"), Name = s.Label,
                Kind = OptionKind.Quantity, UnitName = "шт", CapabilityKey = s.CapabilityKey,
            };
            db.SubscriptionOptions.Add(option);
            db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption
            {
                Id = Guid.NewGuid(), OptionId = option.Id, Quantity = 2,
                BillingAccountId = s.OnExpiredSubscriptionAccount ? expiredAccount : activeAccount,
                EndsAtUtc = s.Ends(now), PaidUntilUtc = s.Paid(now), ActivatedAtUtc = now.AddDays(-30),
            });
            codeByLabel[s.Label] = option.Code;
            if (s.ExpectedLive) expectedCodes.Add(option.Code);
        }
        await db.SaveChangesAsync();

        var sqlRows = await RunGateSqlAsync(db, dbTx, sql);
        var linqRows = (await RetiredLimitOptions.LiveRetiredRows(db, now)
                .AsNoTracking()
                .Select(o => new GateRow(o.BillingAccountId, o.Option.Code, o.Option.CapabilityKey, o.Quantity, o.PaidUntilUtc, o.EndsAtUtc, o.ActivatedAtUtc))
                .ToListAsync())
            .OrderBy(r => r.BillingAccountId).ThenBy(r => r.OptionCode, StringComparer.Ordinal).ToList();

        // Parity: same multiset of rows, column by column.
        sqlRows.Select(r => r.OptionCode).Should().BeEquivalentTo(linqRows.Select(r => r.OptionCode));
        sqlRows.Should().BeEquivalentTo(linqRows, o => o.WithStrictOrdering(), "SQL gate and LINQ twin must agree row for row");

        // And both agree with §385.2 itself (so two identically wrong forms cannot pass).
        sqlRows.Select(r => r.OptionCode).Should().BeEquivalentTo(expectedCodes);
        sqlRows.Should().NotBeEmpty();
        // The account with an expired subscription is reported (§385.2: subscription state is not considered).
        sqlRows.Should().Contain(r => r.BillingAccountId == expiredAccount && r.OptionCode == codeByLabel["comp-on-expired-subscription"]);
        // The exact-now boundaries: paid == now counts, ends == now does not.
        sqlRows.Should().Contain(r => r.OptionCode == codeByLabel["emp-paid-exactly-now"]);
        sqlRows.Should().NotContain(r => r.OptionCode == codeByLabel["comp-ends-exactly-now"]);
        // tx is disposed without commit: everything rolls back.
    }

    /// <summary>The report file uses psql meta-commands (\echo, \pset), so Npgsql cannot run it. When the
    /// test Postgres is a Testcontainers container reachable through the docker CLI, it is executed with
    /// the container's own psql exactly the way deploy-remote.sh does (ON_ERROR_STOP=1) on committed data.
    /// In external-server mode (no container) there is nothing to exec into and the test returns early.</summary>
    [Fact, TestCase("LIM19-020b")]
    public async Task ReportSql_RunsInContainerPsql_WithoutError_OnSeededSet()
    {
        var cs = new NpgsqlConnectionStringBuilder(ConnectionString);
        var containerId = await FindContainerIdAsync(cs.Port);
        if (containerId is null)
        {
            Console.WriteLine("[LIM19-020b] no docker container publishes the test Postgres port; psql run skipped (external-server mode or docker CLI unavailable).");
            return;
        }

        var owner = await RegisterAsync();
        var optionIds = new List<Guid>();
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var account = new BillingAccount { Id = Guid.NewGuid(), OwnerUserId = owner.UserId };
            db.BillingAccounts.Add(account);
            var now = DateTime.UtcNow;
            foreach (var s in Seeds.Where(x => !x.OnExpiredSubscriptionAccount && !x.Label.Contains("exactly-now")))
            {
                var option = new SubscriptionOption
                {
                    Id = Guid.NewGuid(), Code = Unique("lim19-020b-" + s.Label + "-"), Name = s.Label,
                    Kind = OptionKind.Quantity, UnitName = "шт", CapabilityKey = s.CapabilityKey,
                };
                optionIds.Add(option.Id);
                db.SubscriptionOptions.Add(option);
                db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption
                {
                    Id = Guid.NewGuid(), OptionId = option.Id, BillingAccountId = account.Id, Quantity = 1,
                    EndsAtUtc = s.Ends(now), PaidUntilUtc = s.Paid(now), ActivatedAtUtc = now.AddDays(-30),
                });
            }
            await db.SaveChangesAsync();
        }

        try
        {
            var report = await File.ReadAllTextAsync(FindDeployCheck("cycle19-limit-options-report.sql"));
            var (exit, stdout, stderr) = await RunProcessAsync("docker",
                ["exec", "-i", "-e", $"PGPASSWORD={cs.Password}", containerId, "psql", "-h", "localhost", "-U", cs.Username!, "-d", cs.Database!, "-v", "ON_ERROR_STOP=1"],
                report);

            exit.Should().Be(0, $"psql must run the report without error. stderr: {stderr}");
            stderr.Should().NotContain("ERROR");
            stdout.Should().Contain("cycle19-limit-options-report: done.");
            stdout.Should().Contain("live").And.Contain("ended");
            stdout.Should().Contain("emp-null-null", "the seeded live row must be listed in part (б)");
        }
        finally
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.AccountSubscriptionOptions.Where(o => optionIds.Contains(o.OptionId)).ExecuteDeleteAsync();
            await db.SubscriptionOptions.Where(o => optionIds.Contains(o.Id)).ExecuteDeleteAsync();
            await db.BillingAccounts.Where(a => a.OwnerUserId == owner.UserId).ExecuteDeleteAsync();
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private sealed record GateRow(Guid BillingAccountId, string OptionCode, string? CapabilityKey, int Quantity,
        DateTime? PaidUntilUtc, DateTime? EndsAtUtc, DateTime? ActivatedAtUtc);

    private static async Task<(Guid Active, Guid Expired)> SeedAccountsAsync(AppDbContext db, string activeOwnerId, string expiredOwnerId, DateTime now)
    {
        var active = new BillingAccount { Id = Guid.NewGuid(), OwnerUserId = activeOwnerId };
        var expired = new BillingAccount { Id = Guid.NewGuid(), OwnerUserId = expiredOwnerId };
        db.BillingAccounts.AddRange(active, expired);
        db.AccountSubscriptions.Add(new AccountSubscription
        {
            Id = Guid.NewGuid(), OwnerUserId = activeOwnerId, BillingAccountId = active.Id,
            IsActive = true, PaidUntil = now.AddDays(30), CreatedAt = now, UpdatedAt = now,
        });
        db.AccountSubscriptions.Add(new AccountSubscription
        {
            Id = Guid.NewGuid(), OwnerUserId = expiredOwnerId, BillingAccountId = expired.Id,
            IsActive = false, PaidUntil = now.AddDays(-60), CreatedAt = now.AddDays(-90), UpdatedAt = now.AddDays(-60),
        });
        await db.SaveChangesAsync();
        return (active.Id, expired.Id);
    }

    private static async Task<DateTime> DbNowAsync(AppDbContext db, DbTransaction tx)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "select now()";
        var value = (DateTime)(await cmd.ExecuteScalarAsync())!;
        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static async Task<List<GateRow>> RunGateSqlAsync(AppDbContext db, DbTransaction tx, string sql)
    {
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<GateRow>();
        while (await reader.ReadAsync())
        {
            DateTime? Dt(string c) => reader.IsDBNull(reader.GetOrdinal(c)) ? null : DateTime.SpecifyKind(reader.GetDateTime(reader.GetOrdinal(c)), DateTimeKind.Utc);
            rows.Add(new GateRow(
                reader.GetGuid(reader.GetOrdinal("billing_account_id")),
                reader.GetString(reader.GetOrdinal("option_code")),
                reader.IsDBNull(reader.GetOrdinal("capability_key")) ? null : reader.GetString(reader.GetOrdinal("capability_key")),
                reader.GetInt32(reader.GetOrdinal("quantity")),
                Dt("paid_until_utc"), Dt("ends_at_utc"), Dt("activated_at_utc")));
        }
        return rows.OrderBy(r => r.BillingAccountId).ThenBy(r => r.OptionCode, StringComparer.Ordinal).ToList();
    }

    private static string ReadGateSql() => File.ReadAllText(FindDeployCheck("cycle19-retired-limit-options-live.sql"));

    private static string FindDeployCheck(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "deploy", "checks", file);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"deploy/checks/{file} not found above {AppContext.BaseDirectory}");
    }

    private static async Task<string?> FindContainerIdAsync(int port)
    {
        try
        {
            var (exit, stdout, _) = await RunProcessAsync("docker", ["ps", "-q", "--filter", $"publish={port}"], null);
            var id = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            return exit == 0 ? id : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> RunProcessAsync(string file, string[] args, string? stdin)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (stdin is not null) await p.StandardInput.WriteAsync(stdin);
        p.StandardInput.Close();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await p.WaitForExitAsync(cts.Token);
        return (p.ExitCode, await outTask, await errTask);
    }
}
