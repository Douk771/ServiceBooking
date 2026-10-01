using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.API.Services.Showcase.Tariffs;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.3, §579.4, US-28-10 and US-28-13 — the demo profile of the generator: the three roles, the demo owner's public tariff, the client with
/// visits in several companies, reviews, notes and the richer change history. Pure: the graph is built in memory, no database.
/// </summary>
public class ShowcaseDemoProfileTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc);
    private static readonly Lazy<ShowcaseGraph> Demo = new(() => ShowcaseDataset.Build(ShowcaseProfile.Demo, Now));
    private static readonly Lazy<ShowcaseGraph> Prod = new(() => ShowcaseDataset.Build(ShowcaseProfile.Prod, Now));

    private static string Fingerprint(ShowcaseGraph g)
    {
        var sb = new StringBuilder();
        foreach (var u in g.Users) sb.Append(u.Id).Append(u.PhoneNumber).Append(';');
        foreach (var s in g.Subscriptions) sb.Append(s.Id).Append(s.PlanConfigId).Append(s.PaidUntil?.Ticks).Append(';');
        foreach (var b in g.Bookings) sb.Append(b.Id).Append(b.ClientId).Append(b.GuestPhone).Append(b.Status).Append(b.Date).Append(b.StartTime).Append(';');
        foreach (var e in g.BookingEvents) sb.Append(e.Id).Append(e.Kind).Append(e.ActorKind).Append(e.OccurredAtUtc.Ticks).Append(e.NewDate).Append(e.NewStartTime).Append(';');
        foreach (var r in g.Reviews) sb.Append(r.Id).Append(r.Rating).Append(r.Comment).Append(r.CreatedAt.Ticks).Append(';');
        foreach (var n in g.ClientNotes) sb.Append(n.Id).Append(n.Note).Append(n.CreatedAt.Ticks).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>The users of the salon part of the demo graph: the generator of shops (cycle 35) appends its people AFTER every salon user and takes its phones after
    /// the salon ones (ARCHITECTURE_CYCLE35.md §35.9.1), so the salon users are exactly the first <see cref="SalonUserCount"/> of the list.</summary>
    private const int SalonUserCount = 159;

    /// <summary>ARCHITECTURE_CYCLE35.md §35.9.5 — the salon part of the demo (companies of kind Services, their bookings, events, working hours, reviews, notes,
    /// subscriptions and the users who are not shop people), computed on commit a1e2259 BEFORE the shops generator existed. Never edited to make a test pass.</summary>
    private const string FrozenDemoSalonFingerprint = "B0BBF722A1288490F76478AE6E0E18E8021E361E52643B044DDEB675DC2B60CC";

    private static string SalonFingerprint(ShowcaseGraph g)
    {
        var sb = new StringBuilder();
        foreach (var u in g.Users.Take(SalonUserCount)) sb.Append(u.Id).Append(u.PhoneNumber).Append(u.FirstName).Append(u.LastName).Append(u.SecurityStamp).Append(u.CreatedAt.Ticks).Append(';');
        foreach (var c in g.Companies.Where(c => c.Kind == CompanyKind.Services)) sb.Append(c.Id).Append(c.Slug).Append(c.Name).Append(c.CreatedAt.Ticks).Append(c.ShowcaseBookingOpen).Append(';');
        foreach (var s in g.Subscriptions) sb.Append(s.Id).Append(s.PlanConfigId).Append(s.PaidUntil?.Ticks).Append(';');
        foreach (var b in g.Bookings) sb.Append(b.Id).Append(b.ClientId).Append(b.GuestPhone).Append(b.Status).Append(b.Date).Append(b.StartTime).Append(b.Price).Append(b.CreatedAt.Ticks).Append(';');
        foreach (var e in g.BookingEvents) sb.Append(e.Id).Append(e.Kind).Append(e.ActorKind).Append(e.OccurredAtUtc.Ticks).Append(e.NewDate).Append(e.NewStartTime).Append(';');
        foreach (var w in g.WorkingHours) sb.Append(w.Id).Append(w.Date).Append(w.IsWorking).Append(';');
        foreach (var r in g.Reviews) sb.Append(r.Id).Append(r.Rating).Append(r.Comment).Append(r.CreatedAt.Ticks).Append(';');
        foreach (var n in g.ClientNotes) sb.Append(n.Id).Append(n.Note).Append(n.CreatedAt.Ticks).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    [Fact]
    public void Demo_SalonPart_IsFrozenSinceCycle34_TheShopsGeneratorDoesNotMoveAByte() =>
        FrozenFingerprintCulture.Run(() => SalonFingerprint(Demo.Value)).Should().Be(FrozenDemoSalonFingerprint);

    // ── profile and determinism ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void DemoProfile_HasItsOwnNameAndTheRichFlags_ProdHasNone()
    {
        ShowcaseProfile.Demo.Name.Should().Be("demo");
        ShowcaseProfile.Demo.Should().Match<ShowcaseProfile>(p => p.DemoRoles && p.Reviews && p.ClientNotes && p.RichHistory);
        ShowcaseProfile.Prod.Should().Match<ShowcaseProfile>(p => !p.DemoRoles && !p.Reviews && !p.ClientNotes && !p.RichHistory && p.RescheduleChance == 0.05);
    }

    [Fact]
    public void Demo_TwoRunsWithTheSameArguments_GiveTheSameGraph() =>
        Fingerprint(ShowcaseDataset.Build(ShowcaseProfile.Demo, Now)).Should().Be(Fingerprint(Demo.Value));

    [Fact]
    public void Prod_IsUntouchedByTheDemoAdditions_NoReviewsNoNotesNoDemoClient()
    {
        Prod.Value.Reviews.Should().BeEmpty("D-4: there are no reviews on production");
        Prod.Value.ClientNotes.Should().BeEmpty();
        Prod.Value.Users.Should().NotContain(u => u.Id == ShowcaseDemoRoles.UserIdOf(ShowcaseDemoRoles.Client));
        Prod.Value.Subscriptions.Should().OnlyContain(s => s.PlanConfigId == ShowcaseCatalog.ShowcasePlanId && s.PaidUntil == null);
    }

    [Fact]
    public void Demo_SharesTheProductionCompaniesAndSlugs_ButNoId()
    {
        // Since cycle 35 the demo also has the five shops of «Заказы» (Kind = Orders): the SALON part is what shares the production companies.
        var salons = Demo.Value.Companies.Where(c => c.Kind == CompanyKind.Services).ToList();
        salons.Select(c => c.Slug).Should().Equal(Prod.Value.Companies.Select(c => c.Slug));
        Demo.Value.Companies.Select(c => c.Id).Should().NotIntersectWith(Prod.Value.Companies.Select(c => c.Id), "the profile name is part of every id");
        Demo.Value.Users.Take(SalonUserCount).Count().Should().Be(Prod.Value.Users.Count + 1, "the only extra salon account is the demo client");
        Demo.Value.Companies.Count(c => c.Kind == CompanyKind.Orders).Should().Be(5);
    }

    // ── the three roles ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Roles_AreKnown_AndOnlyTheseSix()
    {
        ShowcaseDemoRoles.All.Select(r => r.Role).Should().Equal("owner", "master", "client", "shop-owner", "shop-staff", "shop-customer");
        ShowcaseDemoRoles.IsKnown("owner").Should().BeTrue();
        ShowcaseDemoRoles.IsKnown("superadmin").Should().BeFalse();
        ShowcaseDemoRoles.IsKnown(null).Should().BeFalse();
        ShowcaseDemoRoles.IsKnown("Owner").Should().BeFalse("the wire names are lower case");
        ShowcaseDemoRoles.UserIdOf("superadmin").Should().BeNull();
    }

    [Fact]
    public void Roles_AreRealAccountsOfTheDemoGraph_WithTheRightRoles()
    {
        var g = Demo.Value;
        var ownerId = ShowcaseDemoRoles.UserIdOf("owner")!;
        var masterId = ShowcaseDemoRoles.UserIdOf("master")!;
        var clientId = ShowcaseDemoRoles.UserIdOf("client")!;

        new[] { ownerId, masterId, clientId }.Should().OnlyHaveUniqueItems();
        g.Users.Select(u => u.Id).Should().Contain([ownerId, masterId, clientId]);
        g.UserRoles.Should().Contain((ownerId, "CompanyOwner"));
        g.UserRoles.Should().Contain((masterId, "Master"));
        g.UserRoles.Should().Contain((clientId, "Client"));

        var flagship = g.Companies.Single(c => c.Slug == "primer-lavanda");
        flagship.OwnerUserId.Should().Be(ownerId);
        g.Members.Should().Contain(m => m.CompanyId == flagship.Id && m.UserId == masterId && m.Role == UserRole.Master);
        g.Users.Single(u => u.Id == clientId).Should().Match<AppUser>(u =>
            u.IsShowcase && u.PasswordHash == null && u.Email == null && ShowcasePhones.IsShowcasePhone(u.PhoneNumber));
    }

    [Fact]
    public void Roles_KeepTheirIdsAndSecurityStamps_AcrossDatesAndBuilds_SoAnOldTokenSurvivesTheNightlyReset()
    {
        var tomorrow = ShowcaseDataset.Build(ShowcaseProfile.Demo, Now.AddDays(1));

        foreach (var role in new[] { "owner", "master", "client" })
        {
            var id = ShowcaseDemoRoles.UserIdOf(role)!;
            var today = Demo.Value.Users.Single(u => u.Id == id);
            var later = tomorrow.Users.Single(u => u.Id == id);
            later.SecurityStamp.Should().Be(today.SecurityStamp);
            later.PhoneNumber.Should().Be(today.PhoneNumber);
        }
    }

    [Fact]
    public void DemoOwner_IsOnThePublicSalonTariff_PaidThirtyDaysAhead_OthersStayOnTheHiddenOne()
    {
        var g = Demo.Value;
        var owner = g.Users.Single(u => u.Id == ShowcaseDemoRoles.UserIdOf("owner"));
        var subscription = g.Subscriptions.Single(s => s.OwnerUserId == owner.Id);

        subscription.PlanConfigId.Should().Be(ZapisTariffCatalog.SalonId);
        subscription.IsActive.Should().BeTrue();
        var moscowMidnightUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Unspecified), TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"));
        subscription.PaidUntil.Should().Be(moscowMidnightUtc.AddDays(30));

        g.Subscriptions.Where(s => s.Id != subscription.Id).Should().OnlyContain(s => s.PlanConfigId == ShowcaseCatalog.ShowcasePlanId && s.PaidUntil == null);
        g.BillingAccounts.Should().OnlyContain(a => a.IsShowcase, "the demo account is still a showcase one: it never mixes with real accounts");
    }

    [Fact]
    public void FlagshipSalon_FitsTheSalonTariffLimits()
    {
        var g = Demo.Value;
        var flagship = g.Companies.Single(c => c.Slug == "primer-lavanda");

        g.Members.Count(m => m.CompanyId == flagship.Id).Should().BeLessThanOrEqualTo(ZapisTariffCatalog.Salon.MaxEmployees!.Value);
        g.Companies.Count(c => c.BillingAccountId == flagship.BillingAccountId).Should().BeLessThanOrEqualTo(ZapisTariffCatalog.Salon.MaxCompanies!.Value);
    }

    // ── the demo client's visits ────────────────────────────────────────────────────────────────────

    [Fact]
    public void DemoClient_HasPastAndFutureVisitsInSeveralCompanies_TakenFromExistingSlots()
    {
        var g = Demo.Value;
        var clientId = ShowcaseDemoRoles.UserIdOf("client")!;
        var mine = g.Bookings.Where(b => b.ClientId == clientId).ToList();
        var today = new DateOnly(2026, 10, 1); // all three companies are in Moscow time

        mine.Select(b => b.CompanyId).Distinct().Count().Should().Be(ShowcaseDemoRoles.ClientCompanyKeys.Count);
        foreach (var companyId in mine.Select(b => b.CompanyId).Distinct())
        {
            var inCompany = mine.Where(b => b.CompanyId == companyId).ToList();
            inCompany.Count(b => b.Status == BookingStatus.Completed && b.Date < today).Should().BeGreaterThanOrEqualTo(3);
            inCompany.Count(b => b.Status == BookingStatus.Confirmed && b.Date > today).Should().BeGreaterThanOrEqualTo(1);
        }
        mine.Should().OnlyContain(b => b.GuestName == null && b.GuestPhone == null && b.ShowcaseKind == ShowcaseBookingKind.Seeded);
        mine.Should().OnlyContain(b => b.Status == BookingStatus.Completed || b.Status == BookingStatus.Confirmed);

        // The "created" event says the client made the booking himself.
        var created = g.BookingEvents.Where(e => e.Kind == BookingEventKind.Created && mine.Select(b => b.Id).Contains(e.BookingId)).ToList();
        created.Should().HaveCount(mine.Count);
        created.Should().OnlyContain(e => e.ActorKind == BookingActorKind.Client && e.ActorUserId == clientId && e.ActorRoleSnapshot == UserRole.Client);

        // No slot is booked twice: re-assigning must not have created a second booking anywhere.
        g.Bookings.GroupBy(b => (b.MasterId, b.Date, b.StartTime)).Should().OnlyContain(x => x.Count() == 1);
        g.Bookings.Should().OnlyContain(b => b.ClientId != null || !string.IsNullOrEmpty(b.GuestPhone));
    }

    [Fact]
    public void DemoClient_KeepsAtLeastOneCompletedVisitWithoutAReview_ToTryLeavingOne()
    {
        var g = Demo.Value;
        var clientId = ShowcaseDemoRoles.UserIdOf("client")!;
        var reviewed = g.Reviews.Select(r => r.BookingId).ToHashSet();

        g.Bookings.Where(b => b.ClientId == clientId && b.Status == BookingStatus.Completed).Should().Contain(b => !reviewed.Contains(b.Id));
    }

    [Fact]
    public void DemoMaster_HasBookingsAndClientNotes()
    {
        var g = Demo.Value;
        var masterId = ShowcaseDemoRoles.UserIdOf("master")!;

        g.Bookings.Count(b => b.MasterId == masterId).Should().BeGreaterThan(100);
        g.ClientNotes.Count(n => n.MasterId == masterId).Should().BeGreaterThan(5, "his 'Клиенты' screen is not empty");
    }

    // ── reviews ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Reviews_AreOnCompletedVisitsOfRegisteredClients_OnePerBooking_InsideTheRatingRange()
    {
        var g = Demo.Value;
        var bookings = g.Bookings.ToDictionary(b => b.Id);

        g.Reviews.Should().NotBeEmpty();
        g.Reviews.Select(r => r.BookingId).Should().OnlyHaveUniqueItems("the unique index on Reviews.BookingId");
        g.Reviews.Select(r => r.Id).Should().OnlyHaveUniqueItems();
        foreach (var review in g.Reviews)
        {
            var booking = bookings[review.BookingId];
            booking.Status.Should().Be(BookingStatus.Completed);
            review.ClientId.Should().Be(booking.ClientId).And.NotBeNull("only a registered client's own booking can be reviewed");
            review.CompanyId.Should().Be(booking.CompanyId);
            review.MasterId.Should().Be(booking.MasterId);
            review.Rating.Should().BeInRange(1, 5);
            review.ReviewerName.Should().NotBeNullOrWhiteSpace();
            review.CreatedAt.Should().BeOnOrBefore(Now).And.BeAfter(booking.CreatedAt);
        }
    }

    [Fact]
    public void Reviews_AreAPlausibleShare_WithAGoodButNotPerfectAverage_InEveryCompany()
    {
        var g = Demo.Value;
        var candidates = g.Bookings.Count(b => b.Status == BookingStatus.Completed && b.ClientId != null);

        ((double)g.Reviews.Count / candidates).Should().BeInRange(0.2, 0.4);
        g.Reviews.Average(r => r.Rating).Should().BeInRange(4.0, 4.8);
        g.Reviews.Select(r => r.Rating).Distinct().Should().BeEquivalentTo(new[] { 1, 2, 3, 4, 5 }, "a real list has every grade");
        foreach (var company in g.Companies.Where(c => c.Kind == CompanyKind.Services))
            g.Reviews.Count(r => r.CompanyId == company.Id).Should().BeGreaterThan(0, $"{company.Slug} shows a rating");
    }

    // ── client notes ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ClientNotes_AreNeutral_WrittenByTheVisitsMaster_AboutARegisteredClientOrAGuestPhone()
    {
        var g = Demo.Value;
        var bookings = g.Bookings.ToDictionary(b => b.Id);
        var health = new[] { "аллерг", "здоров", "диагноз", "лекарств", "беремен", "болезн", "давлен", "диабет", "заболев" };

        g.ClientNotes.Should().NotBeEmpty();
        g.ClientNotes.Select(n => n.Id).Should().OnlyHaveUniqueItems();
        foreach (var note in g.ClientNotes)
        {
            var booking = bookings[note.BookingId!.Value];
            booking.Status.Should().Be(BookingStatus.Completed);
            note.MasterId.Should().Be(booking.MasterId);
            note.CompanyId.Should().Be(booking.CompanyId);
            (note.ClientId is not null ^ note.GuestPhone is not null).Should().BeTrue("a note is about a registered client OR a guest phone, never both");
            note.Note.Should().NotBeNullOrWhiteSpace();
            health.Should().NotContain(w => note.Note.Contains(w, StringComparison.OrdinalIgnoreCase), "US-28-04: no health data in the demo");
            if (note.GuestPhone is not null) ShowcasePhones.IsShowcasePhone(note.GuestPhone).Should().BeTrue();
        }
        g.ClientNotes.Count.Should().BeInRange(100, 2500);
    }

    [Fact]
    public void DemoGraph_HasNoHealthNotesNoPhotosNoConsentRows()
    {
        // The graph has no slot for them at all: the lists simply do not exist (US-28-04 holds for the demo too). The one thing to check is that the notes carry no photos.
        Demo.Value.ClientNotes.Should().OnlyContain(n => n.Photos.Count == 0);
    }

    // ── change history ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void History_IsRicherThanOnProduction_MoreMoves_SomeTwice()
    {
        var demo = Demo.Value;
        var prod = Prod.Value;

        double Share(ShowcaseGraph g) => (double)g.BookingEvents.Count(e => e.Kind == BookingEventKind.Rescheduled) / g.Bookings.Count;

        Share(prod).Should().BeInRange(0.03, 0.06, "production: one booking in twenty");
        Share(demo).Should().BeGreaterThan(Share(prod) * 2);
        demo.BookingEvents.Where(e => e.Kind == BookingEventKind.Rescheduled).GroupBy(e => e.BookingId).Count(x => x.Count() == 2).Should().BeGreaterThan(20);
        prod.BookingEvents.Where(e => e.Kind == BookingEventKind.Rescheduled).GroupBy(e => e.BookingId).Should().OnlyContain(x => x.Count() == 1);
    }

    [Fact]
    public void History_MovesFormAChain_EndingAtTheBookingsFinalSlot_AndNoMoveMovesNothing()
    {
        var g = Demo.Value;
        var bookings = g.Bookings.ToDictionary(b => b.Id);

        foreach (var chain in g.BookingEvents.Where(e => e.Kind == BookingEventKind.Rescheduled).GroupBy(e => e.BookingId))
        {
            var moves = chain.OrderBy(e => e.OccurredAtUtc).ToList();
            var booking = bookings[chain.Key];
            for (var i = 0; i < moves.Count; i++)
            {
                (moves[i].PreviousDate, moves[i].PreviousStartTime).Should().NotBe((moves[i].NewDate, moves[i].NewStartTime), "a move that moves nothing is a lie in the journal");
                if (i > 0) (moves[i].PreviousDate, moves[i].PreviousStartTime).Should().Be((moves[i - 1].NewDate, moves[i - 1].NewStartTime));
            }
            (moves[^1].NewDate, moves[^1].NewStartTime).Should().Be((booking.Date, booking.StartTime));
            moves[0].OccurredAtUtc.Should().BeAfter(booking.CreatedAt);
        }
    }

    [Fact]
    public void History_EveryBookingStillHasExactlyOneCreatedEvent_AndTheUpdatedAtMatchesTheLastEvent()
    {
        var g = Demo.Value;

        g.BookingEvents.Where(e => e.Kind == BookingEventKind.Created).GroupBy(e => e.BookingId).Should().OnlyContain(x => x.Count() == 1);
        g.BookingEvents.Select(e => e.BookingId).Distinct().Count().Should().Be(g.Bookings.Count);
        g.BookingEvents.Select(e => e.Id).Should().OnlyHaveUniqueItems();
        foreach (var group in g.BookingEvents.GroupBy(e => e.BookingId).Take(500))
            g.Bookings.First(b => b.Id == group.Key).UpdatedAt.Should().Be(group.Max(e => e.OccurredAtUtc));
    }
}
