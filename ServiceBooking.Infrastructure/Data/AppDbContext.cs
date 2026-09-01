using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;

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
    public DbSet<AccountSubscription> AccountSubscriptions => Set<AccountSubscription>();
    public DbSet<WeeklyScheduleTemplate> WeeklyScheduleTemplates => Set<WeeklyScheduleTemplate>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ClientNote> ClientNotes => Set<ClientNote>();
    public DbSet<MailLog> MailLogs => Set<MailLog>();
    public DbSet<SubscriptionPlanConfig> SubscriptionPlanConfigs => Set<SubscriptionPlanConfig>();
    public DbSet<SubscriptionChangeLog> SubscriptionChangeLogs => Set<SubscriptionChangeLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Company>(e =>
        {
            e.HasIndex(c => c.Slug).IsUnique();
            e.HasOne(c => c.Owner).WithMany().HasForeignKey(c => c.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
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
        });

        builder.Entity<SubscriptionChangeLog>(e =>
        {
            e.HasIndex(l => l.OwnerUserId);
        });

        builder.Entity<Booking>(e =>
        {
            e.Property(b => b.Price).HasColumnType("decimal(10,2)");
            e.Property(b => b.CommissionPercent).HasColumnType("decimal(18,2)");
            e.HasOne(b => b.Company).WithMany(c => c.Bookings).HasForeignKey(b => b.CompanyId);
            e.HasOne(b => b.Service).WithMany(s => s.Bookings).HasForeignKey(b => b.ServiceId);
            e.HasOne(b => b.Master).WithMany(u => u.MasterBookings).HasForeignKey(b => b.MasterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(b => b.Client).WithMany(u => u.ClientBookings).HasForeignKey(b => b.ClientId).OnDelete(DeleteBehavior.SetNull);
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
        });

        builder.Entity<MailLog>(e => {
            e.HasOne(m => m.Company).WithMany().HasForeignKey(m => m.CompanyId);
            e.HasOne(m => m.SentBy).WithMany().HasForeignKey(m => m.SentById).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
