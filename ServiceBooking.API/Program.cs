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
// Cycle 8 phase 2 (ARCHITECTURE_CYCLE8_PHASE2.md §96): preserveStaticLogger defaults to false, which
// makes THIS host's logger win the process-wide static Log.Logger — harmless with one host per process,
// but under per-test-class parallelism (several WebApplicationFactory hosts alive at once, ARCHITECTURE_
// CYCLE8_PHASE2.md §92.4) the last host to start "wins" the static logger for every other host's writes,
// and a host's own DisposeAsync can close a Log.Logger some OTHER still-running host is still using.
// Scoped strictly to the Testing environment so Development/Production keep today's behavior byte-for-
// byte, including Log.CloseAndFlush's shutdown-time flush semantics that depend on it.
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
        .WriteTo.File(new CompactJsonFormatter(), Path.Combine(LogDirectory(context.Configuration), "app-.json"),
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
}, preserveStaticLogger: builder.Environment.IsEnvironment("Testing"));

// Fail-fast on obviously-unsafe deployment configuration (US-10 → US-48, ARCHITECTURE.md §13). Runs
// before anything reads these values, and BEFORE builder.Build() — so a misconfigured deployment never
// finishes starting instead of silently running with a guessable/placeholder secret.
// CustomWebApplicationFactory (tests) uses ASPNETCORE_ENVIRONMENT=Testing, so this never fires there.
//
// Allow-list, not deny-list (US-48, cycle C): an environment nobody told this code about yet (Staging,
// Preview, Demo) must be treated as production-grade. Only the two environments we KNOW are developer
// contexts are exempt — everything else gets the full set of checks, including anything introduced later.
// The gate and the checks themselves live in DeploymentSafetyChecks (US-48 test-coverage gap, QA cycle
// C) — pure, DI-free static methods so ServiceBooking.UnitTests can exercise them directly without
// booting a host; this call site is unchanged behavior, just delegated.
var isDeveloperEnvironment = DeploymentSafetyChecks.IsDeveloperEnvironment(builder.Environment.EnvironmentName);
if (!isDeveloperEnvironment)
{
    DeploymentSafetyChecks.ValidateSecrets(builder.Configuration, builder.Environment.ContentRootPath);
}

// Cycle 4 (US-54, US-35, US-30 — ARCHITECTURE_CYCLE4.md §24.2, §24.5, §34.3): all three are pure
// config/filesystem checks with no dependency on the DI container, so — like ValidateSecrets above —
// they run here, before Build(). Each is self-gated on environment/Enabled internally (unlike
// ValidateSecrets they are NOT wrapped in `if (!isDeveloperEnvironment)`: ValidateNotificationSecrets'
// rule 3 must run even in Development, and the other two are no-ops there anyway).
DeploymentSafetyChecks.ValidateNotificationSecrets(builder.Configuration, builder.Environment.EnvironmentName);
{
    // Reviewer note: this check's one non-fatal warning (a key rotation ack that's about to be
    // consumed, §24.4/§24.5) used to go through Console.WriteLine, which never reaches Serilog/GlitchTip
    // at all. The full pipeline (builder.Host.UseSerilog(...) above) is only wired up once the host is
    // actually built, which is AFTER this call by design (fail-fast before Build(), same as
    // ValidateSecrets) — so this is a minimal bootstrap logger, console-only, JUST for this one warning,
    // disposed immediately after. It intentionally does not duplicate the file/GlitchTip sinks above.
    using var bootstrapLogger = new LoggerConfiguration()
        .WriteTo.Console(new CompactJsonFormatter())
        .CreateLogger();
    DeploymentSafetyChecks.ValidateChannelKeyFingerprint(
        builder.Configuration, builder.Environment.EnvironmentName, builder.Environment.ContentRootPath,
        warn: message => bootstrapLogger.Warning(message));
}
DeploymentSafetyChecks.ValidateTimeZoneDatabase(builder.Environment.EnvironmentName);
DeploymentSafetyChecks.ValidateProviderDeliveryConsentMode(builder.Configuration);
DeploymentSafetyChecks.ValidateGreenApiServerCountry(builder.Configuration);
DeploymentSafetyChecks.ValidateRetentionPeriods(builder.Configuration);
// ARCHITECTURE_CYCLE9.md §105.3 (проход C, Web Push мастеру) — own secret (VAPID), own provider switch,
// checked the same "fail loud outside a developer environment" way as ValidateNotificationSecrets above,
// but gated on ITS OWN Provider value, independent of Notifications:Provider.
DeploymentSafetyChecks.ValidateStaffPushSecrets(builder.Configuration, builder.Environment.EnvironmentName);
// ARCHITECTURE_CYCLE13.md §206/§209.2 — own secret (Yandex Geocoder API key), own provider switch, own
// unconditional check (CacheHours ≤ 720 is a licence ceiling, checked in every environment, not just
// outside Development — see the method's own doc comment).
DeploymentSafetyChecks.ValidateAddressVerification(builder.Configuration, builder.Environment.EnvironmentName);
// ARCHITECTURE_CYCLE14.md §150.2 — own secret set (PHONEVERIFY_*), own provider switch
// (PhoneVerification:Provider), checked unconditionally (even in Development — an unrecognized
// provider value is a config-correctness bug there too, unlike the secrets themselves).
DeploymentSafetyChecks.ValidatePhoneVerificationSecrets(builder.Configuration, builder.Environment.EnvironmentName);

