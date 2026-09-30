using Microsoft.AspNetCore.Identity;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services.Showcase;

/// <summary>
/// The whole showcase as plain entity objects with every foreign key already set (no navigation properties), produced by <see cref="ShowcaseDataset.Build"/>
/// without touching a database. The generator persists it in dependency order; the planner only counts it. City ids and image urls are resolved at persistence
/// time (<see cref="CityNameByCompany"/>, the asset keys below), so the graph is a pure function of the profile and the date.
/// </summary>
public sealed class ShowcaseGraph
{
    public List<AppUser> Users { get; } = [];
    public List<(string UserId, string RoleName)> UserRoles { get; } = [];
    public List<BillingAccount> BillingAccounts { get; } = [];
    public List<AccountSubscription> Subscriptions { get; } = [];
    public List<Company> Companies { get; } = [];
    public List<CompanyMember> Members { get; } = [];
    public List<Service> Services { get; } = [];
    public List<MasterService> MasterServices { get; } = [];
    public List<WeeklyScheduleTemplate> ScheduleTemplates { get; } = [];
    public List<WorkingHours> WorkingHours { get; } = [];
    public List<ScheduleBreak> ScheduleBreaks { get; } = [];
    public List<Booking> Bookings { get; } = [];
    public List<BookingService> BookingServices { get; } = [];
    public List<BookingEvent> BookingEvents { get; } = [];

    /// <summary>Company id → the name of its city in the directory (the generator resolves the id, and refuses if the city is missing).</summary>
    public Dictionary<Guid, string> CityNameByCompany { get; } = [];

    /// <summary>Company id → asset key of its logo (§575.6).</summary>
    public Dictionary<Guid, string> LogoKeyByCompany { get; } = [];

    /// <summary>Company id → asset keys of its gallery photos, in order (3–6). A key missing from the manifest is skipped, no placeholder is drawn.</summary>
    public Dictionary<Guid, IReadOnlyList<string>> PhotoKeysByCompany { get; } = [];

    /// <summary>Service id → asset key of its picture.</summary>
    public Dictionary<Guid, string> ServiceImageKeys { get; } = [];

    /// <summary>The number of rows per kind, for the report (<c>companies=9 users=158 …</c>). <c>Photos</c> and <c>Files</c> are filled in by the caller
    /// that knows which assets exist.</summary>
    public ShowcaseCounts Counts(int photos = 0, int files = 0) => new(
        Companies.Count, Users.Count, BillingAccounts.Count, Services.Count, Bookings.Count, BookingEvents.Count, photos, files);
}

/// <summary>The figures of an operator report line (API_CONTRACT_CYCLE28.md §602).</summary>
public sealed record ShowcaseCounts(int Companies, int Users, int BillingAccounts, int Services, int Bookings, int BookingEvents, int Photos, int Files)
{
    public override string ToString() =>
        $"companies={Companies} users={Users} billingAccounts={BillingAccounts} services={Services} bookings={Bookings} bookingEvents={BookingEvents} photos={Photos} files={Files}";
}
