using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.Services.Legal;

namespace ServiceBooking.UnitTests;

/// <summary>
/// GetDocument is called directly (no HTTP server, no DB) against a controller built from a real
/// LegalDocumentProvider pointed at a temp on-disk manifest — everything else the controller needs
/// (ConsentLedger, UserManager, TokenService) is unused by this action, so it's never constructed.
///
/// Contract check, cycle 11, round 1 (before code-reviewer): Enum.TryParse&lt;LegalDocumentType&gt; parses
/// ANY numeric string ("5", "99", "-1") as "valid" even when the value is outside the five defined
/// members — it only rejects non-numeric garbage. That let /api/legal/documents/5 and /documents/99 fall
/// through to the 503 "temporarily unavailable" branch instead of the contract's documented 404 for an
/// unrecognized type. Fixed by adding an Enum.IsDefined check alongside TryParse.
///
/// Contract check, cycle 11, round 2 (schemathesis re-run after that fix): Enum.IsDefined alone still let
/// an IN-RANGE numeric string ("0") through as if it were a name — "0" parsed to LegalDocumentType.Privacy
/// and returned 200, even though the contract's {type} is a closed enum of NAMED string values and "0" is
/// not one of them. Fixed by rejecting any input that parses as a plain integer before ever trying
/// Enum.TryParse.
/// </summary>
public class LegalControllerGetDocumentTests : IDisposable
{
    private readonly string _root;

    public LegalControllerGetDocumentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sb-legal-getdoc-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);

        File.WriteAllText(Path.Combine(_root, "legal.json"), """
            {
              "documents": [
                { "type": "Privacy", "version": "v1", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "gate": "Global", "title": "Privacy", "file": "privacy.html" },
                { "type": "TermsClient", "version": "v1", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "gate": "Global", "title": "Terms", "file": "terms.html" },
                { "type": "TermsOwner", "version": "v1", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "gate": "OwnerScope", "title": "Terms owner", "file": "terms-owner.html" },
                { "type": "PdnConsent", "version": "v1", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "gate": "None", "title": "Pdn", "file": "pdn.html", "purposes": [ { "key": "ProviderDelivery", "title": "Доставка" } ] },
                { "type": "ChannelRiskNotice", "version": "v1", "effectiveFrom": "2026-09-08", "isDraft": false, "changeKind": "Material", "gate": "None", "title": "Risk", "file": "risk.html" }
              ],
              "uiTexts": [
                { "key": "BookingNotice", "version": "v1", "isDraft": false, "file": "booking-notice.html" },
                { "key": "TemplateAdWarning", "version": "v1", "isDraft": false, "file": "ad-warning.html" },
                { "key": "UnsubscribePage", "version": "v1", "isDraft": false, "file": "unsubscribe.html" },
                { "key": "PhotoConsent", "version": "v1", "isDraft": false, "file": "photo-consent.html" },
                { "key": "HealthDataConsent", "version": "v1", "isDraft": false, "file": "health-consent.html" },
                { "key": "GuardianConfirmation", "version": "v1", "isDraft": false, "file": "guardian.html" }
              ]
            }
            """);
        foreach (var file in new[]
                 {
                     "privacy.html", "terms.html", "terms-owner.html", "pdn.html", "risk.html",
                     "booking-notice.html", "ad-warning.html", "unsubscribe.html", "photo-consent.html",
                     "health-consent.html", "guardian.html"
                 })
        {
            File.WriteAllText(Path.Combine(_root, file), "<p>text</p>");
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private LegalController CreateController()
    {
        var provider = new LegalDocumentProvider(
            Options.Create(new LegalOptions { Root = _root, ReloadSeconds = 0 }),
            new FakeWebHostEnvironment(),
            NullLogger<LegalDocumentProvider>.Instance);
        provider.LoadAtStartup();

        // ConsentLedger/UserManager/TokenService are only touched by other actions (Accept, GetConsentStatus).
        var controller = new LegalController(provider, null!, null!, null!)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
            }
        };
        return controller;
    }

    [Theory]
    [InlineData("Privacy")]
    [InlineData("privacy")]
    public void GetDocument_KnownType_ReturnsOk(string type)
    {
        var result = CreateController().GetDocument(type);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Theory]
    [InlineData("5")] // one past the last defined member (ChannelRiskNotice = 4)
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("0")] // in-range numeric alias for Privacy — must still be rejected, {type} is a closed string enum
    [InlineData("1")] // in-range numeric alias for TermsClient
    [InlineData("NotARealType")]
    public void GetDocument_UnknownType_Returns404NotServiceUnavailable(string type)
    {
        var result = CreateController().GetDocument(type);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
