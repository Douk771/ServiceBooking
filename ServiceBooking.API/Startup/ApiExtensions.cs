using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models;
using ServiceBooking.API.Services;

namespace ServiceBooking.API.Startup;

/// <summary>
/// MVC/JSON/Swagger, CORS, forwarded headers, the 500 handler and the public uploads. Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): moved verbatim out of Program.cs, whose call order is unchanged.
/// </summary>
internal static class ApiExtensions
{
    public static void AddServiceBookingControllers(this WebApplicationBuilder builder)
    {
    builder.Services.AddControllers(options =>
        {
            // Cycle 29 (ARCHITECTURE_CYCLE29.md §29.4): a malformed form/query key answers 400, not 500.
            MalformedKeyGuardValueProviderFactory.Install(options.ValueProviderFactories);
            // Global, runs on every authenticated request (US-37, ARCHITECTURE.md §6.3) — a TypeFilter, so
            // LegalDocumentProvider is resolved from DI per-request rather than requiring a service-locator
            // pattern here.
            options.Filters.Add<ServiceBooking.API.Services.Legal.LegalConsentFilter>();
            // ARCHITECTURE_CYCLE28.md §579.5: in demo mode, a demo token (claim sb_demo) is refused the actions marked [DemoForbidden] with a 403 that has a body.
            // A no-op everywhere else.
            options.Filters.Add<ServiceBooking.API.Services.Demo.DemoForbiddenFilter>();
        })
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
    }

    public static void AddServiceBookingCors(this WebApplicationBuilder builder)
    {
    // CORS for React frontend
    builder.Services.AddCors(opt =>
        opt.AddDefaultPolicy(p =>
            p.WithOrigins(builder.Configuration["AllowedOrigins"]?.Split(',') ?? ["http://localhost:5173"])
             .AllowAnyHeader()
             .AllowAnyMethod()
             .AllowCredentials()
             // Cycle 28, pass B: a cross-origin front end (a dev server) can only read these headers if they are exposed; they carry the demo's two special answers.
             .WithExposedHeaders("X-Demo-Restricted", "X-Demo-Resetting", "Retry-After")));
    }

    public static void AddServiceBookingForwardedHeaders(this WebApplicationBuilder builder)
    {
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
    }

    public static void UseServiceBookingExceptionHandler(this WebApplication app)
    {
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
    }

    public static void UseServiceBookingSwagger(this WebApplication app)
    {
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(opt =>
        {
            opt.SwaggerEndpoint("/swagger/v1/swagger.json", "ServiceBooking API v1");
            opt.RoutePrefix = "swagger";
        });
    }
    }

    public static void UseServiceBookingPublicUploads(this WebApplication app)
    {
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
    }
}
