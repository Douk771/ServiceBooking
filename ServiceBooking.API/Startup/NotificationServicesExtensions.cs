using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Startup;

/// <summary>
/// WhatsApp/MAX transports and provisioning (provider switch, registries, webhook parsers) and staff Web Push. Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): moved verbatim out of Program.cs, whose call order is unchanged.
/// </summary>
internal static class NotificationServicesExtensions
{
    public static void AddNotifications(this WebApplicationBuilder builder)
    {
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
    builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.StaffPushLinks>();

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
    }).ConfigurePrimaryHttpMessageHandler(ServiceBooking.API.Services.Notifications.WebPush.WebPushHandlerFactory.Create);

    builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.WebPush.LoggingWebPushSender>();
    builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.WebPush.LibWebPushSender>();
    var staffPushProvider = builder.Configuration["Notifications:StaffPush:Provider"];
    builder.Services.AddSingleton<ServiceBooking.API.Services.Notifications.WebPush.IWebPushSender>(sp =>
        string.Equals(staffPushProvider, "web-push", StringComparison.OrdinalIgnoreCase)
            ? sp.GetRequiredService<ServiceBooking.API.Services.Notifications.WebPush.LibWebPushSender>()
            : sp.GetRequiredService<ServiceBooking.API.Services.Notifications.WebPush.LoggingWebPushSender>());
    }
}
