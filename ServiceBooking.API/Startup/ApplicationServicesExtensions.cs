using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.API.Services.Scheduling.Tasks;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Startup;

/// <summary>
/// Database, application services, address/phone verification and background tasks. Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): moved verbatim out of Program.cs, whose call order is unchanged.
/// </summary>
internal static class ApplicationServicesExtensions
{
    public static void AddServiceBookingDatabase(this WebApplicationBuilder builder)
    {
    // Database
    builder.Services.AddDbContext<AppDbContext>(opt =>
        opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
    }

    public static void AddServiceBookingApplicationServices(this WebApplicationBuilder builder)
    {
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
    // Cycle 18 (ARCHITECTURE_CYCLE18.md §345.2, B4).
    builder.Services.Configure<ServiceBooking.API.Services.Billing.TrialOptions>(
        builder.Configuration.GetSection(ServiceBooking.API.Services.Billing.TrialOptions.SectionName));
    builder.Services.AddScoped<ServiceBooking.API.Services.Billing.TrialActivationService>();
    // Cycle 28 (ARCHITECTURE_CYCLE28.md §573.1): `ops tariffs plan|apply` — created by the operator command, never at startup.
    builder.Services.AddScoped<ServiceBooking.API.Services.Showcase.Tariffs.TariffCatalogSeeder>();
    // Cycle 28 (§576, §579.1): demo mode is off by default (the demo stand itself is pass B); the guard holds on the showcase mark alone.
    builder.Services.Configure<ServiceBooking.API.Services.Demo.DemoModeOptions>(
        builder.Configuration.GetSection(ServiceBooking.API.Services.Demo.DemoModeOptions.SectionName));
    builder.Services.AddScoped<ServiceBooking.API.Services.Showcase.ShowcaseOutboundGuard>();
    // Cycle 28, pass B (ARCHITECTURE_CYCLE28.md §579–§580): the demo stand. Everything below is inert unless DemoMode:Enabled — the routes answer 404, the middleware and
    // the global filter pass through, the reset refuses. The flag file is shared with the `ops demo reset` process, so the flag is a singleton (one second of cache).
    builder.Services.AddSingleton<ServiceBooking.API.Services.Demo.DemoMaintenanceFlag>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Demo.DemoLoginService>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Demo.DemoResetService>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Startup.SuperAdminSeeder>();
    // Cycle 28 (§575): the showcase generator and its operator commands (`ops showcase …`) — no HTTP route exists for any of it.
    builder.Services.AddScoped<ServiceBooking.API.Services.Showcase.ShowcaseAssetStore>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Showcase.ShowcaseGenerator>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Showcase.ShowcaseEraser>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Showcase.ShowcaseCommands>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Billing.TrialStateReader>();
    // Cycle 4 (ARCHITECTURE_CYCLE4.md §25.3, T4-B7): the other backend developer's queueing service, called
    // directly from BookingsController (create/cancel/reschedule) — registered here because Program.cs is
    // this developer's file this cycle.
    builder.Services.AddScoped<ServiceBooking.API.Services.NotificationScheduler>();
    // Cycle 22 (ARCHITECTURE_CYCLE22.md §375 F14, §379): channel funding, batched across billing accounts.
    builder.Services.AddScoped<ServiceBooking.API.Services.Notifications.ChannelFundingReader>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Bookings.BookingEventLog>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Bookings.BookingActorResolver>();
    // Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the body of POST /api/bookings, moved out of
    // BookingsController unchanged (+ the shared service-selection validation the slot endpoints use).
    builder.Services.AddScoped<ServiceBooking.API.Services.Bookings.BookingCreationService>();
    // Cycle 22 P5 (§378): CompanyDto assembly and the company stats report, moved out of CompaniesController.
    builder.Services.Configure<ServiceBooking.API.Services.PublicSites.PublicSitesOptions>(
        builder.Configuration.GetSection(ServiceBooking.API.Services.PublicSites.PublicSitesOptions.SectionName));
    builder.Services.AddSingleton<ServiceBooking.API.Services.PublicSites.PublicSiteLinks>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Companies.CompanyDtoAssembler>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Companies.CompanyCreationService>();

    // ARCHITECTURE_CYCLE23.md §402 — shops and pickup orders.
    builder.Services.Configure<ServiceBooking.API.Services.Orders.OrdersOptions>(
        builder.Configuration.GetSection(ServiceBooking.API.Services.Orders.OrdersOptions.SectionName));
    builder.Services.AddSingleton<ServiceBooking.API.Services.Shops.PhoneVerificationAvailability>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Shops.ShopAccessResolver>();

    // Cycle 37 (ARCHITECTURE_CYCLE37.md §37.16): "Дома".
    builder.Services.Configure<ServiceBooking.API.Services.Stays.StaysOptions>(
        builder.Configuration.GetSection(ServiceBooking.API.Services.Stays.StaysOptions.SectionName));
    builder.Services.AddSingleton<ServiceBooking.API.Services.Stays.IStaysClock, ServiceBooking.API.Services.Stays.SystemStaysClock>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Stays.StaysAccessResolver>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Stays.StaysPlanResolver>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Stays.StaysCompanyService>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Stays.StaysTrialService>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Stays.HouseService>();

    builder.Services.AddScoped<ServiceBooking.API.Services.Shops.ShopManageMapper>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Shops.CatalogMapper>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.StockLedger>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.OrderEventLog>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.OrderActorResolver>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.OrderNumberAllocator>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.OrderPhoneThrottle>();
    builder.Services.AddSingleton<ServiceBooking.API.Services.Orders.OrderDtoMapper>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.OrderCreationService>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.PublicOrderService>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.OrderTransitionService>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.OrderEditService>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Companies.CompanyStatsService>();

    // ARCHITECTURE_CYCLE24.md §464 — time, availability, notifications and tariffs of shops.
    builder.Services.AddScoped<ServiceBooking.API.Services.Billing.OrdersPlanResolver>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Billing.ChannelEligibility>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Shops.ShopGateLoader>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Shops.DailyMenuService>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Shops.ShopChannelReader>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.OrderMonthlyCounter>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.OrderLimitWarner>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.StaffOrderDtoFactory>();
    builder.Services.AddSingleton<ServiceBooking.API.Services.Orders.CustomerOrderNotificationsBuilder>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.Notifications.OrderNotificationPlanner>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.Notifications.OrderStaffPushQueue>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.Notifications.OrderStaffMaxQueue>();
    // ARCHITECTURE_CYCLE25.md §501–§504: reports (history, summary, pick list) and the customer card.
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.Reports.OrderReportQueries>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.Reports.ShopReportService>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.Reports.PickListService>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.Reports.ShopCustomerService>();
    // ARCHITECTURE_CYCLE25.md §505: the anonymous goods catalog.
    builder.Services.AddScoped<ServiceBooking.API.Services.Shops.GoodsCatalogService>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.Notifications.CustomerOrderPushQueue>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Orders.Notifications.OrderMessageScheduler>();
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
    // ARCHITECTURE_CYCLE20.md §406.2 (US-20-05) — the single writer for GuestDataGateEvent.
    builder.Services.AddScoped<ServiceBooking.API.Services.Subjects.GuestDataGateJournal>();
    // ARCHITECTURE_CYCLE20.md §404.3/§404.7 (US-20-03/US-20-07) — platform notices: builds/validates/saves
    // PlatformNotice rows (AdminNoticesController, CompanyPhotosController's PhotoRemoved) and counts the
    // CURRENT audience for the admin list/preview/publish responses.
    builder.Services.AddScoped<ServiceBooking.API.Services.Legal.PlatformNoticePublisher>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Legal.NoticeAudienceCounter>();
    // ARCHITECTURE_CYCLE20.md §402.4 (US-20-01) — the one cascade shared by every entry point that lifts a
    // written-health-consent mark.
    builder.Services.AddScoped<ServiceBooking.API.Services.Legal.WrittenHealthConsentRevoker>();
    // Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the bodies of GET /api/profile/export and
    // POST /api/profile/delete-account (+ preview), moved out of ProfileController unchanged.
    builder.Services.AddScoped<ServiceBooking.API.Services.Subjects.SubjectDataExporter>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Subjects.AccountDeletionService>();
    }

    public static void AddPhoneVerification(this WebApplicationBuilder builder)
    {
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

    // ARCHITECTURE_CYCLE25.md §499.3: the outgoing side of MAX for staff is a SEPARATE interface, implemented by the same MaxBotClient singleton
    // (shared rate limiters); under "stub" it is a structural no-op with no HttpClient.
    builder.Services.AddSingleton<ServiceBooking.API.Services.PhoneVerification.Max.StubMaxBotMessenger>();
    builder.Services.AddSingleton<ServiceBooking.API.Services.PhoneVerification.Max.IMaxBotMessenger>(sp =>
        string.Equals(phoneVerificationProvider, "max-bot", StringComparison.OrdinalIgnoreCase)
            ? sp.GetRequiredService<ServiceBooking.API.Services.PhoneVerification.Max.MaxBotClient>()
            : sp.GetRequiredService<ServiceBooking.API.Services.PhoneVerification.Max.StubMaxBotMessenger>());

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

    // ── Cycle 25: "MAX for staff" (ARCHITECTURE_CYCLE25.md §498) ────────────────────────────────────────
    // The shared webhook handler gets its extension point; with no IMaxBotUpdateHandler registered the cycle-14 behaviour is unchanged.
    builder.Services.Configure<ServiceBooking.API.Services.StaffMax.StaffMaxOptions>(
        builder.Configuration.GetSection(ServiceBooking.API.Services.StaffMax.StaffMaxOptions.SectionName));
    builder.Services.AddSingleton<ServiceBooking.API.Services.StaffMax.StaffMaxAvailability>();
    builder.Services.AddScoped<ServiceBooking.API.Services.StaffMax.StaffMaxLinkService>();
    builder.Services.AddScoped<ServiceBooking.API.Services.StaffMax.StaffMaxStartHandler>();
    builder.Services.AddScoped<ServiceBooking.API.Services.PhoneVerification.Max.IMaxBotUpdateHandler>(sp =>
        sp.GetRequiredService<ServiceBooking.API.Services.StaffMax.StaffMaxStartHandler>());
    builder.Services.AddScoped<ServiceBooking.API.Services.PhoneVerification.PhoneVerificationSessionService>();
    builder.Services.AddScoped<ServiceBooking.API.Services.PhoneVerification.PhoneVerificationWriter>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Bookings.GuestBookingLookup>();

    // §146.3, Q4: re-subscribes once at process start. Registered ONLY when the provider is actually
    // "max-bot" — under "stub" there is nothing to subscribe (and StubMaxBotClient.SubscribeAsync would
    // just return false forever, which is correct but pointless to schedule at all).
    if (string.Equals(phoneVerificationProvider, "max-bot", StringComparison.OrdinalIgnoreCase))
        builder.Services.AddHostedService<ServiceBooking.API.Services.Hosting.MaxWebhookStartupSubscriber>();
    }

    public static void AddBackgroundTasks(this WebApplicationBuilder builder)
    {
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
    // ARCHITECTURE_CYCLE24.md §456.2 — web-push to customers without an account (10-second period from configuration).
    builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.CustomerOrderPushDispatchTask>();
    // ARCHITECTURE_CYCLE25.md §499.4 — MAX messages to staff (5-second period, "realtime" lane).
    builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.StaffMaxDispatchTask>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Notifications.OrderPushSubscriptionWriter>();
    // ARCHITECTURE_CYCLE14.md §146.3 — the SIXTH task, "max-webhook-renew" (period 4h, under the platform's
    // own 8h no-response-drops-the-subscription window, О4). Registered unconditionally, same as every other
    // IScheduledTask — a no-op in practice while PhoneVerification:Provider = "stub" (its own doc comment).
    builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.MaxWebhookRenewTask>();
    // TD-03-quater — the SEVENTH task, "subject-request-due-soon" (period 1 day): sends a GlitchTip signal
    // one working day before a subject request's DueAtUtc, for requests not yet Answered/Rejected.
    builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.SubjectRequestDueSoonTask>();
    // Cycle 18, B7 (ARCHITECTURE_CYCLE18.md §337.1) — the EIGHTH task, "trial-lifecycle" (period 1 hour):
    // self-heals missed mailing-window starts, closes ended windows, issues 7/3/1-day warnings from each
    // account's own snapshot, and materializes trial expiry onto the system Free plan (fail-closed if none
    // is configured).
    builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.TrialLifecycleTask>();
    // Cycle 28, BE-7 (ARCHITECTURE_CYCLE28.md §575.7) — "showcase-reseed" (period 1 hour): weekly re-seed of the showcase; returns "disabled" at once
    // while Showcase:Reseed:Enabled is off (the default).
    builder.Services.Configure<ServiceBooking.API.Services.Showcase.ShowcaseReseedOptions>(
        builder.Configuration.GetSection(ServiceBooking.API.Services.Showcase.ShowcaseReseedOptions.SectionName));
    builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.ShowcaseReseedTask>();
    // Cycle 28, pass B (ARCHITECTURE_CYCLE28.md §580) — "demo-reset" (period 10 minutes): the nightly reset of the demo. Registered ONLY in demo mode, so a production
    // machine does not list it at all (and its state row never appears in the admin's list of tasks).
    if (builder.Configuration.GetValue("DemoMode:Enabled", false))
    {
        builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.DemoResetTask>();
        // Cycle 35 (ARCHITECTURE_CYCLE35.md §35.10.3) — "demo-board-tick" (every 2 minutes): the live board of the demo shops. Same rule: demo mode only.
        builder.Services.AddScoped<ServiceBooking.API.Services.Demo.DemoBoardTicker>();
        builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.DemoBoardTickTask>();
    }

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
    // ARCHITECTURE_CYCLE28.md §577.4 — deletes bookings made by site visitors in open showcase companies after Retention:ShowcaseVisitorBookingHours (24).
    builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
        ServiceBooking.API.Services.Retention.Rules.ShowcaseVisitorBookingRule>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
        ServiceBooking.API.Services.Retention.Rules.BookingPersonalizationRule>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
        ServiceBooking.API.Services.Retention.Rules.ClientNoteRule>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
        ServiceBooking.API.Services.Retention.Rules.ClientNotePhotoRule>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
        ServiceBooking.API.Services.Retention.Rules.BookingEventRule>();
    // ARCHITECTURE_CYCLE23.md §398.5 — order-personalization (does nothing while Retention:OrderPersonalDataDays is 0).
    builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
        ServiceBooking.API.Services.Retention.Rules.OrderPersonalizationRule>();
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
    // Cycle 18, B8 (ARCHITECTURE_CYCLE18.md §343) — the 19th rule: TrialPhoneRegistration is NOT
    // anonymized data (Д14), so it needs its own destruction date like everything else here, 3 years from
    // the date of grant (Д16) plus destruction on HMAC key rotation (К3).
    builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
        ServiceBooking.API.Services.Retention.Rules.TrialPhoneRegistrationRule>();
    // ARCHITECTURE_CYCLE20.md §406.2/§404.6 (US-20-05, US-20-03) — the 20th and 21st rules.
    builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
        ServiceBooking.API.Services.Retention.Rules.GuestDataGateEventRule>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
        ServiceBooking.API.Services.Retention.Rules.PlatformNoticeRule>();
    // ARCHITECTURE_CYCLE24.md §456.4 [legal L16] — the 22nd and 23rd rules.
    builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
        ServiceBooking.API.Services.Retention.Rules.OrderPushSubscriptionRule>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
        ServiceBooking.API.Services.Retention.Rules.CustomerOrderPushNotificationRule>();
    // ARCHITECTURE_CYCLE25.md §504.4, §508 [legal L20] — the 24th–26th rules.
    builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
        ServiceBooking.API.Services.Retention.Rules.StaffMaxLinkRule>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
        ServiceBooking.API.Services.Retention.Rules.StaffMaxMessageRule>();
    builder.Services.AddScoped<ServiceBooking.API.Services.Retention.IRetentionRule,
        ServiceBooking.API.Services.Retention.Rules.ShopCustomerNoteRule>();
    builder.Services.AddScoped<IScheduledTask, ServiceBooking.API.Services.Scheduling.Tasks.DataRetentionTask>();

    builder.Services.AddHostedService<ScheduledTaskRunner>();
    }
}
