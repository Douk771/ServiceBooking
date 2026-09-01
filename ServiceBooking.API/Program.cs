using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Fail-fast on obviously-unsafe production configuration (US-10, decision Q4). Runs before anything
// reads these values, and BEFORE builder.Build() — so a misconfigured Production deployment never
// finishes starting instead of silently running with a guessable/placeholder secret.
// CustomWebApplicationFactory (tests) uses ASPNETCORE_ENVIRONMENT=Testing, so this never fires there.
if (builder.Environment.IsProduction())
{
    var jwtKeyValue = builder.Configuration["Jwt:Key"];
    if (string.IsNullOrEmpty(jwtKeyValue) || jwtKeyValue.Length < 32 ||
        jwtKeyValue == "CHANGE_ME_TO_A_LONG_SECRET_KEY_AT_LEAST_32_CHARS")
        throw new InvalidOperationException(
            "Jwt:Key is missing, too short (<32 chars) or still the placeholder. Set Jwt__Key in .env.");

    // Two placeholders reach this check, not one: "Admin12345" ships in appsettings.json, and
    // "CHANGE_ME" ships in .env.production.example. The second one is the more dangerous of the two —
    // it passes a naive placeholder check but fails the Identity password policy (no digit, no
    // lowercase), so the seed below would fail to create the account and the operator would see an
    // obscure downstream error instead of this message.
    var superAdminPassword = builder.Configuration["SuperAdmin:Password"];
    if (string.IsNullOrEmpty(superAdminPassword) ||
        superAdminPassword is "Admin12345" or "CHANGE_ME")
        throw new InvalidOperationException(
            "SuperAdmin:Password is missing or still a placeholder. Set SuperAdmin__Password in .env " +
            "to a real password (at least 8 characters, with a digit, an uppercase and a lowercase letter).");

    // The seeded phone isn't a secret the way the password/JWT key are, so this is a warning, not a
    // fail-fast: a deployment that forgot to override it stays reachable, just with a foreseeable login.
    if (builder.Configuration["SuperAdmin:Phone"] == "+70000000000")
        Console.WriteLine(
            "WARNING: SuperAdmin:Phone is still the placeholder +70000000000. Set SuperAdmin__Phone in .env.");
}

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();

// Swagger / OpenAPI — Development only (US-10): the API surface, including auth flows, shouldn't be
// browsable/probeable in Production or in any deployed environment.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerGen(opt =>
    {
        opt.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "ServiceBooking API",
            Version = "v1",
            Description = "API для SaaS-платформы онлайн-записи на услуги (салоны, барбершопы и т.п.). " +
                          "Поддерживает роли Client, Master, CompanyOwner и SuperAdmin."
        });

        opt.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Вставьте JWT-токен, полученный из /api/auth/login или /api/auth/register (без слова 'Bearer')."
        });
        opt.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                []
            }
        });

        var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
        if (File.Exists(xmlFile)) opt.IncludeXmlComments(xmlFile, includeControllerXmlComments: true);
    });
}

// Database
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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

                var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
                var user = await userManager.FindByIdAsync(userId);
                if (user is null) { context.Fail("User no longer exists"); return; }

                // A JWT lives up to 7 days, so changing a leaked password must invalidate tokens issued
                // before it. ASP.NET Identity already rotates SecurityStamp on ChangePasswordAsync/
                // SetUserNameAsync; we compare a hash of the stamp baked into the token with a hash of
                // the current one (TokenService.HashSecurityStamp) — the raw stamp is never put in the
                // token in the first place. `user` is already loaded for the role refresh below, so this
                // costs no extra query.
                var stampHash = principal!.FindFirstValue("sstamp");
                if (stampHash is null || stampHash != TokenService.HashSecurityStamp(user.SecurityStamp))
                { context.Fail("Token has been revoked"); return; }

                var currentRoles = await userManager.GetRolesAsync(user);

                var identity = (ClaimsIdentity)principal!.Identity!;
                foreach (var staleRoleClaim in identity.FindAll(identity.RoleClaimType).ToList())
                    identity.RemoveClaim(staleRoleClaim);
                foreach (var role in currentRoles)
                    identity.AddClaim(new Claim(identity.RoleClaimType, role));
            }
        };
    });

