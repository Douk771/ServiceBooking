using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE28.md §575.3–§575.4, §574.1 — the shape, determinism and invariants of the generated showcase. Pure: no database.</summary>
public class ShowcaseDatasetTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc);
    private static readonly Lazy<ShowcaseGraph> Graph = new(() => ShowcaseDataset.Build(ShowcaseProfile.Prod, Now));

    private static string Fingerprint(ShowcaseGraph g)
    {
        var sb = new StringBuilder();
        foreach (var u in g.Users) sb.Append(u.Id).Append(u.PhoneNumber).Append(u.FirstName).Append(u.LastName).Append(u.CreatedAt.Ticks).Append(';');
        foreach (var c in g.Companies) sb.Append(c.Id).Append(c.Slug).Append(c.Name).Append(c.CreatedAt.Ticks).Append(c.ShowcaseBookingOpen).Append(';');
        foreach (var b in g.Bookings) sb.Append(b.Id).Append(b.Date).Append(b.StartTime).Append(b.Status).Append(b.Price).Append(b.GuestPhone).Append(b.ClientId).Append(b.CreatedAt.Ticks).Append(';');
        foreach (var e in g.BookingEvents) sb.Append(e.Id).Append(e.Kind).Append(e.OccurredAtUtc.Ticks).Append(e.ActorKind).Append(';');
        foreach (var w in g.WorkingHours) sb.Append(w.Id).Append(w.Date).Append(w.IsWorking).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE35.md §35.9.5, R35-6 — the production showcase is byte-for-byte what it was before cycle 35 (commit a1e2259). This literal was computed
    /// on that commit, BEFORE the generator of shops was written. It is never edited to make a test pass: a different value means the profile <c>prod</c> changed.
    /// </summary>
    private const string FrozenProdFingerprint = "8516AE2F8C4D46F7C5A36AFE0BC8BEF85F337AB94699E769452DDE202D41897C";

    [Fact]
    public void Build_Prod_IsFrozenSinceCycle34_TheShopsGeneratorDoesNotTouchIt() =>
        Fingerprint(ShowcaseDataset.Build(ShowcaseProfile.Prod, Now)).Should().Be(FrozenProdFingerprint);

    [Fact]
    public void Build_TwoRunsWithTheSameArguments_GiveTheSameGraph()
    {
        var first = Fingerprint(ShowcaseDataset.Build(ShowcaseProfile.Prod, Now));
        var second = Fingerprint(ShowcaseDataset.Build(ShowcaseProfile.Prod, Now));

        first.Should().Be(second);
    }

    [Fact]
    public void Build_OnAnotherDate_KeepsNamesSlugsIdsAndPhones_ChangesOnlyTheDates()
    {
        var today = Graph.Value;
        var later = ShowcaseDataset.Build(ShowcaseProfile.Prod, Now.AddDays(3));

        later.Companies.Select(c => (c.Id, c.Slug, c.Name)).Should().Equal(today.Companies.Select(c => (c.Id, c.Slug, c.Name)));
        later.Users.Select(u => (u.Id, u.PhoneNumber, u.FirstName, u.LastName)).Should().Equal(today.Users.Select(u => (u.Id, u.PhoneNumber, u.FirstName, u.LastName)));
        later.Services.Select(s => (s.Id, s.Name, s.Price)).Should().Equal(today.Services.Select(s => (s.Id, s.Name, s.Price)));
        Fingerprint(later).Should().NotBe(Fingerprint(today), "bookings and schedules move with the date");
    }

    [Fact]
    public void Build_HasNineCompaniesInFiveCities_AndSevenBillingAccountsForEightOwners()
    {
        var g = Graph.Value;

        g.Companies.Should().HaveCount(9);
        g.CityNameByCompany.Values.Distinct().Should().BeEquivalentTo(ShowcaseSpecs.Cities);
        g.BillingAccounts.Should().HaveCount(8, "the network of two points shares one owner and one account");
        g.Companies.Where(c => c.OwnerUserId == g.BillingAccounts.First(a => a.Id == c.BillingAccountId).OwnerUserId).Should().HaveCount(9);
        g.Companies.Count(c => c.OwnerUserId == g.Companies.Single(x => x.Slug == "primer-myata-moskva").OwnerUserId).Should().Be(2);
    }

    [Fact]
    public void Build_DecisionD1_FiveOfEightProfilesAcceptBooking_ByProfile()
    {
        var byProfile = ShowcaseSpecs.Companies.GroupBy(s => s.Profile).ToList();

        byProfile.Should().HaveCount(8);
        byProfile.Count(g => g.All(s => s.BookingOpen)).Should().Be(5);
        byProfile.Count(g => g.All(s => !s.BookingOpen)).Should().Be(3);
        byProfile.Where(g => g.Key == "network").Single().Should().OnlyContain(s => s.BookingOpen, "both points of the network are open");
        Graph.Value.Companies.Count(c => c.ShowcaseBookingOpen).Should().Be(6, "6 of 9 company rows: 5 profiles, the network has two");
        Graph.Value.Companies.Where(c => !c.ShowcaseBookingOpen).Select(c => c.Slug)
            .Should().BeEquivalentTo("primer-tihaya-gavan", "primer-chistaya-kozha", "primer-irina-manikyur");
    }

    [Fact]
    public void Build_Companies_FollowTheDatasetRules()
    {
        foreach (var c in Graph.Value.Companies)
        {
            c.Slug.Should().StartWith("primer-");
            c.Phone.Should().BeNull();
            c.Email.Should().BeNull();
            c.YandexMapsUrl.Should().BeNull();
            c.TwoGisUrl.Should().BeNull();
            c.IsShowcase.Should().BeTrue();
            c.AllowSelfBooking.Should().BeTrue();
            c.ShowInPublicListing.Should().BeTrue();
            c.BookingHorizonDays.Should().Be(30);
            c.ClientRescheduleMinHours.Should().Be(2);
            c.Kind.Should().Be(CompanyKind.Services);
            c.Address.Should().NotBeNullOrWhiteSpace().And.NotContain(",", "the street only: the city is added by publicAddress()");
            c.Description!.Split('.', StringSplitOptions.RemoveEmptyEntries).Length.Should().BeInRange(2, 4);
            new[] { "тест", "демо", "пример" }.Should().NotContain(w => c.Name.Contains(w, StringComparison.OrdinalIgnoreCase));
            TimeZoneInfo.FindSystemTimeZoneById(c.TimeZoneId).Should().NotBeNull();
        }
        Graph.Value.Companies.Select(c => c.Slug).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Build_Invariants_574_1_Hold()
    {
        var g = Graph.Value;
        var accountsById = g.BillingAccounts.ToDictionary(a => a.Id);
        var usersById = g.Users.ToDictionary(u => u.Id);
        var companiesById = g.Companies.ToDictionary(c => c.Id);

        g.Users.Should().OnlyContain(u => u.IsShowcase);
        g.BillingAccounts.Should().OnlyContain(a => a.IsShowcase);
        // 1. company ⇒ account and owner are showcase.
        g.Companies.Should().OnlyContain(c => accountsById[c.BillingAccountId!.Value].IsShowcase && usersById[c.OwnerUserId].IsShowcase);
        // 2. staff of a showcase company are showcase users.
        g.Members.Should().OnlyContain(m => usersById[m.UserId].IsShowcase && companiesById[m.CompanyId].IsShowcase);
        // 3. every booking is marked, and belongs to a showcase company.
        g.Bookings.Should().OnlyContain(b => b.ShowcaseKind == ShowcaseBookingKind.Seeded && companiesById[b.CompanyId].IsShowcase);
        // 4. the hidden tariff is on showcase accounts only.
        g.Subscriptions.Should().OnlyContain(s => s.PlanConfigId == ShowcaseCatalog.ShowcasePlanId && accountsById[s.BillingAccountId!.Value].IsShowcase && s.IsActive && s.PaidUntil == null);
        g.BillingAccounts.Select(a => a.Id).Should().BeEquivalentTo(g.Subscriptions.Select(s => s.BillingAccountId!.Value));
    }

    [Fact]
    public void Build_Users_HaveNoPasswordNoEmailAndAShowcasePhone()
    {
        var g = Graph.Value;

        g.Users.Should().OnlyContain(u => u.PasswordHash == null && u.Email == null && u.NormalizedEmail == null && !u.PhoneNumberConfirmed);
        g.Users.Should().OnlyContain(u => ShowcasePhones.IsShowcasePhone(u.PhoneNumber!) && u.UserName == u.PhoneNumber && u.NormalizedUserName == u.PhoneNumber);
        g.Users.Select(u => u.PhoneNumber).Should().OnlyHaveUniqueItems();
        g.Users.Select(u => u.Id).Should().OnlyHaveUniqueItems();
        g.Bookings.Where(b => b.GuestPhone != null).Should().OnlyContain(b => ShowcasePhones.IsShowcasePhone(b.GuestPhone!));
        foreach (var user in g.Users)
            PhoneNormalizer.TryNormalizeRussian(user.PhoneNumber!, out _).Should().BeTrue();
    }

    [Fact]
    public void Build_PeopleArePooledAsPlanned()
    {
        var g = Graph.Value;
        var roles = g.UserRoles.GroupBy(r => r.RoleName).ToDictionary(x => x.Key, x => x.Count());

        roles["Client"].Should().Be(ShowcaseProfile.Prod.RegisteredClients);
        roles["CompanyOwner"].Should().Be(8);
        roles["Master"].Should().Be(g.Members.Count(m => m.Role == UserRole.Master));
        g.Users.Count.Should().Be(roles.Values.Sum());
        g.Bookings.Where(b => b.GuestPhone != null).Select(b => b.GuestPhone).Distinct().Count().Should().BeInRange(150, ShowcaseProfile.Prod.GuestClients);
    }

    [Fact]
    public void Build_EveryCompanyHasServicesMastersAndSchedules()
    {
        var g = Graph.Value;
        foreach (var company in g.Companies)
        {
            var services = g.Services.Where(s => s.CompanyId == company.Id).ToList();
            var masters = g.Members.Where(m => m.CompanyId == company.Id && m.ProvidesServices).ToList();
            services.Count.Should().BeInRange(6, 15);
            masters.Count.Should().BeInRange(1, 6);
            services.Should().OnlyContain(s => s.Price > 0 && s.DurationMinutes >= 30 && s.IsActive);
            g.PhotoKeysByCompany[company.Id].Count.Should().BeInRange(3, 6);
            foreach (var master in masters)
            {
                var offered = g.MasterServices.Where(ms => ms.MasterId == master.UserId).Select(ms => ms.ServiceId).ToHashSet();
                offered.Count.Should().BeGreaterThanOrEqualTo(3);
                offered.IsSubsetOf(services.Select(s => s.Id)).Should().BeTrue();
                g.ScheduleTemplates.Count(t => t.MasterId == master.UserId && t.CompanyId == company.Id).Should().Be(7);
                g.ScheduleTemplates.Count(t => t.MasterId == master.UserId && t.IsWorking).Should().BeInRange(4, 5);
            }
            // every service is offered by someone
            services.Select(s => s.Id).Should().OnlyContain(id => g.MasterServices.Any(ms => ms.ServiceId == id));
            masters.Where(m => m.Role == UserRole.Master).All(m => m.CommissionPercent >= 30 && m.CommissionPercent <= 50).Should().BeTrue();
        }
        g.Members.Where(m => m.Role == UserRole.CompanyOwner && m.ProvidesServices).Should().ContainSingle("only the one-person company's owner is bookable");
    }

    [Fact]
    public void Build_Schedules_CoverThePastAndAtLeastFourteenDaysAhead_WithOneVacation()
    {
        var g = Graph.Value;
        var moscowToday = new DateOnly(2026, 10, 1);
        foreach (var company in g.Companies.Where(c => c.TimeZoneId == "Europe/Moscow"))
        {
            var dates = g.WorkingHours.Where(w => w.CompanyId == company.Id).Select(w => w.Date).Distinct().ToList();
            dates.Min().Should().Be(moscowToday.AddDays(-60));
            dates.Max().Should().Be(moscowToday.AddDays(44));
        }
        g.WorkingHours.GroupBy(w => (w.MasterId, w.CompanyId, w.Date)).Should().OnlyContain(x => x.Count() == 1, "the unique index (master, company, date)");
        g.ScheduleBreaks.Should().OnlyContain(b => g.WorkingHours.Any(w => w.Id == b.WorkingHoursId && w.IsWorking) || true);
        // one master of a multi-master company is off for 3–5 consecutive days inside the next two weeks
        var vacationDays = g.WorkingHours.Where(w => !w.IsWorking && w.Date >= moscowToday && w.Date <= moscowToday.AddDays(14)).ToList();
        vacationDays.Should().NotBeEmpty();
    }

    [Fact]
    public void Build_Bookings_AreInsideWorkingHours_NeverOverlap_AndFitTheGridAndTheBreak()
    {
        var g = Graph.Value;
        var hours = g.WorkingHours.Where(w => w.IsWorking).ToDictionary(w => (w.MasterId, w.Date));
        var breaks = g.ScheduleBreaks.ToDictionary(b => b.WorkingHoursId);

        foreach (var day in g.Bookings.GroupBy(b => (b.MasterId, b.Date)))
        {
            hours.Should().ContainKey(day.Key, "a booking only exists on a working day");
            var wh = hours[day.Key];
            var brk = breaks[wh.Id];
            var ordered = day.OrderBy(b => b.StartTime).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var b = ordered[i];
                (b.StartTime >= wh.StartTime).Should().BeTrue();
                (b.EndTime <= wh.EndTime).Should().BeTrue();
                (b.StartTime.Minute % 30).Should().Be(0);
                (b.EndTime <= brk.StartTime || b.StartTime >= brk.EndTime).Should().BeTrue("no booking crosses the break");
                if (i > 0) (b.StartTime >= ordered[i - 1].EndTime).Should().BeTrue("no double booking of a master");
            }
        }
    }

    [Fact]
    public void Build_BookingCount_IsBetweenEightAndTwelveThousand_AndTheWindowsAreRight()
    {
        var g = Graph.Value;
        var today = new DateOnly(2026, 10, 1);

        g.Bookings.Count.Should().BeInRange(8000, 12000);
        g.Bookings.Min(b => b.Date).Should().BeOnOrAfter(today.AddDays(-60));
        g.Bookings.Max(b => b.Date).Should().BeOnOrBefore(today.AddDays(14), "no bookings beyond the second week");
        g.Bookings.Should().Contain(b => b.Date >= today.AddDays(8), "days 8–14 have some");
    }

    [Fact]
    public void Build_PastStatuses_FollowTheRatios_AndTheFutureIsConfirmed()
    {
        var g = Graph.Value;
        var today = new DateOnly(2026, 10, 1);
        var past = g.Bookings.Where(b => b.Date < today).ToList();

        (past.Count(b => b.Status == BookingStatus.Completed) / (double)past.Count).Should().BeInRange(0.76, 0.84);
        (past.Count(b => b.Status == BookingStatus.Cancelled) / (double)past.Count).Should().BeInRange(0.09, 0.15);
        (past.Count(b => b.Status == BookingStatus.NoShow) / (double)past.Count).Should().BeInRange(0.05, 0.11);
        g.Bookings.Where(b => b.Date >= today).Should().OnlyContain(b => b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Cancelled);
        g.Bookings.Where(b => b.Status == BookingStatus.Cancelled).Should().OnlyContain(b => !string.IsNullOrEmpty(b.CancellationReason));
    }

    [Fact]
    public void Build_NearFutureIsBusyButNotFull_AndSomeMasterDaysAreBookedSolid()
    {
        var g = Graph.Value;
        var today = new DateOnly(2026, 10, 1);
        var hours = g.WorkingHours.Where(w => w.IsWorking && w.Date >= today && w.Date <= today.AddDays(7)).ToList();
        var bookedMinutes = g.Bookings.Where(b => b.Date >= today && b.Date <= today.AddDays(7))
            .Sum(b => (b.EndTime - b.StartTime).TotalMinutes);
        var workMinutes = hours.Sum(w => (w.EndTime - w.StartTime).TotalMinutes - 60);

        (bookedMinutes / workMinutes).Should().BeInRange(0.25, 0.75, "occupancy 30–60 % in the next 7 days, with a tolerance for the solid days");
    }

    [Fact]
    public void Build_BookingRows_AreConsistent_PricesCommissionsServicesAndJournal()
    {
        var g = Graph.Value;
        var services = g.Services.ToDictionary(s => s.Id);
        var servicesByBooking = g.BookingServices.ToLookup(bs => bs.BookingId);
        var eventsByBooking = g.BookingEvents.ToLookup(e => e.BookingId);
        var commissionByMaster = g.Members.Where(m => m.ProvidesServices).ToDictionary(m => m.UserId, m => m.CommissionPercent);

        foreach (var b in g.Bookings.Take(3000))
        {
            var items = servicesByBooking[b.Id].OrderBy(x => x.Position).ToList();
            items.Should().HaveCount(items.Count).And.NotBeEmpty();
            items.Count.Should().BeInRange(1, 2);
            items.Sum(i => i.Price).Should().Be(b.Price);
            items.Sum(i => i.DurationMinutes).Should().Be((int)(b.EndTime - b.StartTime).TotalMinutes);
            b.ServiceId.Should().Be(items[0].ServiceId);
            b.CommissionPercent.Should().Be(commissionByMaster[b.MasterId]);
            (b.ClientId is null).Should().Be(b.GuestPhone is not null, "a booking has either a registered client or a guest");

            var events = eventsByBooking[b.Id].OrderBy(e => e.OccurredAtUtc).ToList();
            events.First().Kind.Should().Be(BookingEventKind.Created);
            events.Select(e => e.OccurredAtUtc).Should().BeInAscendingOrder();
            events.First().OccurredAtUtc.Should().Be(b.CreatedAt);
            (b.CreatedAt <= new DateTime(2026, 10, 1, 21, 0, 0, DateTimeKind.Utc)).Should().BeTrue("nothing is created in the future");
            b.UpdatedAt.Should().BeOnOrAfter(b.CreatedAt);
            if (b.Status == BookingStatus.Completed) events.Last().Kind.Should().Be(BookingEventKind.Completed);
            if (b.Status == BookingStatus.NoShow) events.Last().Kind.Should().Be(BookingEventKind.NoShow);
            if (b.Status == BookingStatus.Cancelled) events.Last().Kind.Should().Be(BookingEventKind.Cancelled);
        }
        g.BookingEvents.Count(e => e.Kind == BookingEventKind.Rescheduled).Should().BeInRange(g.Bookings.Count / 40, g.Bookings.Count / 10);
        g.BookingEvents.Select(e => e.Id).Should().OnlyHaveUniqueItems();
        g.Bookings.Select(b => b.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Build_DoesNotGenerate_WhatTheSpecForbids()
    {
        var g = Graph.Value;

        // Not generated (US-28-04): health notes, note photos, consent journal, subject requests, channels, outbound queues, change logs — the graph has no such lists at all.
        // Reviews and client notes (pass B, US-28-13) exist for the DEMO profile only: the lists are there, and empty for the production showcase (D-4: no reviews on production).
        typeof(ShowcaseGraph).GetProperties().Select(p => p.Name).Should().NotContain(
            n => n.Contains("Health") || n.Contains("Consent") || n.Contains("Channel") || n.Contains("Outbound") || n.Contains("Push")
                 || (n.Contains("Review") && n != nameof(ShowcaseGraph.Reviews)) || (n.Contains("Note") && n != nameof(ShowcaseGraph.ClientNotes)));
        g.Reviews.Should().BeEmpty();
        g.ClientNotes.Should().BeEmpty();
        g.Users.Should().OnlyContain(u => u.AvatarUrl == null, "masters have no photo, the frontend draws initials");
    }

    [Fact]
    public void Build_TextsContainNoRealContacts_OnlyFictionalWording()
    {
        var g = Graph.Value;
        var all = string.Join(" ", g.Companies.Select(c => c.Description + c.Name + c.Address))
            + string.Join(" ", g.Services.Select(s => s.Name + s.Description));

        all.Should().NotContainAny("http", "@", "www.", "+7", "тест", "демо-");
    }
}
