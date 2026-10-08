using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Company> Companies => Set<Company>();

    // ARCHITECTURE_CYCLE23.md §388.2 — pickup orders for shops (Company.Kind = Orders).
    public DbSet<ShopSettings> ShopSettings => Set<ShopSettings>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();
    public DbSet<OrderDailyCounter> OrderDailyCounters => Set<OrderDailyCounter>();

    // ARCHITECTURE_CYCLE24.md §448.2 — time, availability, notifications and tariffs of shops.
    public DbSet<ShopSpecialDay> ShopSpecialDays => Set<ShopSpecialDay>();
    public DbSet<ShopDailyMenu> ShopDailyMenus => Set<ShopDailyMenu>();
    public DbSet<ShopDailyMenuItem> ShopDailyMenuItems => Set<ShopDailyMenuItem>();
    public DbSet<OrderPushSubscription> OrderPushSubscriptions => Set<OrderPushSubscription>();
    public DbSet<CustomerOrderPushNotification> CustomerOrderPushNotifications => Set<CustomerOrderPushNotification>();
    public DbSet<OrdersSubscription> OrdersSubscriptions => Set<OrdersSubscription>();

    // Cycle 37 (ARCHITECTURE_CYCLE37.md §37.2): "Дома".
    public DbSet<StaysSettings> StaysSettings => Set<StaysSettings>();
    public DbSet<House> Houses => Set<House>();
    public DbSet<HousePhoto> HousePhotos => Set<HousePhoto>();
    public DbSet<HousePricePeriod> HousePricePeriods => Set<HousePricePeriod>();
    public DbSet<HouseRegistryAttestation> HouseRegistryAttestations => Set<HouseRegistryAttestation>();
    public DbSet<HouseBlock> HouseBlocks => Set<HouseBlock>();
    public DbSet<HouseBlockEvent> HouseBlockEvents => Set<HouseBlockEvent>();
    public DbSet<HouseOccupancy> HouseOccupancies => Set<HouseOccupancy>();
    public DbSet<StayBooking> StayBookings => Set<StayBooking>();
    public DbSet<StayBookingCharge> StayBookingCharges => Set<StayBookingCharge>();
    public DbSet<StayBookingEvent> StayBookingEvents => Set<StayBookingEvent>();
    public DbSet<StayPaymentProof> StayPaymentProofs => Set<StayPaymentProof>();
    public DbSet<StayGuestPushSubscription> StayGuestPushSubscriptions => Set<StayGuestPushSubscription>();
    public DbSet<StayGuestPushNotification> StayGuestPushNotifications => Set<StayGuestPushNotification>();
    public DbSet<StaysSubscription> StaysSubscriptions => Set<StaysSubscription>();

    // Cycle 39 (ARCHITECTURE_CYCLE39.md §39.2): time-slot services of «Дома».
    public DbSet<StayService> StayServices => Set<StayService>();
    public DbSet<StayServicePhoto> StayServicePhotos => Set<StayServicePhoto>();
    public DbSet<StayServiceWeeklyWindow> StayServiceWeeklyWindows => Set<StayServiceWeeklyWindow>();
    public DbSet<StayServiceDateOverride> StayServiceDateOverrides => Set<StayServiceDateOverride>();
    public DbSet<StayServiceScheduleEvent> StayServiceScheduleEvents => Set<StayServiceScheduleEvent>();
    public DbSet<StayServicePriceRule> StayServicePriceRules => Set<StayServicePriceRule>();
    public DbSet<StayServiceItem> StayServiceItems => Set<StayServiceItem>();
    public DbSet<StayServiceSession> StayServiceSessions => Set<StayServiceSession>();
    public DbSet<StayServiceOrder> StayServiceOrders => Set<StayServiceOrder>();
    public DbSet<StayServiceOrderEvent> StayServiceOrderEvents => Set<StayServiceOrderEvent>();
    public DbSet<StaysReminderTemplateChange> StaysReminderTemplateChanges => Set<StaysReminderTemplateChange>();
    public DbSet<OrderMonthlyUsage> OrderMonthlyUsages => Set<OrderMonthlyUsage>();
    public DbSet<CompanyMember> CompanyMembers => Set<CompanyMember>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<MasterService> MasterServices => Set<MasterService>();
    public DbSet<WorkingHours> WorkingHours => Set<WorkingHours>();
    public DbSet<ScheduleBreak> ScheduleBreaks => Set<ScheduleBreak>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingService> BookingServices => Set<BookingService>();
    public DbSet<AccountSubscription> AccountSubscriptions => Set<AccountSubscription>();
    public DbSet<WeeklyScheduleTemplate> WeeklyScheduleTemplates => Set<WeeklyScheduleTemplate>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ClientNote> ClientNotes => Set<ClientNote>();
    public DbSet<MailLog> MailLogs => Set<MailLog>();
    public DbSet<SubscriptionPlanConfig> SubscriptionPlanConfigs => Set<SubscriptionPlanConfig>();
    public DbSet<SubscriptionOption> SubscriptionOptions => Set<SubscriptionOption>();
    public DbSet<SubscriptionChangeLog> SubscriptionChangeLogs => Set<SubscriptionChangeLog>();
    public DbSet<BillingAccount> BillingAccounts => Set<BillingAccount>();
    public DbSet<CompanyOwnerChangeLog> CompanyOwnerChangeLogs => Set<CompanyOwnerChangeLog>();
    public DbSet<PlanOptionRule> PlanOptionRules => Set<PlanOptionRule>();
    public DbSet<ClientNotePhoto> ClientNotePhotos => Set<ClientNotePhoto>();
    public DbSet<BookingEvent> BookingEvents => Set<BookingEvent>();
    public DbSet<CompanyPhoto> CompanyPhotos => Set<CompanyPhoto>();
    public DbSet<ScheduledTaskState> ScheduledTaskStates => Set<ScheduledTaskState>();
    // Cycle 18 (ARCHITECTURE_CYCLE18.md §332.4, §332.5).
    public DbSet<TrialGrant> TrialGrants => Set<TrialGrant>();
    public DbSet<TrialPhoneRegistration> TrialPhoneRegistrations => Set<TrialPhoneRegistration>();

    // Cycle 5 — consent journal (ARCHITECTURE_CYCLE5.md §44.2), replaces cycle 3's UserConsent.
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<ClientHealthNote> ClientHealthNotes => Set<ClientHealthNote>();
    public DbSet<SubjectRequest> SubjectRequests => Set<SubjectRequest>();

    // Cycle 4 — WhatsApp notifications (ARCHITECTURE_CYCLE4.md §23, §25).
    public DbSet<City> Cities => Set<City>();
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<ChannelCompanyAssignment> ChannelCompanyAssignments => Set<ChannelCompanyAssignment>();
    public DbSet<ChannelStateEvent> ChannelStateEvents => Set<ChannelStateEvent>();
    public DbSet<ChannelPaymentLog> ChannelPaymentLogs => Set<ChannelPaymentLog>();
    public DbSet<OutboundNotification> OutboundNotifications => Set<OutboundNotification>();
    public DbSet<CompanyNotificationSettings> CompanyNotificationSettings => Set<CompanyNotificationSettings>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationTemplateHistory> NotificationTemplateHistories => Set<NotificationTemplateHistory>();
    public DbSet<NotificationOptOut> NotificationOptOuts => Set<NotificationOptOut>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    public DbSet<PlatformSettingChangeLog> PlatformSettingChangeLogs => Set<PlatformSettingChangeLog>();

    // ARCHITECTURE_CYCLE9.md §105.4 (US-116/US-123, proход C: Web Push мастеру).
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<StaffPushNotification> StaffPushNotifications => Set<StaffPushNotification>();

    // Cycle 25 (ARCHITECTURE_CYCLE25.md §497.2).
    public DbSet<StaffMaxLink> StaffMaxLinks => Set<StaffMaxLink>();
    public DbSet<StaffMaxLinkSession> StaffMaxLinkSessions => Set<StaffMaxLinkSession>();
    public DbSet<StaffMaxMessage> StaffMaxMessages => Set<StaffMaxMessage>();
    public DbSet<ShopCustomerNote> ShopCustomerNotes => Set<ShopCustomerNote>();

    // Cycle 7, stage 3 (ARCHITECTURE_CYCLE7.md §43.3): what's paid for on an account's subscription —
    // today, only read for the "notifications.whatsapp" option's Quantity (§47.1's N).
    public DbSet<AccountSubscriptionOption> AccountSubscriptionOptions => Set<AccountSubscriptionOption>();

    // ARCHITECTURE_CYCLE14.md §142 — platform phone-ownership verification (MAX bot track). Deliberately
    // NOT part of the Notifications:* subsystem (own top-level config section, own secrets, §140.2/R5).
    public DbSet<PhoneVerificationSession> PhoneVerificationSessions => Set<PhoneVerificationSession>();
    public DbSet<VerifiedPhone> VerifiedPhones => Set<VerifiedPhone>();

    // Cycle 20 (ARCHITECTURE_CYCLE20.md §406.2, §404.1) — guest-data-gate journal and platform notices.
    public DbSet<GuestDataGateEvent> GuestDataGateEvents => Set<GuestDataGateEvent>();
    public DbSet<PlatformNotice> PlatformNotices => Set<PlatformNotice>();
    public DbSet<PlatformNoticeAcknowledgement> PlatformNoticeAcknowledgements => Set<PlatformNoticeAcknowledgement>();

    /// <summary>
    /// ARCHITECTURE_CYCLE39.md §39.5.2 — "the board revision is ALWAYS the last lock". Inside a transaction the bump of StaysSettings.BookingsRevision is only REMEMBERED here and
    /// executed right before the next SaveChanges (the write of the transaction), so a lazy release or any journal append no longer holds the settings row while the transaction
    /// goes on taking the locks of services, orders and bookings (that order of locks was the 40P01 of the review).
    /// </summary>
    public HashSet<Guid> PendingRevisionBumps { get; } = [];

    public async Task BumpRevisionAsync(Guid companyId)
    {
        if (Database.CurrentTransaction is null) await ExecuteBumpAsync(companyId);
        else PendingRevisionBumps.Add(companyId);
    }

    private Task<int> ExecuteBumpAsync(Guid companyId) =>
        Database.ExecuteSqlInterpolatedAsync($"""UPDATE "StaysSettings" SET "BookingsRevision" = "BookingsRevision" + 1 WHERE "CompanyId" = {companyId}""");

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (PendingRevisionBumps.Count > 0)
        {
            var pending = PendingRevisionBumps.OrderBy(i => i).ToList();
            PendingRevisionBumps.Clear();
            foreach (var id in pending) await ExecuteBumpAsync(id);
        }
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Cycle 37 (§37.2.7): the EXCLUDE constraints of HouseOccupancies / HousePricePeriods need btree_gist (a trusted extension since PG 13).
        builder.HasPostgresExtension("btree_gist");

        builder.Entity<Company>(e =>
        {
            e.HasIndex(c => c.Slug).IsUnique();
            e.HasOne(c => c.Owner).WithMany().HasForeignKey(c => c.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            // Cycle 4 (§23.3, §35 migration 2): Restrict — a city referenced by a company can't be
            // deleted from the directory out from under it. City.Id starts at 1, so 0 stays unused and
            // NULL means "not migrated yet" is unambiguous during the backfill.
            e.HasOne(c => c.City).WithMany().HasForeignKey(c => c.CityId).OnDelete(DeleteBehavior.Restrict);
            e.Property(c => c.TimeZoneId).HasMaxLength(64);
            // Cycle 7 (ARCHITECTURE_CYCLE7.md §43.4): "who pays" — Restrict so a billing account
            // can't be deleted out from under a company that still belongs to it. Stage 6 (§43.6,
            // B5-13): NOT NULL at the database level (`BackfillBillingAccounts`/`AddCoTenancyKeys`
            // already populated every row) and an alternate key on (Id, BillingAccountId) — the
            // composite FK from ChannelCompanyAssignment pins to it, which is what makes "assign a
            // company to a number belonging to a different account" physically impossible.
            e.Property(c => c.BillingAccountId).IsRequired();
            e.HasOne(c => c.BillingAccount).WithMany().HasForeignKey(c => c.BillingAccountId)
                .IsRequired().OnDelete(DeleteBehavior.Restrict);
            e.HasAlternateKey(c => new { c.Id, c.BillingAccountId });
            // Cycle 28 (ARCHITECTURE_CYCLE28.md §572.1): showcase marks. Partial indexes — on production almost empty.
            e.HasIndex(c => c.Id).HasDatabaseName("IX_Companies_IsShowcase").HasFilter("\"IsShowcase\"");
            e.ToTable(t => t.HasCheckConstraint("CK_Companies_ShowcaseBookingOpen", "NOT \"ShowcaseBookingOpen\" OR \"IsShowcase\""));
            // Cycle 9 (ARCHITECTURE_CYCLE9.md §103.5) — GET /api/companies/public filters/sorts/pages
            // in SQL on exactly this shape (ShowInPublicListing, then CityId equality, then Name order);
            // partial on the same "IsActive AND ShowInPublicListing" predicate the query itself applies,
            // so the index only ever covers rows that can actually appear in the public directory.
            e.HasIndex(c => new { c.ShowInPublicListing, c.CityId, c.Name })
                .HasDatabaseName("IX_Companies_PublicListing")
                .HasFilter("\"IsActive\" AND \"ShowInPublicListing\"");
            // Cycle 9 code review (US-115 follow-up): IX_Companies_PublicListing's leading CityId column
            // only helps the ?cityId=... branch of GET /api/companies/public. With no cityId (the
            // default "все города" view — also what the home page opens with first, ARCHITECTURE_CYCLE9.md
            // §103.5) that index cannot serve the ORDER BY Name at all: Postgres falls back to a full
            // scan + sort of every publicly-listed company. Measured live against a throwaway Postgres 16
            // container seeded with 20,000 companies: ~8.3ms (Seq Scan + Sort) without this index vs
            // ~0.3ms (Index Scan, no sort step) with it — not a marginal difference at this table's
            // realistic future size. A second, narrower partial index — same filter, but ordered by
            // (ShowInPublicListing, Name, Id) with no CityId — serves exactly that default branch instead.
            e.HasIndex(c => new { c.ShowInPublicListing, c.Name, c.Id })
                .HasDatabaseName("IX_Companies_PublicListing_Default")
                .HasFilter("\"IsActive\" AND \"ShowInPublicListing\"");
            // ARCHITECTURE_CYCLE19.md §383.3: five columns from the cycle-13 address-verification
            // geocoder (removed целиком in cycle 19, §388.1). Kept as EF SHADOW properties — no CLR
            // property on Company any more — purely so `dotnet ef migrations add` generates no
            // DropColumn (ломающие миграции запрещены с 25.09.2026). Code never reads or writes them.
            // Types match the applied migration/snapshot exactly. Do NOT turn these back into CLR
            // properties; physical removal is a separate, later cycle's decision after production data
            // is checked (§393).
            e.Property<string?>("AddressVerifiedInputKey").HasMaxLength(300);
            e.Property<DateTime?>("AddressVerifiedAt");
            e.Property<int?>("AddressPrecision");
            e.Property<double?>("AddressLatitude");
            e.Property<double?>("AddressLongitude");
            // ARCHITECTURE_CYCLE15.md §252 — owner-pasted map links, stored byte-for-byte. 500 gives a
            // 3x margin over the ~150-char real links in 0-bis П2 while still being a boundary the
            // server rejects at, rather than silently truncating (MapLinkValidation.MaxLength).
            e.Property(c => c.YandexMapsUrl).HasMaxLength(500);
            e.Property(c => c.TwoGisUrl).HasMaxLength(500);
            // §252 — NOT NULL DEFAULT 2 at the DB level: "not set" has no meaning for this rule, it
            // must apply to every company. ClientRescheduleWindow.Default duplicates the literal (this
            // project can't reference the API assembly from Infrastructure) — same convention Company.cs
            // already uses for BookingHorizonDays/TimeZoneId's own literals.
            e.Property(c => c.ClientRescheduleMinHours).HasDefaultValue(2);
            // ARCHITECTURE_CYCLE23.md §388.1 — every existing row is a salon (Services = 0), no backfill.
            e.Property(c => c.Kind).HasDefaultValue(CompanyKind.Services);
        });

        builder.Entity<Service>(e =>
        {
            e.Property(s => s.Price).HasColumnType("decimal(10,2)");
        });

        // ── Cycle 23: shop orders (ARCHITECTURE_CYCLE23.md §388.2) ────────────────────────────────────
        builder.Entity<ShopSettings>(e =>
        {
            e.HasKey(s => s.CompanyId);
            e.HasOne<Company>().WithOne().HasForeignKey<ShopSettings>(s => s.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.Property(s => s.AllowCustomerCancel).HasDefaultValue(true);
            e.Property(s => s.SellerLegalName).HasMaxLength(300);
            e.Property(s => s.SellerInn).HasMaxLength(12);
            e.Property(s => s.SellerOgrn).HasMaxLength(15);
            e.Property(s => s.SellerLegalAddress).HasMaxLength(500);

            // Cycle 24 (ARCHITECTURE_CYCLE24.md §448.1): a DB default on EVERY new NOT NULL column —
            // OrderEventLog's upsert INSERTs only the cycle-23 columns.
            e.Property(s => s.WorkingHoursJson).HasColumnType("jsonb");
            e.Property(s => s.OrdersStopped).HasDefaultValue(false);
            e.Property(s => s.AcceptanceChangedByName).HasMaxLength(200);
            e.Property(s => s.AsapEnabled).HasDefaultValue(true);
            e.Property(s => s.ScheduledEnabled).HasDefaultValue(false);
            e.Property(s => s.SlotStepMinutes).HasDefaultValue(15);
            // Cycle 25 (§497.1): DB default true — OrderEventLog's upsert does not list this column.
            e.Property(s => s.StaffMaxEnabled).HasDefaultValue(true);
            e.Property(s => s.PreorderDays).HasDefaultValue(0);
            e.Property(s => s.MinPrepMinutes).HasDefaultValue(15).HasSentinel(-1); // 0 minutes is a real value
            e.Property(s => s.CustomerWebPushEnabled).HasDefaultValue(true);
            e.Property(s => s.CustomerMessengerEnabled).HasDefaultValue(false);
        });

        builder.Entity<ShopSpecialDay>(e =>
        {
            e.HasKey(d => new { d.CompanyId, d.Date });
            e.HasOne<Company>().WithMany().HasForeignKey(d => d.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.Property(d => d.IntervalsJson).HasColumnType("jsonb");
        });

        builder.Entity<ShopDailyMenu>(e =>
        {
            e.HasOne<Company>().WithMany().HasForeignKey(m => m.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(m => new { m.CompanyId, m.Date }).IsUnique();
        });

        builder.Entity<ShopDailyMenuItem>(e =>
        {
            e.HasKey(i => new { i.DailyMenuId, i.ProductId });
            e.HasOne(i => i.DailyMenu).WithMany(m => m.Items).HasForeignKey(i => i.DailyMenuId).OnDelete(DeleteBehavior.Cascade);
            // Products are deleted softly, the FK never gets in the way.
            e.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(i => i.ProductId);
        });

        builder.Entity<OrderPushSubscription>(e =>
        {
            e.HasOne(s => s.Order).WithMany().HasForeignKey(s => s.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.Property(s => s.Endpoint).HasMaxLength(500);
            e.Property(s => s.KeyId).HasMaxLength(16);
            e.HasIndex(s => new { s.OrderId, s.Endpoint }).IsUnique();
            e.HasIndex(s => s.CreatedAtUtc);
        });

        builder.Entity<CustomerOrderPushNotification>(e =>
        {
            e.HasOne(n => n.Order).WithMany().HasForeignKey(n => n.OrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(n => n.Company).WithMany().HasForeignKey(n => n.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(n => n.Subscription).WithMany().HasForeignKey(n => n.SubscriptionId).OnDelete(DeleteBehavior.SetNull);
            e.Property(n => n.Payload).HasMaxLength(1000);
            e.Property(n => n.ReasonDetail).HasMaxLength(300);
            e.Property(n => n.IdempotencyKey).HasMaxLength(200);
            e.HasIndex(n => n.IdempotencyKey).IsUnique();
            // The copy of IX_StaffPushNotifications_Dispatch (Status = 0 is Pending — a raw literal, see NotificationStatus).
            e.HasIndex(n => new { n.ExpiresAtUtc, n.CreatedAt })
                .HasDatabaseName("IX_CustomerOrderPushNotifications_Dispatch")
                .HasFilter("\"Status\" = 0")
                .IncludeProperties(n => new { n.CompanyId, n.SubscriptionId });
            e.HasIndex(n => n.OrderId);
        });

        builder.Entity<OrdersSubscription>(e =>
        {
            e.HasOne(s => s.BillingAccount).WithMany().HasForeignKey(s => s.BillingAccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.PlanConfig).WithMany().HasForeignKey(s => s.PlanConfigId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(s => s.BillingAccountId).IsUnique();
        });

        // ── Cycle 37: "Дома" (ARCHITECTURE_CYCLE37.md §37.2). The three EXCLUDE constraints are created by the migration with raw SQL
        // (EF cannot model them): EX_HouseOccupancies_NoOverlap and EX_HousePricePeriods_NoOverlap.
        builder.Entity<StaysSettings>(e =>
        {
            e.HasKey(s => s.CompanyId);
            e.HasOne<Company>().WithOne().HasForeignKey<StaysSettings>(s => s.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.Property(s => s.PaymentDetails).HasMaxLength(1000);
            e.Property(s => s.PaymentPurpose).HasMaxLength(200);
            e.Property(s => s.CheckInInfoText).HasMaxLength(2000);
            e.Property(s => s.ProviderName).HasMaxLength(300);
            e.Property(s => s.ProviderInn).HasMaxLength(12);
            e.Property(s => s.ProviderOgrn).HasMaxLength(15);
            e.Property(s => s.ProviderClaimsAddress).HasMaxLength(500);
            e.Property(s => s.UpdatedByUserId).HasMaxLength(450);
            // Cycle 39 (§39.2.2): companies of cycle 37 get 18:00, the default text, no push text, no orders without a stay.
            e.Property(s => s.ArrivalReminderTime).HasDefaultValue(new TimeOnly(18, 0));
            e.Property(s => s.ArrivalReminderTemplate).HasMaxLength(700);
            e.Property(s => s.ArrivalReminderPushText).HasDefaultValue(false);
            e.Property(s => s.AcceptServiceOrdersWithoutStay).HasDefaultValue(false);
            // A DB default on every NOT NULL column: StayBookingEventLog bumps the revision with a raw UPDATE/upsert.
            e.Property(s => s.BookingsRevision).HasDefaultValue(0L);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_StaysSettings_CheckOutNotAfterCheckIn", "\"CheckOutTime\" <= \"CheckInTime\"");
                t.HasCheckConstraint("CK_StaysSettings_Nights", "\"MinNights\" BETWEEN 1 AND 30 AND \"MaxNights\" BETWEEN 1 AND 90 AND \"MinNights\" <= \"MaxNights\"");
            });
        });

        builder.Entity<House>(e =>
        {
            e.HasOne(h => h.Company).WithMany().HasForeignKey(h => h.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.Property(h => h.Slug).HasMaxLength(50);
            e.Property(h => h.Name).HasMaxLength(100);
            e.Property(h => h.Description).HasMaxLength(4000);
            e.Property(h => h.Address).HasMaxLength(500);
            e.Property(h => h.YandexMapsUrl).HasMaxLength(500);
            e.Property(h => h.TwoGisUrl).HasMaxLength(500);
            e.Property(h => h.CheckInInfoText).HasMaxLength(2000);
            e.Property(h => h.RegistryNumber).HasMaxLength(32);
            e.Property(h => h.RegistryUrl).HasMaxLength(500);
            e.HasIndex(h => new { h.CompanyId, h.Slug }).IsUnique();
            e.HasIndex(h => new { h.CompanyId, h.Position });
            e.HasIndex(h => h.CompanyId).HasDatabaseName("IX_Houses_Published").HasFilter("\"IsPublished\" AND \"ArchivedAtUtc\" IS NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_Houses_NotPublishedAndArchived", "NOT (\"IsPublished\" AND \"ArchivedAtUtc\" IS NOT NULL)"));
        });

        builder.Entity<HousePhoto>(e =>
        {
            e.HasOne(p => p.House).WithMany(h => h.Photos).HasForeignKey(p => p.HouseId).OnDelete(DeleteBehavior.Cascade);
            e.Property(p => p.Url).HasMaxLength(500);
            e.Property(p => p.ThumbnailUrl).HasMaxLength(500);
            e.HasIndex(p => new { p.HouseId, p.Position });
        });

        builder.Entity<HousePricePeriod>(e =>
        {
            e.HasOne<House>().WithMany().HasForeignKey(p => p.HouseId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => new { p.HouseId, p.StartDate }).IsUnique().HasDatabaseName("UX_HousePricePeriods_SingleDay").HasFilter("\"StartDate\" = \"EndDate\"");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_HousePricePeriods_Dates", "\"StartDate\" <= \"EndDate\" AND \"EndDate\" - \"StartDate\" <= 730");
                t.HasCheckConstraint("CK_HousePricePeriods_Price", "\"PriceRub\" BETWEEN 1 AND 1000000");
            });
        });

        builder.Entity<HouseRegistryAttestation>(e =>
        {
            e.HasOne<House>().WithMany().HasForeignKey(a => a.HouseId).OnDelete(DeleteBehavior.Restrict);
            e.Property(a => a.RegistryNumber).HasMaxLength(32);
            e.Property(a => a.RegistryUrl).HasMaxLength(500);
            e.Property(a => a.NoticeVersion).HasMaxLength(80);
            e.Property(a => a.AttestedByUserId).HasMaxLength(450);
            e.Property(a => a.IpAddress).HasMaxLength(45);
            e.HasIndex(a => new { a.HouseId, a.AttestedAtUtc });
        });

        builder.Entity<HouseBlock>(e =>
        {
            e.HasOne<House>().WithMany().HasForeignKey(b => b.HouseId).OnDelete(DeleteBehavior.Restrict);
            e.Property(b => b.Comment).HasMaxLength(300);
            e.Property(b => b.CreatedByUserId).HasMaxLength(450);
            e.HasIndex(b => new { b.CompanyId, b.HouseId });
            e.ToTable(t => t.HasCheckConstraint("CK_HouseBlocks_Dates", "\"EndDate\" > \"StartDate\""));
        });

        builder.Entity<HouseBlockEvent>(e =>
        {
            e.HasOne<HouseBlock>().WithMany().HasForeignKey(b => b.HouseBlockId).OnDelete(DeleteBehavior.Restrict);
            e.Property(b => b.ActorUserId).HasMaxLength(450);
            e.Property(b => b.ActorNameSnapshot).HasMaxLength(200);
            e.Property(b => b.BeforeJson).HasColumnType("jsonb");
            e.Property(b => b.AfterJson).HasColumnType("jsonb");
            e.HasIndex(b => new { b.HouseBlockId, b.OccurredAtUtc });
        });

        builder.Entity<HouseOccupancy>(e =>
        {
            e.HasOne<Company>().WithMany().HasForeignKey(o => o.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<House>().WithMany().HasForeignKey(o => o.HouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StayBooking>().WithMany().HasForeignKey(o => o.StayBookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<HouseBlock>().WithMany().HasForeignKey(o => o.HouseBlockId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(o => o.StayBookingId).IsUnique().HasDatabaseName("UX_HouseOccupancies_Booking").HasFilter("\"StayBookingId\" IS NOT NULL");
            e.HasIndex(o => o.HouseBlockId).IsUnique().HasDatabaseName("UX_HouseOccupancies_Block").HasFilter("\"HouseBlockId\" IS NOT NULL");
            e.HasIndex(o => new { o.HouseId, o.EndDate }).HasDatabaseName("IX_HouseOccupancies_Active").HasFilter("\"ReleasedAtUtc\" IS NULL");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_HouseOccupancies_Dates", "\"EndDate\" > \"StartDate\"");
                t.HasCheckConstraint("CK_HouseOccupancies_Source", "((\"Source\" = 0) = (\"StayBookingId\" IS NOT NULL)) AND ((\"Source\" = 1) = (\"HouseBlockId\" IS NOT NULL))");
            });
        });

        builder.Entity<StayBooking>(e =>
        {
            e.HasOne(b => b.Company).WithMany().HasForeignKey(b => b.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(b => b.House).WithMany().HasForeignKey(b => b.HouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(b => b.GuestUserId).OnDelete(DeleteBehavior.SetNull);
            e.Property(b => b.Version).IsConcurrencyToken();
            e.Property(b => b.PublicToken).HasMaxLength(64);
            e.Property(b => b.GuestName).HasMaxLength(100);
            e.Property(b => b.GuestPhone).HasMaxLength(20);
            e.Property(b => b.Comment).HasMaxLength(500);
            e.Property(b => b.NightPricesJson).HasColumnType("jsonb");
            e.Property(b => b.ProviderSnapshotJson).HasColumnType("jsonb");
            e.Property(b => b.TimeZoneIdSnapshot).HasMaxLength(64);
            e.Property(b => b.PaymentDetailsSnapshot).HasMaxLength(1000);
            e.Property(b => b.PaymentPurposeSnapshot).HasMaxLength(200);
            e.Property(b => b.ConsentPrivacyVersion).HasMaxLength(64);
            e.Property(b => b.ConsentTermsVersion).HasMaxLength(64);
            e.Property(b => b.BookingNoticeVersion).HasMaxLength(64);
            e.Property(b => b.BookingTermsVersion).HasMaxLength(64);
            e.Property(b => b.CancellationTermsVersion).HasMaxLength(64);
            e.Property(b => b.MessengerConsentVersion).HasMaxLength(64);
            e.Property(b => b.StatusReason).HasMaxLength(300);
            e.Property(b => b.PaymentConfirmedByUserId).HasMaxLength(450);
            e.Property(b => b.PaymentConfirmedByNameSnapshot).HasMaxLength(200);
            e.Property(b => b.ArrivalReminderPageText).HasMaxLength(1200);
            e.HasIndex(b => b.PublicToken).IsUnique();
            e.HasIndex(b => new { b.CompanyId, b.IdempotencyKey }).IsUnique();
            e.HasIndex(b => new { b.CompanyId, b.Status });
            e.HasIndex(b => new { b.HouseId, b.CheckInDate });
            e.HasIndex(b => new { b.GuestUserId, b.CreatedAtUtc });
            e.HasIndex(b => new { b.GuestPhone, b.CreatedAtUtc }).HasDatabaseName("IX_StayBookings_Phone").HasFilter("\"GuestPhone\" IS NOT NULL");
            e.HasIndex(b => b.HoldExpiresAtUtc).HasDatabaseName("IX_StayBookings_HoldExpiry").HasFilter("\"Status\" = 0");
            e.HasIndex(b => b.CheckInDate).HasDatabaseName("IX_StayBookings_ConfirmedCheckIn").HasFilter("\"Status\" = 2");
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_StayBookings_Dates", "\"CheckOutDate\" > \"CheckInDate\" AND \"CheckOutDate\" - \"CheckInDate\" <= 366");
                t.HasCheckConstraint("CK_StayBookings_Nights", "\"Nights\" = \"CheckOutDate\" - \"CheckInDate\"");
            });
        });

        builder.Entity<StayBookingCharge>(e =>
        {
            e.HasOne<StayBooking>().WithMany(b => b.Charges).HasForeignKey(c => c.StayBookingId).OnDelete(DeleteBehavior.Cascade);
            e.Property(c => c.Label).HasMaxLength(200);
            e.HasIndex(c => new { c.StayBookingId, c.Position });
            // Cycle 39 (§39.2.2): the lines of a session; a service is paid on site, never from the prepayment.
            e.HasOne<StayServiceSession>().WithMany().HasForeignKey(c => c.ServiceSessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => c.ServiceSessionId).HasFilter("\"ServiceSessionId\" IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_StayBookingCharges_ServiceNotPrepaid", "NOT (\"Kind\" IN (5, 6) AND \"PrepayEligible\")"));
        });

        builder.Entity<StayBookingEvent>(e =>
        {
            e.HasOne<StayBooking>().WithMany().HasForeignKey(v => v.StayBookingId).OnDelete(DeleteBehavior.Cascade);
            e.Property(v => v.ActorUserId).HasMaxLength(450);
            e.Property(v => v.ActorNameSnapshot).HasMaxLength(200);
            e.Property(v => v.Reason).HasMaxLength(300);
            e.Property(v => v.DetailsJson).HasColumnType("jsonb");
            e.HasIndex(v => new { v.StayBookingId, v.OccurredAtUtc });
            e.HasIndex(v => v.OccurredAtUtc);
            e.HasOne<StayServiceSession>().WithMany().HasForeignKey(v => v.ServiceSessionId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<StayPaymentProof>(e =>
        {
            e.HasOne<StayBooking>().WithMany(b => b.PaymentProofs).HasForeignKey(p => p.StayBookingId).OnDelete(DeleteBehavior.Restrict);
            e.Property(p => p.StorageKey).HasMaxLength(200);
            e.Property(p => p.ContentType).HasMaxLength(50);
            e.HasIndex(p => p.StayBookingId);
            // Cycle 39 (§39.2.2): the proof of a stand-alone order; exactly one owner.
            e.HasOne<StayServiceOrder>().WithMany(o => o.PaymentProofs).HasForeignKey(p => p.StayServiceOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => p.StayServiceOrderId).HasFilter("\"StayServiceOrderId\" IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_StayPaymentProofs_OneOwner", "num_nonnulls(\"StayBookingId\", \"StayServiceOrderId\") = 1"));
        });

        builder.Entity<StayGuestPushSubscription>(e =>
        {
            e.HasOne(s => s.StayBooking).WithMany().HasForeignKey(s => s.StayBookingId).OnDelete(DeleteBehavior.Cascade);
            e.Property(s => s.Endpoint).HasMaxLength(500);
            e.Property(s => s.KeyId).HasMaxLength(16);
            e.HasIndex(s => new { s.StayBookingId, s.Endpoint }).IsUnique();
            e.HasIndex(s => s.CreatedAtUtc);
            // Cycle 39 (§39.2.2): the browser of a guest on the page of a stand-alone order.
            e.HasOne<StayServiceOrder>().WithMany().HasForeignKey(s => s.StayServiceOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.StayServiceOrderId, s.Endpoint }).IsUnique().HasDatabaseName("UX_StayGuestPushSubscriptions_Order_Endpoint")
                .HasFilter("\"StayServiceOrderId\" IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_StayGuestPushSubscriptions_OneOwner", "num_nonnulls(\"StayBookingId\", \"StayServiceOrderId\") = 1"));
        });

        builder.Entity<StayGuestPushNotification>(e =>
        {
            e.HasOne(n => n.StayBooking).WithMany().HasForeignKey(n => n.StayBookingId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Company>().WithMany().HasForeignKey(n => n.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(n => n.Subscription).WithMany().HasForeignKey(n => n.SubscriptionId).OnDelete(DeleteBehavior.SetNull);
            e.Property(n => n.Payload).HasMaxLength(1000);
            e.Property(n => n.ReasonDetail).HasMaxLength(300);
            e.Property(n => n.IdempotencyKey).HasMaxLength(200);
            e.HasIndex(n => n.IdempotencyKey).IsUnique();
            e.HasIndex(n => new { n.ExpiresAtUtc, n.CreatedAt })
                .HasDatabaseName("IX_StayGuestPushNotifications_Dispatch")
                .HasFilter("\"Status\" = 0")
                .IncludeProperties(n => new { n.CompanyId, n.SubscriptionId });
            e.HasIndex(n => n.StayBookingId);
            // Cycle 39: at most one owner (SetNull of the booking may leave none — that is why not «exactly one»).
            e.HasOne<StayServiceOrder>().WithMany().HasForeignKey(n => n.StayServiceOrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(n => n.StayServiceOrderId).HasFilter("\"StayServiceOrderId\" IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_StayGuestPushNotifications_OneOwner", "num_nonnulls(\"StayBookingId\", \"StayServiceOrderId\") <= 1"));
        });

        // ── Cycle 39: time-slot services (ARCHITECTURE_CYCLE39.md §39.2). EX_StayServiceSessions_NoOverlap is created by the migration with raw SQL (EF cannot model it). ──
        builder.Entity<StayService>(e =>
        {
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Slug).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.HasIndex(x => new { x.CompanyId, x.Slug }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.Position });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_StayServices_Hours", "1 <= \"MinHours\" AND \"MinHours\" <= \"MaxHours\" AND \"MaxHours\" <= 12");
                t.HasCheckConstraint("CK_StayServices_Step", "\"StepMinutes\" IN (30, 60)");
                t.HasCheckConstraint("CK_StayServices_Buffer", "\"BufferMinutes\" BETWEEN 0 AND 240 AND \"BufferMinutes\" % 15 = 0");
                t.HasCheckConstraint("CK_StayServices_Lead", "\"MinLeadMinutes\" BETWEEN 0 AND 2880 AND \"MinLeadMinutes\" % 30 = 0");
                t.HasCheckConstraint("CK_StayServices_Prepay", "\"StandalonePrepayPercent\" IS NULL OR \"StandalonePrepayPercent\" BETWEEN 1 AND 100");
                t.HasCheckConstraint("CK_StayServices_Boundary", "\"CancellationBoundaryHours\" BETWEEN 1 AND 24");
                t.HasCheckConstraint("CK_StayServices_PublishedNotArchived", "NOT (\"IsPublished\" AND \"ArchivedAtUtc\" IS NOT NULL)");
            });
        });

        builder.Entity<StayServicePhoto>(e =>
        {
            e.HasOne<StayService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Url).HasMaxLength(300);
            e.Property(x => x.ThumbnailUrl).HasMaxLength(300);
            e.HasIndex(x => new { x.ServiceId, x.Position });
        });

        builder.Entity<StayServiceWeeklyWindow>(e =>
        {
            e.HasOne<StayService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ServiceId, x.DayOfWeek });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_StayServiceWeeklyWindows_Day", "\"DayOfWeek\" BETWEEN 1 AND 7");
                t.HasCheckConstraint("CK_StayServiceWeeklyWindows_Minutes", "\"StartMinute\" >= 0 AND \"EndMinute\" > \"StartMinute\" AND \"EndMinute\" <= 2880");
            });
        });

        builder.Entity<StayServiceDateOverride>(e =>
        {
            e.HasOne<StayService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.WindowsJson).HasColumnType("jsonb");
            e.Property(x => x.Comment).HasMaxLength(300);
            e.Property(x => x.UpdatedByUserId).HasMaxLength(450);
            e.HasIndex(x => new { x.ServiceId, x.BusinessDate }).IsUnique();
        });

        builder.Entity<StayServiceScheduleEvent>(e =>
        {
            e.HasOne<StayService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.ActorUserId).HasMaxLength(450);
            e.Property(x => x.ActorNameSnapshot).HasMaxLength(200);
            e.Property(x => x.BeforeJson).HasColumnType("jsonb");
            e.Property(x => x.AfterJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.ServiceId, x.OccurredAtUtc });
            e.HasIndex(x => x.OccurredAtUtc);
        });

        builder.Entity<StayServicePriceRule>(e =>
        {
            e.HasOne<StayService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ServiceId);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_StayServicePriceRules_Days", "\"DaysMask\" BETWEEN 1 AND 127");
                t.HasCheckConstraint("CK_StayServicePriceRules_Hours", "\"FromHour\" BETWEEN 6 AND 29 AND \"ToHour\" > \"FromHour\" AND \"ToHour\" <= 30");
                t.HasCheckConstraint("CK_StayServicePriceRules_Price", "\"PriceRub\" BETWEEN 1 AND 100000");
            });
        });

        builder.Entity<StayServiceItem>(e =>
        {
            e.HasOne<StayService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => new { x.ServiceId, x.Position });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_StayServiceItems_Price", "\"PriceRub\" BETWEEN 0 AND 100000");
                t.HasCheckConstraint("CK_StayServiceItems_Max", "\"MaxPerSession\" BETWEEN 1 AND 50");
            });
        });

        builder.Entity<StayServiceOrder>(e =>
        {
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StayService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.GuestUserId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.PublicToken).HasMaxLength(64);
            e.Property(x => x.StatusReason).HasMaxLength(300);
            e.Property(x => x.GuestName).HasMaxLength(100);
            e.Property(x => x.GuestPhone).HasMaxLength(20);
            e.Property(x => x.Comment).HasMaxLength(500);
            e.Property(x => x.TimeZoneIdSnapshot).HasMaxLength(64);
            e.Property(x => x.PaymentDetailsSnapshot).HasMaxLength(1000);
            e.Property(x => x.PaymentPurposeSnapshot).HasMaxLength(200);
            e.Property(x => x.ProviderSnapshotJson).HasColumnType("jsonb");
            e.Property(x => x.ConsentPrivacyVersion).HasMaxLength(64);
            e.Property(x => x.ConsentTermsVersion).HasMaxLength(64);
            e.Property(x => x.BookingNoticeVersion).HasMaxLength(64);
            e.Property(x => x.BookingTermsVersion).HasMaxLength(64);
            e.Property(x => x.CancellationTermsVersion).HasMaxLength(64);
            e.Property(x => x.MessengerConsentVersion).HasMaxLength(64);
            e.Property(x => x.PaymentConfirmedByUserId).HasMaxLength(450);
            e.Property(x => x.PaymentConfirmedByNameSnapshot).HasMaxLength(200);
            e.HasIndex(x => x.PublicToken).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.CompanyId, x.Status });
            e.HasIndex(x => x.HoldExpiresAtUtc).HasDatabaseName("IX_StayServiceOrders_HoldExpiry").HasFilter("\"Status\" = 0");
            e.HasIndex(x => new { x.GuestPhone, x.CreatedAtUtc }).HasDatabaseName("IX_StayServiceOrders_Phone").HasFilter("\"GuestPhone\" IS NOT NULL");
            e.HasIndex(x => new { x.GuestUserId, x.CreatedAtUtc });
            e.ToTable(t => t.HasCheckConstraint("CK_StayServiceOrders_Money", "\"TotalRub\" = \"ServiceAmountRub\" + \"ItemsAmountRub\" AND \"DueOnSiteRub\" = \"TotalRub\" - \"PrepayRub\""));
        });

        builder.Entity<StayServiceSession>(e =>
        {
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StayService>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StayBooking>().WithMany().HasForeignKey(x => x.StayBookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StayServiceOrder>().WithMany().HasForeignKey(x => x.StayServiceOrderId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Property(x => x.ServiceNameSnapshot).HasMaxLength(100);
            e.Property(x => x.HourPricesJson).HasColumnType("jsonb");
            e.Property(x => x.ItemsJson).HasColumnType("jsonb");
            e.Property(x => x.AddedByUserId).HasMaxLength(450);
            e.Property(x => x.AddedByNameSnapshot).HasMaxLength(200);
            e.Property(x => x.AddNoticeVersion).HasMaxLength(64);
            e.Property(x => x.StatusReason).HasMaxLength(300);
            e.HasIndex(x => x.StayServiceOrderId).IsUnique().HasDatabaseName("UX_StayServiceSessions_Order").HasFilter("\"StayServiceOrderId\" IS NOT NULL");
            e.HasIndex(x => new { x.StayBookingId, x.IdempotencyKey }).IsUnique().HasDatabaseName("UX_StayServiceSessions_Booking_Key").HasFilter("\"IdempotencyKey\" IS NOT NULL");
            e.HasIndex(x => new { x.ServiceId, x.StartUtc }).HasDatabaseName("IX_StayServiceSessions_Active").HasFilter("\"ReleasedAtUtc\" IS NULL");
            e.HasIndex(x => x.StayBookingId);
            e.HasIndex(x => new { x.CompanyId, x.BusinessDate });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_StayServiceSessions_OneParent", "num_nonnulls(\"StayBookingId\", \"StayServiceOrderId\") = 1");
                t.HasCheckConstraint("CK_StayServiceSessions_Times", "\"EndUtc\" > \"StartUtc\" AND \"OccupiedUntilUtc\" >= \"EndUtc\"");
                t.HasCheckConstraint("CK_StayServiceSessions_Hours", "\"Hours\" BETWEEN 1 AND 12");
                t.HasCheckConstraint("CK_StayServiceSessions_Released", "(\"ReleasedAtUtc\" IS NULL) = (\"State\" = 0)");
                t.HasCheckConstraint("CK_StayServiceSessions_RequestBasis", "(\"AddedByKind\" IN (2, 3)) = (\"RequestBasis\" IS NOT NULL)");
            });
        });

        builder.Entity<StayServiceOrderEvent>(e =>
        {
            e.HasOne<StayServiceOrder>().WithMany().HasForeignKey(x => x.StayServiceOrderId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.ActorUserId).HasMaxLength(450);
            e.Property(x => x.ActorNameSnapshot).HasMaxLength(200);
            e.Property(x => x.Reason).HasMaxLength(300);
            e.Property(x => x.DetailsJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.StayServiceOrderId, x.OccurredAtUtc });
            e.HasIndex(x => x.OccurredAtUtc);
        });

        builder.Entity<StaysReminderTemplateChange>(e =>
        {
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ChangedByUserId).HasMaxLength(450);
            e.Property(x => x.ChangedByNameSnapshot).HasMaxLength(200);
            e.Property(x => x.PreviousTemplate).HasMaxLength(700);
            e.Property(x => x.NewTemplate).HasMaxLength(700);
            e.Property(x => x.OwnerNoticeVersion).HasMaxLength(80);
            e.Property(x => x.PushNoticeVersion).HasMaxLength(80);
            e.Property(x => x.CodeMarkersHit).HasMaxLength(200);
            e.HasIndex(x => new { x.CompanyId, x.ChangedAtUtc });
        });

        builder.Entity<StaysSubscription>(e =>
        {
            e.HasOne(s => s.BillingAccount).WithMany().HasForeignKey(s => s.BillingAccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.PlanConfig).WithMany().HasForeignKey(s => s.PlanConfigId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(s => s.BillingAccountId).IsUnique();
        });

        builder.Entity<OrderMonthlyUsage>(e =>
        {
            e.HasKey(u => new { u.BillingAccountId, u.Month });
            e.HasOne<BillingAccount>().WithMany().HasForeignKey(u => u.BillingAccountId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ProductCategory>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(100);
            e.HasOne<Company>().WithMany().HasForeignKey(c => c.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => new { c.CompanyId, c.Position });
        });

        builder.Entity<Product>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(200);
            e.Property(p => p.Description).HasMaxLength(2000);
            e.Property(p => p.ImageUrl).HasMaxLength(300);
            e.Property(p => p.ThumbnailUrl).HasMaxLength(300);
            e.Property(p => p.Price).HasColumnType("decimal(10,2)");
            e.Property(p => p.PortionText).HasMaxLength(50);
            e.Property(p => p.CompositionAndAllergens).HasMaxLength(2000);
            e.HasOne<Company>().WithMany().HasForeignKey(p => p.CompanyId).OnDelete(DeleteBehavior.Restrict);
            // Restrict is a safety net only: CatalogController nulls CategoryId of soft-deleted products
            // before it deletes an (empty) category.
            e.HasOne<ProductCategory>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => new { p.CompanyId, p.CategoryId, p.Position });
            e.HasIndex(p => p.CompanyId).HasDatabaseName("IX_Products_CompanyId_Live").HasFilter("\"DeletedAtUtc\" IS NULL");
            // Cycle 24 (§448.1): the weekday mask, bit 0 = Monday … bit 6 = Sunday; 127 = every day.
            // Sentinel -1: mask 0 ("only by daily menu") is a real value and must not be mistaken for "unset".
            e.Property(p => p.AvailableWeekdaysMask).HasDefaultValue(127).HasSentinel(-1);
            e.ToTable(t => t.HasCheckConstraint("CK_Products_AvailableWeekdaysMask_Range", "\"AvailableWeekdaysMask\" BETWEEN 0 AND 127"));
            // §388.4-4: stock is never negative. NULL = not tracked.
            e.ToTable(t => t.HasCheckConstraint("CK_Products_StockOnHand_NonNegative", "\"StockOnHand\" IS NULL OR \"StockOnHand\" >= 0"));
        });

        builder.Entity<Order>(e =>
        {
            e.Property(o => o.PublicToken).HasMaxLength(64);
            e.Property(o => o.CustomerName).HasMaxLength(100);
            e.Property(o => o.CustomerPhone).HasMaxLength(20);
            e.Property(o => o.Comment).HasMaxLength(500);
            e.Property(o => o.StatusReason).HasMaxLength(300);
            e.Property(o => o.EstimatedTotal).HasColumnType("decimal(10,2)");
            e.Property(o => o.FinalTotal).HasColumnType("decimal(10,2)");
            e.Property(o => o.ConsentPrivacyVersion).HasMaxLength(64);
            e.Property(o => o.ConsentTermsVersion).HasMaxLength(64);
            e.Property(o => o.CheckoutNoticeVersion).HasMaxLength(32);
            // §396.2: the concurrency token. Incremented by hand on every change (int, not rowversion).
            e.Property(o => o.Version).IsConcurrencyToken();
            e.HasOne<Company>().WithMany().HasForeignKey(o => o.CompanyId).OnDelete(DeleteBehavior.Restrict);
            // Deleting the customer's account does not delete the shop's books — the link is just cleared.
            e.HasOne<AppUser>().WithMany().HasForeignKey(o => o.CustomerUserId).OnDelete(DeleteBehavior.SetNull);
            // Cycle 24 (§448.1, §451.4): the number is unique within the PICKUP day. Creation day (BusinessDate) keeps a plain index for "created per day" reports.
            e.Property(o => o.PickupKind).HasDefaultValue(PickupKind.Asap);
            e.Property(o => o.MessengerConsentVersion).HasMaxLength(32);
            e.HasIndex(o => new { o.CompanyId, o.PickupDate, o.Number }).IsUnique();
            e.HasIndex(o => new { o.CompanyId, o.PickupDate, o.PickupStartUtc });
            e.HasIndex(o => new { o.CompanyId, o.BusinessDate });
            e.HasIndex(o => o.PublicToken).IsUnique();
            e.HasIndex(o => new { o.CompanyId, o.IdempotencyKey }).IsUnique();
            e.HasIndex(o => new { o.CompanyId, o.Status });
            e.HasIndex(o => new { o.CompanyId, o.CompletedAtUtc });
            e.HasIndex(o => new { o.CustomerUserId, o.CreatedAtUtc });
            // Phone throttle and the subject export (§395.2 step 6, §398.1).
            e.HasIndex(o => new { o.CustomerPhone, o.CreatedAtUtc }).HasFilter("\"CustomerPhone\" IS NOT NULL");
            // Cycle 25 (§497.1): the customer card and the notes retention rule.
            e.HasIndex(o => new { o.CompanyId, o.CustomerPhone })
                .HasDatabaseName("IX_Orders_CompanyId_CustomerPhone")
                .HasFilter("\"CustomerPhone\" IS NOT NULL");
            // Cycle 25 (§497.1): index-only totals for history and summary.
            e.HasIndex(o => new { o.CompanyId, o.PickupDate })
                .HasDatabaseName("IX_Orders_Report")
                .IncludeProperties(o => new { o.Status, o.EstimatedTotal, o.FinalTotal });
        });

        builder.Entity<OrderItem>(e =>
        {
            e.Property(i => i.NameSnapshot).HasMaxLength(200);
            e.Property(i => i.PortionTextSnapshot).HasMaxLength(50);
            e.Property(i => i.UnitPrice).HasColumnType("decimal(10,2)");
            e.Property(i => i.LineTotalEstimated).HasColumnType("decimal(10,2)");
            e.Property(i => i.LineTotalFinal).HasColumnType("decimal(10,2)");
            e.HasOne(i => i.Order).WithMany(o => o.Items).HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(i => i.OrderId);
            e.HasIndex(i => i.ProductId);
        });

        builder.Entity<OrderEvent>(e =>
        {
            e.Property(ev => ev.ActorNameSnapshot).HasMaxLength(200);
            e.Property(ev => ev.Reason).HasMaxLength(300);
            e.Property(ev => ev.Comment).HasMaxLength(500);
            e.Property(ev => ev.ChangesJson).HasColumnType("jsonb");
            e.Property(ev => ev.TotalBefore).HasColumnType("decimal(10,2)");
            e.Property(ev => ev.TotalAfter).HasColumnType("decimal(10,2)");
            e.HasOne(ev => ev.Order).WithMany(o => o.Events).HasForeignKey(ev => ev.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(ev => new { ev.OrderId, ev.OccurredAtUtc });
            e.HasIndex(ev => ev.OccurredAtUtc);
        });

        builder.Entity<OrderDailyCounter>(e =>
        {
            e.HasKey(c => new { c.CompanyId, c.PickupDate });
            e.HasOne<Company>().WithMany().HasForeignKey(c => c.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<CompanyMember>(e =>
        {
            e.HasOne(cm => cm.Company).WithMany(c => c.Members).HasForeignKey(cm => cm.CompanyId);
            e.HasOne(cm => cm.User).WithMany(u => u.CompanyMemberships).HasForeignKey(cm => cm.UserId);
            e.Property(cm => cm.CommissionPercent).HasColumnType("decimal(18,2)");
            // Makes "one membership row per company+user" a hard DB guarantee, not just the application-
            // level check-then-act AnyAsync in CompaniesController.AddMember — same class of bug as
            // WorkingHours (audit B3): without it, a race lets two concurrent requests both pass the
            // "not already a member" check and both insert, and every reader that assumes at most one
            // row per (CompanyId, UserId) — e.g. ReportsController's per-master commission lookup —
            // breaks permanently on the resulting duplicate.
            e.HasIndex(cm => new { cm.CompanyId, cm.UserId }).IsUnique();
        });

        builder.Entity<MasterService>(e =>
        {
            e.HasOne(ms => ms.Master).WithMany(u => u.MasterServices).HasForeignKey(ms => ms.MasterId);
            e.HasOne(ms => ms.Service).WithMany(s => s.MasterServices).HasForeignKey(ms => ms.ServiceId);
        });

        builder.Entity<WorkingHours>(e =>
        {
            e.HasOne(wh => wh.Master).WithMany(u => u.WorkingHours).HasForeignKey(wh => wh.MasterId);
            e.HasOne(wh => wh.Company).WithMany().HasForeignKey(wh => wh.CompanyId);
            // Makes "one row per master+company+date" a hard DB guarantee, not just an application-level
            // find-or-create — without it, ScheduleTemplateController.Apply's
            // existing.ToDictionary(wh => wh.Date) throws ArgumentException on any duplicate (audit B3).
            e.HasIndex(wh => new { wh.MasterId, wh.CompanyId, wh.Date }).IsUnique();
        });

        builder.Entity<AccountSubscription>(e =>
        {
            e.HasOne(s => s.Owner).WithMany().HasForeignKey(s => s.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.OwnerUserId).IsUnique();
            e.HasOne(s => s.PlanConfig).WithMany().HasForeignKey(s => s.PlanConfigId).OnDelete(DeleteBehavior.SetNull);
            // Cycle 7 (ARCHITECTURE_CYCLE7.md §43.4): one subscription row per account. Cascade
            // mirrors the Owner FK above — deleting the account takes its subscription row with it.
            // Stage 6 (§43.6, B5-13): NOT NULL at the database level.
            e.Property(s => s.BillingAccountId).IsRequired();
            e.HasOne(s => s.BillingAccount).WithMany().HasForeignKey(s => s.BillingAccountId)
                .IsRequired().OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.BillingAccountId).IsUnique();
        });

        builder.Entity<SubscriptionChangeLog>(e =>
        {
            e.HasIndex(l => l.OwnerUserId);
            // Cycle 5 (§43.4, §51.3) — the account and (for CompanyTransferred rows) company axes of
            // this log. Restrict: a billing account/company that still has history behind it can't be
            // deleted out from under that history.
            e.HasOne(l => l.BillingAccount).WithMany().HasForeignKey(l => l.BillingAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(l => l.BillingAccountId);
            e.HasOne(l => l.Company).WithMany().HasForeignKey(l => l.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(l => l.CompanyId);
            e.Property(l => l.OldOptionsSummary).HasMaxLength(500);
            e.Property(l => l.NewOptionsSummary).HasMaxLength(500);
            // Cycle 20 (ARCHITECTURE_CYCLE20.md §403.1, US-20-02).
            e.Property(l => l.ReasonDetails).HasMaxLength(1000);
        });

        // Cycle 7 (ARCHITECTURE_CYCLE7.md §43.3): payer/rules-owner of companies and subscriptions —
        // see Company.BillingAccountId / AccountSubscription.BillingAccountId remarks.
        builder.Entity<BillingAccount>(e =>
        {
            e.HasOne(a => a.Owner).WithMany().HasForeignKey(a => a.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.OwnerUserId).IsUnique();
            e.Property(a => a.Name).HasMaxLength(100);
            // Cycle 28 (§572.1).
            e.HasIndex(a => a.Id).HasDatabaseName("IX_BillingAccounts_IsShowcase").HasFilter("\"IsShowcase\"");
            // Cycle 5, stage 5 (§49) — the owner's single pending plan/options request; see the
            // entity's own remarks for why this isn't a separate SubscriptionRequest table.
            e.HasOne(a => a.RequestedPlan).WithMany().HasForeignKey(a => a.RequestedPlanId).OnDelete(DeleteBehavior.SetNull);
            e.Property(a => a.RequestedComment).HasMaxLength(500);
            // Cycle 18 (§332.3).
            e.Property(a => a.TrialWarningThresholdsDays).HasMaxLength(32);
            e.Property(a => a.TrialTermsVersion).HasMaxLength(32);
            // §337.1 — the only query the trial-lifecycle background task runs against BillingAccounts.
            e.HasIndex(a => a.TrialEndsAtUtc).HasDatabaseName("IX_BillingAccounts_TrialExpiry")
                .HasFilter("\"TrialEndsAtUtc\" IS NOT NULL AND \"TrialExpiredHandledAtUtc\" IS NULL");
            // Cycle 20 (ARCHITECTURE_CYCLE20.md §402.5, Т20-04 п. 3) — operator details for the written
            // health-consent form. All three optional by customer decision (the form prints fine blank).
            e.Property(a => a.ConsentOperatorFullName).HasMaxLength(300);
            e.Property(a => a.ConsentOperatorAddress).HasMaxLength(500);
            e.Property(a => a.ConsentOperatorInn).HasMaxLength(12);
        });

        // Cycle 5, stage 5 (§43.3, US-66) — per-plan option availability matrix.
        builder.Entity<PlanOptionRule>(e =>
        {
            e.HasOne(r => r.PlanConfig).WithMany().HasForeignKey(r => r.PlanConfigId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Option).WithMany().HasForeignKey(r => r.OptionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(r => new { r.PlanConfigId, r.OptionId }).IsUnique();
        });

        // Cycle 7 (ARCHITECTURE_CYCLE7.md §43.3): US-64 p.4 — who manages a company changed, and when.
        builder.Entity<CompanyOwnerChangeLog>(e =>
        {
            e.HasOne(l => l.Company).WithMany().HasForeignKey(l => l.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => l.CompanyId);
            e.Property(l => l.Comment).HasMaxLength(500);
        });

        builder.Entity<Booking>(e =>
        {
            e.Property(b => b.Price).HasColumnType("decimal(10,2)");
            e.Property(b => b.CommissionPercent).HasColumnType("decimal(18,2)");
            // ARCHITECTURE.md §5.2: a legal document version snapshot, same 64-char cap the manifest
            // loader enforces on the source (LegalDocumentProvider.LoadDocument) — was left as
            // unbounded `text` before this fix (code review finding).
            e.Property(b => b.ConsentPrivacyVersion).HasMaxLength(64);
            e.Property(b => b.ConsentTermsVersion).HasMaxLength(64);
            e.Property(b => b.GuardianConfirmationVersion).HasMaxLength(64);
            e.Property(b => b.BookingNoticeVersion).HasMaxLength(64);
            e.HasOne(b => b.Company).WithMany(c => c.Bookings).HasForeignKey(b => b.CompanyId);
            e.HasOne(b => b.Service).WithMany(s => s.Bookings).HasForeignKey(b => b.ServiceId);
            e.HasOne(b => b.Master).WithMany(u => u.MasterBookings).HasForeignKey(b => b.MasterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(b => b.Client).WithMany(u => u.ClientBookings).HasForeignKey(b => b.ClientId).OnDelete(DeleteBehavior.SetNull);
            // ARCHITECTURE_CYCLE14.md §142.5 (Q16): the change-phone gate (US-14-17) asks "does this
            // number have any guest booking" on every phone change, against the fastest-growing table in
            // the product. Partial — only guest rows carry a value here, a registered client's Booking
            // row always has GuestPhone null — so the index stays a fraction of the table's size.
            e.HasIndex(b => b.GuestPhone).HasDatabaseName("IX_Bookings_GuestPhone").HasFilter("\"GuestPhone\" IS NOT NULL");
            // ARCHITECTURE_CYCLE22.md §379 (F5): the most frequent booking read is "this master on this
            // date / date range" (slots, the master's schedule, conflict checks). MasterId leads, so the
            // same index also serves the Master FK — EF's ForeignKeyIndexConvention drops the separate
            // IX_Bookings_MasterId once an index with MasterId as its prefix exists. No IncludeProperties
            // (§379: extra size for little gain).
            e.HasIndex(b => new { b.MasterId, b.Date });
            // Cycle 28 (ARCHITECTURE_CYCLE28.md §572.1, §577.4): the retention rule for visitor bookings of showcase companies
            // scans exactly this shape; partial, so on production the index is empty.
            e.HasIndex(b => b.CreatedAt).HasDatabaseName("IX_Bookings_ShowcaseVisitor")
                .HasFilter("\"ShowcaseKind\" = 2");
        });

        // Cycle 28 (§572.1): users of the showcase. AppUser has no other Fluent configuration (Identity owns it).
        builder.Entity<AppUser>(e =>
        {
            e.HasIndex(u => u.Id).HasDatabaseName("IX_AspNetUsers_IsShowcase").HasFilter("\"IsShowcase\"");
        });

        builder.Entity<BookingService>(e =>
        {
            e.Property(bs => bs.NameSnapshot).HasMaxLength(200);
            e.Property(bs => bs.Price).HasColumnType("decimal(10,2)");
            e.HasIndex(bs => new { bs.BookingId, bs.Position }).IsUnique();
            e.HasIndex(bs => bs.ServiceId);
            e.HasOne(bs => bs.Booking).WithMany(b => b.BookingServices).HasForeignKey(bs => bs.BookingId).OnDelete(DeleteBehavior.Cascade);
            // Restrict, not Cascade: a service that has ever been part of a visit can't be hard-deleted
            // out from under the historical record — the product already only soft-deletes services
            // (Service.IsActive) for exactly this reason.
            e.HasOne(bs => bs.Service).WithMany().HasForeignKey(bs => bs.ServiceId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Review>(e =>
        {
            e.HasIndex(r => r.BookingId).IsUnique();
            e.HasOne(r => r.Booking).WithMany().HasForeignKey(r => r.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Company).WithMany().HasForeignKey(r => r.CompanyId);
            e.HasOne(r => r.Master).WithMany().HasForeignKey(r => r.MasterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Client).WithMany().HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<WeeklyScheduleTemplate>(e =>
        {
            e.HasIndex(t => new { t.MasterId, t.CompanyId });
            e.HasOne(t => t.Master).WithMany().HasForeignKey(t => t.MasterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Company).WithMany().HasForeignKey(t => t.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ClientNote>(e =>
        {
            e.HasIndex(n => new { n.CompanyId, n.ClientId });
            e.HasIndex(n => new { n.CompanyId, n.GuestPhone });
            e.HasOne(n => n.Company).WithMany().HasForeignKey(n => n.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(n => n.Master).WithMany().HasForeignKey(n => n.MasterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(n => n.Client).WithMany().HasForeignKey(n => n.ClientId).OnDelete(DeleteBehavior.SetNull);
            // SetNull rather than Cascade: deleting a booking (the product never does this today — see
            // Booking's own comment) must not delete the note and its photos with it, since the work was
            // still performed (US-20 p.5, ARCHITECTURE.md §5.1).
            e.HasOne(n => n.Booking).WithMany().HasForeignKey(n => n.BookingId).OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ClientNotePhoto>(e =>
        {
            e.HasOne(p => p.ClientNote).WithMany(n => n.Photos)
                .HasForeignKey(p => p.ClientNoteId).OnDelete(DeleteBehavior.Cascade);
            // Deleting the uploader must not delete company data: notes and photos belong to the
            // company, not to the employee (same rule as ClientNote.MasterId's comment and RemoveMember,
            // US-20 p.4). This is also what makes the phone-normalisation migration's account merges
            // safe (ARCHITECTURE.md §14.2) — a merged-away account's uploads simply lose an attribution,
            // not the photo itself.
            e.HasOne(p => p.UploadedBy).WithMany()
                .HasForeignKey(p => p.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(p => p.ClientNoteId);
            e.HasIndex(p => new { p.CompanyId, p.CreatedAt }); // quota sum AND retention scan (§6.1, §7.2)
            e.HasIndex(p => new { p.ClientNoteId, p.ContentHash }).IsUnique(); // idempotent re-upload, §6.3
        });

        builder.Entity<BookingEvent>(e =>
        {
            e.Property(be => be.ActorNameSnapshot).HasMaxLength(200);
            e.Property(be => be.CancellationReason).HasMaxLength(300);
            e.HasOne(be => be.Booking).WithMany(b => b.Events).HasForeignKey(be => be.BookingId).OnDelete(DeleteBehavior.Cascade);
            // Deleting the actor's account must not delete the journal row — the event still happened;
            // only the identifier link is cleared, exactly like ClientNotePhoto.UploadedByUserId.
            e.HasOne(be => be.ActorUser).WithMany().HasForeignKey(be => be.ActorUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(be => new { be.BookingId, be.OccurredAtUtc }); // the one read query, §105
            e.HasIndex(be => new { be.CompanyId, be.OccurredAtUtc }); // retention scan, §107
        });

        builder.Entity<CompanyPhoto>(e =>
        {
            e.Property(p => p.Url).HasMaxLength(300);
            e.Property(p => p.ThumbnailUrl).HasMaxLength(300);
            e.Property(p => p.ContentType).HasMaxLength(100);
            e.Property(p => p.ContentHash).HasMaxLength(64);

            e.HasOne(p => p.Company).WithMany(c => c.Photos)
                .HasForeignKey(p => p.CompanyId).OnDelete(DeleteBehavior.Cascade);
            // Deleting the uploader must not delete the showcase photo — same convention as
            // ClientNotePhoto.UploadedByUserId above: the photo belongs to the company, not the employee.
            e.HasOne(p => p.UploadedBy).WithMany()
                .HasForeignKey(p => p.UploadedByUserId).OnDelete(DeleteBehavior.SetNull);

            // ARCHITECTURE_CYCLE10.md §102.2: deliberately NOT unique. Reordering updates Position on
            // several rows one UPDATE at a time (EF Core), and a non-DEFERRABLE Postgres unique index
            // would fail on the transient state where two rows briefly share a position. Integrity is
            // instead guaranteed by the server always renumbering ALL of a company's photos to 0..n-1
            // inside one transaction under the advisory lock "company-photos:{companyId}"
            // (CompanyPhotoOrdering) — this index exists only to make "list this company's photos in
            // order" and "count this company's photos" cheap, not to enforce uniqueness.
            e.HasIndex(p => new { p.CompanyId, p.Position });
            // Dedup-by-hash, same convention as ClientNotePhoto's (CompanyId, ContentHash) analogue
            // above — a double-click/retry re-upload of the same file returns the existing row instead
            // of creating an 11th one and silently burning a slot in the 10-photo limit.
            e.HasIndex(p => new { p.CompanyId, p.ContentHash }).IsUnique();
        });

        builder.Entity<ScheduledTaskState>(e =>
        {
            e.HasKey(s => s.Name);
            e.Property(s => s.Name).HasMaxLength(100);
        });

        builder.Entity<ConsentRecord>(e =>
        {
            e.Property(c => c.SubjectPhone).HasMaxLength(20);
            e.Property(c => c.DocumentKey).HasMaxLength(64);
            e.Property(c => c.DocumentVersion).HasMaxLength(64);
            e.Property(c => c.DocumentHash).HasMaxLength(64);
            e.Property(c => c.IpAddress).HasMaxLength(45);
            e.Property(c => c.UserAgent).HasMaxLength(256);
            e.Property(c => c.RevokeReason).HasMaxLength(256);
            // Cycle 20 (ARCHITECTURE_CYCLE20.md §402.2) — the paper-consent form number ("HD-XXXXXXXX")
            // and who lifted a mark. No FK on RevokedByUserId (same §44.2 p.5 reasoning as every other
            // "who acted" column here).
            e.Property(c => c.FormId).HasMaxLength(16);
            e.Property(c => c.RevokedByUserId).HasMaxLength(450);

            // NO ACTION on all three FKs, deliberately not Cascade (ARCHITECTURE_CYCLE5.md §44.2 p.5):
            // this table is evidence — a user row being physically removed (it never is, §7.4's
            // tombstone, but the schema must not rely on that) must never take the proof of what they
            // consented to down with it.
            e.HasOne(c => c.User).WithMany()
                .HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(c => c.Company).WithMany()
                .HasForeignKey(c => c.CompanyId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(c => c.RecordedByUser).WithMany()
                .HasForeignKey(c => c.RecordedByUserId).OnDelete(DeleteBehavior.NoAction);

            // §44.2 p.4 — deliberately no unique index anywhere on this table (that IS US-66 p.1;
            // double-click protection moves to ConsentLedger.GrantAsync's idempotency window, §45.4).
            // "Current, non-revoked row for this user+key+purpose" — ConsentLedger's ForUser reads.
            e.HasIndex(c => new { c.UserId, c.DocumentKey, c.Purpose, c.GrantedAtUtc })
                .HasDatabaseName("IX_ConsentRecords_CurrentByUser")
                .IsDescending(false, false, false, true)
                .HasFilter("\"RevokedAtUtc\" IS NULL AND \"UserId\" IS NOT NULL");

            // "Current, non-revoked row for this phone+company+key" — ConsentLedger's ForPhoneInCompany
            // reads (salon-facing consents: photo, health — a subject without an account included).
            e.HasIndex(c => new { c.SubjectPhone, c.CompanyId, c.DocumentKey, c.GrantedAtUtc })
                .HasDatabaseName("IX_ConsentRecords_CurrentBySubject")
                .IsDescending(false, false, false, true)
                .HasFilter("\"RevokedAtUtc\" IS NULL AND \"SubjectPhone\" IS NOT NULL");

            // Retention sweep's scan order (ARCHITECTURE_CYCLE5.md §49.1) — not built by this task, but
            // the index belongs with the table it serves.
            e.HasIndex(c => c.GrantedAtUtc).HasDatabaseName("IX_ConsentRecords_Retention");
        });

        builder.Entity<SubjectRequest>(e =>
        {
            e.Property(r => r.Reference).HasMaxLength(16);
            e.HasIndex(r => r.Reference).IsUnique();
            e.Property(r => r.SubjectPhone).HasMaxLength(20);
            e.Property(r => r.ContactValue).HasMaxLength(200);
            e.Property(r => r.Message).HasMaxLength(4000);
            e.Property(r => r.Resolution).HasMaxLength(2000);
            e.HasIndex(r => r.SubjectPhone);
            e.HasIndex(r => new { r.Status, r.DueAtUtc });
            // Cycle 20 (ARCHITECTURE_CYCLE20.md §410, US-20-09) — manual registration by the superadmin.
            e.Property(r => r.RegisteredByUserId).HasMaxLength(450);
        });

        builder.Entity<ClientHealthNote>(e =>
        {
            e.Property(n => n.GuestPhone).HasMaxLength(20);
            e.Property(n => n.Ciphertext).HasColumnType("text");
            e.Property(n => n.KeyId).HasMaxLength(16);
            e.HasOne(n => n.Company).WithMany().HasForeignKey(n => n.CompanyId).OnDelete(DeleteBehavior.Cascade);
            // NO ACTION, not SetNull/Cascade — same reasoning as ConsentRecord (§44.2 p.5): the row is
            // this company's own record, not personal convenience data that should vanish quietly if the
            // client's account is (never physically, but the schema must not rely on that) removed.
            e.HasOne(n => n.Client).WithMany()
                .HasForeignKey(n => n.ClientId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(n => n.UpdatedByUser).WithMany()
                .HasForeignKey(n => n.UpdatedByUserId).OnDelete(DeleteBehavior.NoAction);
            // §44.3: "one health note per client per company" — a hard DB guarantee, not an application-
            // level find-or-create, mirroring ClientNotePhoto's idempotency-by-index convention.
            e.HasIndex(n => new { n.CompanyId, n.ClientId }).IsUnique().HasFilter("\"ClientId\" IS NOT NULL");
            e.HasIndex(n => new { n.CompanyId, n.GuestPhone }).IsUnique().HasFilter("\"GuestPhone\" IS NOT NULL");
        });

        builder.Entity<MailLog>(e => {
            e.HasOne(m => m.Company).WithMany().HasForeignKey(m => m.CompanyId);
            e.HasOne(m => m.SentBy).WithMany().HasForeignKey(m => m.SentById).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Cycle 4: WhatsApp notifications (ARCHITECTURE_CYCLE4.md §23) ───────────────────────────

        builder.Entity<City>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.Region).HasMaxLength(200);
            e.Property(c => c.TimeZoneId).HasMaxLength(64);
            e.Property(c => c.SearchName).HasMaxLength(200);
            e.HasIndex(c => c.SearchName);
            e.HasIndex(c => new { c.Region, c.Name });
            // ARCHITECTURE_CYCLE9.md §103.3 (US-113): added by ExpandCityDirectory alongside a
            // one-time DELETE of any pre-existing (Name, Region) duplicates — "no duplicates" becomes a
            // schema property from this migration forward, not something callers have to re-check.
            e.HasIndex(c => new { c.Name, c.Region }).IsUnique();
        });

        builder.Entity<NotificationChannel>(e =>
        {
            e.HasOne(c => c.Owner).WithMany().HasForeignKey(c => c.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => c.OwnerUserId);
            // Cycle 7, stage 3 (ARCHITECTURE_CYCLE7.md §43.4, §47): who PAYS for/owns this number —
            // OwnerUserId stays "who set it up and manages it". Restrict (like Company.BillingAccountId)
            // — an account can't be deleted out from under a number it still owns. Stage 6 (§43.6,
            // B5-13): NOT NULL at the database level plus an alternate key on (Id, BillingAccountId)
            // that ChannelCompanyAssignment's composite FK pins to.
            e.Property(c => c.BillingAccountId).IsRequired();
            e.HasOne(c => c.BillingAccount).WithMany().HasForeignKey(c => c.BillingAccountId)
                .IsRequired().OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => c.BillingAccountId);
            // ARCHITECTURE_CYCLE9.md §104.3: widened from (Id, BillingAccountId) to include Transport —
            // ChannelCompanyAssignment's composite FK now pins to THIS key, which is what makes "a
            // company assigned to a channel whose Transport doesn't match the assignment's own
            // (denormalized) Transport" physically impossible, the same trick cycle 7 used for
            // BillingAccountId itself.
            e.HasAlternateKey(c => new { c.Id, c.BillingAccountId, c.Transport });
            e.Property(c => c.Inn).HasMaxLength(12); // T5-B4: 10 (Company) or 12 (Ip/SelfEmployed) digits
            // Filtered unique index: a channel with no instance yet has ProviderInstanceId == null, and
            // there is exactly one live column value we must never see twice.
            e.HasIndex(c => c.ProviderInstanceId).IsUnique().HasFilter("\"ProviderInstanceId\" IS NOT NULL");
            e.HasIndex(c => c.State);
            e.HasOne(c => c.ReplacedByChannel).WithMany()
                .HasForeignKey(c => c.ReplacedByChannelId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ChannelCompanyAssignment>(e =>
        {
            // Cycle 7, stage 6 (ARCHITECTURE_CYCLE7.md §43.6) — the Company-side FK is still composite,
            // pinned to the (Id, BillingAccountId) alternate key on Companies. This is (half of) the
            // co-tenancy guarantee: a row can only exist while the channel and the company it's
            // assigned to agree on BillingAccountId, so "assign a company to a number belonging to a
            // different account" is impossible at the database level, and CompanyTransferService is
            // forced to delete the assignment strictly before it can change Company.BillingAccountId
            // (§51.3 step 6/7) — the DB rejects the update otherwise.
            e.HasOne(a => a.Company).WithMany()
                .HasForeignKey(a => new { a.CompanyId, a.BillingAccountId })
                .HasPrincipalKey(c => new { c.Id, c.BillingAccountId })
                .OnDelete(DeleteBehavior.Cascade);
            // ARCHITECTURE_CYCLE9.md §104.3 — the Channel-side FK grows a THIRD column, Transport,
            // pinned to NotificationChannels' widened (Id, BillingAccountId, Transport) alternate key.
            // Combined with NotificationChannel.Transport never changing after creation, this makes the
            // assignment's own (denormalized) Transport column physically incapable of disagreeing with
            // the channel it points at — the co-tenancy trick, applied to a second column.
            e.HasOne(a => a.Channel).WithMany(c => c.Assignments)
                .HasForeignKey(a => new { a.ChannelId, a.BillingAccountId, a.Transport })
                .HasPrincipalKey(c => new { c.Id, c.BillingAccountId, c.Transport })
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.ChannelId);
            // ARCHITECTURE_CYCLE9.md §104.3 (US-119, US-61 p.7 widened): a company may be assigned to at
            // most one channel PER TRANSPORT — a hard DB guarantee, not application-level check-then-act.
            e.HasIndex(a => new { a.CompanyId, a.Transport }).IsUnique();
        });

        builder.Entity<ChannelStateEvent>(e =>
        {
            e.HasOne(ev => ev.Channel).WithMany().HasForeignKey(ev => ev.ChannelId).OnDelete(DeleteBehavior.Cascade);
            e.Property(ev => ev.Detail).HasMaxLength(500);
            e.HasIndex(ev => new { ev.ChannelId, ev.OccurredAtUtc });
        });

        builder.Entity<ChannelPaymentLog>(e =>
        {
            e.HasOne(l => l.Channel).WithMany().HasForeignKey(l => l.ChannelId).OnDelete(DeleteBehavior.Cascade);
            e.Property(l => l.Amount).HasColumnType("decimal(10,2)");
            e.HasIndex(l => l.ChannelId);
        });

        builder.Entity<OutboundNotification>(e =>
        {
            e.HasOne(n => n.Company).WithMany().HasForeignKey(n => n.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(n => n.Channel).WithMany().HasForeignKey(n => n.ChannelId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(n => n.Booking).WithMany().HasForeignKey(n => n.BookingId).OnDelete(DeleteBehavior.SetNull);
            // Cycle 24 (§448.1): a message about an order — OrderId instead of BookingId.
            e.HasOne(n => n.Order).WithMany().HasForeignKey(n => n.OrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(n => n.OrderId);
            // Cycle 37 (§37.2.1): a message about a house booking; at most one subject per row.
            e.HasOne(n => n.StayBooking).WithMany().HasForeignKey(n => n.StayBookingId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(n => n.StayBookingId);
            // Cycle 39 (§39.2.2): a message about a stand-alone order of a service.
            e.HasOne(n => n.StayServiceOrder).WithMany().HasForeignKey(n => n.StayServiceOrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(n => n.StayServiceOrderId).HasFilter("\"StayServiceOrderId\" IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_OutboundNotifications_OneSubject",
                "num_nonnulls(\"BookingId\", \"OrderId\", \"StayBookingId\", \"StayServiceOrderId\") <= 1"));
            e.Property(n => n.RecipientPhone).HasMaxLength(20);
            e.Property(n => n.Body).HasMaxLength(2000);
            e.Property(n => n.ReasonDetail).HasMaxLength(300);
            e.Property(n => n.ProviderMessageId).HasMaxLength(100);
            e.Property(n => n.IdempotencyKey).HasMaxLength(200);
            e.HasIndex(n => n.IdempotencyKey).IsUnique();

            // §23.4 index 1 — the dispatcher's ONLY read path. Partial on Status = Pending (the enum's
            // int value, NOT its name — see NotificationStatus's doc comment for why Pending must stay
            // 0 and why this filter is written as a raw literal rather than translated from the enum).
            e.HasIndex(n => new { n.VisitStartUtc, n.CreatedAt })
                .HasDatabaseName("IX_OutboundNotifications_Dispatch")
                .HasFilter("\"Status\" = 0")
                .IncludeProperties(n => new { n.DueAtUtc, n.ChannelId, n.CompanyId });

            // §23.4 index 2 — delivery log (US-32) and the 30-day summary.
            e.HasIndex(n => new { n.CompanyId, n.CreatedAt })
                .HasDatabaseName("IX_OutboundNotifications_Company_CreatedAt");

            // §23.4 index 3 — webhook lookup. Deliberately NOT unique (a 500 on a rare id collision
            // between provider instances is worse than a theoretical extra row).
            e.HasIndex(n => n.ProviderMessageId)
                .HasDatabaseName("IX_OutboundNotifications_ProviderMessageId")
                .HasFilter("\"ProviderMessageId\" IS NOT NULL");

            // §23.4 index 4 — the three bulk "cancel/reassign everything on this channel" operations.
            e.HasIndex(n => new { n.ChannelId, n.Status })
                .HasDatabaseName("IX_OutboundNotifications_Channel_Status");
        });

        builder.Entity<CompanyNotificationSettings>(e =>
        {
            e.HasKey(s => s.CompanyId);
            e.HasOne(s => s.Company).WithMany().HasForeignKey(s => s.CompanyId).OnDelete(DeleteBehavior.Cascade);
            // ARCHITECTURE_CYCLE9.md §105.4/§105.7: DB-level default TRUE — without this, EF's generated
            // migration would default the new column to the CLR type's own default (false), which would
            // silently flip push OFF for every row that already exists (every company that saved
            // notification settings before this cycle) the instant the migration runs. "По умолчанию
            // true, включая компании, созданные до цикла" is a schema property, not something a
            // backfill script fixes after the fact.
            e.Property(s => s.StaffPushEnabled).HasDefaultValue(true);
        });

        builder.Entity<NotificationTemplate>(e =>
        {
            e.HasOne(t => t.Company).WithMany().HasForeignKey(t => t.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.Property(t => t.Body).HasMaxLength(1000);
            e.HasIndex(t => new { t.CompanyId, t.Type }).IsUnique();
        });

        // ARCHITECTURE_CYCLE9.md §105.4 (US-123). Cascade on the owner: an account tombstone
        // (ProfileController.DeleteAccount) removes the AppUser row's own FK-reachable rows, but §105.5
        // notes the cascade does NOT fire on account deletion in THIS product (deletion leaves a
        // tombstone, AppUser.Id is never actually removed) — ProfileController therefore also deletes
        // these rows explicitly; Cascade here is only the safety net for any OTHER path that really does
        // remove an AppUser row (e.g. a future hard-delete admin tool).
        builder.Entity<PushSubscription>(e =>
        {
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.UserId);
            // Cycle 24 (§454): the site the device subscribed from; the per-user ceiling and lists work inside (UserId, Site).
            e.Property(s => s.Site).HasDefaultValue(CompanyKind.Services);
            e.HasIndex(s => new { s.UserId, s.Site });
            e.Property(s => s.Endpoint).HasMaxLength(500);
            e.HasIndex(s => s.Endpoint).IsUnique();
            e.Property(s => s.DeviceLabel).HasMaxLength(100);
            e.Property(s => s.KeyId).HasMaxLength(16);
        });

        builder.Entity<StaffPushNotification>(e =>
        {
            e.HasOne(n => n.Company).WithMany().HasForeignKey(n => n.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(n => n.Booking).WithMany().HasForeignKey(n => n.BookingId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(n => n.Order).WithMany().HasForeignKey(n => n.OrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(n => n.OrderId);
            e.HasOne(n => n.StayBooking).WithMany().HasForeignKey(n => n.StayBookingId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(n => n.StayBookingId);
            e.HasOne(n => n.StayServiceOrder).WithMany().HasForeignKey(n => n.StayServiceOrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(n => n.StayServiceOrderId).HasFilter("\"StayServiceOrderId\" IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_StaffPushNotifications_OneSubject",
                "num_nonnulls(\"BookingId\", \"OrderId\", \"StayBookingId\", \"StayServiceOrderId\") <= 1"));
            e.HasOne(n => n.Subscription).WithMany().HasForeignKey(n => n.SubscriptionId).OnDelete(DeleteBehavior.SetNull);
            e.Property(n => n.Payload).HasMaxLength(1000);
            e.Property(n => n.ReasonDetail).HasMaxLength(300);
            e.Property(n => n.IdempotencyKey).HasMaxLength(200);
            e.HasIndex(n => n.IdempotencyKey).IsUnique();

            // §105.4's exact-copy-of-cycle-4 dispatch index: partial on Status = Pending (the enum's int
            // value — see NotificationStatus's doc comment for why this MUST be a raw literal, not a
            // translated enum comparison), ordered by (ExpiresAtUtc, CreatedAt) for early-expiry-first
            // scanning, covering the columns StaffPushDispatchTask reads for every candidate row.
            e.HasIndex(n => new { n.ExpiresAtUtc, n.CreatedAt })
                .HasDatabaseName("IX_StaffPushNotifications_Dispatch")
                .HasFilter("\"Status\" = 0")
                .IncludeProperties(n => new { n.UserId, n.CompanyId, n.SubscriptionId });
        });

        // Cycle 25 (ARCHITECTURE_CYCLE25.md §497.2).
        builder.Entity<StaffMaxLink>(e =>
        {
            e.HasOne(l => l.User).WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(l => l.UserId).IsUnique();
            e.Property(l => l.ChatKey).HasMaxLength(64);
            e.HasIndex(l => l.ChatKey);
            e.Property(l => l.KeyId).HasMaxLength(16);
        });

        builder.Entity<StaffMaxLinkSession>(e =>
        {
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.UserId);
            e.Property(s => s.PayloadHash).HasMaxLength(64);
            e.HasIndex(s => s.PayloadHash).IsUnique();
        });

        builder.Entity<StaffMaxMessage>(e =>
        {
            e.HasOne(m => m.Company).WithMany().HasForeignKey(m => m.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Order).WithMany().HasForeignKey(m => m.OrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(m => m.OrderId);
            e.HasOne(m => m.StayBooking).WithMany().HasForeignKey(m => m.StayBookingId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(m => m.StayBookingId);
            e.HasOne(m => m.StayServiceOrder).WithMany().HasForeignKey(m => m.StayServiceOrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(m => m.StayServiceOrderId).HasFilter("\"StayServiceOrderId\" IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_StaffMaxMessages_OneSubject", "num_nonnulls(\"OrderId\", \"StayBookingId\", \"StayServiceOrderId\") <= 1"));
            e.Property(m => m.ChatKey).HasMaxLength(64);
            e.Property(m => m.Text).HasMaxLength(2000);
            e.Property(m => m.ReasonDetail).HasMaxLength(300);
            e.Property(m => m.IdempotencyKey).HasMaxLength(200);
            e.HasIndex(m => m.IdempotencyKey).IsUnique();
            e.HasIndex(m => new { m.ExpiresAtUtc, m.CreatedAt })
                .HasDatabaseName("IX_StaffMaxMessages_Dispatch")
                .HasFilter("\"Status\" = 0")
                .IncludeProperties(m => new { m.CompanyId, m.ChatKey });
        });

        builder.Entity<ShopCustomerNote>(e =>
        {
            e.HasOne(n => n.Company).WithMany().HasForeignKey(n => n.CompanyId).OnDelete(DeleteBehavior.Restrict);
            e.Property(n => n.Phone).HasMaxLength(20);
            e.Property(n => n.Text).HasMaxLength(1000);
            e.Property(n => n.UpdatedByUserId).HasMaxLength(450);
            e.Property(n => n.UpdatedByName).HasMaxLength(200);
            e.HasIndex(n => new { n.CompanyId, n.Phone }).IsUnique();
        });

        builder.Entity<NotificationTemplateHistory>(e =>
        {
            e.HasIndex(h => new { h.CompanyId, h.Type, h.ChangedAtUtc });
            e.Property(h => h.NewBody).HasMaxLength(1000); // matches NotificationTemplateValidator.MaxLength
            e.Property(h => h.WarningVersion).HasMaxLength(64);
            e.Property(h => h.AdMarkersHit).HasMaxLength(200);
        });

        builder.Entity<NotificationOptOut>(e =>
        {
            e.Property(o => o.Phone).HasMaxLength(20);
            e.HasIndex(o => o.Phone).IsUnique();
        });

        builder.Entity<PlatformSetting>(e =>
        {
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(100);
            // T5-B12/M7 (ARCHITECTURE_CYCLE5.md §44.7): 200 → 2000 — the ad-markers dictionary
            // (PlatformSettings.AdMarkersKey) is a comma-separated list that doesn't fit in 200.
            e.Property(s => s.Value).HasMaxLength(2000);
        });

        builder.Entity<PlatformSettingChangeLog>(e =>
        {
            e.HasIndex(l => l.Key);
        });

        // Cycle 7 (ARCHITECTURE_CYCLE7.md §43.4): at most one system-free plan row, ever.
        builder.Entity<SubscriptionPlanConfig>(e =>
        {
            // Cycle 24 (§448.1): one system free tariff PER LINE.
            e.Property(p => p.Line).HasDefaultValue(CompanyKind.Services);
            e.Property(p => p.AllowOrders).HasDefaultValue(true);
            e.HasIndex(p => p.Line).HasDatabaseName("IX_SubscriptionPlanConfigs_Line_SystemFree").IsUnique().HasFilter("\"IsSystemFree\" = true");
            // Cycle 18 (§332.1): at most one system-trial plan row, ever.
            e.HasIndex(p => p.IsSystemTrial).IsUnique().HasFilter("\"IsSystemTrial\" = true");
        });

        // Cycle 18 (ARCHITECTURE_CYCLE18.md §332.4).
        builder.Entity<TrialGrant>(e =>
        {
            e.HasOne(g => g.BillingAccount).WithMany().HasForeignKey(g => g.BillingAccountId).OnDelete(DeleteBehavior.Cascade);
            e.Property(g => g.WarningThresholdsDays).HasMaxLength(32).IsRequired();
            e.Property(g => g.Reason).HasMaxLength(500);
            e.Property(g => g.TermsVersion).HasMaxLength(32).IsRequired();
            e.Property(g => g.TermsTextSha256).HasMaxLength(64).IsRequired();
            // 🔴 This IS the once-only guarantee (US-18-04): at most one non-emergency grant per
            // billing account, at the database level. The service-level check exists for a readable
            // error message, not for the guarantee itself; this index is what serializes the race of a
            // double-click. Source == SuperAdminOverride (value 2) is deliberately excluded — an
            // emergency regrant may happen more than once, and each stays in history.
            // Cycle 37 (§37.2.1): once per account PER LINE (Line = 0 is every pre-cycle-37 row).
            e.Property(g => g.Line).HasDefaultValue(CompanyKind.Services);
            e.HasIndex(g => new { g.BillingAccountId, g.Line }).IsUnique()
                .HasDatabaseName("UX_TrialGrants_OnePerAccount")
                .HasFilter("\"Source\" <> 2");
            e.HasIndex(g => g.GrantedAtUtc);
        });

        // Cycle 18 (ARCHITECTURE_CYCLE18.md §332.5). No FK on purpose — see the entity's own remarks.
        builder.Entity<TrialPhoneRegistration>(e =>
        {
            e.Property(r => r.PhoneKeyHash).HasMaxLength(64).IsRequired();
            e.Property(r => r.KeyId).HasMaxLength(16).IsRequired();
            e.Property(r => r.Line).HasDefaultValue(CompanyKind.Services);
            e.HasIndex(r => new { r.Line, r.PhoneKeyHash }).IsUnique().HasDatabaseName("UX_TrialPhoneRegistrations_Key");
            e.HasIndex(r => r.RegisteredAtUtc);
            e.HasIndex(r => r.KeyId);
        });

        // Cycle 7 (ARCHITECTURE_CYCLE7.md §43.3): option catalog for the pricing screen.
        builder.Entity<SubscriptionOption>(e =>
        {
            e.Property(o => o.Code).HasMaxLength(64);
            e.HasIndex(o => o.Code).IsUnique();
            e.Property(o => o.Name).HasMaxLength(100);
            e.Property(o => o.Description).HasMaxLength(500);
            e.Property(o => o.CapabilityKey).HasMaxLength(64);
            e.Property(o => o.PricePerMonth).HasPrecision(10, 2);
            e.Property(o => o.UnitName).HasMaxLength(32);
        });

        // Cycle 7, stage 3 (ARCHITECTURE_CYCLE7.md §43.3): what's paid for on an account's
        // subscription — one row per (account, option). The request-workflow fields
        // (RequestedQuantity/RequestedAtUtc/RequestedByUserId, US-70) are carried in the entity now so a
        // later stage's admin approval endpoint doesn't need its own migration, but nothing writes them
        // yet in this stage.
        builder.Entity<AccountSubscriptionOption>(e =>
        {
            e.HasOne(o => o.BillingAccount).WithMany().HasForeignKey(o => o.BillingAccountId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(o => o.Option).WithMany().HasForeignKey(o => o.OptionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(o => new { o.BillingAccountId, o.OptionId }).IsUnique();
        });

        // AccountUsageReader's raw-SQL projection (§46.1) — never queried through normal LINQ, and
        // backed by no real table (ToView(null)) so it never shows up as an empty migrated table.
        builder.Entity<AccountUsageRow>().HasNoKey().ToView(null);

        // ARCHITECTURE_CYCLE14.md §142.1 (Q1, Q7). PayloadHash is the ONLY thing an incoming bot_started
        // update can be looked up by — unique so a hash collision fails loudly instead of silently
        // reusing another session. (Status, ExpiresAtUtc) backs both the "Expired" computation and the
        // retention sweep; (UserId, CreatedAtUtc) backs "my open sessions" and DeleteAccount's cleanup.
        builder.Entity<PhoneVerificationSession>(e =>
        {
            e.Property(s => s.PayloadHash).HasMaxLength(64);
            e.HasIndex(s => s.PayloadHash).IsUnique();
            e.Property(s => s.StatusTokenHash).HasMaxLength(64);
            e.Property(s => s.CanonicalPhone).HasMaxLength(32);
            e.Property(s => s.ExternalAccountKey).HasMaxLength(64);
            e.Property(s => s.MismatchedPhoneMasked).HasMaxLength(32);
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => new { s.Status, s.ExpiresAtUtc });
            e.HasIndex(s => new { s.UserId, s.CreatedAtUtc });
        });

        // ARCHITECTURE_CYCLE14.md §142.2 (Q3, Q10). Phone is unique — verification belongs to the number,
        // not the account (Р4). ExternalAccountKey is indexed — §147.5's per-MAX-account ceiling is a
        // single COUNT(*) against it.
        builder.Entity<VerifiedPhone>(e =>
        {
            e.Property(v => v.Phone).HasMaxLength(32);
            e.HasIndex(v => v.Phone).IsUnique();
            e.Property(v => v.ExternalAccountKey).HasMaxLength(64);
            e.HasIndex(v => v.ExternalAccountKey);
            e.HasOne(v => v.User).WithMany().HasForeignKey(v => v.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.Session).WithMany().HasForeignKey(v => v.SessionId).OnDelete(DeleteBehavior.SetNull);
        });

        // Cycle 20 (ARCHITECTURE_CYCLE20.md §406.2, US-20-05) — the guest-data-gate journal. No FK on
        // UserId on purpose: the gate can fire on the very account-deletion request that removes that
        // user, and the journal must outlive the account (§406.2's own reasoning, same as ConsentRecord).
        builder.Entity<GuestDataGateEvent>(e =>
        {
            e.Property(g => g.UserId).HasMaxLength(450);
            e.Property(g => g.TraceId).HasMaxLength(64);
            e.HasIndex(g => g.OccurredAtUtc);
            e.HasIndex(g => new { g.UserId, g.OccurredAtUtc });
        });

        // Cycle 20 (ARCHITECTURE_CYCLE20.md §404.1, US-20-03) — in-cabinet notices with acknowledgement.
        builder.Entity<PlatformNotice>(e =>
        {
            e.Property(n => n.Title).HasMaxLength(200);
            e.Property(n => n.Body).HasColumnType("text");
            e.Property(n => n.TemplateVersion).HasMaxLength(64);
            e.Property(n => n.LinkUrl).HasMaxLength(500);
            e.Property(n => n.AttachmentTitle).HasMaxLength(200);
            e.Property(n => n.AttachmentHtml).HasColumnType("text");
            e.Property(n => n.AttachmentSha256).HasMaxLength(64).IsFixedLength();
            e.Property(n => n.CreatedByUserId).HasMaxLength(450);
            e.Property(n => n.RevokedByUserId).HasMaxLength(450);
            e.Property(n => n.RevokeReason).HasMaxLength(500);
            // §404.6 (retention) and §404.1's "read active notices" — a single indexed scan.
            e.HasIndex(n => n.VisibleUntilUtc).HasDatabaseName("IX_PlatformNotices_VisibleUntil");
        });

        builder.Entity<PlatformNoticeAcknowledgement>(e =>
        {
            e.Property(a => a.UserId).HasMaxLength(450);
            // Cascade: an acknowledgement is meaningless without the notice it belongs to, and a notice
            // is never deleted by application code anyway (only revoked, or removed by the retention
            // rule together with its acknowledgements, §404.6).
            e.HasOne(a => a.Notice).WithMany(n => n.Acknowledgements)
                .HasForeignKey(a => a.NoticeId).OnDelete(DeleteBehavior.Cascade);
            // §404.1 — idempotent "I've read it": a repeat click must not create a second row.
            e.HasIndex(a => new { a.NoticeId, a.UserId }).IsUnique()
                .HasDatabaseName("IX_PlatformNoticeAcknowledgements_NoticeUser");
        });
    }
}

/// <summary>Keyless row shape for <c>AccountUsageReader.GetAsync</c>'s raw SQL projection
/// (ARCHITECTURE_CYCLE7.md §46.1) — lives here (not in ServiceBooking.API, which depends on this
/// project, not the other way around) purely so <c>AppDbContext</c> can map it; never queried through
/// normal LINQ, only ever the target of <c>FromSqlInterpolated</c>.</summary>
public sealed class AccountUsageRow
{
    public Guid BillingAccountId { get; set; }
    public int CompaniesUsed { get; set; }
    public int SeatsUsed { get; set; }
}
