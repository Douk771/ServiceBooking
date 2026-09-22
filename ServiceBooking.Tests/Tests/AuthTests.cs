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

    // US-60 (API_CONTRACT_CYCLE6.md §39.6): this is the exact scenario the cycle exists for — a
    // password rejected by Identity's policy must come back as a JSON ARRAY of {code, description}
    // objects, not the plain-text 400 every other validation failure on this endpoint uses. Before this
    // cycle the whole suite only ever registered with valid passwords, so nothing exercised this branch
    // at all (CURRENT_STATE.md notes this gap explicitly as what let the underlying bug reach the stand).
    [Fact, TestCase("AUTH-014")]
    public async Task Register_WithAllDigitPassword_ReturnsBadRequestArrayOfIdentityErrors()
    {
        var response = await RegisterRawAsync(UniquePhone(), "12345678");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json",
            "Identity-rejected passwords/logins are the ONE 400 shape on this endpoint that is JSON, " +
            "not text/plain (API_CONTRACT_CYCLE6.md §39.6) — the client must be able to tell them apart");

        var errors = (await response.Content.ReadFromJsonAsync<List<IdentityErrorDto>>())!;
        errors.Should().NotBeNullOrEmpty();
        errors.Select(e => e.Code).Should().Contain("PasswordRequiresLower");
        errors.Select(e => e.Code).Should().Contain("PasswordRequiresUpper");
    }

    private sealed record IdentityErrorDto(string Code, string Description);

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
        // US-60 (ARCHITECTURE_CYCLE6.md §42.3, API_CONTRACT_CYCLE6.md §39.3): lockout is now a
        // distinguishable 423, not the same 401 as a plain wrong password — this is the whole point of
        // the four-way split login now makes (401/423/403/429/5xx).
        var phone = UniquePhone();
        await RegisterAsync(phone, "Password123!");

        // ASP.NET Core Identity's own CheckPasswordSignInAsync locks the account out AS PART OF the
        // failed attempt that pushes AccessFailedCount to Lockout:MaxFailedAccessAttempts (5,
        // Program.cs) — that fifth wrong attempt itself already comes back 423, not 401. Only the first
        // four are still plain "wrong password".
        for (var i = 0; i < 4; i++)
        {
            var attempt = await LoginRawAsync(phone, "WrongPassword!");
            attempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        var fifthAttempt = await LoginRawAsync(phone, "WrongPassword!");
        fifthAttempt.StatusCode.Should().Be(HttpStatusCode.Locked);

        // Even the correct password should now be rejected while the lockout window is active — with
        // 423 Locked, not 401, per API_CONTRACT_CYCLE6.md §39.3.
        var response = await LoginRawAsync(phone, "Password123!");
        response.StatusCode.Should().Be(HttpStatusCode.Locked);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("Account temporarily locked");
    }

    // US-61/Q4 (ARCHITECTURE_CYCLE6.md §48.2 "Login" bullet, §48.4): the Russian-only format is a
    // restriction on creating NEW data — it must not lock out accounts that already have a foreign
    // number (pre-cycle registrations, or ones an admin created).
    [Fact, TestCase("AUTH-015")]
    public async Task Login_WithPreExistingForeignPhoneAccount_Succeeds()
    {
        var digits = new string(Guid.NewGuid().ToString("N").Where(char.IsDigit).Take(6).ToArray()).PadRight(6, '1');
        var foreignPhone = "447911" + digits; // UK-shaped: 12 digits, not "7" + 10 digits
        const string password = "Password123!";
        await CreateRawAccountAsync(foreignPhone, password);

        var response = await LoginRawAsync(foreignPhone, password);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the country restriction (§48.1) applies only where new data is created, never to login");
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        body!.Phone.Should().Be(foreignPhone);
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
