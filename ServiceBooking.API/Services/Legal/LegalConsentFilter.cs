using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Legal;

/// <summary>
/// Global gate on every authenticated request (US-37, ARCHITECTURE.md §6.3): compares the "lcp"/"lct"
/// claims baked into the caller's JWT (TokenService) against LegalDocumentProvider's in-memory snapshot.
/// No database access — SPEC §7 p.1 forbids a per-request query here, and both sides of the comparison
/// are already in memory (the claims on the validated token, the snapshot in the singleton provider).
///
/// Runs as an IAsyncAuthorizationFilter, AFTER authentication has already resolved User (so an anonymous
/// caller — including a guest booking — is trivially exempt) and BEFORE the controller action, via
/// options.Filters.Add&lt;LegalConsentFilter&gt;() in Program.cs. It never touches [Authorize]/[AllowAnonymous]
/// semantics and never returns 401/403 — those stay the authorization pipeline's job (§6.5).
/// </summary>
public class LegalConsentFilter(LegalDocumentProvider provider) : IAsyncAuthorizationFilter
{
    private const string BlockedMessage = "Примите обновлённые документы, чтобы продолжить.";

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var httpContext = context.HttpContext;

        // Anonymous callers carry no consent claims to check — a guest's consent is recorded on the
        // booking itself (ARCHITECTURE.md §6.2), not enforced here.
        if (httpContext.User.Identity is not { IsAuthenticated: true })
            return Task.CompletedTask;

        if (IsAllowListed(httpContext.Request))
            return Task.CompletedTask;

        var snapshot = provider.Current;
        // No manifest loaded — only reachable outside Production (fail-fast prevents it there,
        // ARCHITECTURE.md §4.4/§13). Nothing to compare against, so nothing to enforce; the dedicated
        // legal endpoints answer 503 on their own.
        if (snapshot is null)
            return Task.CompletedTask;

        var blockedByMaterialMismatch = false;
        foreach (var doc in snapshot.Documents.Values)
        {
            var acceptedVersion = httpContext.User.FindFirst(ClaimNameFor(doc.Type))?.Value;
            if (acceptedVersion == doc.Version) continue; // this document's claim matches the current version

            // Editorial mismatches never block — API_CONTRACT.md §3 surfaces them as showBanner via
            // GET /api/legal/consent-status instead. Material overrides Editorial: one substantive
            // change among several pending is enough to gate the whole request.
            if (doc.ChangeKind == LegalChangeKind.Material)
                blockedByMaterialMismatch = true;
        }

        if (blockedByMaterialMismatch)
        {
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status451UnavailableForLegalReasons,
                ContentType = "text/plain; charset=utf-8",
                Content = BlockedMessage
            };
        }

        return Task.CompletedTask;
    }

    /// <summary>JWT claim type carrying the accepted version of the given document (TokenService).</summary>
    public static string ClaimNameFor(LegalDocumentType type) => type switch
    {
        LegalDocumentType.Privacy => "lcp",
        LegalDocumentType.Terms => "lct",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    // API_CONTRACT.md §0.4. A caller locked out by a pending Material change must still be able to read
    // what changed, accept it, log out (implicitly — no server-side session to block), see who they are
    // (GET /api/profile, needed to even RENDER the blocking screen), delete their account, and export
    // their data (ARCHITECTURE.md §19.4: a right that must not be conditioned on accepting a new policy).
    private static bool IsAllowListed(HttpRequest request)
    {
        var path = request.Path.Value ?? "";
        var method = request.Method;

        if (path.StartsWith("/api/legal/", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWith("/api/health/", StringComparison.OrdinalIgnoreCase)) return true;
        // API_CONTRACT_CYCLE5.md §39 / row 45 of the status-code table: the legal gate must not apply
        // to GET /api/pricing, including for an authenticated caller with a pending Material consent —
        // the frontend deliberately routes such a user to /pricing anyway (CONSENT_GATE_BYPASS_PATHS).
        if (HttpMethods.IsGet(method) && string.Equals(path, "/api/pricing", StringComparison.OrdinalIgnoreCase)) return true;
        // API_CONTRACT.md §0.4 allow-lists POST /api/auth/**, not the whole path regardless of method.
        // AuthController only exposes POST today, so this was behaviorally identical to a blanket
        // path match — but a future GET /api/auth/* (e.g. a "who am I" probe) would have silently
        // landed in the allow-list too. Matching the contract exactly closes that off now.
        if (HttpMethods.IsPost(method) && path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase)) return true;
        if (HttpMethods.IsGet(method) && string.Equals(path, "/api/profile", StringComparison.OrdinalIgnoreCase)) return true;
        if (HttpMethods.IsPost(method) && string.Equals(path, "/api/profile/delete-account", StringComparison.OrdinalIgnoreCase)) return true;
        if (HttpMethods.IsGet(method) && string.Equals(path, "/api/profile/export", StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}
