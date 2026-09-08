using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Health;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.API.Services.Scheduling.Tasks;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Serilog replaces the host logger entirely (US-45, ARCHITECTURE.md §11.1) — both stdout (docker logs)
// and a rolling file (survives container recreation, docker logs doesn't). CompactJsonFormatter on
// both, so a log line is one JSON object whether it's read live or grepped from disk a day later.
// PhoneMaskingEnricher is the second/third rung of §11.3's defence; it self-limits to Warning+/exception
// events, so it costs nothing on the Information-level "request completed" line every request produces.
builder.Host.UseSerilog((context, services, loggerConfig) =>
{
    loggerConfig
        .MinimumLevel.Information()
        // EF Core logs every SQL statement (with parameter values — i.e. phone numbers, names) at
        // Information by default; without this override that alone would be both a wall of noise and a
        // PII leak that bypasses the enricher (enrichers see the RENDERED event, but EF's own query
        // logger writes parameter values into properties the enricher would still catch — this override
        // means it never has to).
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.With<PhoneMaskingEnricher>()
        .WriteTo.Console(new CompactJsonFormatter())
        .WriteTo.File(new CompactJsonFormatter(), Path.Combine("logs", "app-.json"),
            rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14,
            fileSizeLimitBytes: 100 * 1024 * 1024, rollOnFileSizeLimit: true);

    // Sink to GlitchTip via the Sentry protocol (ARCHITECTURE.md §11.4). Empty DSN → sink not
    // registered at all, same pattern as CaptchaService.IsEnforced: Development/Testing carry no DSN by
    // configuration, so nothing leaves the process there, and a Production deployment that hasn't set
    // one up yet still starts and runs (US-45 pp. 8–9).
    var sentryDsn = context.Configuration["Sentry:Dsn"];
    if (!string.IsNullOrWhiteSpace(sentryDsn))
    {
        loggerConfig.WriteTo.Sentry(o =>
        {
            o.Dsn = sentryDsn;
            o.MinimumEventLevel = LogEventLevel.Error; // only real failures become GlitchTip issues
            o.MinimumBreadcrumbLevel = LogEventLevel.Warning;
            o.SendDefaultPii = false;
            o.Environment = context.HostingEnvironment.EnvironmentName;
            o.Release = context.Configuration["Sentry:Release"];
            // Same masking rule as the log pipeline (§11.3 p.2), applied to the top-level free-text
            // message — the one place Sentry's own SDK doesn't go through Serilog's property pipeline.
            // Individual exception frame messages/stack traces are not scanned (out of scope for this
            // cycle's pass); by construction they should never carry raw request data in the first
            // place, since .NET stack traces contain source locations, not caught values.
            o.SetBeforeSend((sentryEvent, _) =>
            {
                if (sentryEvent.Message?.Message is { } message)
                    sentryEvent.Message.Message = LogMasking.MaskPhoneSequences(message);
                return sentryEvent;
            });
        });
    }
});

// Fail-fast on obviously-unsafe deployment configuration (US-10 → US-48, ARCHITECTURE.md §13). Runs
// before anything reads these values, and BEFORE builder.Build() — so a misconfigured deployment never
// finishes starting instead of silently running with a guessable/placeholder secret.
// CustomWebApplicationFactory (tests) uses ASPNETCORE_ENVIRONMENT=Testing, so this never fires there.
//
// Allow-list, not deny-list (US-48, cycle C): an environment nobody told this code about yet (Staging,
// Preview, Demo) must be treated as production-grade. Only the two environments we KNOW are developer
// contexts are exempt — everything else gets the full set of checks, including anything introduced later.
var isDeveloperEnvironment = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
if (!isDeveloperEnvironment)
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

    // US-19 p.4 / ARCHITECTURE.md §3.4: a private root that resolves inside wwwroot would be served to
    // anyone with the link by UseStaticFiles below — the one realistic way client photos leak by
    // accident (risk R2) is a typo'd .env, so this must stop the deployment, not just log a warning.
    // Duplicates FileStorage's own default-resolution logic rather than resolving it through the DI
    // container, which isn't built yet at this point in Program.cs.
    var contentRoot = builder.Environment.ContentRootPath;
    var configuredPrivateRoot = builder.Configuration["Storage:PrivateRoot"];
    var privateRoot = string.IsNullOrEmpty(configuredPrivateRoot)
        ? Path.Combine(contentRoot, "App_Data", "private-uploads")
        : configuredPrivateRoot;
    var privateRootFull = Path.GetFullPath(privateRoot);
    var wwwrootFull = Path.GetFullPath(Path.Combine(contentRoot, "wwwroot")) + Path.DirectorySeparatorChar;
    if (privateRootFull.StartsWith(wwwrootFull, StringComparison.Ordinal))
        throw new InvalidOperationException(
            "Storage:PrivateRoot resolves inside wwwroot — client photos would be served by " +
            "UseStaticFiles to anyone with the link. Set Storage__PrivateRoot to a path outside wwwroot.");
}

