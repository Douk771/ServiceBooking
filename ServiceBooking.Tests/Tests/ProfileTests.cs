using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.Services;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

public class ProfileTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── GET /api/profile ─────────────────────────────────────────────────────────────────

    [Fact, TestCase("PROF-001")]
    public async Task GetProfile_AsAuthenticatedUser_ReturnsOwnProfile()
    {
        var user = await RegisterAsync(firstName: "Alice", lastName: "Anderson");

        var response = await AuthedClient(user.Token).GetAsync("/api/profile");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = await response.Content.ReadFromJsonAsync<ProfileDto>();
        profile!.Id.Should().Be(user.UserId);
        profile.Phone.Should().Be(user.Phone);
        profile.FirstName.Should().Be("Alice");
        profile.LastName.Should().Be("Anderson");
        profile.Roles.Should().Contain("Client");
    }

    [Fact, TestCase("PROF-002")]
    public async Task GetProfile_Anonymous_ReturnsUnauthorized()
    {
        var response = await AnonymousClient().GetAsync("/api/profile");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── PUT /api/profile ─────────────────────────────────────────────────────────────────

    [Fact, TestCase("PROF-003")]
    public async Task UpdateProfile_ChangesFirstAndLastName()
    {
        var user = await RegisterAsync(firstName: "Old", lastName: "Name");
        var client = AuthedClient(user.Token);

        var response = await client.PutAsJsonAsync("/api/profile", new UpdateProfileDto("New", "Name2"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<ProfileDto>();
        updated!.FirstName.Should().Be("New");
        updated.LastName.Should().Be("Name2");

        // Persisted, not just returned once.
        var reGet = await client.GetAsync("/api/profile");
        var reGetDto = await reGet.Content.ReadFromJsonAsync<ProfileDto>();
        reGetDto!.FirstName.Should().Be("New");
        reGetDto.LastName.Should().Be("Name2");
    }

    [Fact, TestCase("PROF-004")]
    public async Task UpdateProfile_Anonymous_ReturnsUnauthorized()
    {
        var response = await AnonymousClient().PutAsJsonAsync("/api/profile", new UpdateProfileDto("A", "B"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("PROF-005")]
    public async Task UpdateProfile_CannotSetOwnCommissionPercent_FieldIsNotOnTheDto()
    {
        // UpdateProfileDto intentionally has no CommissionPercent field — commission is set exclusively
        // by the company owner via PUT /api/companies/{id}/members/{memberId}/commission (see
        // CompaniesTests.cs); since cycle A that value lives on CompanyMember, not AppUser. Cycle B
        // (US-22) removes the account-level ProfileDto.CommissionPercent entirely — it was a legacy
        // field the UI never showed. This test now only pins that a plain profile update still succeeds
        // for a master with a company-level commission set.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id, commissionPercent: 30);

        var response = await AuthedClient(master.Token).PutAsJsonAsync("/api/profile", new UpdateProfileDto("New", "Name"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<ProfileDto>();
        updated!.FirstName.Should().Be("New");
    }

    // ── POST /api/profile/change-password ───────────────────────────────────────────────

    [Fact, TestCase("PROF-006")]
    public async Task ChangePassword_WithCorrectCurrentPassword_SucceedsAndNewPasswordLogsIn()
    {
        var phone = UniquePhone();
        await RegisterAsync(phone, "OldPassword123!");
        var user = await LoginAsync(phone, "OldPassword123!");
        var client = AuthedClient(user.Token);

        var response = await client.PostAsJsonAsync("/api/profile/change-password",
            new ChangePasswordDto("OldPassword123!", "NewPassword456!"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var loginWithNew = await LoginRawAsync(phone, "NewPassword456!");
        loginWithNew.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginWithOld = await LoginRawAsync(phone, "OldPassword123!");
        loginWithOld.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("PROF-007")]
    public async Task ChangePassword_WithWrongCurrentPassword_ReturnsBadRequest()
    {
        var phone = UniquePhone();
        await RegisterAsync(phone, "CorrectPassword123!");
        var user = await LoginAsync(phone, "CorrectPassword123!");
        var client = AuthedClient(user.Token);

        var response = await client.PostAsJsonAsync("/api/profile/change-password",
            new ChangePasswordDto("TotallyWrongPassword!", "NewPassword456!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Old password must still work — the change was rejected.
        var loginWithOld = await LoginRawAsync(phone, "CorrectPassword123!");
        loginWithOld.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("PROF-008")]
    public async Task ChangePassword_Anonymous_ReturnsUnauthorized()
    {
        var response = await AnonymousClient().PostAsJsonAsync("/api/profile/change-password",
            new ChangePasswordDto("Whatever123!", "NewPassword456!"));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Plan info block ──────────────────────────────────────────────────────────────────

    [Fact, TestCase("PROF-009")]
    public async Task GetProfile_AsPlainClient_HasNoPlanInfo()
    {
        // Subscriptions only apply to company owners — a user who never created a company shouldn't
        // get a fabricated Free plan block.
        var user = await RegisterAsync();

        var response = await AuthedClient(user.Token).GetAsync("/api/profile");
        var profile = await response.Content.ReadJsonAsync<ProfileDto>();

        profile!.Plan.Should().BeNull();
    }

    [Fact, TestCase("PROF-010")]
    public async Task GetProfile_AsOwnerWithoutSubscription_ShowsFreePlanBaseline()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync(attachPlan: false);

        var response = await AuthedClient(owner.Token).GetAsync("/api/profile");
        var profile = await response.Content.ReadJsonAsync<ProfileDto>();

        profile!.Plan.Should().NotBeNull();
        profile.Plan!.PlanName.Should().Be("Free");
        profile.Plan.IsActive.Should().BeTrue();
        profile.Plan.PaidUntil.Should().BeNull();
        profile.Plan.AllowOnlineBooking.Should().BeFalse();
        profile.Plan.MaxEmployees.Should().Be(1);
        profile.Plan.MaxCompanies.Should().Be(1);
    }

    [Fact, TestCase("PROF-011")]
    public async Task GetProfile_AsOwnerWithActiveSubscription_ReflectsPlanConfigDetails()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        await SetSubscriptionAsync(company.Id);

        var response = await AuthedClient(owner.Token).GetAsync("/api/profile");
        var profile = await response.Content.ReadJsonAsync<ProfileDto>();

        profile!.Plan.Should().NotBeNull();
        profile.Plan!.PlanName.Should().Be("QA Full Access");
        profile.Plan.IsActive.Should().BeTrue();
        profile.Plan.IsExpired.Should().BeFalse();
        profile.Plan.PaidUntil.Should().NotBeNull();
        profile.Plan.AllowOnlineBooking.Should().BeTrue();
        profile.Plan.AllowAnalytics.Should().BeTrue();
    }

    // ── POST /api/profile/change-phone ──────────────────────────────────────────────────
    //
    // Phone doubles as the Identity UserName (see AuthController), so this endpoint goes through
    // SetUserNameAsync behind a current-password check — same trust bar as change-password, since it's
    // effectively changing the account's login identifier.

    [Fact, TestCase("PROF-012")]
    public async Task ChangePhone_WithCorrectPassword_SucceedsAndNewPhoneLogsIn()
    {
        var oldPhone = UniquePhone();
        var newPhone = UniquePhone();
        await RegisterAsync(oldPhone, "Password123!");
        var user = await LoginAsync(oldPhone, "Password123!");

        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/profile/change-phone",
            new ChangePhoneDto("Password123!", newPhone));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadJsonAsync<ProfileDto>();
        // US-26: the canonical (digits-only) form is what's stored and returned, not the "+7..." shape
        // the caller sent — PhoneNormalizerTests pins the normalization rule itself; this is the
        // regression guard that ChangePhone actually applies it (was "phone stored as typed" before
        // this cycle).
        updated!.Phone.Should().Be(PhoneNormalizer.Normalize(newPhone));

        var loginWithNew = await LoginRawAsync(newPhone, "Password123!");
        loginWithNew.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginWithOld = await LoginRawAsync(oldPhone, "Password123!");
        loginWithOld.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("PROF-013")]
    public async Task ChangePhone_WithWrongPassword_ReturnsBadRequest_AndPhoneUnchanged()
    {
        var phone = UniquePhone();
        await RegisterAsync(phone, "CorrectPassword123!");
        var user = await LoginAsync(phone, "CorrectPassword123!");

        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/profile/change-phone",
            new ChangePhoneDto("TotallyWrongPassword!", UniquePhone()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var loginWithOld = await LoginRawAsync(phone, "CorrectPassword123!");
        loginWithOld.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("PROF-014")]
    public async Task ChangePhone_ToAnAlreadyRegisteredPhone_ReturnsBadRequest()
    {
        var takenPhone = UniquePhone();
        await RegisterAsync(takenPhone, "Password123!");

        var user = await RegisterAsync(password: "Password123!");

        var response = await AuthedClient(user.Token).PostAsJsonAsync("/api/profile/change-phone",
            new ChangePhoneDto("Password123!", takenPhone));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("PROF-015")]
    public async Task ChangePhone_Anonymous_ReturnsUnauthorized()
    {
        var response = await AnonymousClient().PostAsJsonAsync("/api/profile/change-phone",
            new ChangePhoneDto("Whatever123!", UniquePhone()));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/profile/avatar ──────────────────────────────────────────────

    [Fact, TestCase("PROF-016")]
    public async Task UploadAvatar_ValidImage_SetsAvatarUrlServedUnderUploadsPath()
    {
        var user = await RegisterAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImages.TallJpeg());
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "avatar.jpg");

        var response = await AuthedClient(user.Token).PostAsync("/api/profile/avatar", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadJsonAsync<ProfileDto>();
        dto!.AvatarUrl.Should().NotBeNullOrEmpty();
        dto.AvatarUrl.Should().StartWith("/uploads/avatars/");

        var served = await AnonymousClient().GetAsync(dto.AvatarUrl);
        served.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("PROF-017")]
    public async Task UploadAvatar_ReplacesOldFile()
    {
        var user = await RegisterAsync();

        async Task<string> UploadAsync()
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(TestImages.SolidPng());
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "file", "avatar.png");
            var res = await AuthedClient(user.Token).PostAsync("/api/profile/avatar", content);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await res.Content.ReadJsonAsync<ProfileDto>())!.AvatarUrl!;
        }

        var firstUrl = await UploadAsync();
        var secondUrl = await UploadAsync();

        secondUrl.Should().NotBe(firstUrl);
        (await AnonymousClient().GetAsync(secondUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AnonymousClient().GetAsync(firstUrl)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("PROF-018")]
    public async Task UploadAvatar_ContentIsNotAnImage_ReturnsBadRequest()
    {
        var user = await RegisterAsync();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent([1, 2, 3, 4]);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", "avatar.jpg");

        var response = await AuthedClient(user.Token).PostAsync("/api/profile/avatar", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