builder.Services.AddControllers(options =>
        // Global, runs on every authenticated request (US-37, ARCHITECTURE.md §6.3) — a TypeFilter, so
        // LegalDocumentProvider is resolved from DI per-request rather than requiring a service-locator
        // pattern here.
        options.Filters.Add<ServiceBooking.API.Services.Legal.LegalConsentFilter>())
    .ConfigureApiBehaviorOptions(options =>
    {
        // Cycle 6 contract finding: automatic model-state validation (missing/invalid query or body
        // fields, caught by [ApiController] before the action runs) used to answer with
        // application/problem+json (ValidationProblemDetails). Every OTHER 4xx a controller raises by
        // hand is a bare text/plain string (see openapi-cycle6.yaml's header comment) — the frontend's
        // error reader only understands that form, so the machine-shaped body was silently swallowed
        // into "Проверьте введённые данные", the same failure mode as the US-60 blocker. See
        // ModelValidationErrorFormatter's doc comment for the full story.
        options.InvalidModelStateResponseFactory = ServiceBooking.API.Services.ModelValidationErrorFormatter.BuildResponse;
        // Cycle 13 contract check finding: [ApiController]'s ClientErrorResultFilter auto-converts
        // every bare `return NotFound()`/`Conflict()`/etc. (any IClientErrorActionResult) into
        // application/problem+json, same failure family as the model-validation case fixed above —
        // except this path was never addressed, so all ~90 hand-written `NotFound()` calls across the
        // controllers silently answered with a ProblemDetails body instead of the empty/bare body every
        // contract (cycle 6 onward, including contracts/cycle13/openapi.yaml's NotFoundEmpty) documents
        // and the frontend error reader expects. SuppressMapClientErrors turns this filter off entirely,
        // so IClientErrorActionResult results (NotFoundResult, ConflictResult, UnauthorizedResult, ...)
        // pass through exactly as the controller wrote them — empty body for NotFound()/Conflict(),
        // whatever body a controller explicitly attaches otherwise. InvalidModelStateResponseFactory
        // above is unaffected: it runs before an action even executes, so it never reaches this filter.
        options.SuppressMapClientErrors = true;
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // Cycle 4: lets a DTO property distinguish "omitted from the request" from "present and
        // explicitly null" — see ServiceBooking.API.DTOs.Common.Optional<T>'s doc comment.
        o.JsonSerializerOptions.Converters.Add(new ServiceBooking.API.DTOs.Common.OptionalJsonConverterFactory());
    });
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
builder.Services.AddScoped<AvailabilityService>();
builder.Services.AddScoped<SubscriptionResolver>();
builder.Services.AddScoped<ServiceBooking.API.Services.Billing.BillingAccountProvisioner>();
builder.Services.AddScoped<ServiceBooking.API.Services.Billing.AccountUsageReader>();
builder.Services.AddScoped<ServiceBooking.API.Services.Billing.CompanyOwnerWriter>();
builder.Services.AddScoped<ServiceBooking.API.Services.Billing.CompanyTransferService>();
builder.Services.AddScoped<ServiceBooking.API.Services.Billing.OwnerSubscriptionService>();
// Cycle 4 (ARCHITECTURE_CYCLE4.md §25.3, T4-B7): the other backend developer's queueing service, called
// directly from BookingsController (create/cancel/reschedule) — registered here because Program.cs is
// this developer's file this cycle.
builder.Services.AddScoped<ServiceBooking.API.Services.NotificationScheduler>();
builder.Services.AddScoped<ServiceBooking.API.Services.Bookings.BookingEventLog>();
builder.Services.AddScoped<ServiceBooking.API.Services.Bookings.BookingActorResolver>();
builder.Services.AddHttpClient<CaptchaService>();
// T5-B10 (ARCHITECTURE_CYCLE5.md §50.1, US-74) — reuses the existing CaptchaService/rate-limiting
// machinery, no new infrastructure.
builder.Services.Configure<ServiceBooking.API.Controllers.SubjectRequestOptions>(
    builder.Configuration.GetSection(ServiceBooking.API.Controllers.SubjectRequestOptions.SectionName));

// TD-03-quater (SPEC_CYCLE16_TECH_DEBT.md): the "new subject request" / "due soon" operator signal.
// Reuses the SAME Sentry:Dsn as the Serilog→GlitchTip sink above — one secret, two delivery paths,
// because that sink's own MinimumEventLevel = Error would swallow these informational signals.
builder.Services.Configure<ServiceBooking.API.Services.Signals.GlitchTipSignalOptions>(
    builder.Configuration.GetSection(ServiceBooking.API.Services.Signals.GlitchTipSignalOptions.SectionName));
// Same environment/release the Serilog→Sentry sink stamps on every event (above) — bound separately
// since there is no "Sentry:Environment" config key to bind from (code review, cycle 16).
builder.Services.Configure<ServiceBooking.API.Services.Signals.GlitchTipSignalOptions>(o =>
{
    o.Environment = builder.Environment.EnvironmentName;
    o.Release = builder.Configuration["Sentry:Release"];
});
builder.Services.AddHttpClient("glitchtip-signal");
// Singleton, not Scoped: sent fire-and-forget from SubjectRequestsController outside the request scope
// (code review, cycle 16), and the service itself is stateless (IHttpClientFactory/IOptions/ILogger are
// all singleton-safe dependencies) — a Scoped registration would silently start throwing
// ObjectDisposedException the day a scoped dependency is ever added to it.
builder.Services.AddSingleton<ServiceBooking.API.Services.Signals.IGlitchTipSignalService,
    ServiceBooking.API.Services.Signals.GlitchTipSignalService>();

// Image uploads (US-19, US-25): FileStorage holds no per-request state (just the two configured roots),
// so it's a singleton; ImageUploadService is scoped only because everything else in this layer is —
// it has no state of its own either.
builder.Services.AddSingleton<FileStorage>();
builder.Services.AddScoped<ImageUploadService>();

// Legal documents (US-36, ARCHITECTURE.md §4): a singleton so the in-memory snapshot is shared by every
// request instead of re-parsed per scope — the whole point of the ReloadSeconds cache (§4.3).
builder.Services.Configure<LegalOptions>(builder.Configuration.GetSection("Legal"));
builder.Services.AddSingleton<LegalDocumentProvider>();

// Consent journal (cycle 5, ARCHITECTURE_CYCLE5.md §45.1) — scoped: it only wraps AppDbContext queries,
// unlike LegalDocumentProvider above it holds no snapshot of its own to share across requests.
builder.Services.AddScoped<ConsentLedger>();
// T5-B6 (ARCHITECTURE_CYCLE5.md §48.1) — reuses Notifications:EncryptionKey, no new secret to provision.
builder.Services.AddScoped<HealthNoteProtector>();
// TD-03 (ARCHITECTURE_CYCLE16.md §245.4) — the single gate for "does this account get the
// phone-matching branch of its own guest-recorded data". Scoped: wraps one AppDbContext query.
builder.Services.AddScoped<ServiceBooking.API.Services.Subjects.SubjectScopeResolver>();

// WhatsApp notifications (cycle 4, ARCHITECTURE_CYCLE4.md §21–§37).
builder.Services.Configure<ServiceBooking.API.Services.Notifications.NotificationOptions>(
    builder.Configuration.GetSection(ServiceBooking.API.Services.Notifications.NotificationOptions.SectionName));

// §29.2 (QR response cache) and §23.3 (PlatformSettings' 60s cache) both need IMemoryCache — neither
// AddControllers nor AddMvc registers it by default, unlike (say) AddResponseCaching.
builder.Services.AddMemoryCache();
builder.Services.AddScoped<ServiceBooking.API.Services.Notifications.PlatformSettings>();
builder.Services.AddScoped<ServiceBooking.API.Services.Billing.PricingCatalogCache>();

