using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services;

public class TokenService(IConfiguration config)
{
    // privacyVersion/termsVersion: the version of each document THIS user has actually accepted, as
    // recorded in UserConsent at the moment the token is issued (registration, login, or
    // POST /api/legal/accept) — never the document's CURRENT version. LegalConsentFilter compares these
    // claims against the current snapshot on every authenticated request without touching the database
    // (ARCHITECTURE.md §6.3); null (no consent recorded — an account predating cycle C, or Dev/Testing
    // without a loaded manifest) simply omits the claim, which the filter treats as "does not match".
    public string GenerateToken(AppUser user, IList<string> roles, string? privacyVersion = null, string? termsVersion = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            // Phone is the account identifier; email is optional so it's only added when present.
            new("phone", user.PhoneNumber ?? ""),
            new(JwtRegisteredClaimNames.GivenName, user.FirstName),
            new(JwtRegisteredClaimNames.FamilyName, user.LastName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            // A short hash of the user's current SecurityStamp, compared against a hash of the live value
            // on every request in Program.cs's OnTokenValidated — lets a stamp rotation (password/phone
            // change) revoke every token issued before it, even though a JWT itself lives up to 7 days.
            // JWT payloads are base64, not encrypted, and SecurityStamp also seeds ASP.NET Identity's
            // data-protection tokens (e.g. password reset) — so the raw stamp has no business sitting in
            // a client-held artifact even though forging it without the signing key is not possible.
            new("sstamp", HashSecurityStamp(user.SecurityStamp))
        };
        if (!string.IsNullOrEmpty(user.Email))
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));

        if (privacyVersion is not null) claims.Add(new Claim("lcp", privacyVersion));
        if (termsVersion is not null) claims.Add(new Claim("lct", termsVersion));

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// First 8 hex characters of SHA-256(stamp) — short enough to keep the token small, long enough
    /// (32 bits) that an accidental collision between two different stamps is not a practical concern
    /// for a revocation check. Must match Program.cs's OnTokenValidated, which hashes the live
    /// SecurityStamp the same way before comparing.
    /// </summary>
    public static string HashSecurityStamp(string? stamp)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(stamp ?? ""));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }
}