builder.Services.AddControllers(options =>
        // Global, runs on every authenticated request (US-37, ARCHITECTURE.md §6.3) — a TypeFilter, so
        // LegalDocumentProvider is resolved from DI per-request rather than requiring a service-locator
        // pattern here.
        options.Filters.Add<ServiceBooking.API.Services.Legal.LegalConsentFilter>())
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

// Image uploads (US-19, US-25): FileStorage holds no per-request state (just the two configured roots),
// so it's a singleton; ImageUploadService is scoped only because everything else in this layer is —
// it has no state of its own either.
builder.Services.AddSingleton<FileStorage>();
builder.Services.AddScoped<ImageUploadService>();

// Legal documents (US-36, ARCHITECTURE.md §4): a singleton so the in-memory snapshot is shared by every
// request instead of re-parsed per scope — the whole point of the ReloadSeconds cache (§4.3).
builder.Services.Configure<LegalOptions>(builder.Configuration.GetSection("Legal"));
builder.Services.AddSingleton<LegalDocumentProvider>();

// ForwardedHeaders (US-42, ARCHITECTURE.md §9.1): the container only ever sees the docker bridge's
// gateway address as RemoteIpAddress, never the browser's — nginx sits in front of it. The default
// KnownNetworks/KnownProxies ship with loopback pre-trusted, which happens to be exactly the address
// every request arrives from inside THIS container, so leaving the defaults in place would mean
// trusting X-Forwarded-For from anyone who can reach the port at all. Both are cleared, then only what
// the operator declared in config is trusted back in.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1; // exactly one hop: nginx. More hops in the chain would be spoofable.
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
    foreach (var cidr in builder.Configuration.GetSection("ForwardedHeaders:TrustedNetworks").Get<string[]>() ?? [])
        o.KnownNetworks.Add(IPNetwork.Parse(cidr));
});