// The webhook parser (§32) is registered unconditionally, independent of Notifications:Provider — it is
// pure translation with no secret/network access of its own, and NotificationsController.ProviderWebhook
// resolves it via [FromServices] regardless of which transport is active, so a "logging"-provider
// deployment that nonetheless receives a stray webhook still parses (and safely 200s) it rather than
// throwing on a missing DI registration. The interface (IProviderWebhookParser) lives in
// Services/ProviderWebhookParsing.cs, not Services/Notifications/ — see that file's doc comment.
builder.Services.AddSingleton<ServiceBooking.API.Services.IProviderWebhookParser,
    ServiceBooking.API.Services.Notifications.GreenApi.GreenApiWebhookParser>();

// The dispatcher's abstractions over time, delay and pause randomness (§21 p.7, §26, §27) — production
// defaults everywhere except the dedicated dispatch-test host, which overrides all three with recording/
// fake implementations (NotificationDispatchTestFactory).
builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.INotificationClock,
    ServiceBooking.API.Services.Notifications.SystemNotificationClock>();
builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.IDispatchDelay,
    ServiceBooking.API.Services.Notifications.SystemDispatchDelay>();
builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.IPauseGenerator,
    ServiceBooking.API.Services.Notifications.PauseGenerator>();

// The "green-api" named client (§24.3, §28.1): request/URL logging for THIS client only is silenced at
// the category level (rung 1 of the three-rung defence against a token reaching a log — GreenApiUrls'
// SafeLabel, logged explicitly by the adapter itself, is rung 2), and its primary handler is the
// keep-alive + IPv4-first-ConnectCallback SocketsHttpHandler built by GreenApiHandlerFactory. Registered
// unconditionally (not inside the switch below) — CaptchaService's own named client follows the same
// "always registered, only used when configured" shape, and it means changing Notifications:Provider at
// runtime-config level, without a rebuild, never needs a different DI graph.
builder.Logging.AddFilter("System.Net.Http.HttpClient.green-api.LogicalHandler", LogLevel.None);
builder.Logging.AddFilter("System.Net.Http.HttpClient.green-api.ClientHandler", LogLevel.None);
builder.Services.AddHttpClient("green-api", client =>
    {
        var greenApiOptions = builder.Configuration.GetSection("Notifications:GreenApi").Get<
            ServiceBooking.API.Services.Notifications.NotificationOptions.GreenApiOptions>() ?? new();
        client.Timeout = TimeSpan.FromSeconds(greenApiOptions.TimeoutSeconds);
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var greenApiOptions = builder.Configuration.GetSection("Notifications:GreenApi").Get<
            ServiceBooking.API.Services.Notifications.NotificationOptions.GreenApiOptions>() ?? new();
        return ServiceBooking.API.Services.Notifications.GreenApi.GreenApiHandlerFactory.Create(greenApiOptions);
    });

// Transport/provisioning selection by Notifications:Provider (§28, extended ARCHITECTURE_CYCLE9.md
// §104.2/US-122 to a REGISTRY per transport instead of a single DI-resolved instance). "logging" — the
// default, safe in every environment — never makes a network call at all, for EITHER transport (US-27
// p.9). "green-api" is the real adapter, now with TWO concrete implementations behind it (WhatsApp,
// MAX); an unrecognised Provider value fails LOUD at startup rather than silently falling back to the
// logging stub, which would otherwise be the one way a Production deployment could believe notifications
// are really going out when nothing is.
//
// Every concrete adapter is registered as itself, AND WhatsApp's is additionally registered against the
// bare interface (INotificationTransport/IChannelProvisioning) — the registries below resolve WhatsApp
// through that interface specifically (not the concrete type) so that a TEST HOST overriding it the
// pre-cycle-9 way (`services.AddSingleton<INotificationTransport>(fake)`, added to the collection AFTER
// this block — see NotificationDispatchTestFactory/NotificationDispatchExtraTests) keeps working
// unchanged: "last registration for a service type wins" only helps here if something still asks for
// that exact service type at resolution time. MAX has no such backward-compatibility concern (no test
// overrides it yet — it's new this cycle) and resolves its own concrete type directly. This is what makes
// "add a third transport" a new registry map entry, not a second parallel switch statement.
builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.LoggingNotificationTransport>();
builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.NoopChannelProvisioning>();
builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.GreenApi.GreenApiTransport>();
builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.GreenApiMax.GreenApiMaxTransport>();

var notificationsProvider = builder.Configuration["Notifications:Provider"];
switch (notificationsProvider)
{
    case null or "" or "logging":
        break; // nothing further to register — both registries below route every transport to the stubs
    case "green-api":
        // IChannelProvisioning uses the PLATFORM's own partner token, which can create/delete a live
        // salon's instance — IChannelProvisioning's own doc comment is explicit that DI must make this
        // implementation structurally NOT EXIST outside Production (§28, US-35 p.4), not merely fail at
        // call time because ValidateNotificationSecrets' rules already force both partner tokens empty
        // there. A developer who sets Provider=green-api locally (partner tokens necessarily empty, or
        // startup would already have refused) still gets the harmless no-op for BOTH transports rather
        // than a real adapter with nothing to call.
        if (builder.Environment.IsProduction())
        {
            builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.GreenApi.GreenApiProvisioning>();
            builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.GreenApiMax.GreenApiMaxProvisioning>();
        }
        break;
    default:
        throw new InvalidOperationException($"Unknown Notifications:Provider '{notificationsProvider}'.");
}

