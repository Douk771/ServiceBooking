using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.UnitTests;

/// <summary>
/// No DB, no HTTP server — the filter is invoked directly against a hand-built
/// AuthorizationFilterContext, exactly the way ASP.NET Core would call it, but without hosting anything.
/// </summary>
public class LegalConsentFilterTests : IDisposable
{
    private readonly string _root;

    public LegalConsentFilterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sb-legal-filter-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);

        File.WriteAllText(Path.Combine(_root, "legal.json"), """
            {
              "documents": [
                { "type": "Privacy", "version": "v2", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "title": "Privacy", "file": "privacy.html" },
                { "type": "Terms", "version": "v2", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "title": "Terms", "file": "terms.html" }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(_root, "privacy.html"), "<p>privacy</p>");
        File.WriteAllText(Path.Combine(_root, "terms.html"), "<p>terms</p>");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private LegalConsentFilter CreateFilter()
    {
        var provider = new LegalDocumentProvider(
            Options.Create(new LegalOptions { Root = _root, ReloadSeconds = 0 }),
            new FakeWebHostEnvironment(),
            NullLogger<LegalDocumentProvider>.Instance);
        provider.LoadAtStartup();
        return new LegalConsentFilter(provider);
    }

    // An authenticated caller with no "lcp"/"lct" claims at all is exactly what a token issued before
    // this consent scheme existed would look like — a Material mismatch, must block.
    private static AuthorizationFilterContext CreateContext(string method, string path)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = method;
        httpContext.Request.Path = path;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-1")], authenticationType: "Test"));

        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
            httpContext, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
        return new AuthorizationFilterContext(actionContext, []);
    }

    [Fact]
    public async Task OnAuthorizationAsync_MaterialMismatch_Blocks451ByDefault()
    {
        var context = CreateContext("GET", "/api/bookings/client");
        await CreateFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.ContentResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status451UnavailableForLegalReasons);
    }

    [Fact]
    public async Task OnAuthorizationAsync_PostAuth_IsAllowListed()
    {
        // API_CONTRACT.md §0.4: POST /api/auth/** is exempt regardless of consent state.
        var context = CreateContext("POST", "/api/auth/register");
        await CreateFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    // Blocker fix: previously ANY method under /api/auth/ was exempt, not just POST. A future
    // GET /api/auth/* would have silently ridden along in the allow-list; this pins that it no longer
    // does, matching API_CONTRACT.md §0.4's "POST /api/auth/**" literally.
    [Fact]
    public async Task OnAuthorizationAsync_GetAuth_IsNotAllowListed()
    {
        var context = CreateContext("GET", "/api/auth/whoami");
        await CreateFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeOfType<Microsoft.AspNetCore.Mvc.ContentResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status451UnavailableForLegalReasons);
    }

    [Fact]
    public async Task OnAuthorizationAsync_AnonymousCaller_NeverBlocked()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/api/bookings/client";
        // No User set → Identity.IsAuthenticated is false by default.
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(
            httpContext, new RouteData(), new ActionDescriptor(), new ModelStateDictionary());
        var context = new AuthorizationFilterContext(actionContext, []);

        await CreateFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ServiceBooking.UnitTests";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Testing";
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = null!;
    }
}