// Rate limiting (US-19 p.5, US-42, ARCHITECTURE.md §9–§10). Five named policies, each applied only via
// [EnableRateLimiting("...")] on its specific endpoint(s) — app.UseRateLimiter() below is a no-op for
// everything else, so health checks are unaffected by construction, without needing an explicit
// exclusion (§9.3).
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // uploads: unchanged from cycle B — same policy name, same partition key, same rejection text
    // (uploadError.ts on the frontend is written against this exact string).
    o.AddPolicy("uploads", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = ctx.RequestServices.GetRequiredService<IConfiguration>().GetValue("Uploads:PerUserPerMinute", 10),
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0 // reject immediately rather than queue — no benefit to making the caller wait
        }));

    // auth-login / auth-register: partitioned purely by the (ForwardedHeaders-resolved) caller IP —
    // there is no account yet to key on for register, and for login keying on IP is the point (Identity
    // lockout already protects a single account; this protects against credential-stuffing across many
    // accounts from one address).
    o.AddPolicy("auth-login", ctx => IpWindowPolicy(ctx, "auth-login", defaultPermitLimit: 10, defaultWindowMinutes: 1));
    o.AddPolicy("auth-register", ctx => IpWindowPolicy(ctx, "auth-register", defaultPermitLimit: 5, defaultWindowMinutes: 60));

    // booking-create: an anonymous caller is keyed and capped by IP (guest booking spam); an
    // authenticated caller is keyed by their own user id with a limit an order of magnitude higher —
    // staff recording ten walk-ins in a row never touches the guest limit, because they are not counted
    // by IP at all (ARCHITECTURE.md §9.2). "Authenticated" here only means the JWT parsed — this policy
    // has no idea whether the caller is staff of the target company, and per SPEC §7 p.1 it must not
    // query the database to find out.
    o.AddPolicy("booking-create", ctx =>
    {
        var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var windowMinutes = config.GetValue("RateLimits:booking-create:WindowMinutes", 60);
        var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null)
            return RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("RateLimits:booking-create:PermitLimit", 120),
                Window = TimeSpan.FromMinutes(windowMinutes),
                QueueLimit = 0
            });

        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter($"ip:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimits:booking-create:AnonymousPermitLimit", 10),
            Window = TimeSpan.FromMinutes(windowMinutes),
            QueueLimit = 0
        });
    });

    // data-export: keyed by user id only — the endpoint requires [Authorize], there is no anonymous case.
    o.AddPolicy("data-export", ctx =>
    {
        var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimits:data-export:PermitLimit", 3),
            Window = TimeSpan.FromMinutes(config.GetValue("RateLimits:data-export:WindowMinutes", 1440)),
            QueueLimit = 0
        });
    });

    // 4xx bodies are plain text everywhere in this API (ARCHITECTURE.md §14) — the built-in rejection
    // response is empty, so OnRejected has to write the body itself or the frontend's *Error.ts mappers
    // couldn't tell a 429 apart from a 403. Branches by policy name so each surfaces its own Russian
    // text (ARCHITECTURE.md §9.4) — "uploads" keeps its original English string unchanged, since
    // uploadError.ts is written against that exact value.
    o.OnRejected = async (ctx, cancellationToken) =>
    {
        var policyName = ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        var message = policyName switch
        {
            "auth-login" => "Слишком много попыток входа. Повторите через минуту.",
            "auth-register" => "Слишком много регистраций с этого адреса. Повторите позже.",
            "booking-create" => "Слишком много записей с этого адреса. Повторите позже.",
            "data-export" => "Выгрузка доступна не чаще трёх раз в сутки.",
            _ => "Too many uploads. Try again in a minute."
        };
        await ctx.HttpContext.Response.WriteAsync(message, cancellationToken);
    };
});

// Shared by auth-login/auth-register: partition purely by the caller's (ForwardedHeaders-resolved) IP,
// PermitLimit/WindowMinutes read from RateLimits:{policyName}:* with the given defaults.
static RateLimitPartition<string> IpWindowPolicy(
    HttpContext ctx, string policyName, int defaultPermitLimit, int defaultWindowMinutes)
{
    var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    return RateLimitPartition.GetFixedWindowLimiter($"ip:{ip}", _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = config.GetValue($"RateLimits:{policyName}:PermitLimit", defaultPermitLimit),
        Window = TimeSpan.FromMinutes(config.GetValue($"RateLimits:{policyName}:WindowMinutes", defaultWindowMinutes)),
        QueueLimit = 0
    });
}

// Health checks (US-43, ARCHITECTURE.md §10): "live" never touches anything and always answers 200 —
// it just proves the process is up and can accept HTTP. "ready" additionally proves the database is
// reachable and migrated, tagged "ready" so MapHealthChecks below can select just this one check.
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadyHealthCheck>("database", tags: ["ready"]);

// Scheduled background tasks (US-21): one BackgroundService that ticks whatever IScheduledTask
// implementations are registered — adding a second task later is exactly one more line like this one,
// the runner itself never changes (ARCHITECTURE.md §8.1).
builder.Services.AddScoped<IScheduledTask, PhotoRetentionCleanupTask>();
builder.Services.AddHostedService<ScheduledTaskRunner>();

var app = builder.Build();

