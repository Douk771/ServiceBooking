using Microsoft.AspNetCore.Mvc.Testing;
using ServiceBooking.TestKit;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// ARCHITECTURE_CYCLE8_PHASE2.md §91/§92.2 — one instance per test class (<c>IClassFixture</c>, not
/// <c>ICollectionFixture</c>): takes this class' own slot ("c07", <see cref="TestSlot.NextForClass"/>),
/// clones a fresh database for it from the run's template, boots the one shared
/// <see cref="CustomWebApplicationFactory"/> most functional tests in the class run against, and hands
/// out this class' <see cref="TestData"/> generator.
///
/// Replaces phase 1's <c>SlotDatabaseFixture</c>/<c>ApiDatabaseFixture</c>/<c>LegalDatabaseFixture</c>/
/// <c>DispatchDatabaseFixture</c> trio, which shared exactly three fixed databases ("api"/"legal"/
/// "dispatch") across the whole assembly — the shared suite state (US-96…US-98) that made those three
/// databases unsafe to run in parallel. A class database is created once per class and dropped once the
/// class finishes; <c>EnsureDeletedAsync</c> is still never called on it anywhere (§69.3/§79 п.1 remain
/// in force) — teardown is always DROP DATABASE, guarded by <see cref="TestDatabaseNaming.EnsureOwnedByThisRun"/>.
/// </summary>
public sealed class TestDatabaseFixture : IAsyncLifetime
{
    private TestClassDatabaseLease _lease = null!;

    /// <summary>This test class' database slot ("c07") — xUnit v2 never hands a class fixture its owning
    /// type (§92.2), so this is a bare per-process counter value, not derived from the class name; each
    /// base class logs the slot↔class pairing itself on first use (see <see cref="ApiTestBase"/>/
    /// <see cref="NotificationTestBase"/>).</summary>
    public string ClassSlot { get; private set; } = null!;

    /// <summary>This class' host identity (SuperAdmin credentials, file roots, connection string, ...) —
    /// see <see cref="TestHostSettings"/>. Built from the "api" factoryTag; a class that boots a
    /// secondary, differently-configured host (e.g. <see cref="NotificationDispatchTestFactory"/>) calls
    /// <c>TestHostSettings.Apply</c> again with its own factoryTag but the SAME <see cref="ClassSlot"/>/
    /// <see cref="ConnectionString"/>, so every host in the class still shares one database and one set
    /// of file roots.</summary>
    public TestHostIdentity Identity => Factory.Identity;

    /// <summary>This class' database connection string — taken from the lease, so reading it does not boot
    /// <see cref="Factory"/> (cycle 36, L2: classes that only need the connection string no longer pay for a host
    /// they never use). Kept so the many call sites written against phase 1's <c>fixture.ConnectionString</c>
    /// compile unchanged.</summary>
    public string ConnectionString => _lease.ConnectionString;

    /// <summary>The shared <see cref="CustomWebApplicationFactory"/> of the class — created and started on first access
    /// (cycle 36, L2), by <see cref="ApiTestBase"/>'s constructor or by a test that reads <see cref="Identity"/>.</summary>
    public CustomWebApplicationFactory Factory => _factory.Value;

    private Lazy<CustomWebApplicationFactory> _factory = null!;

    private readonly List<(string Key, IAsyncDisposable Host)> _classHosts = [];
    private readonly object _classHostsLock = new();
    private readonly Dictionary<string, Exception> _classHostFailures = new();

    /// <summary>
    /// One host per CLASS for the given key (cycle 36, L1, ARCHITECTURE_CYCLE36.md §36.7.1): created and started on the first
    /// call, lives until this fixture is disposed (before the class database is dropped). <paramref name="key"/> is unique inside
    /// the class per host type and configuration ("push", "phv", "addr", "addr:permit=3"). Only for tests that satisfy the
    /// admission rule of §36.7.1 (the host's configuration is not changed by the test; recording fakes are filtered by the test's
    /// own data; no rate-limit thresholds; no TTL-cache dependence; no substituted clock left over).
    /// </summary>
    public TFactory ClassHost<TFactory>(string key, Func<string, TFactory> create)
        where TFactory : WebApplicationFactory<Program>
    {
        lock (_classHostsLock)
        {
            foreach (var (existingKey, host) in _classHosts)
            {
                if (existingKey == key)
                    return (TFactory)host;
            }

            // A host whose start failed once is not started again by every following test of the class (each retry would cost the same
            // seconds and leak a half-started factory): the first failure is cached and rethrown as the cause of the following ones.
            if (_classHostFailures.TryGetValue(key, out var earlier))
                throw new InvalidOperationException($"The class host '{key}' failed to start earlier in this class: {earlier.Message}", earlier);

            TFactory? created = null;
            try
            {
                created = create(ConnectionString);
                _ = created.Services; // start now: a failed start surfaces in the test that asked for the host
            }
            catch (Exception ex)
            {
                try { created?.Dispose(); }
                catch (Exception disposeFailure) { Console.WriteLine($"[sb-test] WARNING: не удалось освободить хост класса '{key}' после неудачного старта (slot={ClassSlot}): {disposeFailure.Message}"); }
                _classHostFailures[key] = ex;
                throw;
            }

            _classHosts.Add((key, created));
            return created;
        }
    }