// The bare-interface registration WhatsApp's registry entry resolves through — same override seam every
// consumer used before this cycle (NotificationChannelsController/ChannelHealthTask/NotificationDispatchTask
// all used to take this constructor-injected). Placed AFTER the switch so it forwards to whichever
// concrete adapter the switch above decided on.
builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.INotificationTransport>(sp =>
    string.Equals(notificationsProvider, "green-api", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<ServiceBooking.API.Services.Notifications.GreenApi.GreenApiTransport>()
        : sp.GetRequiredService<ServiceBooking.API.Services.Notifications.LoggingNotificationTransport>());
builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.IChannelProvisioning>(sp =>
    string.Equals(notificationsProvider, "green-api", StringComparison.OrdinalIgnoreCase) && builder.Environment.IsProduction()
        ? sp.GetRequiredService<ServiceBooking.API.Services.Notifications.GreenApi.GreenApiProvisioning>()
        : sp.GetRequiredService<ServiceBooking.API.Services.Notifications.NoopChannelProvisioning>());

builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.INotificationTransportRegistry>(sp =>
    new ServiceBooking.API.Services.Notifications.NotificationTransportRegistry(
        new Dictionary<NotificationTransport, ServiceBooking.API.Services.Notifications.INotificationTransport>
        {
            // Resolved through the INTERFACE, not the concrete type — see this block's own comment above
            // for why (test-host override compatibility).
            [NotificationTransport.WhatsApp] = sp.GetRequiredService<ServiceBooking.API.Services.Notifications.INotificationTransport>(),
            [NotificationTransport.Max] = string.Equals(notificationsProvider, "green-api", StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<ServiceBooking.API.Services.Notifications.GreenApiMax.GreenApiMaxTransport>()
                : sp.GetRequiredService<ServiceBooking.API.Services.Notifications.LoggingNotificationTransport>(),
        }));

builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.IChannelProvisioningRegistry>(sp =>
    new ServiceBooking.API.Services.Notifications.ChannelProvisioningRegistry(
        new Dictionary<NotificationTransport, ServiceBooking.API.Services.Notifications.IChannelProvisioning>
        {
            [NotificationTransport.WhatsApp] = sp.GetRequiredService<ServiceBooking.API.Services.Notifications.IChannelProvisioning>(),
            [NotificationTransport.Max] = string.Equals(notificationsProvider, "green-api", StringComparison.OrdinalIgnoreCase) && builder.Environment.IsProduction()
                ? sp.GetRequiredService<ServiceBooking.API.Services.Notifications.GreenApiMax.GreenApiMaxProvisioning>()
                : sp.GetRequiredService<ServiceBooking.API.Services.Notifications.NoopChannelProvisioning>(),
        }));

// Webhook parsers, keyed by transport for the new provider-webhook/{transport}/{token} route (§104.7,
// B10) — registered unconditionally, same "logging-provider deployment still parses a stray webhook"
// reasoning the original GreenApiWebhookParser registration below documents.
builder.Services.AddSingleton<ServiceBooking.API.Services.IProviderWebhookParserRegistry>(sp =>
{
    var byTransport = new Dictionary<NotificationTransport, ServiceBooking.API.Services.IProviderWebhookParser>
    {
        [NotificationTransport.WhatsApp] = sp.GetRequiredService<ServiceBooking.API.Services.IProviderWebhookParser>(),
        [NotificationTransport.Max] = new ServiceBooking.API.Services.Notifications.GreenApiMax.GreenApiMaxWebhookParser(),
    };
    return new ServiceBooking.API.Services.ProviderWebhookParserRegistry(byTransport);
});

// ── Web Push мастеру (ARCHITECTURE_CYCLE9.md §105, проход C, US-116/117/118/123/124) ─────────────────
// Deliberately its own section, independent of the WhatsApp/MAX switch above: own config section, own
// provider value, own secret (VAPID, not the channel master key), own named HttpClient (§105.1 — "Один
// клиент на два очень разных назначения — источник взаимного влияния таймаутов").
builder.Services.Configure<ServiceBooking.API.Services.Notifications.WebPush.WebPushOptions>(
    builder.Configuration.GetSection(ServiceBooking.API.Services.Notifications.WebPush.WebPushOptions.SectionName));
builder.Services.AddScoped<ServiceBooking.API.Services.Notifications.PushSubscriptionWriter>();
builder.Services.AddScoped<ServiceBooking.API.Services.Notifications.StaffPushScheduler>();

// The "web-push" named client (§105.1) — request/URL logging silenced the same way as "green-api"
// (rung 1 of defence against a key/token reaching a log); PushServiceClient handles its own
// content-type/headers, so no ConfigurePrimaryHttpMessageHandler is needed here (no custom
// IPv4-first ConnectCallback like GreenApiHandlerFactory — push services don't share GREEN-API's
// documented IPv6-flakiness history).
builder.Logging.AddFilter("System.Net.Http.HttpClient.web-push.LogicalHandler", LogLevel.None);
builder.Logging.AddFilter("System.Net.Http.HttpClient.web-push.ClientHandler", LogLevel.None);
builder.Services.AddHttpClient("web-push", client =>
{
    var webPushOptions = builder.Configuration.GetSection(ServiceBooking.API.Services.Notifications.WebPush.WebPushOptions.SectionName)
        .Get<ServiceBooking.API.Services.Notifications.WebPush.WebPushOptions>() ?? new();
    client.Timeout = TimeSpan.FromSeconds(webPushOptions.RequestTimeoutSeconds);
});

builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.WebPush.LoggingWebPushSender>();
builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.WebPush.LibWebPushSender>();
var staffPushProvider = builder.Configuration["Notifications:StaffPush:Provider"];
builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.WebPush.IWebPushSender>(sp =>
    string.Equals(staffPushProvider, "web-push", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<ServiceBooking.API.Services.Notifications.WebPush.LibWebPushSender>()
        : sp.GetRequiredService<ServiceBooking.API.Services.Notifications.WebPush.LoggingWebPushSender>());

// ── Проверка адреса по карте (ARCHITECTURE_CYCLE13.md §206–§209, §215) ─────────────────────────────
// Own section, own Provider switch, own secret — independent of the WhatsApp/MAX switch above, same
// pattern the Web Push block just followed. "logging" (default, safe everywhere) never makes a network
// call at all (LoggingAddressGeocoder) — that IS the intended production state until a licence is bought
// (P2), not a placeholder.
builder.Services.Configure<ServiceBooking.API.Services.Geo.GeoOptions>(
    builder.Configuration.GetSection(ServiceBooking.API.Services.Geo.GeoOptions.SectionName));

// The "yandex-geocoder" named client (§206): request/URL logging silenced at the category level, same
// rung-1 defence as "green-api"/"web-push" — the query string carries `apikey`. Registered
// unconditionally, not inside the switch below, for the same "changing Provider needs no different DI
// graph" reason green-api's own client is registered unconditionally.
builder.Logging.AddFilter("System.Net.Http.HttpClient.yandex-geocoder.LogicalHandler", LogLevel.None);
builder.Logging.AddFilter("System.Net.Http.HttpClient.yandex-geocoder.ClientHandler", LogLevel.None);
builder.Services.AddHttpClient("yandex-geocoder", client =>
    {
        var geoOptions = builder.Configuration.GetSection(ServiceBooking.API.Services.Geo.GeoOptions.SectionName)
            .Get<ServiceBooking.API.Services.Geo.GeoOptions>() ?? new();
        client.Timeout = TimeSpan.FromSeconds(geoOptions.Yandex.TimeoutSeconds);
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var geoOptions = builder.Configuration.GetSection(ServiceBooking.API.Services.Geo.GeoOptions.SectionName)
            .Get<ServiceBooking.API.Services.Geo.GeoOptions>() ?? new();
        return ServiceBooking.API.Services.Geo.GeoHandlerFactory.Create(geoOptions.Yandex);
    });

builder.Services.AddSingleton<ServiceBooking.API.Services.Geo.LoggingAddressGeocoder>();
builder.Services.AddSingleton<ServiceBooking.API.Services.Geo.Yandex.YandexAddressGeocoder>();
var addressVerificationProvider = builder.Configuration["AddressVerification:Provider"];
builder.Services.AddSingleton<ServiceBooking.API.Services.Geo.IAddressGeocoder>(sp =>
    string.Equals(addressVerificationProvider, "yandex", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<ServiceBooking.API.Services.Geo.Yandex.YandexAddressGeocoder>()
        : sp.GetRequiredService<ServiceBooking.API.Services.Geo.LoggingAddressGeocoder>());
builder.Services.AddScoped<ServiceBooking.API.Services.Geo.AddressLookupService>();

// ── Подтверждение телефона через MAX (ARCHITECTURE_CYCLE14.md §140-§158) ──────────────────────────────
// R5/§144.1: a PLATFORM subsystem, deliberately with NO reference anywhere in this block to
// Notifications:*/NotificationChannel/ChannelCompanyAssignment/LegalOptionGuards — own top-level config
// section, own secrets, own registry, its own named HttpClient.
builder.Services.Configure<ServiceBooking.API.Services.PhoneVerification.PhoneVerificationOptions>(
    builder.Configuration.GetSection(ServiceBooking.API.Services.PhoneVerification.PhoneVerificationOptions.SectionName));

builder.Services.AddSingleton<ServiceBooking.API.Services.PhoneVerification.PhoneVerificationDiagnostics>();

// §150.3: the adapter is ALWAYS registered — what actually changes with the provider switch is which
// IMaxBotClient it was built with underneath (stub vs. real), never whether the registry has an entry
// for MaxBot at all.
var phoneVerificationProvider = builder.Configuration["PhoneVerification:Provider"];
builder.Services.AddSingleton<ServiceBooking.API.Services.PhoneVerification.Max.StubMaxBotClient>();
builder.Services.AddSingleton<ServiceBooking.API.Services.PhoneVerification.Max.MaxBotClient>();
builder.Services.AddSingleton<ServiceBooking.API.Services.PhoneVerification.Max.IMaxBotClient>(sp =>
    string.Equals(phoneVerificationProvider, "max-bot", StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<ServiceBooking.API.Services.PhoneVerification.Max.MaxBotClient>()
        : sp.GetRequiredService<ServiceBooking.API.Services.PhoneVerification.Max.StubMaxBotClient>());

// Request/URL logging silenced the same way as "green-api"/"web-push" — the bot token lives in the
// Authorization header (О3), never a query string, but this client's own request logging is muted
// regardless as a second rung of defence.
builder.Logging.AddFilter("System.Net.Http.HttpClient.max-bot.LogicalHandler", LogLevel.None);
builder.Logging.AddFilter("System.Net.Http.HttpClient.max-bot.ClientHandler", LogLevel.None);
builder.Services.AddHttpClient("max-bot", client =>
{
    var maxOptions = builder.Configuration.GetSection("PhoneVerification:Max")
        .Get<ServiceBooking.API.Services.PhoneVerification.Max.MaxBotOptions>() ?? new();
    client.Timeout = TimeSpan.FromSeconds(maxOptions.TimeoutSeconds);
});

builder.Services.AddSingleton<ServiceBooking.API.Services.PhoneVerification.Max.MaxBotVerificationAdapter>();
builder.Services.AddSingleton<ServiceBooking.API.Services.PhoneVerification.IPhoneVerificationMethodAdapter>(sp =>
    sp.GetRequiredService<ServiceBooking.API.Services.PhoneVerification.Max.MaxBotVerificationAdapter>());
builder.Services.AddSingleton<ServiceBooking.API.Services.PhoneVerification.IPhoneVerificationMethodRegistry,
    ServiceBooking.API.Services.PhoneVerification.PhoneVerificationMethodRegistry>();

builder.Services.AddSingleton<ServiceBooking.API.Services.PhoneVerification.Max.MaxWebhookSubscriber>();
builder.Services.AddScoped<ServiceBooking.API.Services.PhoneVerification.Max.MaxWebhookHandler>();
builder.Services.AddScoped<ServiceBooking.API.Services.PhoneVerification.PhoneVerificationSessionService>();
builder.Services.AddScoped<ServiceBooking.API.Services.PhoneVerification.PhoneVerificationWriter>();
builder.Services.AddScoped<ServiceBooking.API.Services.Bookings.GuestBookingLookup>();

// §146.3, Q4: re-subscribes once at process start. Registered ONLY when the provider is actually
// "max-bot" — under "stub" there is nothing to subscribe (and StubMaxBotClient.SubscribeAsync would
// just return false forever, which is correct but pointless to schedule at all).
if (string.Equals(phoneVerificationProvider, "max-bot", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddHostedService<ServiceBooking.API.Services.Hosting.MaxWebhookStartupSubscriber>();

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

    // availability: GET /api/bookings/availability is anonymous (guest booking needs it), so it gets
    // its own IP-keyed limit (ARCHITECTURE_CYCLE6.md §45.6) — 60/min is one request per client per
    // month-view, generous for legitimate calendar navigation.
    o.AddPolicy("availability", ctx => IpWindowPolicy(ctx, "availability", defaultPermitLimit: 60, defaultWindowMinutes: 1));

    // subject-request: US-74/§50.1 asks for BOTH a 3/hour and a 10/day cap; this rate limiter middleware
    // only supports one fixed window per named policy (every other policy in this file has the same
    // shape), so only the HOURLY limit is actually enforced here.
    // Code review, "заодно": this is honestly a WEAKER guarantee than the daily cap alone would be, not
    // a stricter one — 3/hour, sustained, adds up to 72/day, well past the 10/day ceiling §50.1 asks
    // for. The daily cap is simply not enforced by this policy at all; nothing here catches a caller who
    // spaces requests out to stay under the hourly limit. 🟡 Known, disclosed simplification: see the
    // cycle report.
    o.AddPolicy("subject-request", ctx => IpWindowPolicy(ctx, "subject-request", defaultPermitLimit: 3, defaultWindowMinutes: 60));

    // notifications-webhook: the provider calls this anonymously and per-address, keyed the same way
    // as auth-login/auth-register (ARCHITECTURE_CYCLE4.md §32) — 600/min is generous enough for normal
    // delivery-status traffic while still bounding a misbehaving/compromised caller.
    o.AddPolicy("notifications-webhook", ctx => IpWindowPolicy(ctx, "notifications-webhook", defaultPermitLimit: 600, defaultWindowMinutes: 1));

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

    // push-subscribe: ARCHITECTURE_CYCLE9.md §105.5 (US-123) — "20/час на пользователя". Keyed by user
    // id only, same shape as data-export above: the endpoint requires [Authorize], there is no
    // anonymous case, and the caller subscribing THEIR OWN devices is exactly what this bounds (not an
    // IP, which a shared salon computer would make the wrong partition key for).
    o.AddPolicy("push-subscribe", ctx =>
    {
        var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimits:push-subscribe:PermitLimit", 20),
            Window = TimeSpan.FromMinutes(config.GetValue("RateLimits:push-subscribe:WindowMinutes", 60)),
            QueueLimit = 0
        });
    });

    // address-verify: ARCHITECTURE_CYCLE13.md §210/§238 — the tenth named policy, "30/час на
    // пользователя". Keyed by user id only, same shape as data-export/push-subscribe above: all three
    // routes it guards require [Authorize], there is no anonymous case. Applied to all three
    // CompanyAddressController routes — two can reach the paid geocoder, the third writes journal rows —
    // and one budget covers all three on purpose (§210: "один и тот же бюджет одного и того же человека").
    o.AddPolicy("address-verify", ctx =>
    {
        var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimits:address-verify:PermitLimit", 30),
            Window = TimeSpan.FromMinutes(config.GetValue("RateLimits:address-verify:WindowMinutes", 60)),
            QueueLimit = 0
        });
    });

    // ARCHITECTURE_CYCLE14.md §150.4 (Q8) — three new policies, twelve total.
    //
    // phone-verify-start: POST /phone-verification/sessions — 10/час, per user when authenticated
    // (profile flow), otherwise per IP (registration flow, anonymous) — same "user id if present, else
    // IP" shape as booking-create above.
    o.AddPolicy("phone-verify-start", ctx =>
    {
        var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var permitLimit = config.GetValue("RateLimits:phone-verify-start:PermitLimit", 10);
        var windowMinutes = config.GetValue("RateLimits:phone-verify-start:WindowMinutes", 60);
        var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null)
            return RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(windowMinutes), QueueLimit = 0
            });

        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter($"ip:{ip}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(windowMinutes), QueueLimit = 0
        });
    });

    // phone-change: POST /api/profile/change-phone — 5/час на пользователя (R14). The route was NOT
    // covered by any policy before this cycle; US-14-17 turns it into a perebor oracle (Р3), so it gets
    // one now.
    o.AddPolicy("phone-change", ctx =>
    {
        var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimits:phone-change:PermitLimit", 5),
            Window = TimeSpan.FromMinutes(config.GetValue("RateLimits:phone-change:WindowMinutes", 60)),
            QueueLimit = 0
        });
    });

    // phone-verify-webhook: MAX's own webhook — same shape as notifications-webhook above (600/min per IP).
    o.AddPolicy("phone-verify-webhook", ctx => IpWindowPolicy(ctx, "phone-verify-webhook", defaultPermitLimit: 600, defaultWindowMinutes: 1));

    // booking-reschedule: PATCH /api/bookings/{id}/reschedule — 30/час на пользователя
    // (ARCHITECTURE_CYCLE15.md §257.7). Without it a caller with no authority learns nothing more from
    // repeating the request (404 either way), but the 404 itself is cheap enough that unbounded retries
    // are free — this caps the id-guessing budget the same way phone-change caps OTP-guessing.
    // Applies to BOTH branches: staff moving their own bookings is human-paced too, 30/hour is ample.
    o.AddPolicy("booking-reschedule", ctx =>
    {
        var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimits:booking-reschedule:PermitLimit", 30),
            Window = TimeSpan.FromMinutes(config.GetValue("RateLimits:booking-reschedule:WindowMinutes", 60)),
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
            "availability" => "Слишком много запросов. Повторите через минуту.",
            "data-export" => "Выгрузка доступна не чаще трёх раз в сутки.",
            "subject-request" => "Слишком много обращений с этого адреса. Повторите позже.",
            "notifications-webhook" => "Too many requests.",
            "push-subscribe" => "Слишком много подписок устройств. Повторите позже.",
            "address-verify" => "Слишком много обращений к проверке адреса. Повторите позже.",
            "phone-verify-start" => ServiceBooking.API.Services.PhoneVerification.PhoneVerificationTexts.TooManyStartAttempts,
            "phone-change" => ServiceBooking.API.Services.PhoneVerification.PhoneVerificationTexts.TooManyChangePhoneAttempts,
            "phone-verify-webhook" => "Too many requests.",
            "booking-reschedule" => "Слишком много попыток переноса записи. Повторите позже.",
            _ => "Too many uploads. Try again in a minute."
        };
        // WriteAsync alone never sets Content-Type (unlike controller-level BadRequest(string)/Conflict(string),
        // which set it via ASP.NET's content negotiation) — set it explicitly so 429 bodies match the
        // "text/plain everywhere" contract (ARCHITECTURE.md §14) the same way every other 4xx body already does.
        ctx.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await ctx.HttpContext.Response.WriteAsync(message, cancellationToken);
    };
});