// FIRST in the pipeline, before anything reads Connection.RemoteIpAddress — the rate limiter's IP
// partitions (auth-login, auth-register, booking-create) and Serilog's request logging both need the
// REAL client address, not nginx's, and both run later in this pipeline (ARCHITECTURE.md §9.1).
app.UseForwardedHeaders();

// Two more fail-fast checks (US-42, US-36 → US-48, ARCHITECTURE.md §13), added in cycle C. Unlike the
// block above, both need a constructed service provider (IPNetwork parsing for the first is already
// done by ForwardedHeadersOptions above; loading legal.json goes through LegalDocumentProvider), so they
// run here, after builder.Build(), rather than being folded into the pre-Build block.
if (!isDeveloperEnvironment)
{
    var trustedNetworks = builder.Configuration.GetSection("ForwardedHeaders:TrustedNetworks").Get<string[]>();
    if (trustedNetworks is null || trustedNetworks.Length == 0)
        throw new InvalidOperationException(
            "ForwardedHeaders:TrustedNetworks is empty — the rate limiter would partition every caller " +
            "under nginx's own address instead of the real client IP, which is a denial-of-service " +
            "footgun, not a limiter. Set FORWARDEDHEADERS__TRUSTEDNETWORKS__0 in .env (the docker bridge " +
            "subnet — see DEPLOY.md).");

    using var legalCheckScope = app.Services.CreateScope();
    var legalProvider = legalCheckScope.ServiceProvider.GetRequiredService<LegalDocumentProvider>();
    legalProvider.LoadAtStartup();
    if (legalProvider.Current is null)
        throw new InvalidOperationException(
            "Legal documents (App_Data/legal/legal.json) failed to load — without a valid Privacy and " +
            "Terms document the service cannot legally accept registrations. Check the container logs " +
            "above for the specific validation error and fix legal.json or the mounted files.");
}

// One line per request (US-45, ARCHITECTURE.md §11.1) — method, path, status, duration for free, plus
// traceId/userId via EnrichDiagnosticContext. Runs BEFORE UseExceptionHandler (architecture decision:
// the request-completed log line must exist even for the request that trips the exception handler,
// carrying the SAME traceId the 500 response's problem+json puts in front of the operator — that
// pairing is the whole point of "найди по traceId" in DEPLOY.md).
app.UseSerilogRequestLogging(opts =>
{
    // Every DELIBERATE 4xx (400/402/403/404/409/429/451) logs at Information, never Warning/Error
    // (US-45 p.5) — they are normal traffic, not incidents. Only an unhandled exception or a 5xx is
    // Warning/Error-worthy from the request-logging middleware's point of view; background-task and
    // infrastructure-level Warning/Error events (§11.2) are logged separately, by their own code, not
    // through this line.
    // UseExceptionHandler (registered below) already catches the exception and writes the 500 body
    // before this middleware runs its own logging (it wraps everything AFTER it, this line included),
    // so `ex` is normally null here even for a 500 — the status code is the reliable signal.
    opts.GetLevel = (httpContext, _, ex) =>
        ex is not null || httpContext.Response.StatusCode >= 500 ? LogEventLevel.Error : LogEventLevel.Information;
    opts.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        // The SAME value the 500 handler puts into problem+json's traceId — if these two ever diverge,
        // "найди по traceId" in DEPLOY.md stops working, which is the whole point of this line existing.
        diagnosticContext.Set("traceId", Activity.Current?.Id ?? httpContext.TraceIdentifier);
        diagnosticContext.Set("userId", httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));
    };
});

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
app.UseStaticFiles(); // serves wwwroot/uploads/... (company logos, avatars, service images) — the
                       // PUBLIC storage class only; client-note photos never go through this (ARCHITECTURE.md §12.1)
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter(); // no global limiter configured — a no-op except where [EnableRateLimiting] is used
app.MapControllers();

