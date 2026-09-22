using Npgsql;

namespace ServiceBooking.TestKit;

/// <summary>
/// Creates and tears down the disposable databases of one test run against a
/// <see cref="TestServerLease"/> — the template + per-slot scheme from ARCHITECTURE_CYCLE8.md §68,
/// and the ONLY place in the repository allowed to run DROP DATABASE (§69.3). Every drop goes through
/// <see cref="TestDatabaseNaming.EnsureOwnedByThisRun"/> first.
/// </summary>
public sealed class TestDatabaseLease
{
    private const string TemplateSlot = "template";
    private const string NoTemplateEnvironmentVariable = "SERVICEBOOKING_TEST_NO_TEMPLATE";

    /// <summary>Serializes every `CREATE DATABASE ... TEMPLATE` clone (ARCHITECTURE_CYCLE8_PHASE2.md
    /// §91.5 п.2) — concurrent clones from the same template are the one place this scheme still has a
    /// process-wide race, and Postgres itself only serializes them with a chance of "source database is
    /// being accessed by other users" rather than queuing them cleanly.</summary>
    private static readonly SemaphoreSlim CloneGate = new(1, 1);

    private readonly TestServerLease _server;
    private readonly List<string> _createdDatabases = [];
    private readonly Dictionary<string, ResourceLabels.DatabaseMetadata> _metadataByDatabase = [];
    private readonly object _createdDatabasesLock = new();
    private bool _noTemplate;
    private Func<string, Task>? _migrateTemplate;

    public TestDatabaseLease(TestServerLease server)
    {
        _server = server;
    }

    /// <summary>Database name for a given slot ("api", "legal", "dispatch", ...), per the naming
    /// scheme in ARCHITECTURE_CYCLE8.md §66.1.</summary>
    public string DatabaseNameFor(string slot) => $"sbtest_{TestRunKey.Current}_{slot}";

