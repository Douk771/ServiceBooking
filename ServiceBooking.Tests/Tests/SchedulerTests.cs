using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.ClientNotes;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// US-21. The background timer loop itself (ScheduledTaskRunner.ExecuteAsync) is disabled in the
/// Testing environment (appsettings.Testing.json, ARCHITECTURE.md §8.5) so it never races the 300+
/// other functional tests sharing this database. These tests instead exercise the two pieces that
/// matter functionally on their own: the registered IScheduledTask resolved and run directly (the exact
/// same call the runner would make), and the admin liveness endpoint reading whatever state is in the
/// database. The pure "is it due / is it overdue" arithmetic itself is covered by
/// ScheduledTaskScheduleTests (ServiceBooking.UnitTests).
/// </summary>
public class SchedulerTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact, TestCase("SCH-001")]
    public async Task PhotoRetentionCleanupTask_DeletesExpiredPhotoAndItsFiles()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        // SixMonths retention (the plan default) — a photo backdated past the cutoff must be swept.
        await SetSubscriptionAsync(company.Id);

        var addNote = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, null, "+79990001111", Unique("Note ")));
        var noteId = (await addNote.Content.ReadJsonAsync<ClientNoteDto>())!.Id;

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImages.SolidJpeg());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "photo.jpg");
        var upload = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", content);
        var photo = (await upload.Content.ReadJsonAsync<ClientNotePhotoDto>())!;

        string storagePath, thumbnailPath;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.ClientNotePhotos.SingleAsync(p => p.Id == photo.Id);
            row.CreatedAt = DateTime.UtcNow.AddMonths(-7); // past the SixMonths cutoff
            storagePath = row.StoragePath;
            thumbnailPath = row.ThumbnailPath;
            await db.SaveChangesAsync();

            var storage = scope.ServiceProvider.GetRequiredService<FileStorage>();
            using (storage.OpenPrivate(storagePath)) { } // sanity: file exists before the sweep
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "photo-retention-cleanup");
            var outcome = await task.ExecuteAsync(CancellationToken.None);
            outcome.Affected.Should().BeGreaterThanOrEqualTo(1);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ClientNotePhotos.FindAsync(photo.Id)).Should().BeNull();

            var storage = scope.ServiceProvider.GetRequiredService<FileStorage>();
            var actFull = () => storage.OpenPrivate(storagePath);
            actFull.Should().Throw<FileNotFoundException>();
            var actThumb = () => storage.OpenPrivate(thumbnailPath);
            actThumb.Should().Throw<FileNotFoundException>();
        }
    }

    [Fact, TestCase("SCH-002")]
    public async Task PhotoRetentionCleanupTask_ForeverRetention_LeavesOldPhotoUntouched()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var foreverPlan = await CreateTestPlanConfigAsync(photoRetention: PhotoRetention.Forever);
        await SetSubscriptionAsync(company.Id, planConfigId: foreverPlan);

        var addNote = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, null, "+79990002222", Unique("Note ")));
        var noteId = (await addNote.Content.ReadJsonAsync<ClientNoteDto>())!.Id;

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImages.SolidJpeg());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "photo.jpg");
        var upload = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", content);
        var photo = (await upload.Content.ReadJsonAsync<ClientNotePhotoDto>())!;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.ClientNotePhotos.SingleAsync(p => p.Id == photo.Id);
            row.CreatedAt = DateTime.UtcNow.AddYears(-5);
            await db.SaveChangesAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "photo-retention-cleanup");
            await task.ExecuteAsync(CancellationToken.None);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ClientNotePhotos.FindAsync(photo.Id)).Should().NotBeNull();
        }
    }

    // ── SCH-008: orphan-file sweep (code review finding — QA reproduced a deterministic crash here) ──

    [Fact, TestCase("SCH-008")]
    public async Task PhotoRetentionCleanupTask_OrphanFileOlderThanTheMinAge_IsDeletedAndTheRunSucceeds()
    {
        // Reproduces the exact QA scenario: a file under {PrivateRoot}/{companyId}/... with NO matching
        // ClientNotePhoto row (the documented "worst case" of a crash between writing the file and
        // committing its row, or a delete that removed the row but not one of its two files —
        // ARCHITECTURE.md §4.1 steps 10-11) and an mtime older than OrphanMinAge (24h). Before the fix,
        // DeleteOrphanFilesAsync's `SelectMany(p => new[] { p.StoragePath, p.ThumbnailPath })` inside the
        // LINQ-to-SQL query threw InvalidOperationException the moment ANY orphan candidate existed on
        // disk anywhere — every single run of this task failed, regardless of company.
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        string orphanKey;
        string orphanFullPath;
        using (var scope = Factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<FileStorage>();
            // Written directly via FileStorage, deliberately bypassing the upload endpoint (and so never
            // getting a ClientNotePhoto row) — exactly what makes this file an orphan.
            orphanKey = await storage.SavePrivateAsync(company.Id, TestImages.SolidJpeg(), ".jpg");
            orphanFullPath = Path.Combine(storage.PrivateRootFullPath, orphanKey.Replace('/', Path.DirectorySeparatorChar));
            File.SetLastWriteTimeUtc(orphanFullPath, DateTime.UtcNow.AddHours(-25)); // past the 24h OrphanMinAge
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "photo-retention-cleanup");

            // The bug under test made this call throw for every run once any orphan existed — the run
            // must now complete and report the orphan as affected, not fail the whole task (SPEC US-20
            // p.6, US-21 pp.8-9: a stray file must not make the task report itself unhealthy forever).
            var outcome = await task.ExecuteAsync(CancellationToken.None);
            outcome.Affected.Should().BeGreaterThanOrEqualTo(1);
        }

        File.Exists(orphanFullPath).Should().BeFalse("the orphan file must actually be removed from disk");
    }

    [Fact, TestCase("SCH-009")]
    public async Task PhotoRetentionCleanupTask_OrphanFileYoungerThanTheMinAge_IsLeftAlone()
    {
        // Guards risk R3 (ARCHITECTURE.md §4.1): a file the upload pipeline just wrote, whose row hasn't
        // committed yet, must not be swept out from under a request that is still in flight. A file with
        // no matching row but a FRESH mtime is indistinguishable from that in-flight case, so it must
        // survive this run.
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        string freshOrphanFullPath;
        using (var scope = Factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<FileStorage>();
            var freshOrphanKey = await storage.SavePrivateAsync(company.Id, TestImages.SolidJpeg(), ".jpg");
            freshOrphanFullPath = Path.Combine(storage.PrivateRootFullPath, freshOrphanKey.Replace('/', Path.DirectorySeparatorChar));
            // Freshly written by SavePrivateAsync above — mtime is "now", well inside OrphanMinAge.
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "photo-retention-cleanup");
            await task.ExecuteAsync(CancellationToken.None);
        }

        File.Exists(freshOrphanFullPath).Should().BeTrue("a file younger than OrphanMinAge might still be mid-upload");
    }

    [Fact, TestCase("SCH-003")]
    public async Task TryAcquireAsync_SecondSessionSkipsWhileFirstHoldsTheLock()
    {
        var key = Unique("scheduled-task:test-lock-");

        using var holderScope = Factory.Services.CreateScope();
        var holderDb = holderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var holderTx = await holderDb.Database.BeginTransactionAsync();
        (await AdvisoryLock.TryAcquireAsync(holderDb, key)).Should().BeTrue();

        using var contenderScope = Factory.Services.CreateScope();
        var contenderDb = contenderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var contenderTx = await contenderDb.Database.BeginTransactionAsync();
        // A second, independent connection/transaction must NOT block — it gets false immediately
        // (ARCHITECTURE.md §8.4), which is exactly what lets the scheduler skip instead of queuing.
        (await AdvisoryLock.TryAcquireAsync(contenderDb, key)).Should().BeFalse();

        await contenderTx.RollbackAsync();
        await holderTx.RollbackAsync();
    }

    // ── GET /api/admin/scheduled-tasks ──────────────────────────────────────

    [Fact, TestCase("SCH-004")]
    public async Task GetScheduledTasks_ByNonAdmin_ReturnsForbidden()
    {
        var user = await RegisterAsync();

        var response = await AuthedClient(user.Token).GetAsync("/api/admin/scheduled-tasks");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("SCH-005")]
    public async Task GetScheduledTasks_ByAdmin_ListsThePhotoRetentionTaskWithItsLastRun()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var state = await db.ScheduledTaskStates.FindAsync("photo-retention-cleanup");
            if (state is null)
            {
                state = new ScheduledTaskState { Name = "photo-retention-cleanup" };
                db.ScheduledTaskStates.Add(state);
            }
            state.LastStartedAtUtc = DateTime.UtcNow.AddMinutes(-5);
            state.LastFinishedAtUtc = DateTime.UtcNow.AddMinutes(-4);
            state.LastSucceeded = true;
            state.LastDurationMs = 1234;
            state.LastSummary = "scanned 10, deleted 2, freed 1.0 MB";
            await db.SaveChangesAsync();
        }

        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync("/api/admin/scheduled-tasks");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tasks = await response.Content.ReadJsonAsync<List<ScheduledTaskStatusDto>>();
        var entry = tasks.Should().ContainSingle(t => t.Name == "photo-retention-cleanup").Which;
        entry.LastSucceeded.Should().BeTrue();
        entry.LastSummary.Should().Contain("deleted 2");
        entry.IsOverdue.Should().BeFalse();
    }

    [Fact, TestCase("SCH-006")]
    public async Task GetScheduledTasks_NeverRun_IsMarkedOverdue()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var existing = await db.ScheduledTaskStates.FindAsync("photo-retention-cleanup");
            if (existing is not null) db.ScheduledTaskStates.Remove(existing);
            await db.SaveChangesAsync();
        }

        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync("/api/admin/scheduled-tasks");

        var tasks = await response.Content.ReadJsonAsync<List<ScheduledTaskStatusDto>>();
        var entry = tasks.Should().ContainSingle(t => t.Name == "photo-retention-cleanup").Which;
        entry.LastStartedAt.Should().BeNull();
        entry.IsOverdue.Should().BeTrue();
    }

    // ── SCH-007: a failing task must not take the runner (or its neighbours) down with it ──

    /// <summary>Always throws — used only by SCH-007 to prove ScheduledTaskRunner's per-task try/catch
    /// (US-21 p.3) actually holds, rather than trusting the code by inspection.</summary>
    private sealed class ThrowingScheduledTask : IScheduledTask
    {
        public const string TaskName = "test-always-throws";
        public string Name => TaskName;
        public TimeSpan DefaultPeriod => TimeSpan.FromDays(1);

        public Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct) =>
            throw new InvalidOperationException("SCH-007: deliberate failure to prove neighbours survive it");
    }

    [Fact, TestCase("SCH-007")]
    public async Task FailingTask_IsMarkedFailed_ButDoesNotStopTheHostOrItsNeighbour()
    {
        // Isolated per-test directory (code review finding, round 3): this test lets the REAL
        // photo-retention-cleanup task run for up to 20s against a live BackgroundService, and that task
        // deletes files it finds under Storage:PrivateRoot. Left pointed at the shared default root, it
        // would compete with — and possibly delete files out from under — every other test in this suite
        // that uploads a client-note photo, whether they run before, during or after this one. A fresh
        // temp directory (guaranteed empty except for whatever this test itself seeds) makes the cleanup
        // run's behaviour observable without any such cross-test interference.
        var isolatedPrivateRoot = Path.Combine(Path.GetTempPath(), "sb-sch007-private-" + Guid.NewGuid());
        Directory.CreateDirectory(isolatedPrivateRoot);
        try
        {
            // Builds a SEPARATE host from the shared one the rest of this suite uses: the real
            // ScheduledTaskRunner background loop is enabled (appsettings.Testing.json turns it off by
            // default — ARCHITECTURE.md §8.5) and ticks fast, with one extra IScheduledTask registered
            // alongside the real photo-retention-cleanup task. Still points at the same test database.
            using var throttledFactory = Factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ScheduledTasks:Enabled", "true");
                builder.UseSetting("ScheduledTasks:TickSeconds", "1");
                builder.UseSetting("Storage:PrivateRoot", isolatedPrivateRoot);
                builder.ConfigureServices(services => services.AddScoped<IScheduledTask, ThrowingScheduledTask>());
            });

            // Make sure both tasks are actually due on this fresh host: an earlier test run against the
            // same database may have left a recent LastStartedAtUtc for photo-retention-cleanup.
            using (var scope = throttledFactory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                foreach (var name in new[] { "photo-retention-cleanup", ThrowingScheduledTask.TaskName })
                {
                    var state = await db.ScheduledTaskStates.FindAsync(name);
                    if (state is not null) db.ScheduledTaskStates.Remove(state);
                }
                await db.SaveChangesAsync();
            }

            // Starts the real BackgroundService for this host.
            using var client = throttledFactory.CreateClient();

            ScheduledTaskState? throwingState = null;
            ScheduledTaskState? cleanupState = null;
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                using var scope = throttledFactory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                throwingState = await db.ScheduledTaskStates.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Name == ThrowingScheduledTask.TaskName);
                cleanupState = await db.ScheduledTaskStates.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Name == "photo-retention-cleanup");

                if (throwingState?.LastFinishedAtUtc is not null && cleanupState?.LastFinishedAtUtc is not null)
                    break;

                await Task.Delay(300);
            }

            // The throwing task recorded its own failure...
            throwingState.Should().NotBeNull("the runner should have attempted the throwing task within the deadline");
            throwingState!.LastSucceeded.Should().BeFalse();
            throwingState.LastError.Should().Contain("InvalidOperationException");

            // ...but its neighbour still ran and succeeded in the same tick cycle (US-21 p.3: one task's
            // exception is caught per-task, not around the whole tick).
            cleanupState.Should().NotBeNull("photo-retention-cleanup must still run after its neighbour throws");
            cleanupState!.LastSucceeded.Should().BeTrue();

            // And the host process itself is still alive and serving requests.
            var stillAlive = await client.GetAsync("/api/companies");
            stillAlive.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            if (Directory.Exists(isolatedPrivateRoot)) Directory.Delete(isolatedPrivateRoot, recursive: true);
        }
    }
}
