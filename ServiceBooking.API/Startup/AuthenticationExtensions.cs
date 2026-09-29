using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Startup;

/// <summary>
/// Identity, JWT bearer (with the per-request stamp/role refresh) and authorization. Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): moved verbatim out of Program.cs, whose call order is unchanged.
/// </summary>
internal static class AuthenticationExtensions
{
    public static void AddAuth(this WebApplicationBuilder builder)
    {
    // Identity
    builder.Services.AddIdentity<AppUser, IdentityRole>(opt =>
        {
            opt.Password.RequireNonAlphanumeric = false;
            opt.Password.RequiredLength = 8;
            opt.Lockout.MaxFailedAccessAttempts = 5;
            opt.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

    // JWT
    var jwtKey = builder.Configuration["Jwt:Key"]!;
    builder.Services.AddAuthentication(opt =>
        {
            opt.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            opt.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(opt =>
        {
            opt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
            };

            // Roles are baked into the JWT at login/register time. Without this, a role change made via
            // PUT /api/admin/users/{id}/roles or POST /api/companies/{id}/members only takes effect after
            // the affected user logs in again — including role *revocations*, which is a real security gap
            // (e.g. a demoted SuperAdmin keeps acting as one until their token expires, up to 7 days later).
            // Re-reading the current roles from the database on every request makes authorization reflect
            // live state instead of a point-in-time snapshot.
            opt.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var principal = context.Principal;
                    var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (userId is null) { context.Fail("Invalid token"); return; }

                    // Cycle 22 (§375 F20, closes §9.21): ONE query for both things this check needs — the
                    // current SecurityStamp and the current role names — instead of FindByIdAsync +
                    // GetRolesAsync. Still read on EVERY request, never cached: a role revoked in the
                    // database is gone on the very next request (SEC-050, CY22-11). No-tracking — the
                    // user row is not left attached to the request's DbContext.
                    var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    var user = await db.Users.AsNoTracking()
                        .Where(u => u.Id == userId)
                        .Select(u => new
                        {
                            u.SecurityStamp,
                            Roles = db.UserRoles.Where(ur => ur.UserId == u.Id)
                                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
                                .ToList(),
                        })
                        .FirstOrDefaultAsync();
                    if (user is null) { context.Fail("User no longer exists"); return; }

                    // A JWT lives up to 7 days, so changing a leaked password must invalidate tokens issued
                    // before it. ASP.NET Identity already rotates SecurityStamp on ChangePasswordAsync/
                    // SetUserNameAsync; we compare a hash of the stamp baked into the token with a hash of
                    // the current one (TokenService.HashSecurityStamp) — the raw stamp is never put in the
                    // token in the first place.
                    var stampHash = principal!.FindFirstValue("sstamp");
                    if (stampHash is null || stampHash != TokenService.HashSecurityStamp(user.SecurityStamp))
                    { context.Fail("Token has been revoked"); return; }

                    var currentRoles = user.Roles;

                    var identity = (ClaimsIdentity)principal!.Identity!;
                    foreach (var staleRoleClaim in identity.FindAll(identity.RoleClaimType).ToList())
                        identity.RemoveClaim(staleRoleClaim);
                    foreach (var role in currentRoles)
                        identity.AddClaim(new Claim(identity.RoleClaimType, role));
                }
            };
        });

    builder.Services.AddAuthorization();
    }
}
