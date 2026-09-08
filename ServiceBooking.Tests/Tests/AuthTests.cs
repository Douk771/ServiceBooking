using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.Services;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

public class AuthTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    [Fact, TestCase("AUTH-001")]
    public async Task Register_WithValidData_ReturnsTokenAndClientRole()
    {
        var phone = UniquePhone();
        var client = AnonymousClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterDto("Ivan", "Petrov", phone, "Password123!", null, AcceptedLegal: true));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        body!.Token.Should().NotBeNullOrEmpty();
        // US-26: the canonical (digits-only) form is stored and returned, not whatever shape the
        // caller sent — was "phone stored as typed" before this cycle.
        body.Phone.Should().Be(PhoneNormalizer.Normalize(phone));
        body.Email.Should().BeNull(); // email is optional and wasn't supplied
        body.Roles.Should().ContainSingle().Which.Should().Be("Client");
    }

    [Fact, TestCase("AUTH-002")]
    public async Task Register_WithDuplicatePhone_ReturnsBadRequest()
    {
        var phone = UniquePhone();
        await RegisterAsync(phone);

        // Phone is the unique account identifier now, so re-registering the same number is rejected.
        var second = await RegisterRawAsync(phone, "AnotherPass123!");

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory, TestCase("AUTH-003")]
    [InlineData("", "Password123!")]        // missing phone
    [InlineData("+79990000000", "short")]   // password too short
    public async Task Register_WithInvalidData_ReturnsBadRequest(string phone, string password)
    {
        var response = await RegisterRawAsync(phone, password);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact, TestCase("AUTH-004")]
    public async Task Login_WithCorrectCredentials_ReturnsToken()
    {
        var phone = UniquePhone();
        await RegisterAsync(phone, "Password123!");

        var response = await LoginRawAsync(phone, "Password123!");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        body!.Phone.Should().Be(PhoneNormalizer.Normalize(phone));
    }

    [Fact, TestCase("AUTH-005")]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var phone = UniquePhone();
        await RegisterAsync(phone, "Password123!");

        var response = await LoginRawAsync(phone, "TotallyWrongPassword!");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("AUTH-006")]
    public async Task Login_WithUnknownPhone_ReturnsUnauthorized()
    {
        var response = await LoginRawAsync(UniquePhone(), "Password123!");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("AUTH-007")]
    public async Task Login_AfterFiveFailedAttempts_LocksAccountOut()
    {
        var phone = UniquePhone();
        await RegisterAsync(phone, "Password123!");

        for (var i = 0; i < 5; i++)
            await LoginRawAsync(phone, "WrongPassword!");

        // Even the correct password should now be rejected while the lockout window is active.
        var response = await LoginRawAsync(phone, "Password123!");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("AUTH-008")]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        var client = AnonymousClient();
        var response = await client.GetAsync("/api/profile");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("AUTH-009")]
    public async Task ProtectedEndpoint_WithMalformedToken_ReturnsUnauthorized()
    {
        var client = AuthedClient("this.is.not-a-real-jwt");
        var response = await client.GetAsync("/api/profile");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("AUTH-010")]
    public async Task ProtectedEndpoint_WithValidToken_ReturnsOk()
    {
        var user = await RegisterAsync();
        var client = AuthedClient(user.Token);
        var response = await client.GetAsync("/api/profile");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── SecurityStamp-based token revocation (US-17, audit A7) ────────────────

    [Fact, TestCase("AUTH-011")]
    public async Task Token_IssuedBeforePasswordChange_IsRevoked_AfterChangePassword()
    {
        var phone = UniquePhone();
        await RegisterAsync(phone, "OldPassword123!");
        var oldToken = (await LoginAsync(phone, "OldPassword123!")).Token;

        var changeResponse = await AuthedClient(oldToken).PostAsJsonAsync("/api/profile/change-password",
            new ChangePasswordDto("OldPassword123!", "NewPassword456!"));
        changeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Same, never-reissued token — ChangePasswordAsync rotated AppUser.SecurityStamp server-side,
        // so the "sstamp" claim baked into this token no longer matches.
        var response = await AuthedClient(oldToken).GetAsync("/api/profile");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact, TestCase("AUTH-012")]
    public async Task Token_IssuedAfterPasswordChange_IsUsable()
    {
        var phone = UniquePhone();
        await RegisterAsync(phone, "OldPassword123!");
        var oldToken = (await LoginAsync(phone, "OldPassword123!")).Token;

        await AuthedClient(oldToken).PostAsJsonAsync("/api/profile/change-password",
            new ChangePasswordDto("OldPassword123!", "NewPassword456!"));

        var newToken = (await LoginAsync(phone, "NewPassword456!")).Token;
        var response = await AuthedClient(newToken).GetAsync("/api/profile");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("AUTH-013")]
    public async Task Token_IssuedBeforePhoneChange_IsRevoked_AfterChangePhone()
    {
        var oldPhone = UniquePhone();
        var newPhone = UniquePhone();
        await RegisterAsync(oldPhone, "Password123!");
        var oldToken = (await LoginAsync(oldPhone, "Password123!")).Token;

        // ChangePhone persists the new phone via UserManager.SetUserNameAsync, which — like
        // ChangePasswordAsync — rotates AppUser.SecurityStamp as a side effect.
        var changeResponse = await AuthedClient(oldToken).PostAsJsonAsync("/api/profile/change-phone",
            new ChangePhoneDto("Password123!", newPhone));
        changeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await AuthedClient(oldToken).GetAsync("/api/profile");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
