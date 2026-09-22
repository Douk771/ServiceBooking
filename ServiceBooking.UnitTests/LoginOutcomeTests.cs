using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using ServiceBooking.API.Services;
using IdentitySignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace ServiceBooking.UnitTests;

public class LoginOutcomeTests
{
    [Fact]
    public void FromSignInResult_Succeeded_ReturnsSuccess() =>
        LoginOutcomeMapper.FromSignInResult(IdentitySignInResult.Success).Should().Be(LoginOutcome.Success);

    [Fact]
    public void FromSignInResult_LockedOut_ReturnsLockedOut() =>
        LoginOutcomeMapper.FromSignInResult(IdentitySignInResult.LockedOut).Should().Be(LoginOutcome.LockedOut);

    [Fact]
    public void FromSignInResult_NotAllowed_ReturnsNotAllowed() =>
        LoginOutcomeMapper.FromSignInResult(IdentitySignInResult.NotAllowed).Should().Be(LoginOutcome.NotAllowed);

    [Fact]
    public void FromSignInResult_Failed_ReturnsWrongPassword() =>
        LoginOutcomeMapper.FromSignInResult(IdentitySignInResult.Failed).Should().Be(LoginOutcome.WrongPassword);

    [Fact]
    public void ToErrorResponse_LockedOut_Is423WithFixedBody()
    {
        var response = LoginOutcomeMapper.ToErrorResponse(LoginOutcome.LockedOut) as ObjectResult;
        response!.StatusCode.Should().Be(423);
        response.Value.Should().Be("Account temporarily locked");
    }

    [Fact]
    public void ToErrorResponse_NotAllowed_Is403WithFixedBody()
    {
        var response = LoginOutcomeMapper.ToErrorResponse(LoginOutcome.NotAllowed) as ObjectResult;
        response!.StatusCode.Should().Be(403);
        response.Value.Should().Be("Sign-in not allowed");
    }

    [Theory]
    [InlineData(LoginOutcome.UserNotFound)]
    [InlineData(LoginOutcome.WrongPassword)]
    public void ToErrorResponse_UserNotFoundAndWrongPassword_AreByteForByteIdentical401(LoginOutcome outcome)
    {
        // ARCHITECTURE_CYCLE6.md §42.3: И1 (user not found) and И2 (wrong password) must stay
        // indistinguishable from outside — this is the one distinction that must never leak.
        var response = LoginOutcomeMapper.ToErrorResponse(outcome) as UnauthorizedObjectResult;
        response!.StatusCode.Should().Be(401);
        response.Value.Should().Be("Invalid credentials");
    }

    [Fact]
    public void ToErrorResponse_Success_Throws() =>
        FluentActions.Invoking(() => LoginOutcomeMapper.ToErrorResponse(LoginOutcome.Success))
            .Should().Throw<InvalidOperationException>();
}
