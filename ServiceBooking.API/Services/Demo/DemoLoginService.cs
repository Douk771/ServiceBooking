using Microsoft.AspNetCore.Identity;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Demo;

public enum DemoLoginStatus
{
    Ok,
    UnknownRole,

    /// <summary>The demo data is not there yet (first start, before <c>ops demo reset</c>).</summary>
    NotSeeded,
}

public sealed record DemoLoginResult(DemoLoginStatus Status, AuthResponseDto? Response = null);

/// <summary>
/// ARCHITECTURE_CYCLE28.md §579.4, API_CONTRACT_CYCLE28.md §598 — the passwordless sign-in of the demo: finds the ready account of a role by its stable id
/// (<see cref="ShowcaseDemoRoles"/>) and issues a token exactly like <c>POST /api/auth/login</c> does, with two differences:
/// the claim <c>sb_demo = 1</c> (the demo filter reads it), and the legal-document versions are the CURRENT ones (owner terms for the owner) instead of being
/// read from the consent journal — the accounts are fictional and nothing is written to the journal, so the gate of HTTP 451 never stops a demo role.
/// Reachable only through <c>[DemoOnly]</c> routes.
/// </summary>
public sealed class DemoLoginService(UserManager<AppUser> users, TokenService tokens, LegalDocumentProvider legal)
{
    public async Task<DemoLoginResult> LoginAsync(string? role)
    {
        var userId = ShowcaseDemoRoles.UserIdOf(role?.Trim().ToLowerInvariant());
        if (userId is null) return new DemoLoginResult(DemoLoginStatus.UnknownRole);

        var user = await users.FindByIdAsync(userId);
        // Only an account the generator made (marked) may be entered without a password; anything else under that id would be a corrupted demo.
        if (user is null || !user.IsShowcase) return new DemoLoginResult(DemoLoginStatus.NotSeeded);

        var roles = await users.GetRolesAsync(user);
        var snapshot = legal.Current;
        var privacy = snapshot?.Get(LegalDocumentType.Privacy)?.Version;
        var terms = snapshot?.Get(LegalDocumentType.TermsClient)?.Version;
        var ownerTerms = string.Equals(role?.Trim(), ShowcaseDemoRoles.Owner, StringComparison.OrdinalIgnoreCase)
            ? snapshot?.Get(LegalDocumentType.TermsOwner)?.Version
            : null;

        var token = tokens.GenerateToken(user, roles, privacy, terms, ownerTerms, demo: true);
        return new DemoLoginResult(DemoLoginStatus.Ok,
            new AuthResponseDto(token, user.Id, user.PhoneNumber ?? "", user.Email, user.FirstName, user.LastName, roles, user.PhoneNumberConfirmed));
    }
}