    /// <summary>This class' single source of collision-free unique values (§94, Q12).</summary>
    public TestData Data { get; private set; } = null!;

    private int _testClassRecorded;

    /// <summary>T9 review (M3): call once from the owning base class' constructor (the first — and only
    /// — place that actually knows <c>GetType().Name</c>, since xUnit v2 never hands a class fixture its
    /// own class — §92.2) so the slot↔class pairing this doc comment already promised is actually
    /// recorded, not just logged and forgotten. Logs unconditionally (cheap, always useful locally);
    /// annotates the database itself (so a STUCK database found later by <c>status</c>/<c>sweep</c> can
    /// still be traced back) only once per fixture, guarded here rather than relying on every call site
    /// to dedupe — xUnit constructs a fresh test-class instance per <c>[Fact]</c>, so this runs once per
    /// TEST, not once per CLASS, without this guard.</summary>
    public void RecordTestClass(Type testClass)
    {
        var testClassName = testClass.Name;
        Console.WriteLine($"[sb-test] class={testClassName} slot={ClassSlot} db={_lease.DatabaseName}");

        if (Interlocked.Exchange(ref _testClassRecorded, 1) == 1)
            return;

        TestRunMetrics.ClassRecorded(ClassSlot, testClass.FullName ?? testClassName);

        // Fire-and-forget: this is diagnostics, not part of the test's own behaviour, and every test
        // constructor is synchronous — nothing here may block or fail the test.
        _ = _lease.RecordTestClassAsync(testClassName);
    }

    public async Task InitializeAsync()
    {
        await ClassConcurrencyGate.EnterAsync();
        _gateHeld = true;
        try
        {
            ClassSlot = TestSlot.NextForClass();
            _lease = await TestRunEnvironment.LeaseClassDatabaseAsync(ClassSlot);
            Data = new TestData(ClassSlot);

            _factory = new Lazy<CustomWebApplicationFactory>(() =>
            {
                var factory = new CustomWebApplicationFactory(_lease.ConnectionString);
                // Touching Services boots the host, which runs Program.cs's migrate + role/SuperAdmin seed, and
                // populates Factory.Identity (ConfigureWebHost's return value).
                _ = factory.Services;
                return factory;
            });
        }
        catch
        {
            // xUnit не вызывает DisposeAsync у фикстуры, чья InitializeAsync упала: слот вернуть нужно здесь.
            ReleaseGate();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        // Factory.DisposeAsync() stopping the host is not guaranteed to succeed (e.g. a background
        // dispatcher that never finished starting) -- the lease must still be released so the class
        // database is dropped and the process-wide refcount is decremented; otherwise a host-stop
        // failure leaks the database (and, if this was the last class, the whole container) for the
        // rest of the process.
        try
        {
            await DisposeHostsAsync();
        }
        finally
        {
            try
            {
                // InitializeAsync may have failed before the lease was taken: nothing to drop then.
                if (_lease is not null) await _lease.DropAsync();
            }
            finally
            {
                ReleaseGate();
            }
        }
    }

    private bool _gateHeld;

    /// <summary>Stops the class hosts in reverse order of creation, then the shared factory (only if it was ever created).
    /// A host that fails to stop is reported and does not prevent the others, nor the database drop.</summary>
    private async Task DisposeHostsAsync()
    {
        List<(string Key, IAsyncDisposable Host)> hosts;
        lock (_classHostsLock) hosts = [.. _classHosts];
        hosts.Reverse();

        foreach (var (key, host) in hosts)
        {
            try { await host.DisposeAsync(); }
            catch (Exception ex) { Console.WriteLine($"[sb-test] WARNING: не удалось остановить хост класса '{key}' (slot={ClassSlot}): {ex.Message}"); }
        }

        if (_factory is { IsValueCreated: true })
            await _factory.Value.DisposeAsync();
    }

    private void ReleaseGate()
    {
        if (_gateHeld)
        {
            _gateHeld = false;
            ClassConcurrencyGate.Exit();
        }
    }
}
