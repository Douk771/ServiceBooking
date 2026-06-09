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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Company>(e =>
        {
            e.HasIndex(c => c.Slug).IsUnique();
        });

        builder.Entity<Service>(e =>
        {
            e.Property(s => s.Price).HasColumnType("decimal(10,2)");
        });

        builder.Entity<CompanyMember>(e =>
        {
            e.HasOne(cm => cm.Company).WithMany(c => c.Members).HasForeignKey(cm => cm.CompanyId);
            e.HasOne(cm => cm.User).WithMany(u => u.CompanyMemberships).HasForeignKey(cm => cm.UserId);
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
        });

        builder.Entity<Booking>(e =>
        {
            e.HasOne(b => b.Company).WithMany(c => c.Bookings).HasForeignKey(b => b.CompanyId);
            e.HasOne(b => b.Service).WithMany(s => s.Bookings).HasForeignKey(b => b.ServiceId);
            e.HasOne(b => b.Master).WithMany(u => u.MasterBookings).HasForeignKey(b => b.MasterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(b => b.Client).WithMany(u => u.ClientBookings).HasForeignKey(b => b.ClientId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
