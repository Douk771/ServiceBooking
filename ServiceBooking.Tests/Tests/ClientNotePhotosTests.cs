using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.ClientNotes;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

public class ClientNotePhotosTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    private static MultipartFormDataContent JpegUpload(byte[]? bytes = null, string fileName = "photo.jpg")
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes ?? TestImages.SolidJpeg());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    private async Task<(ServiceBooking.API.DTOs.Auth.AuthResponseDto Owner, CompanyDto Company, ServiceBooking.API.DTOs.Auth.AuthResponseDto Master, Guid NoteId)>
        SetUpNoteAsync()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);

        // A note about a GUEST with no booking behind it is invisible in GET /api/masters/clients
        // (that endpoint only ever surfaces clients derived from bookings) — a real client + booking is
        // needed so MC-101's "photo shows up in the clients list" assertion has something to find.
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();
        var booking = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new ServiceBooking.API.DTOs.Bookings.CreateBookingDto(
                company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        booking.StatusCode.Should().Be(HttpStatusCode.Created);

        var addResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, clientUser.UserId, null, Unique("Note ")));
        addResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var note = (await addResponse.Content.ReadJsonAsync<ClientNoteDto>())!;

        // CYCLE5-BREAKING (API_CONTRACT_CYCLE5.md §44.3, US-76): photo upload now 400s without a prior,
        // staff-confirmed photo consent for this client+company pair — grant it here so every test in
        // this file that isn't ITSELF testing that precondition (MC-1xx/2xx/3xx below) doesn't have to.
        await GrantPhotoConsentAsync(master.Token, company.Id, clientUser.UserId);

        return (owner, company, master, note.Id);
    }

    // ── POST /api/client-notes/{noteId}/photos ──────────────────────────────

    [Fact, TestCase("MC-101")]
    public async Task Upload_ByStaffOfTheNotesCompany_ReturnsCreatedAndPhotoAppearsInClientsList()
    {
        var (owner, company, master, noteId) = await SetUpNoteAsync();

        var response = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var photo = (await response.Content.ReadJsonAsync<ClientNotePhotoDto>())!;
        photo.Width.Should().BeGreaterThan(0);
        photo.SizeBytes.Should().BeGreaterThan(0);
        photo.CanDelete.Should().BeTrue();

        var listResponse = await AuthedClient(master.Token).GetAsync($"/api/masters/clients?companyId={company.Id}");
        var entriesPage = await listResponse.Content.ReadJsonAsync<PagedResult<MasterClientDto>>();
        var note = entriesPage!.Items.SelectMany(e => e.Notes).Should().ContainSingle(n => n.Id == noteId).Which;
        note.Photos.Should().ContainSingle(p => p.Id == photo.Id);
    }

    [Fact, TestCase("MC-102")]
    public async Task Upload_ByStaffOfAnotherCompany_ReturnsForbidden()
    {
        var (_, _, _, noteId) = await SetUpNoteAsync();
        var (strangerOwner, _) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(strangerOwner.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // LGL-076-01 (SPEC.md §9.1 US-76 п.6/п.7, API_CONTRACT_CYCLE5.md §44.3). Uploading WITHOUT the
    // staff-confirmed photo consent must 400 — and, per US-76 п.6/US-67 п.4, everything else about
    // serving the client (the note, the booking) must keep working regardless.
    [Fact, TestCase("LGL-076-01")]
    public async Task Upload_WithoutPhotoConsent_ReturnsBadRequest_ButNoteAndBookingStillWork()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var clientUser = await RegisterAsync();
        (await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings",
            new ServiceBooking.API.DTOs.Bookings.CreateBookingDto(
                company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var addResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, clientUser.UserId, null, Unique("Note ")));
        addResponse.StatusCode.Should().Be(HttpStatusCode.Created, "no consent is required to write the note itself");
        var noteId = (await addResponse.Content.ReadJsonAsync<ClientNoteDto>())!.Id;

        // No GrantPhotoConsentAsync call here — this is the precondition itself under test.
        var uploadResponse = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload());
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await uploadResponse.Content.ReadAsStringAsync()).Should().Contain("согласие",
            "the 400 body must explain WHY, not just fail silently");

        // The rest of the client's service must be entirely unaffected (US-67 п.4: refusing an optional
        // consent never closes access to the service itself).
        (await AuthedClient(clientUser.Token).GetAsync("/api/profile")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("MC-103")]
    public async Task Upload_ToUnknownNote_ReturnsNotFound()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(owner.Token).PostAsync($"/api/client-notes/{Guid.NewGuid()}/photos", JpegUpload());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("MC-104")]
    public async Task Upload_SixthPhotoToSameNote_ReturnsBadRequest()
    {
        var (_, _, master, noteId) = await SetUpNoteAsync();
        var client = AuthedClient(master.Token);

        for (var i = 0; i < 5; i++)
        {
            var ok = await client.PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload(TestImages.SolidJpeg(color: RandomColor(i))));
            ok.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var sixth = await client.PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload(TestImages.SolidJpeg(color: RandomColor(5))));

        sixth.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await sixth.Content.ReadAsStringAsync()).Should().Contain("5 photos");
    }

    private static SkiaSharp.SKColor RandomColor(int seed) => new((byte)(seed * 40), (byte)(100 + seed * 10), (byte)(200 - seed * 20));

    [Fact, TestCase("MC-105")]
    public async Task Upload_SameFileTwice_SecondReturnsOkNotCreated_AndDoesNotDuplicateTheRow()
    {
        var (_, company, master, noteId) = await SetUpNoteAsync();
        var bytes = TestImages.SolidJpeg();
        var client = AuthedClient(master.Token);

        var first = await client.PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload(bytes));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstPhoto = (await first.Content.ReadJsonAsync<ClientNotePhotoDto>())!;

        var second = await client.PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload(bytes));
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondPhoto = (await second.Content.ReadJsonAsync<ClientNotePhotoDto>())!;
        secondPhoto.Id.Should().Be(firstPhoto.Id);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ClientNotePhotos.CountAsync(p => p.ClientNoteId == noteId)).Should().Be(1);
    }

    [Fact, TestCase("MC-106")]
    public async Task Upload_FileLargerThanFiveMegabytes_ReturnsBadRequest()
    {
        var (_, _, master, noteId) = await SetUpNoteAsync();
        var oversized = new byte[6 * 1024 * 1024];

        var response = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload(oversized));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("больш");
    }

    [Fact, TestCase("MC-107")]
    public async Task Upload_ContentIsNotAnImage_ReturnsBadRequest()
    {
        var (_, _, master, noteId) = await SetUpNoteAsync();

        var response = await AuthedClient(master.Token).PostAsync(
            $"/api/client-notes/{noteId}/photos", JpegUpload("just plain text, not a jpeg"u8.ToArray()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("JPEG, PNG");
    }

    [Fact, TestCase("MC-108")]
    public async Task Upload_QuotaExhausted_ReturnsBadRequestAndFileIsNotWrittenToDisk()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id, durationMinutes: 30);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        // ClientConsentsController.ResolveClientAsync requires an existing booking behind the guest
        // phone (same invariant MastersController.AddNote's own client-list assumes) — the photo-consent
        // endpoint 404s on a guest phone that never booked, so grant the consent AFTER a real guest
        // booking exists for it, same as a real salon workflow would.
        (await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new ServiceBooking.API.DTOs.Bookings.CreateBookingDto(
                company.Id, service.Id, master.UserId, date, new TimeOnly(8, 0), null, "Walk-in", "+79990009999", null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Zero MB quota: even the first (small) upload has nowhere to fit.
        var zeroQuotaPlan = await CreateTestPlanConfigAsync(photoQuotaMb: 0);
        await SetSubscriptionAsync(company.Id, planConfigId: zeroQuotaPlan);

        var addResponse = await AuthedClient(master.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, null, "+79990009999", Unique("Note ")));
        var noteId = (await addResponse.Content.ReadJsonAsync<ClientNoteDto>())!.Id;
        await GrantPhotoConsentAsync(master.Token, company.Id, "phone:79990009999");

        var response = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("quota");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ClientNotePhotos.CountAsync(p => p.ClientNoteId == noteId)).Should().Be(0);
    }

    // ── GET /api/client-notes/photos/{id} & /thumb ──────────────────────────

    [Fact, TestCase("MC-201")]
    public async Task GetPhoto_ByStaffOfTheSameCompany_ReturnsImage()
    {
        var (_, _, master, noteId) = await SetUpNoteAsync();
        var upload = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload());
        var photo = (await upload.Content.ReadJsonAsync<ClientNotePhotoDto>())!;

        var response = await AuthedClient(master.Token).GetAsync($"/api/client-notes/photos/{photo.Id}");
        var thumbResponse = await AuthedClient(master.Token).GetAsync($"/api/client-notes/photos/{photo.Id}/thumb");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        (await response.Content.ReadAsByteArrayAsync()).Length.Should().BeGreaterThan(0);
        thumbResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("MC-202")]
    public async Task GetPhoto_ByStaffOfAnotherCompany_ReturnsNotFound()
    {
        var (_, _, master, noteId) = await SetUpNoteAsync();
        var upload = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload());
        var photo = (await upload.Content.ReadJsonAsync<ClientNotePhotoDto>())!;

        var (strangerOwner, _) = await CreateOwnerWithCompanyAsync();
        var response = await AuthedClient(strangerOwner.Token).GetAsync($"/api/client-notes/photos/{photo.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("MC-203")]
    public async Task GetPhoto_Anonymous_ReturnsUnauthorized()
    {
        var (_, _, master, noteId) = await SetUpNoteAsync();
        var upload = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload());
        var photo = (await upload.Content.ReadJsonAsync<ClientNotePhotoDto>())!;

        // DoD manual checklist item, automated: a direct link with no Authorization header never serves
        // the image (R2, ARCHITECTURE.md §12.1).
        var response = await AnonymousClient().GetAsync($"/api/client-notes/photos/{photo.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("MC-204")]
    public async Task GetPhoto_AsSuperAdmin_ReturnsForbidden()
    {
        // Decision Q5: the one place in the product SuperAdmin is refused (ARCHITECTURE.md §12.1).
        var (_, _, master, noteId) = await SetUpNoteAsync();
        var upload = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload());
        var photo = (await upload.Content.ReadJsonAsync<ClientNotePhotoDto>())!;

        var admin = await LoginAsSuperAdminAsync();
        var response = await AuthedClient(admin.Token).GetAsync($"/api/client-notes/photos/{photo.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("MC-205")]
    public async Task GetPhoto_UnknownId_ReturnsNotFound()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(owner.Token).GetAsync($"/api/client-notes/photos/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DELETE /api/client-notes/photos/{id} ────────────────────────────────

    [Fact, TestCase("MC-301")]
    public async Task DeletePhoto_ByUploader_Succeeds()
    {
        var (_, _, master, noteId) = await SetUpNoteAsync();
        var upload = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload());
        var photo = (await upload.Content.ReadJsonAsync<ClientNotePhotoDto>())!;

        var response = await AuthedClient(master.Token).DeleteAsync($"/api/client-notes/photos/{photo.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AuthedClient(master.Token).GetAsync($"/api/client-notes/photos/{photo.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("MC-302")]
    public async Task DeletePhoto_ByColleagueWhoNeitherAuthoredNorUploaded_ReturnsForbidden()
    {
        var (owner, company, masterA, noteId) = await SetUpNoteAsync();
        var masterB = await AddMasterAsync(owner.Token, company.Id);
        var upload = await AuthedClient(masterA.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload());
        var photo = (await upload.Content.ReadJsonAsync<ClientNotePhotoDto>())!;

        var response = await AuthedClient(masterB.Token).DeleteAsync($"/api/client-notes/photos/{photo.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("MC-303")]
    public async Task DeletePhoto_ByCompanyOwner_Succeeds()
    {
        var (owner, _, master, noteId) = await SetUpNoteAsync();
        var upload = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload());
        var photo = (await upload.Content.ReadJsonAsync<ClientNotePhotoDto>())!;

        var response = await AuthedClient(owner.Token).DeleteAsync($"/api/client-notes/photos/{photo.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact, TestCase("MC-304")]
    public async Task DeletePhoto_FreesQuotaImmediately()
    {
        var (owner, company, master, noteId) = await SetUpNoteAsync();
        var upload = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload());
        var photo = (await upload.Content.ReadJsonAsync<ClientNotePhotoDto>())!;

        var beforeUsage = (await (await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/photo-usage"))
            .Content.ReadJsonAsync<CompanyPhotoUsageDto>())!;
        beforeUsage.PhotoCount.Should().Be(1);
        beforeUsage.UsedBytes.Should().BeGreaterThan(0);

        (await AuthedClient(master.Token).DeleteAsync($"/api/client-notes/photos/{photo.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterUsage = (await (await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/photo-usage"))
            .Content.ReadJsonAsync<CompanyPhotoUsageDto>())!;
        afterUsage.PhotoCount.Should().Be(0);
        afterUsage.UsedBytes.Should().Be(0);
    }

    [Fact, TestCase("MC-305")]
    public async Task DeleteNote_CascadesItsPhotos()
    {
        var (_, _, master, noteId) = await SetUpNoteAsync();
        var upload = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload());
        var photo = (await upload.Content.ReadJsonAsync<ClientNotePhotoDto>())!;

        var deleteNote = await AuthedClient(master.Token).DeleteAsync($"/api/masters/clients/notes/{noteId}");
        deleteNote.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.ClientNotePhotos.FindAsync(photo.Id)).Should().BeNull();
    }

    // ── Rate limiting (US-19 p.5) ────────────────────────────────────────────

    [Fact, TestCase("MC-206")]
    public async Task Upload_ExceedsPerUserRateLimit_ReturnsTooManyRequestsWithBody()
    {
        // appsettings.Testing.json raises Uploads:PerUserPerMinute to 1000 so the other 300+ functional
        // tests never trip it — this test builds its OWN host with the limit dropped to 1, exactly the
        // pattern ARCHITECTURE.md §10.2 describes for exercising the limiter itself.
        using var throttledFactory = Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Uploads:PerUserPerMinute", "1"));
        var client = throttledFactory.CreateClient();

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register",
            new ServiceBooking.API.DTOs.Auth.RegisterDto("Rate", "Limited", UniquePhone(), "Password123!", null, CurrentRegisterLegalDto()));
        var user = (await registerResponse.Content.ReadFromJsonAsync<ServiceBooking.API.DTOs.Auth.AuthResponseDto>())!;
        var authedClient = throttledFactory.CreateClient();
        authedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", user.Token);

        // Any [EnableRateLimiting("uploads")] endpoint counts against the same per-user bucket — the
        // avatar endpoint is the simplest one to reach without extra setup (no company/note needed).
        var first = await authedClient.PostAsync("/api/profile/avatar", JpegUpload());
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await authedClient.PostAsync("/api/profile/avatar", JpegUpload());

        second.StatusCode.Should().Be((HttpStatusCode)429);
        (await second.Content.ReadAsStringAsync()).Should().Be("Too many uploads. Try again in a minute.");
    }

    // ── Storage isolation (ARCHITECTURE.md §12.1, cycle 3 Program.cs static-files fix) ──

    [Fact, TestCase("MC-207")]
    public async Task UploadedPhoto_StorageKey_IsNotReachableViaAnyStaticUploadsPath()
    {
        // Program.cs's app.UseStaticFiles(...) mounts ONLY FileStorage's PUBLIC root at "/uploads" — the
        // private class (client-note photos) is a structurally separate directory that middleware never
        // touches at all, by construction (see FileStorage's class doc). This pins that guarantee
        // directly against the storage key a real upload actually produced, rather than trusting the
        // structural argument alone: GET /api/client-notes/photos/{id} is the only reachable path,
        // anonymously or under "/uploads/<storageKey>" it must always 404.
        var (_, _, master, noteId) = await SetUpNoteAsync();

        var upload = await AuthedClient(master.Token).PostAsync($"/api/client-notes/{noteId}/photos", JpegUpload());
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var photo = (await upload.Content.ReadJsonAsync<ClientNotePhotoDto>())!;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ClientNotePhotos.SingleAsync(p => p.Id == photo.Id);
        row.StoragePath.Should().NotBeNullOrEmpty();
        row.ThumbnailPath.Should().NotBeNullOrEmpty();

        var anonymous = AnonymousClient();
        (await anonymous.GetAsync($"/uploads/{row.StoragePath}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/uploads/{row.ThumbnailPath}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Same check with the leading company-id segment stripped/reshuffled the way "/uploads/avatars/"
        // etc. are laid out — belt-and-braces against any future refactor that starts mounting the
        // private root under a plausible-looking public prefix.
        (await anonymous.GetAsync($"/uploads/private/{row.StoragePath}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