// Log directory is configurable (ARCHITECTURE_CYCLE8.md §71.4) so each test-run/slot/factory can point
// it at its own temp folder instead of colliding on a repo-relative "logs" — the sole behavioural change
// cycle 8 makes to this file. Default is "logs", exactly as it always was: Development/Production
// behaviour is unchanged byte-for-byte when Logs:Directory isn't set.
static string LogDirectory(IConfiguration configuration) =>
    configuration["Logs:Directory"] is { } configured && configured.Trim().Length > 0 ? configured : "logs";

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

// B2/I5: mask the {token} segment of the two notification routes that embed a secret in the URL, so
// the request-completed log line (Information) never carries it. Returns null for every other path —
// callers only override RequestPath when this returns non-null.
static string? MaskSensitiveRequestPath(string? path)
{
    if (string.IsNullOrEmpty(path)) return null;

    const string webhookPrefix = "/api/notifications/provider-webhook/";
    const string unsubscribePrefix = "/api/notifications/unsubscribe/";
    // ARCHITECTURE_CYCLE14.md §146.1 (R12) — same coordinated fix as the two above, same incident this
    // cycle explicitly avoids repeating (cycle 9's nginx access-log leak, §9 N9-*). This app-level
    // masking covers what THIS process logs; deploy/nginx/ezbook.conf's own map/log_format (added in the
    // SAME commit) is what stops nginx's access log from writing the token before the request even
    // reaches here — neither alone is sufficient.
    const string maxWebhookPrefix = "/api/phone-verification/max/webhook/";

    if (path.StartsWith(webhookPrefix, StringComparison.Ordinal) && path.Length > webhookPrefix.Length)
        return webhookPrefix + "***";
    if (path.StartsWith(unsubscribePrefix, StringComparison.Ordinal) && path.Length > unsubscribePrefix.Length)
        return unsubscribePrefix + "***";
    if (path.StartsWith(maxWebhookPrefix, StringComparison.Ordinal) && path.Length > maxWebhookPrefix.Length)
        return maxWebhookPrefix + "***";

    return null;
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
// Cycle 4 (ARCHITECTURE_CYCLE4.md §26, §30): the dispatcher (1-minute period, its own internal budget)
// and channel health (15-minute period — polling, idle detection, orphaned-instance cleanup).
builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.NotificationDispatchTask>();
builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.ChannelHealthTask>();
// ARCHITECTURE_CYCLE9.md §105.8 — the fifth task, "staff-push-dispatch" (1-minute period, its own
// internal budget, same shape as notification-dispatch above but bounded PARALLEL across devices instead
// of per-channel sequential antiban pacing — see the task's own doc comment for why).
builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.StaffPushDispatchTask>();
// ARCHITECTURE_CYCLE14.md §146.3 — the SIXTH task, "max-webhook-renew" (period 4h, under the platform's
// own 8h no-response-drops-the-subscription window, О4). Registered unconditionally, same as every other
// IScheduledTask — a no-op in practice while PhoneVerification:Provider = "stub" (its own doc comment).
builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.MaxWebhookRenewTask>();
// TD-03-quater — the SEVENTH task, "subject-request-due-soon" (period 1 day): sends a GlitchTip signal
// one working day before a subject request's DueAtUtc, for requests not yet Answered/Rejected.
builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.SubjectRequestDueSoonTask>();

// T5-B8/B9 (ARCHITECTURE_CYCLE5.md §49.1): the fourth task, "data-retention". Every IRetentionRule below
// is registered individually (not discovered by reflection) so the list here IS the list of what runs —
// deliberately including the fact that NO rule for NotificationOptOut exists anywhere in this list.
builder.Services.Configure<ServiceBooking.API.Services.Retention.RetentionPeriods>(
    builder.Configuration.GetSection(ServiceBooking.API.Services.Retention.RetentionPeriods.SectionName));
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.NotificationBodyRedactionRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.NotificationMetadataDeletionRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.TemplateHistoryRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.ConsentRecordRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.InactiveAccountRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.BookingPersonalizationRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.ClientNoteRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.ClientNotePhotoRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.BookingEventRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.ClientHealthNoteRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.ChannelStateEventRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.PaymentLogRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.MailLogRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.AppLogAgeRule>();
// ARCHITECTURE_CYCLE9.md §105.11 — two new rules for the Web Push subsystem. Note (§105.11's own
// warning, kept here too): NO rule for NotificationOptOut exists anywhere in this list either, on
// purpose — a cycle-5 decision this cycle does not revisit.
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.PushSubscriptionRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.StaffPushNotificationRule>();
// ARCHITECTURE_CYCLE14.md §151.1 — the 17th and 18th rules. ⚠️ Registered LAST, deliberately — see
// PhoneVerificationSessionRule's own doc comment for why (N9-6, the pre-existing unprotected foreach
// this cycle does not fix).
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.PhoneVerificationSessionRule>();
builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
    ServiceBooking.API.Services.Retention.Rules.VerifiedPhoneOrphanRule>();
builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.DataRetentionTask>();

builder.Services.AddHostedService<ScheduledTaskRunner>();

var app = builder.Build();

// ARCHITECTURE_CYCLE9.md §104.2 (US-122) — "нераспознанное значение по-прежнему роняет старт; вдобавок
// роняет старт ситуация «в реестре нет реализации для члена NotificationTransport»." Runs unconditionally
// (every environment, including Development/Testing) — this is a CODE-correctness check (is every
// NotificationTransport member actually wired up above), not a secrets/deployment-safety one, so it is
// not gated by isDeveloperEnvironment the way ValidateNotificationSecrets is.
using (var transportCheckScope = app.Services.CreateScope())
{
    var transportRegistry = transportCheckScope.ServiceProvider
        .GetRequiredService<ServiceBooking.API.Services.Notifications.INotificationTransportRegistry>();
    DeploymentSafetyChecks.ValidateTransportRegistryCompleteness(
        nameof(ServiceBooking.API.Services.Notifications.INotificationTransportRegistry), transportRegistry.RegisteredTransports);

    var provisioningRegistry = transportCheckScope.ServiceProvider
        .GetRequiredService<ServiceBooking.API.Services.Notifications.IChannelProvisioningRegistry>();
    DeploymentSafetyChecks.ValidateTransportRegistryCompleteness(
        nameof(ServiceBooking.API.Services.Notifications.IChannelProvisioningRegistry), provisioningRegistry.RegisteredTransports);

    // ARCHITECTURE_CYCLE14.md §144.2 (Q2) — same shape, for the phone-verification method registry.
    var phoneVerificationRegistry = transportCheckScope.ServiceProvider
        .GetRequiredService<ServiceBooking.API.Services.PhoneVerification.IPhoneVerificationMethodRegistry>();
    DeploymentSafetyChecks.ValidateVerificationMethodRegistry(phoneVerificationRegistry.Registered);
}

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
    // Delegated to DeploymentSafetyChecks (see the pre-Build block above for why) — this one doesn't
    // actually need the constructed service provider either, it just historically ran alongside the
    // legal-document check below, which does.
    DeploymentSafetyChecks.ValidateTrustedNetworksConfigured(builder.Configuration);

    using var legalCheckScope = app.Services.CreateScope();
    var legalProvider = legalCheckScope.ServiceProvider.GetRequiredService<LegalDocumentProvider>();
    legalProvider.LoadAtStartup();
    if (legalProvider.Current is null)
        throw new InvalidOperationException(
            "Legal documents (App_Data/legal/legal.json) failed to load — without all five document " +
            "types and all six interface texts (ARCHITECTURE_CYCLE5.md §43.3) the service cannot legally " +
            "accept registrations. Check the container logs above for the specific validation error and " +
            "fix legal.json or the mounted files.");
}

