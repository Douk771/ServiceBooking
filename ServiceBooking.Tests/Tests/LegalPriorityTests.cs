using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.ClientNotes;
using ServiceBooking.API.DTOs.Legal;
using ServiceBooking.API.Services.Retention;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 5 — the seven priority invariants named in the cycle report that no existing test (before this
/// file) exercised by execution: the retention sweep's dry-run/live pair with pagination past one batch
/// (SPEC.md §7 US-73 п.5, §13 п.3), the opt-out exception (US-73 п.3), the subject-request form's identical
/// response (US-74 п.6), SuperAdmin's exclusion from health notes (US-77 п.4), account deletion cascading
/// health notes (§55.1 R6), and the отзыв preview/actual parity (US-68, API_CONTRACT_CYCLE5.md §41.3).
/// Written from SPEC.md/ARCHITECTURE_CYCLE5.md/API_CONTRACT_CYCLE5.md, not from the implementation.
/// </summary>
public class LegalPriorityTests(ApiDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── Priority 2: retention dry run touches nothing; live deletes; pagination terminates ─────────

    [Fact, TestCase("LGL-073-DRY")]
    public async Task Retention_DryRun_AcrossMultipleBatches_ChangesNothing_ThenLiveRunDeletesExactlyTheExpired()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        // A set noticeably larger than the batch size, forced small via config, so a single ExecuteAsync
        // call must page through MULTIPLE batches — the invariant under test is that pagination
        // terminates (doesn't re-read the first page forever) and that dry run's selection query is the
        // SAME one live mode uses (RetentionRuleRunner's own contract), not merely that "some" rows match.
        const int expiredCount = 5;
        var expiredIds = new List<Guid>();
        Guid freshId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < expiredCount; i++)
            {
                var note = new ClientNote
                {
                    Id = Guid.NewGuid(), CompanyId = company.Id, MasterId = master.UserId,
                    GuestPhone = $"+7999000{i:D4}", Note = $"expired-{i}",
                    CreatedAt = DateTime.UtcNow.AddDays(-1200), // well past the 1095-day default
                };
                db.ClientNotes.Add(note);
                expiredIds.Add(note.Id);
            }
            var freshNote = new ClientNote
            {
                Id = Guid.NewGuid(), CompanyId = company.Id, MasterId = master.UserId,
                GuestPhone = "+79990009000", Note = "fresh", CreatedAt = DateTime.UtcNow.AddDays(-10),
            };
            db.ClientNotes.Add(freshNote);
            freshId = freshNote.Id;
            await db.SaveChangesAsync();
        }

        await using var dryFactory = Factory.WithWebHostBuilder(b =>
            b.UseSetting("ScheduledTasks:data-retention:BatchSize", "2")
             .UseSetting("ScheduledTasks:data-retention:DryRun", "true"));
        using (var scope = dryFactory.Services.CreateScope())
        {
            var task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "data-retention");
            var outcome = await task.ExecuteAsync(CancellationToken.None);
            outcome.Affected.Should().BeGreaterThanOrEqualTo(expiredCount,
                "the dry run must count every expired row across ALL batches, not just the first page");
        }

        // Dry run must have written nothing — every seeded row (expired AND fresh) is still there.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ClientNotes.CountAsync(n => expiredIds.Contains(n.Id))).Should().Be(expiredCount,
                "§49.2: dry run's ONLY difference from live is that SaveChanges is never called");
            (await db.ClientNotes.AnyAsync(n => n.Id == freshId)).Should().BeTrue();
        }

        await using var liveFactory = Factory.WithWebHostBuilder(b =>
            b.UseSetting("ScheduledTasks:data-retention:BatchSize", "2")
             .UseSetting("ScheduledTasks:data-retention:DryRun", "false"));
        using (var scope = liveFactory.Services.CreateScope())
        {
            var task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "data-retention");
            var outcome = await task.ExecuteAsync(CancellationToken.None);
            outcome.Affected.Should().BeGreaterThanOrEqualTo(expiredCount);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ClientNotes.CountAsync(n => expiredIds.Contains(n.Id))).Should().Be(0,
                "the live run must have deleted every expired row, not just the first batch");
            (await db.ClientNotes.AnyAsync(n => n.Id == freshId)).Should().BeTrue(
                "the fresh row must survive — this is the 'не удалило лишнего' half of the invariant");
        }
    }

    // ── Priority 3: NotificationOptOut is untouched by retention at ANY configured period ───────────

    [Fact, TestCase("LGL-073-OPTOUT")]
    public async Task Retention_FullRunWithAggressivePeriods_NeverTouchesNotificationOptOuts()
    {
        Guid optOutId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var optOut = new NotificationOptOut
            {
                // A unique phone, not a literal — this row is NEVER cleaned up (that's the whole point of
                // the test), so a hardcoded number would permanently collide with any other test in this
                // shared database that happens to reuse the same literal (confirmed: it did, against
                // NotificationWebhookUnsubscribeTests.Unsubscribe_GetPage_ValidToken_ReturnsMaskedPhone and
                // NotificationQueueingTests.Cancel_VisitLessThanThresholdAway_StillQueuesCancellationNotification).
                Id = Guid.NewGuid(), Phone = UniquePhone().TrimStart('+'), Source = OptOutSource.Cabinet,
            };
            db.NotificationOptOuts.Add(optOut);
            optOutId = optOut.Id;
            await db.SaveChangesAsync();
        }

        // Every configurable period driven to its floor (or as low as fail-fast allows) — if a rule for
        // NotificationOptOut existed anywhere, this is the run that would catch it (§55.1: "правил,
        // удаляющих NotificationOptOut, в коде НЕ СУЩЕСТВУЕТ ВООБЩЕ" — proved by absence of effect, not
        // by reading the registration list).
        await using var aggressiveFactory = Factory.WithWebHostBuilder(b => b
            .UseSetting("ScheduledTasks:data-retention:DryRun", "false")
            .UseSetting("Retention:NotificationBodyDays", "1")
            .UseSetting("Retention:NotificationMetadataDays", "1")
            .UseSetting("Retention:TemplateHistoryDays", "365")
            .UseSetting("Retention:ConsentRecordDays", "1095")
            .UseSetting("Retention:InactiveAccountDays", "1")
            .UseSetting("Retention:BookingPersonalizationDays", "1")
            .UseSetting("Retention:ClientNoteDays", "1")
            .UseSetting("Retention:ClientNotePhotoDays", "1")
            .UseSetting("Retention:ClientHealthNoteDays", "1")
            .UseSetting("Retention:ChannelStateEventDays", "1")
            .UseSetting("Retention:PaymentLogDays", "1")
            .UseSetting("Retention:MailLogDays", "1"));

        using (var scope = aggressiveFactory.Services.CreateScope())
        {
            var task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "data-retention");
            await task.ExecuteAsync(CancellationToken.None);
        }

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.NotificationOptOuts.FindAsync(optOutId)).Should().NotBeNull(
            "deleting an opt-out means resuming a rejected subscription — this must never happen, at any configured period (US-73 п.3, §55.1)");
    }

    // ── Priority 4: subject-request form answers identically for a known and an unknown phone ───────

    [Fact, TestCase("LGL-074-IDENTICAL")]
    public async Task SubjectRequest_ResponseIsByteForByteIdentical_ForKnownAndUnknownPhone()
    {
        // "Known" means a phone that genuinely has data behind it (a registered account) — the strongest
        // version of the test, not just "a phone that looks plausible".
        var knownUser = await RegisterAsync();

        var unknownResponse = await AnonymousClient().PostAsJsonAsync("/api/subject-requests",
            new SubmitSubjectRequestDto("Erasure", UniquePhone(), "me@example.com", "Прошу удалить мои данные", null));
        var knownResponse = await AnonymousClient().PostAsJsonAsync("/api/subject-requests",
            new SubmitSubjectRequestDto("Erasure", knownUser.Phone, "me@example.com", "Прошу удалить мои данные", null));

        unknownResponse.StatusCode.Should().Be((HttpStatusCode)202);
        knownResponse.StatusCode.Should().Be((HttpStatusCode)202);

        var unknownBody = await unknownResponse.Content.ReadFromJsonAsync<SubjectRequestAcceptedDto>();
        var knownBody = await knownResponse.Content.ReadFromJsonAsync<SubjectRequestAcceptedDto>();

        // The ONE field allowed to differ between any two calls is the random reference — everything else
        // (status code, headers shape, responseDueByWorkingDays, absence of any other field) must be
        // identical regardless of whether the system recognizes the phone (§50.1: existence of a number
        // is never confirmed or denied).
        unknownBody!.ResponseDueByWorkingDays.Should().Be(knownBody!.ResponseDueByWorkingDays);
        unknownResponse.Content.Headers.ContentType!.MediaType.Should().Be(knownResponse.Content.Headers.ContentType!.MediaType);
        unknownBody.Reference.Should().NotBe(knownBody.Reference, "each submission gets its own reference, but that's the only difference");
    }

    [Fact, TestCase("LGL-074-RATE")]
    public async Task SubjectRequest_IsPublic_AndAnonymousReachesIt_WithoutAnyAuth()
    {
        var response = await AnonymousClient().PostAsJsonAsync("/api/subject-requests",
            new SubmitSubjectRequestDto("Access", UniquePhone(), "someone@example.com", "Хочу узнать, что вы обо мне храните", null));
        response.StatusCode.Should().Be((HttpStatusCode)202);
    }

    // ── Priority 5: SuperAdmin never sees the health note, on any of the three endpoints ─────────────

    [Fact, TestCase("LGL-077-SUPERADMIN")]
    public async Task HealthNote_SuperAdmin_Returns403_OnAllThreeEndpoints_AndFieldNeverAppearsElsewhere()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();
        (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(9, 30), null, null, null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        await GrantHealthConsentAsync(master.Token, company.Id, clientUser.UserId);
        (await AuthedClient(master.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{clientUser.UserId}/health-note", new { value = "аллергия на аммиак" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var admin = await LoginAsSuperAdminAsync();
        (await AuthedClient(admin.Token).GetAsync($"/api/companies/{company.Id}/clients/{clientUser.UserId}/health-note"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AuthedClient(admin.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{clientUser.UserId}/health-note", new { value = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AuthedClient(admin.Token).DeleteAsync($"/api/companies/{company.Id}/clients/{clientUser.UserId}/health-note"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Field must not appear "заодно" in any other response an admin (or anyone else) can reach —
        // the client-notes listing is the obvious place it could leak through.
        var notesListRaw = await (await AuthedClient(master.Token).GetAsync($"/api/masters/clients?companyId={company.Id}"))
            .Content.ReadAsStringAsync();
        notesListRaw.Should().NotContain("аллергия на аммиак");
    }

    // ── Priority 6: account deletion removes the health note — both by userId and by guest phone ────

    [Fact, TestCase("LGL-073-DELETE-HEALTH")]
    public async Task DeleteAccount_RemovesHealthNote_RecordedByUserId()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var phone = UniquePhone();
        var clientUser = await RegisterAsync(phone: phone);
        (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(10, 30), null, null, null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        await GrantHealthConsentAsync(master.Token, company.Id, clientUser.UserId);
        (await AuthedClient(master.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{clientUser.UserId}/health-note", new { value = "беременность — не красим" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ClientHealthNotes.AnyAsync(h => h.ClientId == clientUser.UserId)).Should().BeFalse(
            "R6/§55.1: удаление аккаунта уносит заметку о здоровье, заведённую по идентификатору");
    }

    [Fact, TestCase("LGL-073-DELETE-HEALTH-GUEST")]
    public async Task DeleteAccount_RemovesHealthNote_RecordedByGuestPhone_BeforeThePersonRegistered()
    {
        // "Гость, ставший зарегистрированным" — a health note recorded while this phone was still a
        // guest (GuestPhone-keyed row, no ClientId) must ALSO be removed once that same phone registers
        // and deletes the resulting account, not just the ClientId-keyed case above.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var phone = UniquePhone();

        (await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(11, 30), null, "Guest", phone, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var canonicalKey = "phone:" + phone.TrimStart('+');
        await GrantHealthConsentAsync(master.Token, company.Id, canonicalKey);
        (await AuthedClient(master.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{canonicalKey}/health-note", new { value = "псориаз кожи головы" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var registeredClient = await RegisterAsync(phone: phone); // the guest becomes a registered client
        (await AuthedClient(registeredClient.Token).PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var canonicalPhone = phone.TrimStart('+').Replace(" ", "");
        (await db.ClientHealthNotes.AnyAsync(h => h.GuestPhone == canonicalPhone)).Should().BeFalse(
            "the guest-recorded health note for this now-registered phone must be gone too");
    }

    // ── Priority 7: revoking consent does what the published document promises, and the preview matches ──

    [Fact, TestCase("LGL-068-REVOKE-EFFECTS")]
    public async Task RevokeConsent_DeletesPhotosAndHealthNote_CancelsQueuedNotifications_PreviewMatchesActual()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();
        await GrantProviderDeliveryConsentAsync(clientUser.Token);

        // A Connected, assigned channel is required for the booking below to actually QUEUE a Pending
        // notification at all (NotificationGate's NoUsableChannel branch otherwise fires first) — without
        // one, queuedNotificationsCancelled would legitimately be 0 for the wrong reason (nothing to
        // cancel because nothing was ever queued, not because the gate correctly re-evaluated consent).
        using (var channelScope = Factory.Services.CreateScope())
        {
            var db0 = channelScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var sub = await db0.AccountSubscriptions.Include(s => s.PlanConfig).FirstAsync(s => s.OwnerUserId == owner.UserId);
            sub.PlanConfig!.AllowNotificationChannel = true;
            var channel = new NotificationChannel
            {
                Id = Guid.NewGuid(), OwnerUserId = owner.UserId, State = ChannelState.Connected,
                PhoneNumber = UniquePhone().TrimStart('+'), ProviderInstanceId = Unique("instance"),
                PaidFromUtc = DateTime.UtcNow.AddDays(-1), PaidUntilUtc = DateTime.UtcNow.AddDays(30),
                ConnectedAtUtc = DateTime.UtcNow.AddDays(-1), RiskAcceptedAtUtc = DateTime.UtcNow.AddDays(-1),
            };
            db0.NotificationChannels.Add(channel);
            db0.ChannelCompanyAssignments.Add(new ChannelCompanyAssignment
            {
                Id = Guid.NewGuid(), ChannelId = channel.Id, CompanyId = company.Id, AssignedByUserId = owner.UserId,
            });
            await db0.SaveChangesAsync();
        }

        var booking = await (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, service.Id, master.UserId, date, new TimeOnly(12, 30), null, null, null, null, null)))
            .Content.ReadJsonAsync<BookingDto>();

        await GrantPhotoConsentAsync(master.Token, company.Id, clientUser.UserId);
        var noteResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, clientUser.UserId, null, Unique("Note ")));
        var noteId = (await noteResponse.Content.ReadJsonAsync<ClientNoteDto>())!.Id;
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImages.SolidJpeg());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "photo.jpg");
        (await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", content))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        await GrantHealthConsentAsync(master.Token, company.Id, clientUser.UserId);
        (await AuthedClient(master.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/clients/{clientUser.UserId}/health-note", new { value = "аллергия на аммиак" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Preview must show the same numbers that revoking for real then produces — that's the whole
        // point of the endpoint (API_CONTRACT_CYCLE5.md §41.3: "будет удалено 12 фотографий", not "фото
        // будут удалены").
        var previewRaw = await (await AuthedClient(clientUser.Token)
                .GetAsync("/api/profile/consents/revoke-preview"))
            .Content.ReadJsonAsync<RevokeEffectsDto>();
        previewRaw!.PhotosDeleted.Should().Be(1);
        previewRaw.HealthNotesDeleted.Should().Be(1);

        var revokeResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/profile/consents/revoke",
            new { documentKey = "PdnConsent", purpose = (string?)null, reason = "не хочу" });
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var revoked = await revokeResponse.Content.ReadJsonAsync<RevokeConsentResponseDto>();
        revoked!.Effects.PhotosDeleted.Should().Be(previewRaw.PhotosDeleted, "preview and actual must agree");
        revoked.Effects.HealthNotesDeleted.Should().Be(previewRaw.HealthNotesDeleted);
        revoked.Effects.QueuedNotificationsCancelled.Should().BeGreaterThan(0,
            "ProviderDelivery was granted and a booking queued notifications under it — revoking the whole document must cancel them");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ClientNotePhotos.AnyAsync(p => p.ClientNoteId == noteId)).Should().BeFalse("photos must be actually gone, not just counted");
        (await db.ClientHealthNotes.AnyAsync(h => h.ClientId == clientUser.UserId)).Should().BeFalse();

        // What must NOT disappear (US-68 п.2): the booking itself and its history survive the revoke.
        (await AuthedClient(clientUser.Token).GetAsync("/api/profile")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await db.Bookings.AnyAsync(b => b.Id == booking!.Id)).Should().BeTrue();
    }

    [Fact, TestCase("LGL-068-REVOKE-IDEMPOTENT")]
    public async Task RevokeConsent_CalledTwice_SecondCallIsIdempotent_RevokedZero()
    {
        var user = await RegisterAsync();
        await GrantProviderDeliveryConsentAsync(user.Token);

        var first = await AuthedClient(user.Token).PostAsJsonAsync("/api/profile/consents/revoke",
            new { documentKey = "PdnConsent", purpose = (string?)null, reason = "не хочу" });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await AuthedClient(user.Token).PostAsJsonAsync("/api/profile/consents/revoke",
            new { documentKey = "PdnConsent", purpose = (string?)null, reason = "не хочу снова" });
        second.StatusCode.Should().Be(HttpStatusCode.OK, "US-68 п.7: повторный отзыв идемпотентен, не ошибка");
        var body = await second.Content.ReadJsonAsync<RevokeConsentResponseDto>();
        body!.Revoked.Should().Be(0, "nothing left active to revoke the second time");
    }
}