    /// <summary>Full Npgsql connection string for a slot's database, with the pool limits from §75
    /// applied. Does not create anything — call <see cref="EnsureTemplateAsync"/>/<see cref="CreateClassDatabaseAsync"/> first.</summary>
    public string ConnectionStringFor(string slot)
    {
        var builder = new NpgsqlConnectionStringBuilder(_server.MaintenanceConnectionString)
        {
            Database = DatabaseNameFor(slot),
            MaxPoolSize = TestInfrastructure.PoolMaxSize,
            ConnectionIdleLifetime = TestInfrastructure.PoolConnectionIdleLifetimeSeconds,
            Timeout = TestInfrastructure.PoolTimeoutSeconds,
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Creates the template database and migrates it once via <paramref name="migrateTemplate"/>
    /// (ARCHITECTURE_CYCLE8_PHASE2.md §91, replacing the fixed api/legal/dispatch slot list from phase 1
    /// with the class-per-database scheme). With <c>SERVICEBOOKING_TEST_NO_TEMPLATE=1</c> this becomes a
    /// no-op — <see cref="CreateClassDatabaseAsync"/> then creates and migrates each class database
    /// directly, with no `CREATE DATABASE ... TEMPLATE` involved at all.
    /// </summary>
    public async Task EnsureTemplateAsync(Func<string, Task> migrateTemplate, CancellationToken cancellationToken = default)
    {
        _migrateTemplate = migrateTemplate;
        _noTemplate = Environment.GetEnvironmentVariable(NoTemplateEnvironmentVariable) == "1";
        if (_noTemplate)
            return;

        await using var connection = new NpgsqlConnection(_server.MaintenanceConnectionString);
        await connection.OpenAsync(cancellationToken);

        var templateName = DatabaseNameFor(TemplateSlot);
        await CreateDatabaseAsync(connection, templateName, template: null, cancellationToken);
        lock (_createdDatabasesLock) _createdDatabases.Add(templateName);

        await migrateTemplate(ConnectionStringFor(TemplateSlot));

        // §91.5 п.1: CREATE DATABASE ... TEMPLATE requires zero live connections to the template —
        // the migration connection above must be fully released first.
        await ReleaseTemplateConnectionsAsync(connection, templateName, cancellationToken);
    }

    /// <summary>ARCHITECTURE_CYCLE8_PHASE2.md §91.5 п.1: clears this process' Npgsql pool for the
    /// template's connection string, then terminates any OTHER still-open backend connected to it (e.g. a
    /// stray tool or a previous failed attempt's half-closed session) — `CREATE DATABASE ... TEMPLATE`
    /// refuses to run while anything is connected to the source database.</summary>
    private async Task ReleaseTemplateConnectionsAsync(NpgsqlConnection maintenanceConnection, string templateName, CancellationToken cancellationToken)
    {
        await using (var templatePoolProbe = new NpgsqlConnection(ConnectionStringFor(TemplateSlot)))
        {
            NpgsqlConnection.ClearPool(templatePoolProbe);
        }

        await using var terminate = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()",
            maintenanceConnection);
        terminate.Parameters.AddWithValue("name", templateName);
        await terminate.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Clones one test class' own database from the run's template (ARCHITECTURE_CYCLE8_PHASE2.md §91 —
    /// Q10, "database on test class"). Every clone goes through <see cref="CloneGate"/> (one at a time,
    /// process-wide) and retries up to 3× on the "source database is being accessed by other users" race
    /// (§91.5 п.2) — concurrent classes cloning from the same template at once is exactly the scenario
    /// that trips it, and it is otherwise fatal to the whole run.
    /// </summary>
    public async Task<string> CreateClassDatabaseAsync(string classSlot, CancellationToken cancellationToken = default)
    {
        var name = DatabaseNameFor(classSlot);

        // L1 (T9 review): SERVICEBOOKING_TEST_NO_TEMPLATE=1 means there is no `CREATE DATABASE ...
        // TEMPLATE` at all -- each class database is created and migrated directly, with no shared
        // template to race against. CloneGate exists ONLY to serialize concurrent clones from one
        // template (§91.5 п.2); taking it here would instead serialize every class' FULL migration behind
        // one process-wide lock, turning the escape-hatch mode into a many-minutes-long single-threaded
        // run instead of the ~46s parallel one it's meant to fall back from.
        if (_noTemplate)
        {
            await using var noTemplateConnection = new NpgsqlConnection(_server.MaintenanceConnectionString);
            await noTemplateConnection.OpenAsync(cancellationToken);
            await CreateDatabaseAsync(noTemplateConnection, name, template: null, cancellationToken);
            lock (_createdDatabasesLock) _createdDatabases.Add(name);
            await _migrateTemplate!(ConnectionStringFor(classSlot));
            return name;
        }

        await CloneGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new NpgsqlConnection(_server.MaintenanceConnectionString);
            await connection.OpenAsync(cancellationToken);

            var templateName = DatabaseNameFor(TemplateSlot);
            const int maxAttempts = 3;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await ReleaseTemplateConnectionsAsync(connection, templateName, cancellationToken);
                    await CreateDatabaseAsync(connection, name, template: templateName, cancellationToken);
                    break;
                }
                catch (Npgsql.PostgresException ex) when (attempt < maxAttempts && IsTemplateBusy(ex))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                }
            }

            lock (_createdDatabasesLock) _createdDatabases.Add(name);
            return name;
        }
        finally
        {
            CloneGate.Release();
        }
    }

    /// <summary>ARCHITECTURE_CYCLE8_PHASE2.md §91.5 п.2 — Postgres serializes concurrent
    /// `CREATE DATABASE ... TEMPLATE` calls against the same source and, rarely, answers one of them with
    /// "source database ... is being accessed by other users" (SQLSTATE 55006) instead of queuing it.</summary>
    private static bool IsTemplateBusy(Npgsql.PostgresException ex) =>
        ex.SqlState == Npgsql.PostgresErrorCodes.ObjectInUse ||
        ex.Message.Contains("is being accessed by other users", StringComparison.OrdinalIgnoreCase);

    /// <summary>Drops one test class' own database (ARCHITECTURE_CYCLE8_PHASE2.md §92.2,
    /// <c>TestDatabaseFixture.DisposeAsync</c>) — guarded by the same
    /// <see cref="TestDatabaseNaming.EnsureOwnedByThisRun"/> check as every other drop in this class.
    /// §91.5 п.3: the caller (<c>TestDatabaseFixture.DisposeAsync</c>) disposes its own host FIRST, so by
    /// the time this runs there are no live application connections to this class' database left to clear
    /// — but this process' own Npgsql pool for THIS class' connection string can still hold idle ones, and
    /// <c>DROP DATABASE ... WITH (FORCE)</c> alone does not reach into this process' pool to release them
    /// before dropping (it only terminates the *server-side* backends, which is enough for the DROP to
    /// succeed, but leaves a stale pooled <see cref="NpgsqlConnection"/> around in this process pointing at
    /// a database that no longer exists). Cleared with a connection-string-scoped
    /// <see cref="NpgsqlConnection.ClearPool"/> — deliberately NOT <c>ClearAllPools()</c>, which would also
    /// tear down the still-live pools of every other class running concurrently in this same process
    /// (P classes in flight at once, §91.4).</summary>
    public async Task DropClassDatabaseAsync(string classSlot, CancellationToken cancellationToken = default)
    {
        var name = DatabaseNameFor(classSlot);

        await using (var poolProbe = new NpgsqlConnection(ConnectionStringFor(classSlot)))
        {
            NpgsqlConnection.ClearPool(poolProbe);
        }

        await using var connection = new NpgsqlConnection(_server.MaintenanceConnectionString);
        await connection.OpenAsync(cancellationToken);
        await DropAsync(connection, name, cancellationToken);
        lock (_createdDatabasesLock)
        {
            _createdDatabases.Remove(name);
            _metadataByDatabase.Remove(name);
        }
    }

    private async Task CreateDatabaseAsync(NpgsqlConnection connection, string name, string? template, CancellationToken cancellationToken)
    {
        // Defence in depth: even though these names are generated by this class, every CREATE DATABASE
        // also goes through the same disposability check that guards DROP DATABASE.
        TestDatabaseNaming.EnsureDisposable(name);

        await using (var existsCommand = new NpgsqlCommand("select 1 from pg_database where datname = @name", connection))
        {
            existsCommand.Parameters.AddWithValue("name", name);
            if (await existsCommand.ExecuteScalarAsync(cancellationToken) is not null)
            {
                // Idempotent only within THIS lease instance — already-created by a previous call, still
                // tracked in _createdDatabases. A database that exists but was never created by THIS lease
                // belongs to some other process/run that happens to share a run key (review finding N2:
                // two processes started with the same SERVICEBOOKING_TEST_RUN_KEY used to silently "adopt"
                // each other's databases here, and then teardown would drop the other process's live
                // database out from under it). Refuse instead of adopting.
                bool alreadyOurs;
                lock (_createdDatabasesLock) alreadyOurs = _createdDatabases.Contains(name);
                if (alreadyOurs)
                    return;

                throw new TestSafetyException(
                    $"[sb-test] Отказ: база \"{name}\" уже существует, но не создавалась этим прогоном.\n" +
                    $"  Похоже, что SERVICEBOOKING_TEST_RUN_KEY=\"{TestRunKey.Current}\" совпадает с ключом другого " +
                    "одновременно выполняющегося прогона (например, двух job'ов одного CI workflow).\n" +
                    "  Что сделать: задайте уникальный SERVICEBOOKING_TEST_RUN_KEY для каждого одновременного прогона, " +
                    "или не задавайте его вовсе — тогда ключ будет случайным. Ничего не создано и не удалено.");
            }
        }

        var sql = template is null
            ? $"CREATE DATABASE \"{name}\""
            : $"CREATE DATABASE \"{name}\" TEMPLATE \"{template}\"";

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        // §70.3 п.3: metadata is written at creation time so the sweeper can later compute an age for
        // this database without guessing. Without this, every server-mode database stays "undetermined"
        // forever (age unknown => never eligible for deletion), which defeats the sweeper entirely.
        var metadata = new ResourceLabels.DatabaseMetadata(
            RunKey: TestRunKey.Current,
            Host: Environment.MachineName,
            Pid: Environment.ProcessId,
            StartedAtUtc: DateTimeOffset.UtcNow,
            Workdir: TestInfrastructure.WorkingCopyRoot);

        lock (_createdDatabasesLock) _metadataByDatabase[name] = metadata;

        await WriteCommentAsync(connection, name, metadata, cancellationToken);
    }

    /// <summary>T9 review (M3): records which test class a class database belongs to, so a stuck
    /// <c>sbtest_&lt;key&gt;_c07</c> found by <c>status</c>/<c>sweep</c> can be traced back to its owner —
    /// the promise ARCHITECTURE_CYCLE8_PHASE2.md §91.3/§92.2 and the schema's <c>testClass</c> field both
    /// already made, but that nothing previously implemented (every call site passed <c>TestClass: null</c>).
    /// Called a SECOND time, after <see cref="CreateClassDatabaseAsync"/> already created and commented the
    /// database — the class name is only knowable once a test-base constructor first runs (§92.2: xUnit v2
    /// never hands a class fixture its own class' <see cref="Type"/>). A no-op if the database was already
    /// dropped (class finished, or InitializeAsync failed) before any test constructed — nothing left to
    /// annotate, and re-creating a comment for a database that no longer exists would just fail.</summary>
    public async Task RecordTestClassAsync(string classSlot, string testClassName, CancellationToken cancellationToken = default)
    {
        var name = DatabaseNameFor(classSlot);

        ResourceLabels.DatabaseMetadata? metadata;
        lock (_createdDatabasesLock) _metadataByDatabase.TryGetValue(name, out metadata);
        if (metadata is null)
            return;

        var updated = metadata with { ClassName = testClassName };
        lock (_createdDatabasesLock) _metadataByDatabase[name] = updated;

        await using var connection = new NpgsqlConnection(_server.MaintenanceConnectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            await WriteCommentAsync(connection, name, updated, cancellationToken);
        }
        catch (PostgresException)
        {
            // Best-effort diagnostics: the class database can legitimately be gone by the time this
            // fires (a fast-finishing class racing its own teardown) — losing the testClass annotation
            // must never fail the test run itself.
        }
    }

    private static async Task WriteCommentAsync(NpgsqlConnection connection, string databaseName,
        ResourceLabels.DatabaseMetadata metadata, CancellationToken cancellationToken)
    {
        var commentJson = ResourceLabels.ToComment(metadata).Replace("'", "''");
        await using var commentCommand = new NpgsqlCommand($"COMMENT ON DATABASE \"{databaseName}\" IS '{commentJson}'", connection);
        await commentCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Drops every database this lease still tracks as created (normally just the template — every class
    /// database is dropped individually by <see cref="DropClassDatabaseAsync"/> as its class finishes),
    /// guarded by <see cref="TestDatabaseNaming.EnsureOwnedByThisRun"/> on each one (§69.3, §70.1). Only
    /// used in <see cref="TestServerMode.External"/> mode — in container mode, disposing the container
    /// throws the databases away for free.
    /// </summary>
    public async Task DropAllAsync(CancellationToken cancellationToken = default)
    {
        string[] snapshot;
        lock (_createdDatabasesLock) snapshot = [.. _createdDatabases];
        if (snapshot.Length == 0)
            return;

        await using var connection = new NpgsqlConnection(_server.MaintenanceConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Drop leaf databases before the template they were copied from.
        foreach (var name in snapshot.Where(n => !n.EndsWith($"_{TemplateSlot}", StringComparison.Ordinal)))
            await DropAsync(connection, name, cancellationToken);

        foreach (var name in snapshot.Where(n => n.EndsWith($"_{TemplateSlot}", StringComparison.Ordinal)))
            await DropAsync(connection, name, cancellationToken);

        lock (_createdDatabasesLock) _createdDatabases.Clear();
    }

    /// <summary>Drops a database belonging to <em>this</em> run (the run whose in-process
    /// <see cref="TestRunKey.Current"/> matches the run-key segment of the name). Used by test teardown,
    /// never by the sweeper — see <see cref="DropLeakedAsync"/> for the cross-process case.</summary>
    public static async Task DropAsync(NpgsqlConnection connection, string databaseName, CancellationToken cancellationToken = default)
    {
        TestDatabaseNaming.EnsureOwnedByThisRun(databaseName);
        await DropUncheckedAsync(connection, databaseName, cancellationToken);
    }

    /// <summary>
    /// Drops a database left behind by a DIFFERENT run. This is the only place in the repository allowed
    /// to drop a database whose run-key does not match <see cref="TestRunKey.Current"/> — the sweeper
    /// (<see cref="Sweeper"/>) is, by definition, a separate process cleaning up after other processes, so
    /// <see cref="TestDatabaseNaming.EnsureOwnedByThisRun"/> can never pass for it (see review finding
    /// blocker #1: sweep --apply could not delete anything). Safety here comes from two things instead:
    /// the caller (Sweeper) verifies <see cref="TestDatabaseNaming.IsDisposable"/> AND applies its own dead/
    /// alive/undetermined classification before calling this, and this method re-asserts the name is at
    /// least formally disposable so nothing outside the sbtest_&lt;key&gt;_&lt;slot&gt; namespace can ever
    /// reach DROP DATABASE.
    /// </summary>
    public static async Task DropLeakedAsync(NpgsqlConnection connection, string databaseName, CancellationToken cancellationToken = default)
    {
        TestDatabaseNaming.EnsureDisposable(databaseName);
        await DropUncheckedAsync(connection, databaseName, cancellationToken);
    }

    private static async Task DropUncheckedAsync(NpgsqlConnection connection, string databaseName, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
