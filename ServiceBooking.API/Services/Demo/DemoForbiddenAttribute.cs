using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Showcase;

namespace ServiceBooking.API.Services.Demo;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §579.5 — marks an action a demo role must not perform (change password or phone, delete the account, send a tariff request,
/// activate the trial, transfer a company). Only a marker: the decision is made by <see cref="DemoForbiddenFilter"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DemoForbiddenAttribute : Attribute
{
}

/// <summary>
/// The global filter behind <see cref="DemoForbiddenAttribute"/> (API_CONTRACT_CYCLE28.md §599). In demo mode, for a caller that is a demo account — a token that
/// carries <c>sb_demo = 1</c> (issued by <c>POST /api/demo/login</c>) OR a token of one of the three demo users (<see cref="ShowcaseDemoRoles.IsDemoUserId"/>) —
/// the marked actions answer 403 <c>text/plain</c> «В демо-версии это действие недоступно.» with
/// <c>X-Demo-Restricted: 1</c> — the one 403 in the project that has a body (SPEC asks for a clear refusal). It is a RESOURCE filter: the refusal comes
/// before model binding, so an invalid body cannot turn it into a 400. Authorization stays ahead of it (an anonymous caller still gets 401).
///
/// The user id is checked as well as the claim because other endpoints re-issue a token to the same user without the claim (<c>POST /api/legal/accept</c>,
/// <c>POST /api/companies</c>): the restriction follows the account, so such a token cannot shed it.
///
/// A visitor who registered himself is neither of those and is not restricted (his data is wiped at night anyway); outside demo mode nothing changes.
/// </summary>
public sealed class DemoForbiddenFilter(IOptions<DemoModeOptions> options) : IAsyncResourceFilter
{
    public const string ClaimType = "sb_demo";
    public const string Message = "В демо-версии это действие недоступно.";
    public const string HeaderName = "X-Demo-Restricted";

    public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        if (options.Value.Enabled
            && context.ActionDescriptor.EndpointMetadata.OfType<DemoForbiddenAttribute>().Any()
            && IsDemoToken(context.HttpContext.User))
        {
            context.HttpContext.Response.Headers[HeaderName] = "1";
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status403Forbidden,
                ContentType = "text/plain; charset=utf-8",
                Content = Message,
            };
            return Task.CompletedTask;
        }

        return next();
    }

    /// <summary>Pure: is the caller a demo account — it carries the demo claim, or it is one of the demo users (a re-issued token has no claim).</summary>
    public static bool IsDemoToken(ClaimsPrincipal user) =>
        user.Identity is { IsAuthenticated: true }
        && (user.FindFirst(ClaimType)?.Value == "1" || ShowcaseDemoRoles.IsDemoUserId(user.FindFirst(ClaimTypes.NameIdentifier)?.Value));
}
