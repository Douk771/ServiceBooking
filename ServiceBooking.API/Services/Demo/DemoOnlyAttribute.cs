using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace ServiceBooking.API.Services.Demo;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §579.4 — marks a route that exists only on a demo instance. Outside demo mode the endpoint answers 404 with an EMPTY body (as if
/// the route were not there at all) BEFORE any action code, binding or validation runs. A resource filter: it runs right after authorization, ahead of
/// model binding. The rate limiter of the demo routes stands aside outside demo mode for the same reason (see <c>RateLimitingExtensions</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DemoOnlyAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<DemoModeOptions>>();
        if (!options.Value.Enabled)
            context.Result = new NotFoundResult();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
