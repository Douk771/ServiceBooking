using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ServiceBooking.API.Services;

/// <summary>
/// The five outcomes <c>POST /api/auth/login</c> can reach (ARCHITECTURE_CYCLE6.md §42.1 p.2):
/// three of them collapsed into an indistinguishable 401 before this cycle. Kept as a pure enum +
/// mapper, testable without HTTP (ARCHITECTURE_CYCLE6.md §42.6).
/// </summary>
public enum LoginOutcome
{
    Success,
    UserNotFound,
    WrongPassword,
    LockedOut,
    NotAllowed,
}

public static class LoginOutcomeMapper
{
    /// <summary>
    /// Maps a <see cref="Microsoft.AspNetCore.Identity.SignInResult"/> (already known to have failed) to the outcome that
    /// determines the HTTP response. <see cref="LoginOutcome.UserNotFound"/> is not derivable from a
    /// SignInResult — the caller supplies it directly when <c>FindByNameAsync</c> returns null.
    /// </summary>
    public static LoginOutcome FromSignInResult(Microsoft.AspNetCore.Identity.SignInResult result)
    {
        if (result.Succeeded) return LoginOutcome.Success;
        if (result.IsLockedOut) return LoginOutcome.LockedOut;
        if (result.IsNotAllowed) return LoginOutcome.NotAllowed;
        return LoginOutcome.WrongPassword;
    }

    /// <summary>
    /// Maps an outcome to the HTTP response body for <c>POST /api/auth/login</c>
    /// (API_CONTRACT_CYCLE6.md §39; ARCHITECTURE_CYCLE6.md §42.3.1). <see cref="LoginOutcome.Success"/>
    /// has no error response — the controller returns 200 with the token itself, not through this
    /// mapper.
    /// </summary>
    public static ActionResult ToErrorResponse(LoginOutcome outcome) => outcome switch
    {
        LoginOutcome.UserNotFound => new UnauthorizedObjectResult("Invalid credentials"),
        LoginOutcome.WrongPassword => new UnauthorizedObjectResult("Invalid credentials"),
        LoginOutcome.LockedOut => new ObjectResult("Account temporarily locked") { StatusCode = StatusCodes.Status423Locked },
        LoginOutcome.NotAllowed => new ObjectResult("Sign-in not allowed") { StatusCode = StatusCodes.Status403Forbidden },
        LoginOutcome.Success => throw new InvalidOperationException("Success has no error response."),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