builder.Services.AddAuthorization();

// CORS for React frontend
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p =>
        p.WithOrigins(builder.Configuration["AllowedOrigins"]?.Split(',') ?? ["http://localhost:5173"])
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials()));

// Services
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<SlotService>();
builder.Services.AddScoped<SubscriptionResolver>();
builder.Services.AddHttpClient<CaptchaService>();

var app = builder.Build();

// In Development the framework's developer exception page already renders the full exception, so the
// handler is only wired up elsewhere. Everywhere else an unhandled exception must still produce a
// machine-readable body with a correlation id instead of an empty 500 — but ONLY for unhandled
// exceptions: every deliberate 400/402/403/404/409 keeps its existing plain-text body, because the SPA's
// error mappers (frontend/src/utils/*Error.ts) parse response.data as a string.
// Registered before UseCors, which is the order the framework documents. The trade-off: when this
// handler fires it clears the response, dropping any CORS headers the inner middleware had set, so a
// cross-origin caller sees a network error instead of this body. That is acceptable here because the
// two never coincide — in Development, where the SPA calls the API cross-origin (localhost:5173 →
// localhost:5000), this handler is not registered at all, and in Production the SPA and the API are
// served same-origin behind nginx (deploy/nginx/ezbook.conf). The one configuration where it would
// bite is serving the SPA from a different host than the API; revisit this ordering if that happens.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        // WriteAsJsonAsync's simple overload always stamps "application/json" over whatever
        // ContentType was set beforehand — the overload that takes an explicit contentType is the
        // only way to actually get "application/problem+json" on the wire.
        await context.Response.WriteAsJsonAsync(
            new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                Title = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError,
                Extensions = { ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier }
            },
            options: null,
            contentType: "application/problem+json");
    }));
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(opt =>
    {
        opt.SwaggerEndpoint("/swagger/v1/swagger.json", "ServiceBooking API v1");
        opt.RoutePrefix = "swagger";
    });
}

app.UseCors();
app.UseStaticFiles(); // serves wwwroot/uploads/... (company logos, etc.)
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Seed roles and super-admin on startup
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await db.Database.MigrateAsync();

    string[] roles = ["Client", "Master", "CompanyOwner", "SuperAdmin"];
    foreach (var role in roles)
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

    // The SuperAdmin, like every account, is now identified by phone (UserName == phone). Email is
    // optional and kept only for display. Config key SuperAdmin:Phone drives login.
    var adminPhone = builder.Configuration["SuperAdmin:Phone"];
    var adminEmail = builder.Configuration["SuperAdmin:Email"];
    var adminPassword = builder.Configuration["SuperAdmin:Password"];
    if (adminPhone is not null && adminPassword is not null)
    {
        var admin = await userManager.FindByNameAsync(adminPhone);
        if (admin is null)
        {
            admin = new AppUser { FirstName = "Super", LastName = "Admin", Email = adminEmail, UserName = adminPhone, PhoneNumber = adminPhone };
            // Without this check a password that fails the Identity policy leaves `admin` unsaved and
            // AddToRoleAsync below then throws something unrelated to the actual cause.
            var createAdmin = await userManager.CreateAsync(admin, adminPassword);
            if (!createAdmin.Succeeded)
                throw new InvalidOperationException(
                    "Failed to seed the SuperAdmin account: " +
                    string.Join("; ", createAdmin.Errors.Select(e => e.Description)));
            await userManager.AddToRoleAsync(admin, "SuperAdmin");
        }
    }
}

app.Run();

// Exposes the implicit Program class so the functional test project can spin up
// the app in-memory via WebApplicationFactory<Program>.
public partial class Program;
