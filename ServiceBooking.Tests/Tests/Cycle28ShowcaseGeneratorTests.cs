using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 28, pass A — what the generator (<c>ops showcase create</c>) actually produces, judged against SPEC.md US-28-02/03/04/05 and the D-1 decision.
/// One production-profile showcase is created once per class database (lazily, by the first test that needs it) and every test here only READS it.
/// Scenarios that change the showcase (delete, recreate, locks, snapshots) are in <see cref="Cycle28ShowcaseLifecycleTests"/>.
/// </summary>
[Trait("Category", "Heavy")]
public class Cycle28ShowcaseContentTests(TestDatabaseFixture fixture) : Cycle28ShowcaseTestBase(fixture)
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<(int Exit, string Output, TimeSpan Took)>>> Created = new();

    private Task<(int Exit, string Output, TimeSpan Took)> EnsureCreatedAsync() =>
        Created.GetOrAdd(ConnectionString, _ => new Lazy<Task<(int, string, TimeSpan)>>(async () =>
        {
            var sw = Stopwatch.StartNew();
            var (exit, output) = await RunOpsAsync("showcase", "create", "--yes");
            return (exit, output, sw.Elapsed);
        })).Value;

    [Fact, TestCase("CY28-16")]
    public async Task Create_OnEmptyDatabase_Succeeds_WithinFiveMinutes_AndReportsCounts()
    {
        var (exit, output, took) = await EnsureCreatedAsync();
        exit.Should().Be(0, output);
        output.Should().Contain("выполнено:").And.Contain("создано:");
        took.Should().BeLessThan(TimeSpan.FromMinutes(5), "§575.5 budget: create ≤ 5 minutes");
        Regex.IsMatch(output, @"7200555\d{4}").Should().BeFalse("phones never appear in the operator output");
    }

    [Fact, TestCase("CY28-17")]
    public async Task Companies_NineRows_FiveCities_SlugsAndNames_NoContacts_RatingsAbsent()
    {
        await EnsureCreatedAsync();
        var companies = await DbAsync(db => db.Companies.AsNoTracking().Where(c => c.IsShowcase).Include(c => c.City).ToListAsync());

        companies.Should().HaveCount(9, "8 profiles; the chain is two companies of one owner");
        companies.Select(c => c.CityId).Distinct().Count().Should().BeGreaterThanOrEqualTo(5, "SPEC: companies of different profiles in several cities");
        companies.Should().OnlyContain(c => c.Slug.StartsWith("primer-") && c.IsActive && c.ShowInPublicListing && c.Kind == CompanyKind.Services);
        companies.Should().OnlyContain(c => c.Phone == null && c.Email == null && c.YandexMapsUrl == null && c.TwoGisUrl == null, "§574.4: no contact that could ring somewhere");
        companies.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Description) && !string.IsNullOrWhiteSpace(c.Address) && !string.IsNullOrWhiteSpace(c.Name));
        companies.Should().OnlyContain(c => c.BookingHorizonDays == 30);
        foreach (var c in companies)
            Regex.IsMatch(c.Name + " " + c.Description, "тест|демо|пример|lorem", RegexOptions.IgnoreCase).Should().BeFalse($"{c.Slug}: no traces of testing in the names and texts (US-28-08)");

        // D-1: 5 of 8 profiles accept bookings. The chain (2 companies, one owner) counts as one profile.
        var open = companies.Count(c => c.ShowcaseBookingOpen);
        var closed = companies.Count(c => !c.ShowcaseBookingOpen);
        closed.Should().Be(3, "massage/SPA, cosmetology and the home-based master are closed");
        open.Should().Be(6, "salon, barbershop, nails, brows/lashes and both points of the chain");
        var owners = companies.GroupBy(c => c.OwnerUserId).Where(g => g.Count() > 1).ToList();
        owners.Should().ContainSingle("exactly one owner runs two points");
        owners.Single().Should().OnlyContain(c => c.ShowcaseBookingOpen);
        owners.Single().Select(c => c.BillingAccountId).Distinct().Should().HaveCount(1, "one billing account for the chain");
    }

    [Fact, TestCase("CY28-18")]
    public async Task EveryCompany_HasServicesWithPricesAndDurations_AndMastersWithBio()
    {
        await EnsureCreatedAsync();
        await DbAsync(async db =>
        {
            var companies = await db.Companies.AsNoTracking().Where(c => c.IsShowcase).Select(c => c.Id).ToListAsync();
            foreach (var id in companies)
            {
                var services = await db.Services.AsNoTracking().Where(s => s.CompanyId == id).ToListAsync();
                services.Count.Should().BeInRange(6, 15, "6–15 services per company");
                services.Should().OnlyContain(s => s.Price > 0 && s.DurationMinutes > 0 && s.IsActive && s.Name != "");

                var members = await db.CompanyMembers.AsNoTracking().Include(m => m.User).Where(m => m.CompanyId == id).ToListAsync();
                var staff = members.Where(m => m.ProvidesServices).ToList();
                staff.Count.Should().BeInRange(1, 6, "1–6 masters");
                staff.Should().OnlyContain(m => m.User.FirstName != "" && m.User.LastName != "" && !string.IsNullOrWhiteSpace(m.Bio), "masters have names and descriptions");
                (await db.MasterServices.CountAsync(ms => staff.Select(m => m.UserId).Contains(ms.MasterId))).Should().BeGreaterThan(0, "masters have their service sets");
            }
        });
    }

    [Fact, TestCase("CY28-19")]
    public async Task EveryCompany_HasLogo_ThreeToSixPhotos_AndServicePictures()
    {
        await EnsureCreatedAsync();
        await DbAsync(async db =>
        {
            foreach (var c in await db.Companies.AsNoTracking().Where(c => c.IsShowcase).ToListAsync())
            {
                c.LogoUrl.Should().NotBeNullOrEmpty(c.Slug);
                (await db.CompanyPhotos.CountAsync(p => p.CompanyId == c.Id)).Should().BeInRange(3, 6, c.Slug);
                (await db.Services.CountAsync(s => s.CompanyId == c.Id && s.ImageUrl != null)).Should().BeGreaterThan(0, c.Slug);
            }
        });
    }

    [Fact, TestCase("CY28-20")]
    public async Task Schedules_DifferBetweenMasters_HaveBreaksAndDaysOff_OneMasterOnVacationWithin14Days()
    {
        await EnsureCreatedAsync();
        await DbAsync(async db =>
        {
            var masterIds = await db.CompanyMembers.AsNoTracking().Where(m => m.Company.IsShowcase && m.ProvidesServices).Select(m => m.UserId).Distinct().ToListAsync();
            var templates = await db.WeeklyScheduleTemplates.AsNoTracking().Where(t => t.Company.IsShowcase).ToListAsync();
            templates.Should().NotBeEmpty("every master has a weekly template");
            templates.Select(t => t.MasterId).Distinct().Count().Should().BeGreaterThanOrEqualTo(masterIds.Count - 2, "templates exist for (almost) every master");
            templates.GroupBy(t => (t.MasterId, t.CompanyId))
                .Select(g => string.Join(";", g.OrderBy(t => t.DayOfWeek).Select(t => $"{t.DayOfWeek}:{t.IsWorking}:{t.StartTime}-{t.EndTime}")))
                .Distinct().Count().Should().BeGreaterThan(3, "schedules differ from master to master");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            (await db.ScheduleBreaks.CountAsync(b => db.WorkingHours.Any(w => w.Id == b.WorkingHoursId && w.Company.IsShowcase))).Should().BeGreaterThan(0, "breaks exist");
            var soon = await db.WorkingHours.AsNoTracking().Where(w => w.Company.IsShowcase && w.Date >= today && w.Date <= today.AddDays(14)).ToListAsync();
            soon.Should().Contain(w => !w.IsWorking, "days off exist in the next 14 days");
            var offByMaster = soon.Where(w => !w.IsWorking).GroupBy(w => (w.MasterId, w.CompanyId)).Select(g => g.Count()).ToList();
            offByMaster.Max().Should().BeGreaterThanOrEqualTo(3, "one master has a vacation of 3+ days in the next 14 days (weekends alone give at most 2 in a row)");
            soon.Where(w => w.IsWorking).Select(w => w.Date).Distinct().Count().Should().BeGreaterThanOrEqualTo(10, "the schedule covers the next two weeks");
        });
    }

    [Fact, TestCase("CY28-21")]
    public async Task Calendar_Next7Days_HasFreeTime_BusySlots_AndFullyBookedDays()
    {
        await EnsureCreatedAsync();
        var admin = await LoginAsSuperAdminAsync();
        var client = AnonymousClient();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var pairs = await DbAsync(db => db.CompanyMembers.AsNoTracking()
            .Where(m => m.Company.IsShowcase && m.ProvidesServices)
            .Select(m => new { m.CompanyId, MasterId = m.UserId, ServiceId = db.MasterServices.Where(ms => ms.MasterId == m.UserId && ms.Service.CompanyId == m.CompanyId && ms.Service.IsActive).OrderBy(ms => ms.Service.DurationMinutes).Select(ms => ms.ServiceId).First() })
            .ToListAsync());

        var statuses = new Dictionary<string, int>();
        foreach (var p in pairs)
        {
            var response = await client.GetAsync($"/api/bookings/availability?companyId={p.CompanyId}&masterId={p.MasterId}&serviceId={p.ServiceId}&from={today:yyyy-MM-dd}&to={today.AddDays(6):yyyy-MM-dd}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            foreach (var day in doc.RootElement.GetProperty("days").EnumerateArray())
            {
                var status = day.GetProperty("status").GetString()!;
                statuses[status] = statuses.GetValueOrDefault(status) + 1;
            }
        }
        statuses.Should().ContainKey("Available", "there is free time in the next 7 days");
        statuses.Should().ContainKey("FullyBooked", "SPEC §4: some days show «Занято»");
        statuses.Should().ContainKey("DayOff");

        // Some busy slots exist too: a master with a partly-filled day (occupied ranges non-empty and free slots non-empty).
        var partial = 0;
        foreach (var p in pairs.Take(12))
        {
            for (var d = 0; d < 7 && partial == 0; d++)
            {
                var date = today.AddDays(d);
                var slots = await client.GetAsync($"/api/bookings/slots?companyId={p.CompanyId}&masterId={p.MasterId}&serviceId={p.ServiceId}&date={date:yyyy-MM-dd}");
                if (slots.StatusCode != HttpStatusCode.OK) continue;
                var freeCount = JsonDocument.Parse(await slots.Content.ReadAsStringAsync()).RootElement.GetArrayLength();
                var occupied = await DbAsync(db => db.Bookings.CountAsync(b => b.MasterId == p.MasterId && b.CompanyId == p.CompanyId && b.Date == date && b.Status != BookingStatus.Cancelled));
                if (freeCount > 0 && occupied > 0) partial++;
            }
        }
        partial.Should().BeGreaterThan(0, "a day with both busy and free time exists");
    }

    [Fact, TestCase("CY28-22")]
    public async Task Bookings_EightToTwelveThousand_PastHistory60Days_NearFuture14Days_StatusMix_JournalPresent()
    {
        await EnsureCreatedAsync();
        await DbAsync(async db =>
        {
            var q = db.Bookings.AsNoTracking().Where(b => b.Company.IsShowcase);
            var total = await q.CountAsync();
            total.Should().BeInRange(8000, 12000, "8–12 thousand bookings");
            (await q.CountAsync(b => b.ShowcaseKind != ShowcaseBookingKind.Seeded)).Should().Be(0, "generator bookings are marked Seeded");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            (await q.MinAsync(b => b.Date)).Should().BeOnOrAfter(today.AddDays(-62));
            (await q.MaxAsync(b => b.Date)).Should().BeOnOrBefore(today.AddDays(15), "no bookings beyond the near future");

            var past = q.Where(b => b.Date < today.AddDays(-1));
            var pastTotal = await past.CountAsync();
            var completed = await past.CountAsync(b => b.Status == BookingStatus.Completed);
            var cancelled = await past.CountAsync(b => b.Status == BookingStatus.Cancelled);
            var noShow = await past.CountAsync(b => b.Status == BookingStatus.NoShow);
            (completed / (double)pastTotal).Should().BeInRange(0.70, 0.90, "~80% completed");
            (cancelled / (double)pastTotal).Should().BeInRange(0.05, 0.20, "~12% cancelled");
            noShow.Should().BeGreaterThan(0);
            (await past.CountAsync(b => b.Status == BookingStatus.Cancelled && (b.CancellationReason == null || b.CancellationReason == ""))).Should().Be(0, "cancelled visits carry a reason");
            (await q.CountAsync(b => b.Price <= 0)).Should().Be(0);

            var withJournal = await db.BookingEvents.AsNoTracking().Where(e => e.Booking.Company.IsShowcase).Select(e => e.BookingId).Distinct().CountAsync();
            withJournal.Should().BeGreaterThan(total * 9 / 10, "the journal exists for the visits");
            (await db.BookingEvents.AnyAsync(e => e.Booking.Company.IsShowcase && e.Kind == BookingEventKind.Rescheduled)).Should().BeTrue("some visits were moved");
            (await db.BookingServices.AsNoTracking().Where(s => s.Booking.Company.IsShowcase).GroupBy(s => s.BookingId).Select(g => g.Count()).MaxAsync()).Should().BeInRange(1, 2, "1–2 services per booking");
        });
    }

    [Fact, TestCase("CY28-23")]
    public async Task Reports_RevenueFor30And60Days_NonEmpty_PerCompanyAndMaster()
    {
        await EnsureCreatedAsync();
        var admin = await LoginAsSuperAdminAsync();
        var client = AuthedClient(admin.Token);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var companyIds = await DbAsync(db => db.Companies.AsNoTracking().Where(c => c.IsShowcase).Select(c => c.Id).ToListAsync());

        foreach (var id in companyIds)
        {
            foreach (var days in new[] { 30, 60 })
            {
                var response = await client.GetAsync($"/api/reports/masters?companyId={id}&from={today.AddDays(-days):yyyy-MM-dd}&to={today:yyyy-MM-dd}");
                response.StatusCode.Should().Be(HttpStatusCode.OK, $"{id} {days}d: " + await response.Content.ReadAsStringAsync());
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var masters = doc.RootElement.EnumerateArray().ToList();
                masters.Should().NotBeEmpty($"{days}-day masters report of {id} must not be empty");
                masters.Sum(m => m.GetProperty("totalAmount").GetDecimal()).Should().BeGreaterThan(0);
            }
            var stats = await client.GetAsync($"/api/companies/{id}/stats?from={today.AddDays(-30):yyyy-MM-dd}&to={today:yyyy-MM-dd}");
            stats.StatusCode.Should().Be(HttpStatusCode.OK);
            (await stats.Content.ReadAsStringAsync()).Should().NotContain("\"totalRevenue\":0,");
        }
    }

    private static bool InBlock(string? phone) => phone != null && long.TryParse(phone.TrimStart('+'), out var n) && n >= 72005550000L && n <= 72005559999L;

    [Fact, TestCase("CY28-24")]
    public async Task Clients_SomeHaveSeveralVisitsToDifferentMasters_PhonesInBlock_NamesFictional_NoPasswordsOrEmails()
    {
        await EnsureCreatedAsync();
        await DbAsync(async db =>
        {
            var multi = await db.Bookings.AsNoTracking().Where(b => b.Company.IsShowcase && b.GuestPhone != null)
                .GroupBy(b => new { b.CompanyId, b.GuestPhone }).Where(g => g.Select(x => x.MasterId).Distinct().Count() >= 2).CountAsync();
            multi.Should().BeGreaterThan(20, "SPEC US-28-04: a part of the clients has several visits to different masters");

            var users = await db.Users.AsNoTracking().Where(u => u.IsShowcase).ToListAsync();
            users.Count.Should().BeGreaterThan(100);
            users.Should().OnlyContain(u => u.PasswordHash == null, "showcase accounts have no password (second lock on login)");
            users.Should().OnlyContain(u => u.Email == null || u.Email.EndsWith("@example.com"));

            users.Where(u => !InBlock(u.PhoneNumber)).Should().BeEmpty("user phones lie in the reserved block +7 (200) 555-xxxx");
            users.Where(u => !InBlock(u.UserName)).Should().BeEmpty();
            var guestPhones = await db.Bookings.AsNoTracking().Where(b => b.Company.IsShowcase && b.GuestPhone != null).Select(b => b.GuestPhone).Distinct().ToListAsync();
            guestPhones.Should().NotBeEmpty();
            guestPhones.Where(p => !InBlock(p)).Should().BeEmpty("guest phones lie in the reserved block");
            (await db.Bookings.CountAsync(b => b.Company.IsShowcase && b.GuestEmail != null && !b.GuestEmail.EndsWith("@example.com"))).Should().Be(0);
            (await db.Bookings.CountAsync(b => b.Company.IsShowcase && b.ConsentAcceptedAtUtc != null)).Should().Be(0, "no consent snapshots on generated bookings");
        });
    }

    [Fact, TestCase("CY28-25")]
    public async Task NotGenerated_Health_NotePhotos_Consents_SubjectRequests_Channels_Outgoing_Reviews()
    {
        await EnsureCreatedAsync();
        await DbAsync(async db =>
        {
            (await db.ClientHealthNotes.CountAsync(n => n.Company.IsShowcase)).Should().Be(0, "no health data");
            (await db.ClientNotePhotos.CountAsync(p => db.Companies.Any(c => c.Id == p.CompanyId && c.IsShowcase))).Should().Be(0);
            (await db.ConsentRecords.CountAsync(c => db.Users.Any(u => u.Id == c.UserId && u.IsShowcase) || db.Companies.Any(x => x.Id == c.CompanyId && x.IsShowcase))).Should().Be(0, "no consent journal entries");
            (await db.SubjectRequests.CountAsync()).Should().Be(0, "no subject requests");
            (await db.NotificationChannels.CountAsync()).Should().Be(0, "no mailing channels");
            (await db.OutboundNotifications.CountAsync()).Should().Be(0);
            (await db.StaffPushNotifications.CountAsync()).Should().Be(0);
            (await db.SubscriptionChangeLogs.CountAsync()).Should().Be(0);
            (await db.MailLogs.CountAsync()).Should().Be(0);
            (await db.Reviews.CountAsync(r => r.Company.IsShowcase)).Should().Be(0, "D-4: no reviews on the production showcase");
            foreach (var (table, _) in ShowcaseOwnership.NeverWritten)
            {
                // The table name comes from the code constant ShowcaseOwnership.NeverWritten, never from input: identifiers cannot be SQL parameters.
#pragma warning disable EF1002
                (await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*)::int AS \"Value\" FROM \"{table}\"").ToListAsync()).Single().Should().Be(0, table);
#pragma warning restore EF1002
            }
        });
    }

    [Fact, TestCase("CY28-26")]
    public async Task Marks_FormOneClosedChain_MixingInvariantsHold_PlanIsHidden()
    {
        await EnsureCreatedAsync();
        await DbAsync(async db =>
        {
            // §574.1 invariants.
            (await db.Companies.CountAsync(c => c.IsShowcase && !c.BillingAccount!.IsShowcase)).Should().Be(0, "showcase company ⇒ showcase billing account");
            (await db.Companies.CountAsync(c => c.IsShowcase && !db.Users.Any(u => u.Id == c.OwnerUserId && u.IsShowcase))).Should().Be(0, "showcase company ⇒ showcase owner");
            (await db.CompanyMembers.CountAsync(m => m.Company.IsShowcase && !m.User.IsShowcase)).Should().Be(0, "staff of a showcase company is showcase");
            (await db.CompanyMembers.CountAsync(m => !m.Company.IsShowcase && m.User.IsShowcase)).Should().Be(0, "no showcase user on a real company");
            (await db.Bookings.CountAsync(b => b.Company.IsShowcase && b.ShowcaseKind == ShowcaseBookingKind.None)).Should().Be(0);
            (await db.Bookings.CountAsync(b => !b.Company.IsShowcase && b.ShowcaseKind != ShowcaseBookingKind.None)).Should().Be(0);
            (await db.AccountSubscriptions.CountAsync(s => s.PlanConfigId == ShowcaseCatalog.ShowcasePlanId && !s.BillingAccount!.IsShowcase)).Should().Be(0);
            (await db.AccountSubscriptions.CountAsync(s => s.BillingAccount!.IsShowcase && s.PlanConfigId != ShowcaseCatalog.ShowcasePlanId)).Should().Be(0, "showcase accounts sit on the hidden tariff");
            (await db.AccountSubscriptions.CountAsync(s => s.BillingAccount!.IsShowcase && (!s.IsActive || s.PaidUntil != null))).Should().Be(0);

            var plan = await db.SubscriptionPlanConfigs.AsNoTracking().FirstAsync(p => p.Id == ShowcaseCatalog.ShowcasePlanId);
            plan.IsActive.Should().BeTrue();
            plan.IsPublic.Should().BeFalse("the hidden service tariff is never on /pricing");
            plan.AllowPublicListing.Should().BeTrue();
            plan.AllowOnlineBooking.Should().BeTrue();
            plan.AllowMailing.Should().BeFalse();
            plan.IsSystemTrial.Should().BeFalse();
            plan.IsSystemFree.Should().BeFalse();

            // Users owned by the showcase keep their own roles like any product account.
            (await db.Users.CountAsync(u => u.IsShowcase)).Should().BeGreaterThan(0);
        });
    }

    [Fact, TestCase("CY28-27")]
    public async Task Login_AllShowcaseOwners_Are401_EvenWithAnyPassword()
    {
        await EnsureCreatedAsync();
        var phones = await DbAsync(db => db.Companies.AsNoTracking().Where(c => c.IsShowcase).Select(c => c.Owner.PhoneNumber!).Distinct().ToListAsync());
        phones.Should().NotBeEmpty();
        foreach (var phone in phones)
        {
            var r = await LoginRawAsync(phone, "Password123!");
            r.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "showcase accounts never log in on a production configuration");
        }
    }

    [Fact, TestCase("CY28-28")]
    public async Task PublicCatalog_ListsAllNine_WithFlagsAndNoContacts_AndAnswersFast()
    {
        await EnsureCreatedAsync();
        var client = AnonymousClient();
        var timings = new List<double>();
        JsonElement items = default;
        for (var i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            var response = await client.GetAsync("/api/companies/public?pageSize=100");
            sw.Stop();
            timings.Add(sw.Elapsed.TotalMilliseconds);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            if (i == 0)
            {
                var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
                items = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("items");
            }
        }
        var showcase = items.EnumerateArray().Where(e => e.GetProperty("isShowcase").GetBoolean()).ToList();
        showcase.Should().HaveCount(9, "«Все города» — все витринные компании");
        showcase.Should().OnlyContain(e => e.GetProperty("slug").GetString()!.StartsWith("primer-"));
        showcase.Should().OnlyContain(e => e.GetProperty("phone").ValueKind == JsonValueKind.Null && e.GetProperty("email").ValueKind == JsonValueKind.Null);
        showcase.Count(e => e.GetProperty("showcaseBookingOpen").GetBoolean()).Should().Be(6);
        showcase.Should().OnlyContain(e => e.GetProperty("reviewCount").GetInt32() == 0 && e.GetProperty("averageRating").ValueKind == JsonValueKind.Null, "D-4: no reviews and no rating");
        showcase.Should().OnlyContain(e => e.GetProperty("onlineBookingEnabled").GetBoolean());

        timings.OrderBy(t => t).ElementAt(timings.Count / 2).Should().BeLessThan(1000, "smoke on the p95 < 300 ms budget (median of 10; a hard 300 ms cannot be asserted on a shared CI machine; the run's timings: " + string.Join(", ", timings.Select(t => t.ToString("0"))) + " ms)");

        // City filter: each city shows only its own companies.
        var byCity = showcase.GroupBy(e => e.GetProperty("cityId").GetInt32()).ToList();
        byCity.Count.Should().BeGreaterThanOrEqualTo(5);
        var one = byCity.First();
        var filtered = await client.GetAsync($"/api/companies/public?cityId={one.Key}&pageSize=100");
        filtered.StatusCode.Should().Be(HttpStatusCode.OK);
        var filteredRoot = JsonDocument.Parse(await filtered.Content.ReadAsStringAsync()).RootElement;
        var filteredItems = (filteredRoot.ValueKind == JsonValueKind.Array ? filteredRoot : filteredRoot.GetProperty("items")).EnumerateArray()
            .Where(e => e.GetProperty("isShowcase").GetBoolean()).ToList();
        filteredItems.Should().HaveCount(one.Count(), "the catalog of a chosen city shows that city's showcase companies");
    }

    [Fact, TestCase("CY28-29")]
    public async Task ClosedShowcaseCompany_RefusesVisitor_OpenOneAccepts_OnTheGeneratedData()
    {
        await EnsureCreatedAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        async Task<(HttpResponseMessage Response, Guid? BookingId)> TryBook(bool open)
        {
            var pick = await DbAsync(db => db.CompanyMembers.AsNoTracking()
                .Where(m => m.Company.IsShowcase && m.Company.ShowcaseBookingOpen == open && m.ProvidesServices)
                .Select(m => new { m.CompanyId, MasterId = m.UserId, ServiceId = db.MasterServices.Where(ms => ms.MasterId == m.UserId && ms.Service.CompanyId == m.CompanyId && ms.Service.IsActive).OrderBy(ms => ms.Service.DurationMinutes).Select(ms => ms.ServiceId).First() })
                .FirstAsync());
            // Find a day/time that is really free through the public slots API.
            for (var d = 8; d < 25; d++)
            {
                var date = today.AddDays(d);
                var slots = await AnonymousClient().GetAsync($"/api/bookings/slots?companyId={pick.CompanyId}&masterId={pick.MasterId}&serviceId={pick.ServiceId}&date={date:yyyy-MM-dd}");
                if (slots.StatusCode != HttpStatusCode.OK) continue;
                var arr = JsonDocument.Parse(await slots.Content.ReadAsStringAsync()).RootElement;
                if (arr.GetArrayLength() == 0) continue;
                var start = arr[0].GetProperty("start").GetString() ?? arr[0].GetProperty("startTime").GetString();
                var response = await AnonymousClient().PostAsJsonAsync("/api/bookings", new
                {
                    companyId = pick.CompanyId, serviceId = pick.ServiceId, masterId = pick.MasterId, date = date.ToString("yyyy-MM-dd"), startTime = start,
                    guestName = "Проверочный Посетитель", guestPhone = UniquePhone(),
                });
                Guid? id = response.StatusCode == HttpStatusCode.Created ? JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid() : null;
                return (response, id);
            }
            throw new InvalidOperationException("no free slot found in the generated schedule");
        }

        var (closed, _) = await TryBook(open: false);
        closed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonDocument.Parse(await closed.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString().Should().Be("ShowcaseBookingClosed");

        var (accepted, bookingId) = await TryBook(open: true);
        try
        {
            accepted.StatusCode.Should().Be(HttpStatusCode.Created, await accepted.Content.ReadAsStringAsync());
            await DbAsync(async db => (await db.Bookings.AsNoTracking().FirstAsync(b => b.Id == bookingId!.Value)).ShowcaseKind.Should().Be(ShowcaseBookingKind.Visitor));
            (await DbAsync(db => db.OutboundNotifications.CountAsync())).Should().Be(0, "no outgoing message for a visitor's showcase booking");
        }
        finally
        {
            // The other tests of this class read the generated data and assume it is exactly what the generator wrote: remove the visitor's booking.
            if (bookingId is { } id)
                await DbAsync(async db =>
                {
                    await db.BookingEvents.Where(e => e.BookingId == id).ExecuteDeleteAsync();
                    await db.BookingServices.Where(x => x.BookingId == id).ExecuteDeleteAsync();
                    await db.Bookings.Where(b => b.Id == id).ExecuteDeleteAsync();
                });
        }
    }
}

