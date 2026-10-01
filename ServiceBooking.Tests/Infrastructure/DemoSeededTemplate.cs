using System.Diagnostics;
using FluentAssertions;
using ServiceBooking.API.Services.Ops;
using ServiceBooking.TestKit;
using ServiceBooking.Tests.Tests;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Cycle 36: the seeded demo template. The product's demo reset (<c>ops demo reset --yes</c>: ~10 thousand bookings, 13-25 s) is run ONCE per test run, lazily, under
/// a process-wide lock, into the database slot <see cref="Slot"/>; a scenario that only needs a fresh demo as its ARRANGE step gets its own database by
/// <c>CREATE DATABASE ... TEMPLATE</c> from it (a second or less) plus a copy of the published files, instead of its own reset. The scenarios in which the reset or the
/// generation itself is the subject (the budget, determinism, "reset removes what visitors did", the generator of the showcase) keep calling the real product reset.
/// <c>DemoSeededTemplateGuardTests</c> compares a clone with the state of a real reset so that the template cannot drift away from the product.
/// The data is dated by "now" of the moment of the template; the run is minutes long, so a difference appears only across midnight, exactly as with a reset per class.
/// </summary>
public static class DemoSeededTemplate
{
    public const string Slot = DemoDatabaseSlots.Template;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _ready;
    private static TimeSpan _resetTook;

    /// <summary>How long the one real reset behind the template took.</summary>
    public static TimeSpan ResetTook => _resetTook;

    public static async Task EnsureAsync()
    {
        if (Volatile.Read(ref _ready)) return;
        await Gate.WaitAsync();
        try
        {
            if (_ready) return;
            // The lease of the template is never dropped by a test: the process-exit teardown of TestRunEnvironment drops every database this run created.
            var lease = await TestRunEnvironment.LeaseClassDatabaseAsync(Slot);
            await using (var factory = new DemoHostFactory(lease.ConnectionString, new Dictionary<string, string?> { ["DemoMode:ResetLocalTime"] = "00:00" }))
            {
                _ = factory.Services;
                var sw = Stopwatch.StartNew();
                var writer = new StringWriter();
                var exit = await OpsCommandRunner.RunAsync(factory.Services, OpsCommandLine.Parse(["ops", "demo", "reset", "--yes"])!, writer);
                _resetTook = sw.Elapsed;
                exit.Should().Be(0, writer.ToString());
            }
            // CREATE DATABASE ... TEMPLATE needs no live connection to the source: the host is stopped, drop this process' pool too.
            using (var probe = new Npgsql.NpgsqlConnection(lease.ConnectionString)) Npgsql.NpgsqlConnection.ClearPool(probe);
            Volatile.Write(ref _ready, true);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>The directory of published files of a database slot (same rule as <see cref="TestHostSettings"/>).</summary>
    public static string FileRootOf(string slot) => Path.Combine(Path.GetTempPath(), "sb-test", TestRunKey.Current, slot);

    /// <summary>Copies the files that the reset of the template published (photos, logos, product pictures) into the root of the clone's slot.</summary>
    public static void CopyFilesTo(string slot)
    {
        foreach (var part in new[] { "public", "private" })
        {
            var from = Path.Combine(FileRootOf(Slot), part);
            if (!Directory.Exists(from)) continue;
            var to = Path.Combine(FileRootOf(slot), part);
            Directory.CreateDirectory(to);
            foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)), overwrite: true);
        }
    }
}
