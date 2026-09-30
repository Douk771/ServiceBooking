using Microsoft.Extensions.Options;

namespace ServiceBooking.API.Services.Demo;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §580, API_CONTRACT_CYCLE28.md §600a — while the demo is being reset every <c>/api/*</c> answers 503 <c>text/plain</c>
/// «Демо обновляется, зайдите через минуту» with <c>Retry-After: 60</c> and <c>X-Demo-Resetting: 1</c>, so a visitor sees a message, not an error. Not affected:
/// <c>/api/health/*</c> (the deploy smoke test and the container health check) and <c>GET /api/demo/status</c> (answers 200 with <c>resetting: true</c>).
/// Everything outside <c>/api/*</c> (static files, the SPA) is not touched: the front end draws its own maintenance screen.
///
/// Registered first after <c>UseForwardedHeaders</c>, before authentication and any database access: the tables are locked by the reset's TRUNCATE
/// for its whole duration. A no-op unless <see cref="DemoModeOptions.Enabled"/>.
/// </summary>
public sealed class DemoMaintenanceMiddleware(RequestDelegate next, IOptions<DemoModeOptions> options, DemoMaintenanceFlag flag)
{
    public const string Message = "Демо обновляется, зайдите через минуту";
    public const string HeaderName = "X-Demo-Resetting";

    public async Task InvokeAsync(HttpContext context)
    {
        if (options.Value.Enabled && IsBlockedPath(context.Request.Path, context.Request.Method) && flag.IsResetting(DateTime.UtcNow))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers["Retry-After"] = "60";
            context.Response.Headers[HeaderName] = "1";
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(Message, context.RequestAborted);
            return;
        }

        await next(context);
    }

    /// <summary>Pure: does this request fall under the maintenance answer while a reset is on.</summary>
    public static bool IsBlockedPath(PathString path, string method)
    {
        if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWithSegments("/api/health", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWithSegments("/api/demo/status", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsGet(method)) return false;
        return true;
    }
}
