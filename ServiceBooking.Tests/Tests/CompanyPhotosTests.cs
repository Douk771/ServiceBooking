using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.ClientNotes;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Cycle 10, Block C (US-125/US-126) — SPEC_CYCLE10_MASTER_BOOKING_HISTORY_PHOTO.md §3,
/// ARCHITECTURE_CYCLE10.md §102.2/§106/§109.3. Written against SPEC/ARCHITECTURE, not the
/// implementation, plus the specific scenarios the backend report named (10-photo limit, dedup,
/// reorder validation, deactivated-company gallery, duplicate Position==0) and the code-review findings
/// (photo-usage/photo-retention-cleanup exclusion).
/// </summary>
public class CompanyPhotosTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    private static MultipartFormDataContent JpegUpload(byte[]? bytes = null, string fileName = "photo.jpg")
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes ?? TestImages.SolidJpeg());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    // ── US-125: upload, cover, delete, reorder ──

    [Fact, TestCase("CPH-001")]
    public async Task Upload_ByOwner_AppearsInGalleryAsCover()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var upload = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", JpegUpload());
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var photo = await upload.Content.ReadJsonAsync<CompanyPhotoDto>();
        photo!.IsCover.Should().BeTrue();
        photo.Position.Should().Be(0);

        var gallery = await AnonymousClient().GetFromJsonAsync<List<CompanyPhotoDto>>($"/api/companies/{company.Id}/photos");
        gallery!.Should().ContainSingle(p => p.Id == photo.Id);
    }

    [Fact, TestCase("CPH-002")]
    public async Task Upload_11thPhoto_Returns400WithLimitMessage()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        for (var i = 0; i < 10; i++)
        {
            var upload = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos",
                JpegUpload(TestImages.SolidJpeg(width: 40 + i, height: 40))); // distinct bytes each time
            upload.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var eleventh = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos",
            JpegUpload(TestImages.SolidJpeg(width: 99, height: 40)));

        eleventh.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var message = await eleventh.Content.ReadAsStringAsync();
        message.Should().Contain("10");
    }

    [Fact, TestCase("CPH-003")]
    public async Task Upload_SameFileTwice_ReturnsSamePhotoAndDoesNotConsumeTheLimit()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var bytes = TestImages.SolidJpeg();

        var first = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", JpegUpload(bytes));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstPhoto = await first.Content.ReadJsonAsync<CompanyPhotoDto>();

        var second = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", JpegUpload(bytes));
        second.StatusCode.Should().Be(HttpStatusCode.OK); // not Created — dedup, same photo returned
        var secondPhoto = await second.Content.ReadJsonAsync<CompanyPhotoDto>();
        secondPhoto!.Id.Should().Be(firstPhoto!.Id);

        var gallery = await AnonymousClient().GetFromJsonAsync<List<CompanyPhotoDto>>($"/api/companies/{company.Id}/photos");
        gallery!.Should().HaveCount(1);
    }

    [Fact, TestCase("CPH-004")]
    public async Task Delete_ByOwner_RemovesFromGalleryAndCompactsPositions()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var first = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos",
            JpegUpload(TestImages.SolidJpeg(41, 40)));
        var firstPhoto = await first.Content.ReadJsonAsync<CompanyPhotoDto>();
        var second = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos",
            JpegUpload(TestImages.SolidJpeg(42, 40)));
        var secondPhoto = await second.Content.ReadJsonAsync<CompanyPhotoDto>();

        var delete = await AuthedClient(owner.Token).DeleteAsync($"/api/companies/{company.Id}/photos/{firstPhoto!.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var gallery = await AnonymousClient().GetFromJsonAsync<List<CompanyPhotoDto>>($"/api/companies/{company.Id}/photos");
        gallery!.Should().ContainSingle(p => p.Id == secondPhoto!.Id);
        gallery!.Single().Position.Should().Be(0); // compacted, new cover
        gallery!.Single().IsCover.Should().BeTrue();
    }

    [Fact, TestCase("CPH-005")]
    public async Task Delete_PhotoBelongingToAnotherCompany_Returns404()
    {
        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync();
        var (ownerB, companyB) = await CreateOwnerWithCompanyAsync();
        var upload = await AuthedClient(ownerA.Token).PostAsync($"/api/companies/{companyA.Id}/photos", JpegUpload());
        var photo = await upload.Content.ReadJsonAsync<CompanyPhotoDto>();

        // ownerB is a real owner (of companyB), attempting to delete companyA's photo by asking for it
        // under companyB's id — must not exist from that angle either.
        var delete = await AuthedClient(ownerB.Token).DeleteAsync($"/api/companies/{companyB.Id}/photos/{photo!.Id}");

        delete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CPH-006")]
    public async Task Reorder_IncompletePermutation_Returns400()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var first = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos",
            JpegUpload(TestImages.SolidJpeg(51, 40)));
        var firstPhoto = await first.Content.ReadJsonAsync<CompanyPhotoDto>();
        await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", JpegUpload(TestImages.SolidJpeg(52, 40)));

        // Only one of the two ids supplied — not a full permutation.
        var reorder = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/photos/order", new { photoIds = new[] { firstPhoto!.Id } });

        reorder.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("CPH-007")]
    public async Task Reorder_WithAForeignPhotoId_Returns400()
    {
        var (ownerA, companyA) = await CreateOwnerWithCompanyAsync();
        var uploadA = await AuthedClient(ownerA.Token).PostAsync($"/api/companies/{companyA.Id}/photos", JpegUpload());
        var photoA = await uploadA.Content.ReadJsonAsync<CompanyPhotoDto>();

        var (ownerB, companyB) = await CreateOwnerWithCompanyAsync();
        var uploadB = await AuthedClient(ownerB.Token).PostAsync($"/api/companies/{companyB.Id}/photos", JpegUpload());
        var photoB = await uploadB.Content.ReadJsonAsync<CompanyPhotoDto>();

        // companyB's own reorder call including a photo id that actually belongs to companyA.
        var reorder = await AuthedClient(ownerB.Token).PutAsJsonAsync(
            $"/api/companies/{companyB.Id}/photos/order", new { photoIds = new[] { photoA!.Id } });

        reorder.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        photoB.Should().NotBeNull(); // sanity: companyB really does have its own photo
    }

    [Fact, TestCase("CPH-008")]
    public async Task Reorder_ValidFullPermutation_ChangesCoverToTheNewFirst()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var first = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos",
            JpegUpload(TestImages.SolidJpeg(61, 40)));
        var firstPhoto = await first.Content.ReadJsonAsync<CompanyPhotoDto>();
        var second = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos",
            JpegUpload(TestImages.SolidJpeg(62, 40)));
        var secondPhoto = await second.Content.ReadJsonAsync<CompanyPhotoDto>();

        var reorder = await AuthedClient(owner.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/photos/order", new { photoIds = new[] { secondPhoto!.Id, firstPhoto!.Id } });
        reorder.StatusCode.Should().Be(HttpStatusCode.OK);

        var gallery = await AnonymousClient().GetFromJsonAsync<List<CompanyPhotoDto>>($"/api/companies/{company.Id}/photos");
        gallery!.Single(p => p.Id == secondPhoto.Id).IsCover.Should().BeTrue();
        gallery!.Single(p => p.Id == secondPhoto.Id).Position.Should().Be(0);
        gallery!.Single(p => p.Id == firstPhoto.Id).Position.Should().Be(1);
    }

    // ── Rights: only owner and SuperAdmin; a master never manages the gallery ──

    [Fact, TestCase("CPH-009")]
    public async Task Master_CannotUploadDeleteOrReorder_Gets403()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var ownerUpload = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", JpegUpload());
        var photo = await ownerUpload.Content.ReadJsonAsync<CompanyPhotoDto>();

        var uploadByMaster = await AuthedClient(master.Token).PostAsync($"/api/companies/{company.Id}/photos",
            JpegUpload(TestImages.SolidJpeg(70, 40)));
        uploadByMaster.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var deleteByMaster = await AuthedClient(master.Token).DeleteAsync($"/api/companies/{company.Id}/photos/{photo!.Id}");
        deleteByMaster.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var reorderByMaster = await AuthedClient(master.Token).PutAsJsonAsync(
            $"/api/companies/{company.Id}/photos/order", new { photoIds = new[] { photo.Id } });
        reorderByMaster.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact, TestCase("CPH-010")]
    public async Task SuperAdmin_CanUploadAndDelete()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var admin = await LoginAsSuperAdminAsync();

        var upload = await AuthedClient(admin.Token).PostAsync($"/api/companies/{company.Id}/photos", JpegUpload());
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var photo = await upload.Content.ReadJsonAsync<CompanyPhotoDto>();

        var delete = await AuthedClient(admin.Token).DeleteAsync($"/api/companies/{company.Id}/photos/{photo!.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── US-126: public exposure — GetBySlug returns the gallery, GetAll returns only the cover ──

    [Fact, TestCase("CPH-011")]
    public async Task GetBySlug_ReturnsPhotosGallery_AnonymousReachable()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var upload = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", JpegUpload());
        var photo = await upload.Content.ReadJsonAsync<CompanyPhotoDto>();

        var response = await AnonymousClient().GetFromJsonAsync<CompanyDto>($"/api/companies/{company.Slug}");

        response!.Photos.Should().NotBeNull();
        response.Photos!.Should().ContainSingle(p => p.Id == photo!.Id);
        response.CoverPhotoUrl.Should().Be(photo!.Url);
    }

    [Fact, TestCase("CPH-012")]
    public async Task GetAll_ReturnsOnlyCover_NotTheFullPhotosArray()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        // Public listing requires ShowInPublicListing/AllowPublicListing — defaults keep it visible for
        // a freshly created company, matching the existing GetAll test conventions in CompaniesTests.cs.
        var upload = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", JpegUpload());
        var photo = await upload.Content.ReadJsonAsync<CompanyPhotoDto>();

        var list = await AnonymousClient().GetFromJsonAsync<List<CompanyDto>>("/api/companies");
        var mine = list!.SingleOrDefault(c => c.Id == company.Id);

        mine.Should().NotBeNull();
        mine!.CoverPhotoUrl.Should().Be(photo!.Url);
        mine.Photos.Should().BeNull(); // not the full gallery — GetBySlug is the only place that fills it
    }

    [Fact, TestCase("CPH-013")]
    public async Task NoPhotos_PublicEmptyStateHasNoCoverUrl()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var response = await AnonymousClient().GetFromJsonAsync<CompanyDto>($"/api/companies/{company.Slug}");

        response!.Photos.Should().NotBeNull().And.BeEmpty();
        response.CoverPhotoUrl.Should().BeNull();
        response.CoverThumbnailUrl.Should().BeNull();
    }

    // ── Regression findings from code review: duplicate Position==0 survives GetAll; a deactivated
    // company's gallery is not exposed. ──

    [Fact, TestCase("CPH-014")]
    public async Task DuplicatePositionZeroRows_DoNotCrashGetAll()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var upload = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", JpegUpload());
        var photo = await upload.Content.ReadJsonAsync<CompanyPhotoDto>();

        // Force a second row at Position == 0 directly — the ordering service prevents this through the
        // normal API, but the (CompanyId, Position) index is deliberately non-unique in the database
        // (§102.2), so a reader must tolerate it without 500ing the whole public catalog.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CompanyPhotos.Add(new CompanyPhoto
            {
                Id = Guid.NewGuid(), CompanyId = company.Id, Url = photo!.Url, ThumbnailUrl = photo.ThumbnailUrl,
                ContentType = "image/jpeg", SizeBytes = 100, Width = 40, Height = 40,
                ContentHash = Guid.NewGuid().ToString("N"), Position = 0, CreatedAtUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var response = await AnonymousClient().GetAsync("/api/companies");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CPH-015")]
    public async Task DeactivatedCompany_GalleryIsNotExposed()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var upload = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", JpegUpload());
        upload.StatusCode.Should().Be(HttpStatusCode.Created);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Companies.SingleAsync(c => c.Id == company.Id);
            row.IsActive = false;
            await db.SaveChangesAsync();
        }

        var response = await AnonymousClient().GetAsync($"/api/companies/{company.Id}/photos");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Quota/retention exclusion (§102.2/§113, code-review finding): showcase photos are NOT client
    // photos — they never count against PhotoQuotaMb and never expire under photo-retention-cleanup. ──

    [Fact, TestCase("CPH-016")]
    public async Task ExhaustedClientPhotoQuota_DoesNotBlockCompanyPhotoUpload()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var quotaConfigId = await CreateTestPlanConfigAsync(photoQuotaMb: 1); // 1 MB quota
        await SetSubscriptionAsync(company.Id, quotaConfigId);

        // Simulate an exhausted client-photo quota directly (bypassing the real upload pipeline, which
        // is not what this test is about) — a note is a real prerequisite FK for ClientNotePhoto.
        var addNote = await AuthedClient(owner.Token).PostAsJsonAsync("/api/masters/clients/notes",
            new AddNoteRequest(company.Id, null, "+79993334455", Unique("Note ")));
        addNote.StatusCode.Should().Be(HttpStatusCode.Created);
        var noteId = (await addNote.Content.ReadJsonAsync<ClientNoteDto>())!.Id;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ClientNotePhotos.Add(new ClientNotePhoto
            {
                Id = Guid.NewGuid(), ClientNoteId = noteId, CompanyId = company.Id,
                StoragePath = $"{company.Id}/exhaust.jpg", ThumbnailPath = $"{company.Id}/exhaust_thumb.jpg",
                ContentType = "image/jpeg", SizeBytes = 2 * 1024 * 1024, // 2 MB — already over the 1 MB quota
                Width = 40, Height = 40, ContentHash = Guid.NewGuid().ToString("N"), CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var usageResponse = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/photo-usage");
        var usage = await usageResponse.Content.ReadJsonAsync<CompanyPhotoUsageDto>();
        usage!.PercentUsed.Should().BeGreaterThan(100);

        // The showcase upload must still succeed — the quota is a client-photo concept only (П5).
        var companyPhotoUpload = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", JpegUpload());
        companyPhotoUpload.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact, TestCase("CPH-017")]
    public async Task CompanyPhotoUpload_DoesNotChangeClientPhotoUsageNumbers()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        var beforeResponse = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/photo-usage");
        var before = await beforeResponse.Content.ReadJsonAsync<CompanyPhotoUsageDto>();

        var upload = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", JpegUpload());
        upload.StatusCode.Should().Be(HttpStatusCode.Created);

        var afterResponse = await AuthedClient(owner.Token).GetAsync($"/api/companies/{company.Id}/photo-usage");
        var after = await afterResponse.Content.ReadJsonAsync<CompanyPhotoUsageDto>();

        after!.UsedBytes.Should().Be(before!.UsedBytes); // company photos never count toward this number
        after.PhotoCount.Should().Be(before.PhotoCount);
    }

    [Fact, TestCase("CPH-018")]
    public async Task PhotoRetentionCleanupTask_NeverDeletesCompanyShowcasePhotos()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var upload = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", JpegUpload());
        var photo = await upload.Content.ReadJsonAsync<CompanyPhotoDto>();

        // Backdate it far past any plausible retention window — if the task ever touched CompanyPhoto
        // rows this would make it a target; it never does (photo-retention-cleanup only queries
        // ClientNotePhotos).
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.CompanyPhotos.SingleAsync(p => p.Id == photo!.Id);
            row.CreatedAtUtc = DateTime.UtcNow.AddYears(-5);
            await db.SaveChangesAsync();
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var task = scope.ServiceProvider.GetServices<ServiceBooking.API.Services.Scheduling.IScheduledTask>()
                .Single(t => t.Name == "photo-retention-cleanup");
            await task.ExecuteAsync(CancellationToken.None);
        }

        var gallery = await AnonymousClient().GetFromJsonAsync<List<CompanyPhotoDto>>($"/api/companies/{company.Id}/photos");
        gallery!.Should().ContainSingle(p => p.Id == photo!.Id);
    }

    // ── Format/size validation, Russian error text ──

    [Fact, TestCase("CPH-019")]
    public async Task Upload_NotAnImage_Returns400WithRussianMessage()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[] { 1, 2, 3, 4, 5 });
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "not-an-image.jpg");

        var response = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var message = await response.Content.ReadAsStringAsync();
        // US-125 (SPEC_CYCLE10_MASTER_BOOKING_HISTORY_PHOTO.md §3): "сообщение по-русски объясняет
        // причину" — a documented acceptance criterion for THIS feature. Intentionally asserting the
        // SPEC'd behavior rather than the actual one: ImageUploadService.ReadAndProcessAsync (shared by
        // every upload conveyor, including this cycle's) currently returns English text
        // ("Unsupported image type — use JPEG, PNG or WEBP"), so this is expected to be RED until fixed
        // — see the QA report's bug list, not a flaky/wrong test.
        message.Should().MatchRegex("[а-яА-Я]"); // a Russian-language explanation, not a bare code
    }

    // ── No consent screen/checkbox in the upload flow (П7): ConsentRecord is never written for a
    // company showcase photo. ──

    [Fact, TestCase("CPH-020")]
    public async Task Upload_WritesNoConsentRecord()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var before = await db.ConsentRecords.CountAsync();

            var upload = await AuthedClient(owner.Token).PostAsync($"/api/companies/{company.Id}/photos", JpegUpload());
            upload.StatusCode.Should().Be(HttpStatusCode.Created);

            var after = await db.ConsentRecords.CountAsync();
            after.Should().Be(before);
        }
    }
}