// Health checks (US-43, ARCHITECTURE.md §10.1). Deliberately NOT [EnableRateLimiting] anywhere near
// these two routes: monitoring must not be able to lock itself out, and there is no global limiter in
// this project (app.UseRateLimiter() above is a no-op except where the attribute is applied), so simply
// never applying the attribute here is the whole mechanism (§9.3). Both are anonymous — a health probe
// cannot authenticate.
//
// Custom two-field ResponseWriter for both endpoints: the framework's default JSON payload includes
// each check's exception message, which for "database" would leak a connection string fragment or a
// driver error straight onto a public, unauthenticated endpoint (US-43 p.3).
app.MapHealthChecks("/api/health/live", new HealthCheckOptions
{
    Predicate = _ => false, // no checks run at all — this route never touches the database
    ResponseWriter = WriteHealthResponse
}).AllowAnonymous();

app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse
}).AllowAnonymous();

// Seed roles and super-admin on startup
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var legalDocumentProvider = scope.ServiceProvider.GetRequiredService<LegalDocumentProvider>();

    await db.Database.MigrateAsync();

    string[] roles = ["Client", "Master", "CompanyOwner", "SuperAdmin"];
    foreach (var role in roles)
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));

    // The SuperAdmin, like every account, is now identified by phone (UserName == phone). Email is
    // optional and kept only for display. Config key SuperAdmin:Phone drives login. US-26: seeded with
    // the SAME canonical form AuthController.Login normalizes to — otherwise a config value like
    // "+70000000000" would seed "UserName = +70000000000" while every login attempt normalizes to
    // "70000000000" and never finds it.
    var adminPhone = builder.Configuration["SuperAdmin:Phone"] is { Length: > 0 } rawAdminPhone
        ? PhoneNormalizer.Normalize(rawAdminPhone)
        : null;
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

            // US-37, ARCHITECTURE.md §6.3: LegalConsentFilter applies to every authenticated route,
            // SuperAdmin's own admin API included — there is no carve-out for the seeded account in the
            // contract. Without this the freshly-seeded SuperAdmin would be locked out of everything
            // outside the allow-list until they happened to call POST /api/legal/accept, which nothing
            // in the admin UI prompts them to do. Recording consent to the currently-loaded documents
            // at seed time is the same conceptual act AuthController.Register performs for every other
            // new account; if no manifest is loaded yet (only possible outside Production), this is
            // skipped and the account behaves like any pre-cycle-C account until it next logs in after
            // the manifest is fixed.
            var legalSnapshot = legalDocumentProvider.Current;
            var seededPrivacyDoc = legalSnapshot?.Get(LegalDocumentType.Privacy);
            var seededTermsDoc = legalSnapshot?.Get(LegalDocumentType.Terms);
            if (seededPrivacyDoc is not null && seededTermsDoc is not null)
            {
                var acceptedAt = DateTime.UtcNow;
                db.UserConsents.AddRange(
                    new UserConsent { Id = Guid.NewGuid(), UserId = admin.Id, DocumentType = LegalDocumentType.Privacy, Version = seededPrivacyDoc.Version, AcceptedAtUtc = acceptedAt },
                    new UserConsent { Id = Guid.NewGuid(), UserId = admin.Id, DocumentType = LegalDocumentType.Terms, Version = seededTermsDoc.Version, AcceptedAtUtc = acceptedAt });
                await db.SaveChangesAsync();
            }
        }
    }
}

app.Run();

// Own JSON body for both health endpoints, two fields only (US-43 p.3): the framework's default
// UIResponseWriter serializes every check's exception message, which for "database" would put a
// connection-string fragment or driver error onto a public, unauthenticated endpoint. "failed" carries
// whichever check's HealthCheckResult.Unhealthy(description) fired first — "database" or "migrations"
// (DatabaseReadyHealthCheck), the two values API_CONTRACT.md §10.2 documents.
static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    if (report.Status == HealthStatus.Healthy)
        return context.Response.WriteAsync("""{"status":"Healthy"}""");

    var failed = report.Entries.Values.FirstOrDefault(e => e.Status != HealthStatus.Healthy).Description ?? "database";
    return context.Response.WriteAsJsonAsync(new { status = "Unhealthy", failed });
}

// Exposes the implicit Program class so the functional test project can spin up
// the app in-memory via WebApplicationFactory<Program>.
public partial class Program;
