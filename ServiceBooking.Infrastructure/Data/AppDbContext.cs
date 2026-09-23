using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.Infrastructure.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Company> Companies => Set<Company>();
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
    public DbSet<ScheduledTaskState> ScheduledTaskStates => Set<ScheduledTaskState>();

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

    // Cycle 7, stage 3 (ARCHITECTURE_CYCLE7.md §43.3): what's paid for on an account's subscription —
    // today, only read for the "notifications.whatsapp" option's Quantity (§47.1's N).
    public DbSet<AccountSubscriptionOption> AccountSubscriptionOptions => Set<AccountSubscriptionOption>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

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
        });

        builder.Entity<Service>(e =>
        {
            e.Property(s => s.Price).HasColumnType("decimal(10,2)");
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
        });

        // Cycle 7 (ARCHITECTURE_CYCLE7.md §43.3): payer/rules-owner of companies and subscriptions —
        // see Company.BillingAccountId / AccountSubscription.BillingAccountId remarks.
        builder.Entity<BillingAccount>(e =>
        {
            e.HasOne(a => a.Owner).WithMany().HasForeignKey(a => a.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.OwnerUserId).IsUnique();
            e.Property(a => a.Name).HasMaxLength(100);
            // Cycle 5, stage 5 (§49) — the owner's single pending plan/options request; see the
            // entity's own remarks for why this isn't a separate SubscriptionRequest table.
            e.HasOne(a => a.RequestedPlan).WithMany().HasForeignKey(a => a.RequestedPlanId).OnDelete(DeleteBehavior.SetNull);
            e.Property(a => a.RequestedComment).HasMaxLength(500);
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
            e.HasAlternateKey(c => new { c.Id, c.BillingAccountId });
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
            // Cycle 7, stage 6 (ARCHITECTURE_CYCLE7.md §43.6) — both FKs are composite, pinned to the
            // (Id, BillingAccountId) alternate keys on NotificationChannels/Companies. This is the
            // co-tenancy guarantee: a row can only exist while the channel and the company it's
            // assigned to agree on BillingAccountId, so "assign a company to a number belonging to a
            // different account" is impossible at the database level, and CompanyTransferService is
            // forced to delete the assignment strictly before it can change Company.BillingAccountId
            // (§51.3 step 6/7) — the DB rejects the update otherwise.
            e.HasOne(a => a.Channel).WithMany(c => c.Assignments)
                .HasForeignKey(a => new { a.ChannelId, a.BillingAccountId })
                .HasPrincipalKey(c => new { c.Id, c.BillingAccountId })
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Company).WithMany()
                .HasForeignKey(a => new { a.CompanyId, a.BillingAccountId })
                .HasPrincipalKey(c => new { c.Id, c.BillingAccountId })
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.ChannelId);
            // US-61 p.7: a company may be assigned to at most one channel — a hard DB guarantee, not
            // application-level check-then-act.
            e.HasIndex(a => a.CompanyId).IsUnique();
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
        });

        builder.Entity<NotificationTemplate>(e =>
        {
            e.HasOne(t => t.Company).WithMany().HasForeignKey(t => t.CompanyId).OnDelete(DeleteBehavior.Cascade);
            e.Property(t => t.Body).HasMaxLength(1000);
            e.HasIndex(t => new { t.CompanyId, t.Type }).IsUnique();
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
            e.HasIndex(p => p.IsSystemFree).IsUnique().HasFilter("\"IsSystemFree\" = true");
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
