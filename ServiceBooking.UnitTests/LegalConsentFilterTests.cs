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

        // CYCLE5-BREAKING: "Terms" is renamed to "TermsClient", and a valid manifest now needs all five
        // document types plus all six uiTexts keys to load at all (ARCHITECTURE_CYCLE5.md §43.3). Cycle
        // 13 (§220.1) adds a seventh uiTexts key, PublicAddressNotice, under the same rule. Only
        // Privacy/TermsClient carry `gate: Global` — the only gate this filter ever enforces (§46.3).
        File.WriteAllText(Path.Combine(_root, "legal.json"), """
            {
              "documents": [
                { "type": "Privacy", "version": "v2", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "gate": "Global", "title": "Privacy", "file": "privacy.html" },
                { "type": "TermsClient", "version": "v2", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "gate": "Global", "title": "Terms", "file": "terms.html" },
                { "type": "TermsOwner", "version": "v2", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "gate": "OwnerScope", "title": "Terms owner", "file": "terms-owner.html" },
                { "type": "PdnConsent", "version": "v2", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "gate": "None", "title": "Pdn", "file": "pdn.html", "purposes": [ { "key": "ProviderDelivery", "title": "Доставка" } ] },
                { "type": "ChannelRiskNotice", "version": "v2", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "gate": "None", "title": "Risk", "file": "risk.html" }
              ],
              "uiTexts": [
                { "key": "BookingNotice", "version": "v2", "isDraft": false, "file": "booking-notice.html" },
                { "key": "TemplateAdWarning", "version": "v2", "isDraft": false, "file": "ad-warning.html" },
                { "key": "UnsubscribePage", "version": "v2", "isDraft": false, "file": "unsubscribe.html" },
                { "key": "PhotoConsent", "version": "v2", "isDraft": false, "file": "photo-consent.html" },
                { "key": "HealthDataConsent", "version": "v2", "isDraft": false, "file": "health-consent.html" },
                { "key": "GuardianConfirmation", "version": "v2", "isDraft": false, "file": "guardian.html" },
                { "key": "PublicAddressNotice", "version": "v2", "isDraft": false, "file": "public-address-notice.html" }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(_root, "privacy.html"), "<p>privacy</p>");
        File.WriteAllText(Path.Combine(_root, "terms.html"), "<p>terms</p>");
        File.WriteAllText(Path.Combine(_root, "terms-owner.html"), "<p>owner</p>");
        File.WriteAllText(Path.Combine(_root, "pdn.html"), "<p>pdn</p>");
        File.WriteAllText(Path.Combine(_root, "risk.html"), "<p>risk</p>");
        File.WriteAllText(Path.Combine(_root, "booking-notice.html"), "<p>booking notice</p>");
        File.WriteAllText(Path.Combine(_root, "ad-warning.html"), "<p>ad warning</p>");
        File.WriteAllText(Path.Combine(_root, "unsubscribe.html"), "<p>unsubscribe</p>");
        File.WriteAllText(Path.Combine(_root, "photo-consent.html"), "<p>photo consent</p>");
        File.WriteAllText(Path.Combine(_root, "health-consent.html"), "<p>health consent</p>");
        File.WriteAllText(Path.Combine(_root, "guardian.html"), "<p>guardian</p>");
        File.WriteAllText(Path.Combine(_root, "public-address-notice.html"), "<p>address notice</p>");
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

    // ARCHITECTURE_CYCLE5.md §46.3: TermsOwner has `gate: OwnerScope`, not `Global` — this filter must
    // never block a request over it, even with no "lco" claim at all (a non-owner has none by design).
    // OwnerScope enforcement is a separate mechanism ([RequiresOwnerTerms]), outside this filter's scope.
    [Fact]
    public async Task OnAuthorizationAsync_NoOwnerTermsClaim_ButValidPrivacyAndTermsClaims_DoesNotBlock()
    {
        var context = CreateContext("GET", "/api/bookings/client");
        context.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
                new Claim("lcp", "v2"),
                new Claim("lct", "v2")
                // deliberately no "lco" claim
            ], authenticationType: "Test"));

        await CreateFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    // PdnConsent/ChannelRiskNotice have `gate: None` — they must never block regardless of claim state,
    // and this filter doesn't even have a claim to check them against (§45.2's "Claim'ов для PdnConsent
    // нет и не будет"). This test pins that a caller who HAS accepted Privacy/TermsClient is let through
    // cleanly, proving the loop doesn't trip over the gate:None documents it iterates past.
    [Fact]
    public async Task OnAuthorizationAsync_ValidPrivacyAndTermsClaims_NotBlockedByGateNoneDocuments()
    {
        var context = CreateContext("GET", "/api/bookings/client");
        context.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
                new Claim("lcp", "v2"),
                new Claim("lct", "v2")
            ], authenticationType: "Test"));

        await CreateFilter().OnAuthorizationAsync(context);

        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnAuthorizationAsync_ProfileConsentsEndpoints_AreAllowListed()
    {
        foreach (var (method, path) in new[]
                 {
                     ("GET", "/api/profile/consents"), ("POST", "/api/profile/consents"),
                     ("POST", "/api/profile/consents/revoke"), ("GET", "/api/profile/consents/revoke-preview")
                 })
        {
            var context = CreateContext(method, path);
            await CreateFilter().OnAuthorizationAsync(context);
            context.Result.Should().BeNull($"{method} {path} must stay reachable under a pending Material change (US-68 p.5)");
        }
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
