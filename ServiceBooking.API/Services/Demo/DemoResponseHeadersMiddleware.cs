using Microsoft.Extensions.Options;

namespace ServiceBooking.API.Services.Demo;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §579.3, API_CONTRACT_CYCLE28.md §600a — in demo mode every API response carries <c>X-Robots-Tag: noindex, nofollow</c>
/// (nginx of the demo adds the same header to static files and to <c>robots.txt</c>). Set just before the headers go out, so it also marks the 503 of the
/// maintenance middleware and the error responses. A no-op unless <see cref="DemoModeOptions.Enabled"/>.
/// </summary>
public sealed class DemoResponseHeadersMiddleware(RequestDelegate next, IOptions<DemoModeOptions> options)
{
    public const string HeaderName = "X-Robots-Tag";
    public const string HeaderValue = "noindex, nofollow";

    public Task InvokeAsync(HttpContext context)
    {
        if (options.Value.Enabled)
            context.Response.OnStarting(static state =>
            {
                ((HttpContext)state).Response.Headers[HeaderName] = HeaderValue;
                return Task.CompletedTask;
            }, context);

        return next(context);
    }
}
