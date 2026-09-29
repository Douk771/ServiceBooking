using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ServiceBooking.API.Startup;

/// <summary>
/// The named rate-limit policies (+ D9 shared IP/user window helpers) and their 429 bodies. Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): moved verbatim out of Program.cs, whose call order is unchanged.
/// </summary>
internal static class RateLimitingExtensions
{
    public static void AddServiceBookingRateLimiting(this WebApplicationBuilder builder)
    {
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
        o.AddPolicy("data-export", ctx => UserWindowPolicy(ctx, "data-export", defaultPermitLimit: 3, defaultWindowMinutes: 1440));

        // push-subscribe: ARCHITECTURE_CYCLE9.md §105.5 (US-123) — "20/час на пользователя". Keyed by user
        // id only, same shape as data-export above: the endpoint requires [Authorize], there is no
        // anonymous case, and the caller subscribing THEIR OWN devices is exactly what this bounds (not an
        // IP, which a shared salon computer would make the wrong partition key for).
        o.AddPolicy("push-subscribe", ctx => UserWindowPolicy(ctx, "push-subscribe", defaultPermitLimit: 20, defaultWindowMinutes: 60));

        // address-verify: ARCHITECTURE_CYCLE19.md §388.2 — сохранение адреса и правовой гейт публичности
        // адреса. Имя и значения по умолчанию (30/час на пользователя) не меняются с цикла 13, хотя
        // геокодер, который был третьим потребителем этого бюджета, в цикле 19 удалён.
        o.AddPolicy("address-verify", ctx => UserWindowPolicy(ctx, "address-verify", defaultPermitLimit: 30, defaultWindowMinutes: 60));

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
        o.AddPolicy("phone-change", ctx => UserWindowPolicy(ctx, "phone-change", defaultPermitLimit: 5, defaultWindowMinutes: 60));

        // trial-activate: POST /api/billing/trial — 5/сутки на пользователя (ARCHITECTURE_CYCLE18.md §342).
        // Без него кнопка активации становится бесплатным способом перебирать отказы (перебор редакций
        // condition, статуса телефона и т.д.).
        o.AddPolicy("trial-activate", ctx =>
        {
            var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
            var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
            return RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("RateLimits:TrialActivatePerDay", 5),
                Window = TimeSpan.FromHours(24),
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
        o.AddPolicy("booking-reschedule", ctx => UserWindowPolicy(ctx, "booking-reschedule", defaultPermitLimit: 30, defaultWindowMinutes: 60));

        // ── Cycle 23 (ARCHITECTURE_CYCLE23.md §395.4): four policies for the orders module ─────────────────
        // storefront: the public storefront and the cart quote — 120/min per IP.
        o.AddPolicy("storefront", ctx => IpWindowPolicy(ctx, "storefront", defaultPermitLimit: 120, defaultWindowMinutes: 1));
        // order-create: POST /api/storefront/{slug}/orders — 20/hour per IP anonymously, 60/hour per user
        // (same "user id if present, else IP" shape as booking-create). The per-phone limits live in
        // OrderPhoneThrottle (by the database), not here.
        o.AddPolicy("order-create", ctx =>
        {
            var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
            var windowMinutes = config.GetValue("RateLimits:order-create:WindowMinutes", 60);
            var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId is not null)
                return RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = config.GetValue("RateLimits:order-create:PermitLimit", 60),
                    Window = TimeSpan.FromMinutes(windowMinutes), QueueLimit = 0
                });
            var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
            return RateLimitPartition.GetFixedWindowLimiter($"ip:{ip}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("RateLimits:order-create:AnonymousPermitLimit", 20),
                Window = TimeSpan.FromMinutes(windowMinutes), QueueLimit = 0
            });
        });
        // order-public: the order page by token and the cancellation — 120/min per IP (the token is 256 bits,
        // the limit only bounds a flood of 404s).
        o.AddPolicy("order-public", ctx => IpWindowPolicy(ctx, "order-public", defaultPermitLimit: 120, defaultWindowMinutes: 1));
        // order-board: the staff board poll every 5 s per screen — 120/min per user leaves room for several tabs.
        o.AddPolicy("order-board", ctx => UserWindowPolicy(ctx, "order-board", defaultPermitLimit: 120, defaultWindowMinutes: 1));
        // order-push (ARCHITECTURE_CYCLE24.md §456.1): subscribing a browser to one order's notifications — anonymous, so bounded per IP, 20/hour.
        o.AddPolicy("order-push", ctx => IpWindowPolicy(ctx, "order-push", defaultPermitLimit: 20, defaultWindowMinutes: 60));

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
                // ARCHITECTURE_CYCLE19.md §388.2/§414 — новый текст: адрес больше не "проверяется", только сохраняется.
                "address-verify" => "Слишком много попыток изменить адрес. Повторите позже.",
                "phone-verify-start" => ServiceBooking.API.Services.PhoneVerification.PhoneVerificationTexts.TooManyStartAttempts,
                "phone-change" => ServiceBooking.API.Services.PhoneVerification.PhoneVerificationTexts.TooManyChangePhoneAttempts,
                "phone-verify-webhook" => "Too many requests.",
                "booking-reschedule" => "Слишком много попыток переноса записи. Повторите позже.",
                "storefront" or "order-public" or "order-board" => "Слишком много запросов — подождите минуту",
                "order-create" => "Слишком много заказов подряд — попробуйте через несколько минут",
                "order-push" => "Слишком много запросов — подождите минуту",
                _ => "Too many uploads. Try again in a minute."
            };
            // WriteAsync alone never sets Content-Type (unlike controller-level BadRequest(string)/Conflict(string),
            // which set it via ASP.NET's content negotiation) — set it explicitly so 429 bodies match the
            // "text/plain everywhere" contract (ARCHITECTURE.md §14) the same way every other 4xx body already does.
            ctx.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
            await ctx.HttpContext.Response.WriteAsync(message, cancellationToken);
        };
    });
    }

    // Shared by auth-login/auth-register: partition purely by the caller's (ForwardedHeaders-resolved) IP,
    // PermitLimit/WindowMinutes read from RateLimits:{policyName}:* with the given defaults.
    private static RateLimitPartition<string> IpWindowPolicy(
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

    // Cycle 22 D9 — the user-keyed twin of IpWindowPolicy, shared by data-export, push-subscribe,
    // address-verify, phone-change and booking-reschedule: partition by the caller's user id ("anonymous"
    // if the JWT carries none — all five routes require [Authorize]), PermitLimit/WindowMinutes read from
    // RateLimits:{policyName}:* with the given defaults. Configuration is still resolved on every request,
    // exactly as the five inline copies did.
    private static RateLimitPartition<string> UserWindowPolicy(
        HttpContext ctx, string policyName, int defaultPermitLimit, int defaultWindowMinutes)
    {
        var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var userId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter($"user:{userId}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue($"RateLimits:{policyName}:PermitLimit", defaultPermitLimit),
            Window = TimeSpan.FromMinutes(config.GetValue($"RateLimits:{policyName}:WindowMinutes", defaultWindowMinutes)),
            QueueLimit = 0
        });
    }
}