// One line per request (US-45, ARCHITECTURE.md §11.1) — method, path, status, duration for free, plus
// traceId/userId via EnrichDiagnosticContext. Runs BEFORE UseExceptionHandler (architecture decision:
// the request-completed log line must exist even for the request that trips the exception handler,
// carrying the SAME traceId the 500 response's problem+json puts in front of the operator — that
// pairing is the whole point of "найди по traceId" in DEPLOY.md).
// Code review note (US-45 p.2/§11.3): the logged RequestPath below never carries the query string, so
// e.g. GET /api/admin/users?search=<телефон> does NOT leak a phone number here — Serilog.AspNetCore's
// RequestLoggingOptions.IncludeQueryInRequestPath defaults to false, and it stays false; it is NOT set
// here on purpose. Do not "fix" this by turning it on: PhoneMaskingEnricher (§11.3 point 3) only scans
// events at Warning+ and events with an exception, and this middleware's own request-completed line is
// logged at Information for every normal 2xx/4xx — an enabled query string would ride straight past the
// enricher into the log in the clear. If a future need arises to see query strings, it must go through
// the same masking as anything else with a phone in it, not through this switch.
app.UseSerilogRequestLogging(opts =>
{
    // Cycle 8 phase 2 (ARCHITECTURE_CYCLE8_PHASE2.md §96): RequestLoggingMiddleware defaults to the
    // STATIC Serilog.Log.Logger when opts.Logger is left null — the one fallback in this pipeline that
    // preserveStaticLogger (set above on builder.Host.UseSerilog) does not route around by itself. Under
    // Testing's per-host preserveStaticLogger=true, that default would mean this middleware's own
    // request-completed line goes to whichever OTHER host most recently claimed the static logger (or
    // nowhere, if none has) instead of THIS host's own file sink — resolved here by pointing it
    // explicitly at the Serilog.ILogger this exact host's DI container built (registered by UseSerilog
    // regardless of preserveStaticLogger). No behavior change outside Testing: in Development/Production
    // there is only ever one host per process, so this is the SAME logger the static field would have
    // pointed at anyway.
    opts.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
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
    // N1 (review round 2): RequestPath masking does NOT belong in EnrichDiagnosticContext — this
    // middleware builds the final LogEvent's properties as collectedProperties.Concat([RequestMethod,
    // RequestPath, StatusCode, Elapsed]) and applies them via AddOrUpdateProperty IN THAT ORDER, so the
    // middleware's OWN RequestPath (added last) overwrites whatever EnrichDiagnosticContext set under the
    // same name — a previous version of this code relied on diagnosticContext.Set("RequestPath", ...)
    // winning, which it does not; §37's "ноль совпадений в логах приложения" was not actually met, and
    // the unsubscribe token (a signed phone number) was reaching Information-level logs, upstream of
    // PhoneMaskingEnricher (Warning+ only). GetMessageTemplateProperties exists in Serilog.AspNetCore
    // specifically for this — it's what BUILDS RequestMethod/RequestPath/StatusCode/Elapsed in the first
    // place, so masking here is authoritative rather than racing the middleware for the last write.
    opts.GetMessageTemplateProperties = (httpContext, requestPath, elapsedMs, statusCode) =>
    {
        var maskedPath = MaskSensitiveRequestPath(requestPath) ?? requestPath;
        return
        [
            new LogEventProperty("RequestMethod", new ScalarValue(httpContext.Request.Method)),
            new LogEventProperty("RequestPath", new ScalarValue(maskedPath)),
            new LogEventProperty("StatusCode", new ScalarValue(statusCode)),
            new LogEventProperty("Elapsed", new ScalarValue(elapsedMs)),
        ];
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

// Serves the public storage class (company logos, avatars, service images) at /uploads/... — the
// PRIVATE storage class (client-note photos) never goes through this (ARCHITECTURE.md §12.1), see the
// fail-fast check above/in DeploymentSafetyChecks that refuses to start if it would.
//
// Deliberately NOT the parameterless app.UseStaticFiles() (US-19/US-25 bugfix, cycle D): that overload
// resolves its file provider from IWebHostEnvironment.WebRootFileProvider — i.e. wwwroot — which is a
// SEPARATE piece of configuration from where FileStorage actually writes (Storage:PublicRoot, falling
// back to the same default only by coincidence). Two independent ways of naming "the same" directory
// meant that (a) setting Storage:PublicRoot away from the default silently broke serving with no error
// anywhere, and (b) on a fresh checkout wwwroot doesn't exist at all — WebRootFileProvider resolves
// ONCE at host startup, so a wwwroot created after that point (by the first upload) is never picked up,
// no matter how many files land in it afterward. Resolving FileStorage.PublicRootFullPath here instead
// makes it the single source of truth for both reading and writing, and creating the directory BEFORE
// constructing the PhysicalFileProvider means the provider is never handed a directory that doesn't
// exist yet.
var publicUploadsRoot = app.Services.GetRequiredService<FileStorage>().PublicRootFullPath;
Directory.CreateDirectory(publicUploadsRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publicUploadsRoot),
    RequestPath = "/uploads"
});
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
    var consentLedger = scope.ServiceProvider.GetRequiredService<ConsentLedger>();

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
            var seededTermsDoc = legalSnapshot?.Get(LegalDocumentType.TermsClient);
            if (seededPrivacyDoc is not null && seededTermsDoc is not null)
            {
                var seedSubject = ServiceBooking.API.Services.Legal.ConsentSubject.ForUser(admin.Id);
                await consentLedger.GrantAsync(new ServiceBooking.API.Services.Legal.ConsentGrant(
                    seedSubject, LegalDocumentType.Privacy.ToString(), seededPrivacyDoc.Version, seededPrivacyDoc.ContentHash,
                    Purpose: null, ConsentAct.Acknowledged, ConsentSource.Registration));
                await consentLedger.GrantAsync(new ServiceBooking.API.Services.Legal.ConsentGrant(
                    seedSubject, LegalDocumentType.TermsClient.ToString(), seededTermsDoc.Version, seededTermsDoc.ContentHash,
                    Purpose: null, ConsentAct.Accepted, ConsentSource.Registration));
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