/// <summary>Helpers shared by the generator scenarios that change the showcase.</summary>
public abstract class Cycle28GeneratorTestBase(TestDatabaseFixture fixture) : Cycle28ShowcaseTestBase(fixture)
{
    /// <summary>A content fingerprint of the showcase rows that must be identical for one date (ids of bookings are ignored, content is not).</summary>
    protected async Task<string> FingerprintAsync() => await DbAsync(async db =>
    {
        var sb = new StringBuilder();
        foreach (var c in await db.Companies.AsNoTracking().Where(c => c.IsShowcase).OrderBy(c => c.Slug).ToListAsync())
            sb.Append($"C|{c.Id}|{c.Slug}|{c.Name}|{c.Description}|{c.CityId}|{c.ShowcaseBookingOpen}\n");
        foreach (var s in await db.Services.AsNoTracking().Where(s => s.Company.IsShowcase).OrderBy(s => s.Id).ToListAsync())
            sb.Append($"S|{s.Id}|{s.Name}|{s.Price}|{s.DurationMinutes}\n");
        foreach (var u in await db.Users.AsNoTracking().Where(u => u.IsShowcase).OrderBy(u => u.PhoneNumber).ToListAsync())
            sb.Append($"U|{u.PhoneNumber}|{u.FirstName}|{u.LastName}\n");
        foreach (var b in await db.Bookings.AsNoTracking().Where(b => b.Company.IsShowcase).OrderBy(b => b.CompanyId).ThenBy(b => b.MasterId).ThenBy(b => b.Date).ThenBy(b => b.StartTime).Select(b => new { b.CompanyId, b.MasterId, b.Date, b.StartTime, b.Status, b.Price, b.GuestPhone, b.GuestName }).ToListAsync())
            sb.Append($"B|{b.CompanyId}|{b.MasterId}|{b.Date}|{b.StartTime}|{b.Status}|{b.Price}|{b.GuestPhone}|{b.GuestName}\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    });

    protected async Task<int> MarkedRowsAsync() => await DbAsync(async db =>
    {
        var total = 0;
        foreach (var step in ShowcaseOwnership.DeleteSteps)
            total += (await db.Database.SqlQueryRaw<int>(step.CountSql).ToListAsync()).Single();
        total += await db.Bookings.CountAsync(b => b.ShowcaseKind != ShowcaseBookingKind.None);
        return total;
    });
}

/// <summary>
/// CY28-30 — own class and own fresh database. The scenario is what an operator really does: <c>create</c>, look, <c>delete</c>, later <c>create</c> again —
/// with the planner statistics of the freshly loaded tables NOT refreshed in between (autovacuum re-analyses a minute or more later). The class first
/// leaves the tables "analysed while empty" (create + ANALYZE + delete + ANALYZE), which is the state after any earlier delete. A statement of the eraser
/// then runs 10–15× slower than with fresh statistics (measured 7 s against 0.4–0.6 s) and under the default 30 s command timeout (the generator raises it to
/// 300 s only for the insert) it fails with exit code 1 as soon as the machine is busy. Defect recorded in the QA report, see CY28-30 notes.
/// </summary>
[Trait("Category", "Heavy")]
public class Cycle28ShowcaseFreshDeleteTests(TestDatabaseFixture fixture) : Cycle28GeneratorTestBase(fixture)
{
    [Fact, TestCase("CY28-30")]
    public async Task CreateDeleteCreate_OnOneDate_GivesTheSameFingerprint_AndDeleteLeavesNothingMarked()
    {
        // Stale-statistics state (see the class comment): the tables have been analysed while empty.
        (await RunOpsAsync("showcase", "create", "--yes")).Exit.Should().Be(0);
        await DbAsync(db => db.Database.ExecuteSqlRawAsync("ANALYZE"));
        (await RunOpsAsync("showcase", "delete", "--yes")).Exit.Should().Be(0);
        await DbAsync(db => db.Database.ExecuteSqlRawAsync("ANALYZE"));

        var (exit, output) = await RunOpsAsync("showcase", "create", "--yes");
        exit.Should().Be(0, output);
        var first = await FingerprintAsync();
        first.Should().NotBeNullOrEmpty();

        var sw = Stopwatch.StartNew();
        var (delExit, delOutput) = await RunOpsAsync("showcase", "delete", "--yes");
        sw.Stop();
        delExit.Should().Be(0, delOutput);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(1), "§575.5 budget: delete ≤ 1 minute");
        (await MarkedRowsAsync()).Should().Be(0, "after delete not one marked row is left in any table of the ownership chain");
        (await DbAsync(db => db.Companies.CountAsync(c => c.IsShowcase))).Should().Be(0);
        (await DbAsync(db => db.Users.CountAsync(u => u.IsShowcase))).Should().Be(0);
        (await DbAsync(db => db.BillingAccounts.CountAsync(u => u.IsShowcase))).Should().Be(0);
        (await DbAsync(db => db.SubscriptionPlanConfigs.AnyAsync(p => p.Id == ShowcaseCatalog.ShowcasePlanId))).Should().BeTrue("the tariff row is a catalog row: it is never deleted");

        var (again, againOutput) = await RunOpsAsync("showcase", "create", "--yes");
        again.Should().Be(0, againOutput);
        (await FingerprintAsync()).Should().Be(first, "US-28-06: one date ⇒ one and the same set of names, companies, services and visits");
    }
}

/// <summary>
/// QA cycle 28, pass A — US-28-06 (create / recreate / delete, isolation of real data, determinism, exit codes, locks) and the retention side of US-28-05.
/// Own database; every test starts from a normalised state (<c>ops showcase delete --yes</c>).
/// </summary>
[Trait("Category", "Heavy")]
public class Cycle28ShowcaseLifecycleTests(TestDatabaseFixture fixture) : Cycle28GeneratorTestBase(fixture)
{
    private static readonly Regex CountsAfter = new(@"создано: (?<c>companies=.*?)( за |$)", RegexOptions.Multiline);

    /// <summary>
    /// Refreshes planner statistics. Every scenario of this class calls it after a create: <c>ops showcase delete</c> straight after <c>create</c> is 10–15× slower
    /// on stale statistics and can pass the default 30 s command timeout on a busy machine (defect reproduced by CY28-30 in
    /// <see cref="Cycle28ShowcaseFreshDeleteTests"/>); one defect must not turn every test of this class red.
    /// </summary>
    private Task AnalyzeAsync() => DbAsync(db => db.Database.ExecuteSqlRawAsync("ANALYZE"));

    private async Task NormaliseAsync()
    {
        await AnalyzeAsync();
        var (exit, output) = await RunOpsAsync("showcase", "delete", "--yes");
        exit.Should().Be(0, output);
    }

    private async Task<(int Exit, string Output)> CreateAsync()
    {
        var result = await RunOpsAsync("showcase", "create", "--yes");
        await AnalyzeAsync();
        return result;
    }

    /// <summary>A fingerprint of every row that is NOT marked, in the tables the generator touches (platform settings are excluded: the last re-seed stamp is by design).</summary>
    private async Task<string> RealDataFingerprintAsync() => await DbAsync(async db =>
    {
        var sb = new StringBuilder();
        foreach (var u in await db.Users.AsNoTracking().Where(u => !u.IsShowcase).OrderBy(u => u.Id).Select(u => new { u.Id, u.PhoneNumber, u.FirstName, u.LastName, u.PasswordHash }).ToListAsync()) sb.Append($"U|{u.Id}|{u.PhoneNumber}|{u.FirstName}|{u.LastName}|{u.PasswordHash}\n");
        foreach (var c in await db.Companies.AsNoTracking().Where(c => !c.IsShowcase).OrderBy(c => c.Id).Select(c => new { c.Id, c.Name, c.Slug, c.OwnerUserId, c.BillingAccountId, c.LogoUrl }).ToListAsync()) sb.Append($"C|{c.Id}|{c.Name}|{c.Slug}|{c.OwnerUserId}|{c.BillingAccountId}|{c.LogoUrl}\n");
        foreach (var b in await db.Bookings.AsNoTracking().Where(b => b.ShowcaseKind == ShowcaseBookingKind.None).OrderBy(b => b.Id).Select(b => new { b.Id, b.CompanyId, b.Status, b.Date, b.StartTime, b.Price, b.GuestPhone }).ToListAsync()) sb.Append($"B|{b.Id}|{b.CompanyId}|{b.Status}|{b.Date}|{b.StartTime}|{b.Price}|{b.GuestPhone}\n");
        foreach (var s in await db.Services.AsNoTracking().Where(s => !s.Company.IsShowcase).OrderBy(s => s.Id).Select(s => new { s.Id, s.Name, s.Price, s.ImageUrl }).ToListAsync()) sb.Append($"S|{s.Id}|{s.Name}|{s.Price}|{s.ImageUrl}\n");
        foreach (var m in await db.CompanyMembers.AsNoTracking().Where(m => !m.Company.IsShowcase).OrderBy(m => m.Id).Select(m => new { m.Id, m.CompanyId, m.UserId, m.Role }).ToListAsync()) sb.Append($"M|{m.Id}|{m.CompanyId}|{m.UserId}|{m.Role}\n");
        foreach (var w in await db.WorkingHours.AsNoTracking().Where(w => !w.Company.IsShowcase).OrderBy(w => w.Id).Select(w => new { w.Id, w.Date, w.IsWorking }).ToListAsync()) sb.Append($"W|{w.Id}|{w.Date}|{w.IsWorking}\n");
        foreach (var a in await db.BillingAccounts.AsNoTracking().Where(a => !a.IsShowcase).OrderBy(a => a.Id).Select(a => new { a.Id, a.OwnerUserId }).ToListAsync()) sb.Append($"A|{a.Id}|{a.OwnerUserId}\n");
        foreach (var s in await db.AccountSubscriptions.AsNoTracking().Where(s => !s.BillingAccount!.IsShowcase).OrderBy(s => s.Id).Select(s => new { s.Id, s.PlanConfigId, s.PaidUntil, s.IsActive }).ToListAsync()) sb.Append($"P|{s.Id}|{s.PlanConfigId}|{s.PaidUntil}|{s.IsActive}\n");
        foreach (var r in await db.UserRoles.AsNoTracking().Where(r => !db.Users.Any(u => u.Id == r.UserId && u.IsShowcase)).OrderBy(r => r.UserId).ThenBy(r => r.RoleId).ToListAsync()) sb.Append($"R|{r.UserId}|{r.RoleId}\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    });

    [Fact, TestCase("CY28-31")]
    public async Task RealData_IsIdenticalBeforeAndAfterCreateRecreateDelete()
    {
        await NormaliseAsync();
        // Real data: an owner with a company, a master, a service and a completed booking, a real client.
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        var booking = await AnonymousClient().PostAsJsonAsync("/api/bookings", new
        {
            companyId = company.Id, serviceId = service.Id, masterId = master.UserId, date = date.ToString("yyyy-MM-dd"), startTime = "10:00:00", guestName = "Реальный Гость", guestPhone = UniquePhone(),
        });
        booking.StatusCode.Should().Be(HttpStatusCode.Created);
        await RegisterAsync();

        var before = await RealDataFingerprintAsync();

        (await CreateAsync()).Exit.Should().Be(0);
        (await RealDataFingerprintAsync()).Should().Be(before, "create must not touch a single unmarked row");

        (await RunOpsAsync("showcase", "recreate", "--yes")).Exit.Should().Be(0);
        (await RealDataFingerprintAsync()).Should().Be(before, "recreate must not touch a single unmarked row");
        await AnalyzeAsync();

        (await RunOpsAsync("showcase", "delete", "--yes")).Exit.Should().Be(0);
        (await RealDataFingerprintAsync()).Should().Be(before, "delete must not touch a single unmarked row");

        // The real company still works over HTTP.
        (await AnonymousClient().GetAsync($"/api/companies/{company.Slug}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CY28-32")]
    public async Task Delete_RemovesShowcaseFiles_IncludingPicturesUploadedOutsideTheShowcaseFolder()
    {
        await NormaliseAsync();
        (await CreateAsync()).Exit.Should().Be(0);

        using var scope = Factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<FileStorage>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bytes = Encoding.UTF8.GetBytes("not really a picture, only a file");
        await storage.SavePublicNamedAsync(PublicArea.Showcase, "cy28-asset.jpg", bytes);
        var uploadedUrl = await storage.SavePublicAsync(PublicArea.Companies, bytes, ".jpg");
        var company = await db.Companies.FirstAsync(c => c.IsShowcase);
        company.LogoUrl = uploadedUrl;
        await db.SaveChangesAsync();

        string Physical(string url) => Path.Combine(storage.PublicRootFullPath, url.Replace("/uploads/", "", StringComparison.Ordinal).TrimStart('/'));
        File.Exists(Physical(uploadedUrl)).Should().BeTrue("precondition: the uploaded logo exists");
        storage.CountPublicAreaFiles(PublicArea.Showcase).Should().BeGreaterThan(0);

        var (exit, output) = await RunOpsAsync("showcase", "delete", "--yes");
        exit.Should().Be(0, output);

        storage.CountPublicAreaFiles(PublicArea.Showcase).Should().Be(0, "no file of the showcase folder is left");
        File.Exists(Physical(uploadedUrl)).Should().BeFalse("a logo uploaded into a showcase company outside the showcase folder is removed too");
    }

    [Fact, TestCase("CY28-33")]
    public async Task Recreate_NeverShowsAnEmptyCatalog_KeepsIdsAndSlugs()
    {
        await NormaliseAsync();
        (await CreateAsync()).Exit.Should().Be(0);
        var before = await DbAsync(db => db.Companies.AsNoTracking().Where(c => c.IsShowcase).OrderBy(c => c.Slug).Select(c => new { c.Id, c.Slug }).ToListAsync());

        var minSeen = int.MaxValue;
        using var stop = new CancellationTokenSource();
        var watcher = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var count = await DbAsync(db => db.Companies.CountAsync(c => c.IsShowcase && c.ShowInPublicListing));
                minSeen = Math.Min(minSeen, count);
                await Task.Delay(15);
            }
        });
        var (exit, output) = await RunOpsAsync("showcase", "recreate", "--yes");
        stop.Cancel();
        await watcher;

        exit.Should().Be(0, output);
        minSeen.Should().BeGreaterThan(0, "US-28-06/§575.5: delete + create is one transaction; the catalog never has an empty window");
        var after = await DbAsync(db => db.Companies.AsNoTracking().Where(c => c.IsShowcase).OrderBy(c => c.Slug).Select(c => new { c.Id, c.Slug }).ToListAsync());
        after.Should().BeEquivalentTo(before, "external links and caches survive a re-seed: same ids and slugs");
        (await DbAsync(db => db.PlatformSettings.AnyAsync(s => s.Key == ShowcaseCatalog.LastReseedKey))).Should().BeTrue("the last (re)seed time is stamped for the weekly task");
    }

    [Fact, TestCase("CY28-34")]
    public async Task ReportBeforeExecution_PlanChangesNothing_WithoutYesNothingChanges_AndPlanNumbersEqualTheRealOnes()
    {
        await NormaliseAsync();

        var (planExit, plan) = await RunOpsAsync("showcase", "plan", "create");
        planExit.Should().Be(0, plan);
        plan.Should().Contain("будет создано:").And.Contain("только показать");
        (await DbAsync(db => db.Companies.CountAsync(c => c.IsShowcase))).Should().Be(0, "plan writes nothing");

        var (noYesExit, noYes) = await RunOpsAsync("showcase", "create");
        noYesExit.Should().Be(0);
        noYes.Should().Contain("только показать");
        (await DbAsync(db => db.Companies.CountAsync(c => c.IsShowcase))).Should().Be(0, "a changing command without --yes only prints the plan");

        var (createExit, createOut) = await CreateAsync();
        createExit.Should().Be(0);
        var created = CountsAfter.Match(createOut).Groups["c"].Value.Trim();
        created.Should().NotBeNullOrEmpty(createOut);

        var (recreatePlanExit, recreatePlan) = await RunOpsAsync("showcase", "plan", "recreate");
        recreatePlanExit.Should().Be(0, recreatePlan);
        var toDelete = Regex.Match(recreatePlan, @"будет удалено: (?<c>.+)").Groups["c"].Value.Trim();
        toDelete.Should().Be(created, "smoke 5 of the contract: «будет удалено» equals the real numbers of the creation report");

        var (deletePlanExit, deletePlan) = await RunOpsAsync("showcase", "plan", "delete");
        deletePlanExit.Should().Be(0, deletePlan);
        (await DbAsync(db => db.Companies.CountAsync(c => c.IsShowcase))).Should().Be(9, "plan delete removed nothing");
        deletePlan.Should().Contain("будет удалено:");
    }

    [Fact, TestCase("CY28-35")]
    public async Task ExitCodes_CreateWhenExists_Is2_DeleteWhenNothing_Is0_UnknownCommand_Is64()
    {
        await NormaliseAsync();
        (await RunOpsAsync("showcase", "delete", "--yes")).Exit.Should().Be(0, "nothing to delete is not an error");
        (await CreateAsync()).Exit.Should().Be(0);

        var (exit, output) = await CreateAsync();
        exit.Should().Be(2, "the showcase already exists");
        output.Should().Contain("recreate");
        (await DbAsync(db => db.Companies.CountAsync(c => c.IsShowcase))).Should().Be(9, "a refusal changes nothing");

        var unknown = await RunOpsAsync("showcase", "explode");
        unknown.Exit.Should().Be(64);
        unknown.Output.Should().Contain("Использование");

        (await RunOpsAsync("demo", "reset", "--yes")).Exit.Should().Be(2, "on a production configuration the demo reset refuses");
    }

    [Fact, TestCase("CY28-36")]
    public async Task ParallelRun_WhileAnotherHoldsTheLock_Exits4_AndChangesNothing()
    {
        await NormaliseAsync();
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand($"SELECT pg_advisory_xact_lock(hashtextextended('{ShowcaseCatalog.LockKey}', 0))", connection, transaction))
            await cmd.ExecuteNonQueryAsync();

        var (exit, output) = await CreateAsync();
        exit.Should().Be(4, output);
        (await DbAsync(db => db.Companies.CountAsync(c => c.IsShowcase))).Should().Be(0);

        await using (var tariffLock = new NpgsqlConnection(ConnectionString))
        {
            await tariffLock.OpenAsync();
            await using var tx2 = await tariffLock.BeginTransactionAsync();
            await using (var cmd = new NpgsqlCommand($"SELECT pg_advisory_xact_lock(hashtextextended('{ShowcaseCatalog.TariffsLockKey}', 0))", tariffLock, tx2))
                await cmd.ExecuteNonQueryAsync();
            (await RunOpsAsync("tariffs", "apply")).Exit.Should().Be(4, "the tariffs command has its own lock");
        }

        await transaction.RollbackAsync();
        (await DbAsync(db => db.Companies.CountAsync(c => c.IsShowcase))).Should().Be(0, "a refused run wrote nothing");
    }

    [Fact, TestCase("CY28-37")]
    public async Task Retention_FullRun_DoesNotFailOnShowcase_AndDoesNotDeleteIt()
    {
        await NormaliseAsync();
        (await CreateAsync()).Exit.Should().Be(0);
        await DbAsync(async db =>
        {
            // Make every showcase account "inactive for years": only the IsShowcase filter can save them from the inactive-account rule.
            await db.Database.ExecuteSqlRawAsync("UPDATE \"AspNetUsers\" SET \"CreatedAt\" = now() - interval '3000 days' WHERE \"IsShowcase\"");
        });
        var counts = async () => await DbAsync(async db => (
            Companies: await db.Companies.CountAsync(c => c.IsShowcase), Users: await db.Users.CountAsync(u => u.IsShowcase && u.DeletedAtUtc == null),
            Bookings: await db.Bookings.CountAsync(b => b.ShowcaseKind == ShowcaseBookingKind.Seeded), Events: await db.BookingEvents.CountAsync(e => e.Booking.Company.IsShowcase)));
        var before = await counts();

        await using var live = Factory.WithWebHostBuilder(b => b.UseSetting("ScheduledTasks:data-retention:DryRun", "false"));
        using (var scope = live.Services.CreateScope())
        {
            var outcome = await scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "data-retention").ExecuteAsync(CancellationToken.None);
            outcome.Should().NotBeNull("retention must not throw on showcase data");
        }

        (await counts()).Should().Be(before, "§577.6: retention neither fails on nor deletes the showcase");
        (await DbAsync(db => db.Users.CountAsync(u => u.IsShowcase && u.FirstName == ""))).Should().Be(0, "showcase users are not anonymised either");
    }
}
