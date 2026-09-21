using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.Services;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Sanitation-cycle regression coverage for the Program.cs bugfix that serves <c>/uploads</c> from
/// <see cref="FileStorage.PublicRootFullPath"/> instead of the parameterless
/// <c>app.UseStaticFiles()</c>'s hard-wired <c>wwwroot</c> (see the long comment at the
/// <c>app.UseStaticFiles(new StaticFileOptions {...})</c> call site).
///
/// PROF-016/PROF-017 in ProfileTests.cs already exercise the avatar-upload-then-fetch round trip, but
/// only against <see cref="CustomWebApplicationFactory"/>'s DEFAULT configuration — no
/// <c>Storage:PublicRoot</c> override, repo's own content root — under which the broken parameterless
/// <c>app.UseStaticFiles()</c> and the fix resolve to the exact same directory
/// (<c>&lt;repo&gt;/ServiceBooking.API/wwwroot/uploads</c>). They only fail today because that directory
/// happens not to be committed to git; <c>.gitignore</c> carries a <c>!.../wwwroot/uploads/**/.gitkeep</c>
/// negation that invites someone to add one, and the day that happens PROF-016/PROF-017 stay green with
/// the regression back in place. These two tests are independent of git content entirely — each points a
/// dedicated host (<see cref="UploadsStaticFilesTestFactory"/>) at a configuration where the broken and
/// fixed code paths provably diverge.
/// </summary>
public class UploadsStaticFilesTests
{
    private static string RandomPhone()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsDigit).Take(9).ToArray();
        return $"+79{new string(digits).PadRight(9, '2')}";
    }

    // CYCLE5-BREAKING (compile-only adaptation, see ApiTestBase.RegisterAsync's own note): only an
    // HttpClient is available here (no DI scope), so the manifest is read over HTTP from the same host
    // the registration call itself targets.
    private static async Task<RegisterLegalDto> CurrentRegisterLegalDtoAsync(HttpClient client)
    {
        var manifest = await client.GetFromJsonAsync<LegalManifestDto>("/api/legal/documents");
        return new RegisterLegalDto(
            manifest!.Documents.First(d => d.Type == "Privacy").Version,
            manifest.Documents.First(d => d.Type == "TermsClient").Version);
    }

    private static async Task<string> RegisterAndGetTokenAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterDto("Test", "User", RandomPhone(), "Password123!", null, await CurrentRegisterLegalDtoAsync(client)));
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        return dto!.Token;
    }

    private static async Task<string> UploadAvatarAndGetUrlAsync(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImages.TallJpeg());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "avatar.jpg");

        var response = await client.PostAsync("/api/profile/avatar", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the upload itself must succeed before we can assert anything about serving it back");
        var dto = await response.Content.ReadFromJsonAsync<ProfileDto>();
        dto!.AvatarUrl.Should().NotBeNullOrEmpty();
        return dto.AvatarUrl!;
    }

    // ── Scenario 1: Storage:PublicRoot points OUTSIDE the content root ─────────────────────
    //
    // A bare app.UseStaticFiles() always serves WebRootFileProvider (wwwroot under the content root),
    // regardless of Storage:PublicRoot — so with the public root moved elsewhere entirely, the broken
    // code would 404 on every request no matter what's on disk. This is the case the fix's own comment
    // calls out: "setting Storage:PublicRoot away from the default silently broke serving with no error
    // anywhere".

    [Fact, TestCase("UPL-001")]
    public async Task UploadedFile_ServedFrom_ConfiguredPublicRoot_OutsideContentRoot()
    {
        var tempPublicRoot = Path.Combine(Path.GetTempPath(), $"sb-uploads-test-{Guid.NewGuid():N}");
        // Deliberately not pre-creating tempPublicRoot itself — Program.cs's Directory.CreateDirectory
        // call is what's expected to create it, same as the default wwwroot/uploads case.

        await using var factory = new UploadsStaticFilesTestFactory(publicRootOverride: tempPublicRoot);
        try
        {
            var client = factory.CreateClient();
            var token = await RegisterAndGetTokenAsync(client);

            var avatarUrl = await UploadAvatarAndGetUrlAsync(client, token);

            // The file must genuinely have landed under the configured root, not wwwroot.
            var relative = avatarUrl["/uploads/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var expectedOnDisk = Path.Combine(tempPublicRoot, relative);
            File.Exists(expectedOnDisk).Should().BeTrue(
                $"the upload pipeline (FileStorage.SavePublicAsync) must write under the configured " +
                $"Storage:PublicRoot ({tempPublicRoot}), not the default wwwroot/uploads");

            var served = await factory.CreateClient().GetAsync(avatarUrl);
            served.StatusCode.Should().Be(HttpStatusCode.OK,
                "app.UseStaticFiles must serve from FileStorage.PublicRootFullPath (Storage:PublicRoot), " +
                "not a bare wwwroot-bound app.UseStaticFiles() — the latter 404s here unconditionally " +
                "since the file was never written under wwwroot at all");
        }
        finally
        {
            if (Directory.Exists(tempPublicRoot)) Directory.Delete(tempPublicRoot, recursive: true);
        }
    }

    // ── Scenario 2: content root has no wwwroot at all ──────────────────────────────────────
    //
    // Pins Directory.CreateDirectory(publicUploadsRoot) running BEFORE the PhysicalFileProvider is
    // constructed. IWebHostEnvironment.WebRootFileProvider resolves once, at host startup, against
    // whatever wwwroot looks like at that instant — on a directory that doesn't exist yet, a
    // PhysicalFileProvider built directly over it (without the pre-creation) throws or otherwise never
    // picks up a wwwroot created afterward by the first upload. This is the fresh-checkout half of the
    // bug the fix's comment describes.

    [Fact, TestCase("UPL-002")]
    public async Task UploadedFile_ServedFrom_FreshContentRoot_WithNoPreexistingWwwroot()
    {
        var tempContentRoot = Path.Combine(Path.GetTempPath(), $"sb-contentroot-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempContentRoot);
        // Deliberately NOT creating a wwwroot subfolder under it — that's the point of this scenario.
        // Asserted BEFORE the factory/host is even constructed: CreateClient() below starts the host,
        // which (per the fix under test) itself calls Directory.CreateDirectory(publicUploadsRoot)
        // during pipeline construction, before any request — so checking this after CreateClient() would
        // just be re-confirming the very fix this test exists to pin, not the test's actual precondition.
        Directory.Exists(Path.Combine(tempContentRoot, "wwwroot"))
            .Should().BeFalse("sanity check — this scenario is only meaningful if wwwroot genuinely did not exist before the host started");

        await using var factory = new UploadsStaticFilesTestFactory(contentRootOverride: tempContentRoot);
        try
        {
            var client = factory.CreateClient();
            var token = await RegisterAndGetTokenAsync(client);

            var avatarUrl = await UploadAvatarAndGetUrlAsync(client, token);

            var served = await factory.CreateClient().GetAsync(avatarUrl);
            served.StatusCode.Should().Be(HttpStatusCode.OK,
                "the public root directory must be created (Directory.CreateDirectory) before the " +
                "PhysicalFileProvider backing app.UseStaticFiles is constructed — otherwise a wwwroot/" +
                "uploads created only by the first upload is never picked up for the lifetime of the host");
        }
        finally
        {
            if (Directory.Exists(tempContentRoot)) Directory.Delete(tempContentRoot, recursive: true);
        }
    }
}
