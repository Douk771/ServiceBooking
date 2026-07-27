using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Auth;
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
            new RegisterDto("Ivan", "Petrov", phone, "Password123!", null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        body!.Token.Should().NotBeNullOrEmpty();
        body.Phone.Should().Be(phone);
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
        body!.Phone.Should().Be(phone);
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
}
