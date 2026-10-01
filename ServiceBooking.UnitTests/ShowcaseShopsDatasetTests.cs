using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE35.md §35.9 — the shops of the demo: shape, determinism and the invariants the product relies on (numbers, counters, usage, stock, journal ↔ status,
/// revision). Pure: the graph is built in memory, no database. The salon part and the production showcase are frozen by <c>ShowcaseDatasetTests</c> and
/// <c>ShowcaseDemoProfileTests</c>.
/// </summary>
public class ShowcaseShopsDatasetTests
{
    // 04:00 at Moscow — the nightly reset — and 12:30 — a manual reset before a meeting.
    private static readonly DateTime Night = new(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Noon = new(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc);

    private static readonly Lazy<ShowcaseGraph> AtNight = new(() => ShowcaseDataset.Build(ShowcaseProfile.Demo, Night));
    private static readonly Lazy<ShowcaseGraph> AtNoon = new(() => ShowcaseDataset.Build(ShowcaseProfile.Demo, Noon));

    private static IEnumerable<Company> Shops(ShowcaseGraph g) => g.Companies.Where(c => c.Kind == CompanyKind.Orders);

    private static Company Shop(ShowcaseGraph g, string key) => g.Companies.Single(c => c.Slug == $"primer-{key}");

    private static string Fingerprint(ShowcaseGraph g)
    {
        var sb = new StringBuilder();
        foreach (var c in Shops(g)) sb.Append(c.Id).Append(c.Slug).Append(c.Name).Append(c.Phone).Append(';');
        foreach (var p in g.Products) sb.Append(p.Id).Append(p.Name).Append(p.Price).Append(p.StockOnHand).Append(';');
        foreach (var o in g.Orders.OrderBy(o => o.Id))
            sb.Append(o.Id).Append(o.Number).Append(o.PickupStartUtc.Ticks).Append(o.Status).Append(o.CustomerPhone).Append(o.EstimatedTotal).Append(o.FinalTotal).Append(o.PublicToken).Append(';');
        foreach (var e in g.OrderEvents.OrderBy(e => e.Id)) sb.Append(e.Id).Append(e.Kind).Append(e.OccurredAtUtc.Ticks).Append(e.ActorUserId).Append(';');
        foreach (var n in g.ShopCustomerNotes.OrderBy(n => n.Id)) sb.Append(n.Id).Append(n.Text).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    // ── Profile flag ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OnlyTheDemoProfileHasShops_TheProductionShowcaseHasNone()
    {
        ShowcaseProfile.Demo.Shops.Should().BeTrue();
        ShowcaseProfile.Prod.Shops.Should().BeFalse();
        ShowcaseDataset.Build(ShowcaseProfile.Prod, Night).HasShops.Should().BeFalse("ops showcase create on production never creates the hidden tariff of Orders");
    }

    [Fact]
    public void TheDemoGraphHasTheFiveShops_InTheirCities_WithTheirOwnOwnersAccountsAndTariff()
    {
        var g = AtNight.Value;

        g.HasShops.Should().BeTrue();
        Shops(g).Select(c => c.Slug).Should().Equal("primer-kofeinya", "primer-pekarnya", "primer-stolovaya", "primer-cvety", "primer-fermerskaya");
        Shops(g).Should().OnlyContain(c => c.IsShowcase && c.IsActive && c.ShowInPublicListing && !c.AllowSelfBooking && c.Email == null && c.Slug.StartsWith(ShowcaseCatalog.SlugPrefix));
        g.CityNameByCompany[Shop(g, "kofeinya").Id].Should().Be("Москва");
        g.CityNameByCompany[Shop(g, "pekarnya").Id].Should().Be("Москва");
        g.CityNameByCompany[Shop(g, "stolovaya").Id].Should().Be("Санкт-Петербург");
        g.CityNameByCompany[Shop(g, "cvety").Id].Should().Be("Казань");
        g.CityNameByCompany[Shop(g, "fermerskaya").Id].Should().Be("Новосибирск");
        Shop(g, "fermerskaya").TimeZoneId.Should().Be("Asia/Novosibirsk");

        // one account, owner and subscription of the hidden demo tariff per shop
        foreach (var shop in Shops(g))
        {
            var account = g.BillingAccounts.Single(a => a.Id == shop.BillingAccountId);
            account.IsShowcase.Should().BeTrue();
            account.OwnerUserId.Should().Be(shop.OwnerUserId);
            g.OrdersSubscriptions.Single(s => s.BillingAccountId == account.Id).Should()
                .Match<OrdersSubscription>(s => s.PlanConfigId == ShowcaseCatalog.OrdersShowcasePlanId && s.PaidUntil == null && s.IsActive);
        }
        g.BillingAccounts.Select(a => a.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryShopPhoneAndEveryPersonPhoneIsInTheShowcaseBlock_AndNeverRepeats()
    {
        var g = AtNight.Value;
        var shopPhones = Shops(g).Select(c => new string(c.Phone!.Where(char.IsDigit).ToArray())).ToList();
        shopPhones.Should().OnlyContain(p => ShowcasePhones.IsShowcasePhone(p));
        Shops(g).Should().OnlyContain(c => c.Phone!.StartsWith("+7 (200) 555-"), "the display form of the block");

        var all = g.Users.Select(u => u.PhoneNumber!).Concat(shopPhones).Concat(g.Orders.Where(o => o.CustomerUserId == null).Select(o => o.CustomerPhone!)).ToList();
        all.Should().OnlyContain(p => ShowcasePhones.IsShowcasePhone(p));
        g.Users.Select(u => u.PhoneNumber).Should().OnlyHaveUniqueItems();
        shopPhones.Intersect(g.Users.Select(u => u.PhoneNumber!)).Should().BeEmpty();
    }

    // ── Determinism ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoBuildsWithTheSameArguments_GiveTheSameGraph() =>
        Fingerprint(ShowcaseDataset.Build(ShowcaseProfile.Demo, Night)).Should().Be(Fingerprint(AtNight.Value));

    [Fact]
    public void AnotherDate_KeepsTheNamesSlugsIdsAndPhonesOfShopsAndProducts_AndMovesTheOrders()
    {
        var today = AtNight.Value;
        var later = ShowcaseDataset.Build(ShowcaseProfile.Demo, Night.AddDays(3));

        later.Companies.Where(c => c.Kind == CompanyKind.Orders).Select(c => (c.Id, c.Slug, c.Name, c.Phone))
            .Should().Equal(Shops(today).Select(c => (c.Id, c.Slug, c.Name, c.Phone)));
        later.Products.Select(p => (p.Id, p.Name, p.Price)).Should().Equal(today.Products.Select(p => (p.Id, p.Name, p.Price)));
        later.Users.Select(u => (u.Id, u.PhoneNumber)).Should().Equal(today.Users.Select(u => (u.Id, u.PhoneNumber)));
        Fingerprint(later).Should().NotBe(Fingerprint(today));
    }

    [Fact]
    public void TheDemoRolesAreTheRealAccountsOfTheCoffeeShopAndOfTheCustomerPool()
    {
        var g = AtNight.Value;
        var kofeinya = Shop(g, "kofeinya");
        var ownerId = ShowcaseDemoRoles.UserIdOf(ShowcaseDemoRoles.ShopOwner)!;
        var staffId = ShowcaseDemoRoles.UserIdOf(ShowcaseDemoRoles.ShopStaff)!;
        var customerId = ShowcaseDemoRoles.UserIdOf(ShowcaseDemoRoles.ShopCustomer)!;

        kofeinya.OwnerUserId.Should().Be(ownerId);
        g.UserRoles.Should().Contain((ownerId, "CompanyOwner"));
        g.UserRoles.Should().Contain((staffId, "Master"));
        g.UserRoles.Should().Contain((customerId, "Client"));
        g.Members.Should().Contain(m => m.CompanyId == kofeinya.Id && m.UserId == staffId && m.Role == UserRole.Master);
        g.Members.Should().Contain(m => m.CompanyId == kofeinya.Id && m.UserId == ownerId && m.Role == UserRole.CompanyOwner);
        g.Users.Single(u => u.Id == customerId).Should().Match<AppUser>(u => u.IsShowcase && u.PasswordHash == null && u.Email == null);
        Shops(g).Where(c => c.OwnerUserId == ownerId).Should().ContainSingle("the demo owner owns the coffee shop only, not the salon 'Лаванда'");
        g.Companies.Single(c => c.Slug == "primer-lavanda").OwnerUserId.Should().NotBe(ownerId);
    }

    [Fact]
    public void TheTokenOfAShopRole_SurvivesTheReset_SameIdAndSameSecurityStampOnAnotherDay()
    {
        var tomorrow = ShowcaseDataset.Build(ShowcaseProfile.Demo, Night.AddDays(1));
        foreach (var role in new[] { ShowcaseDemoRoles.ShopOwner, ShowcaseDemoRoles.ShopStaff, ShowcaseDemoRoles.ShopCustomer })
        {
            var id = ShowcaseDemoRoles.UserIdOf(role)!;
            tomorrow.Users.Single(u => u.Id == id).SecurityStamp.Should().Be(AtNight.Value.Users.Single(u => u.Id == id).SecurityStamp, role);
        }
    }

    // ── Catalogs and listing ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryShopHasThreeToSevenCategories_AndFifteenToFortyProducts()
    {
        var g = AtNight.Value;
        foreach (var shop in Shops(g))
        {
            g.ProductCategories.Count(c => c.CompanyId == shop.Id).Should().BeInRange(3, 7, shop.Slug);
            g.Products.Count(p => p.CompanyId == shop.Id).Should().BeInRange(15, 40, shop.Slug);
        }
    }

    [Fact]
    public void EveryShopPassesEveryPointOfTheCatalogListing_SoTheyAreAllInTheGoodsCatalog()
    {
        var g = AtNight.Value;
        foreach (var shop in Shops(g))
        {
            var settings = g.ShopSettings.Single(s => s.CompanyId == shop.Id);
            var hours = ShopScheduleRules.Parse(settings.WorkingHoursJson);
            var result = CatalogListingRules.Evaluate(new CatalogListingInput(
                shop.IsActive, hours is { Days.Count: > 0 }, g.Products.Any(p => p.CompanyId == shop.Id && p.IsPublished && p.DeletedAtUtc == null),
                AllowedByPlan: true, shop.ShowInPublicListing));
            result.Visible.Should().BeTrue(shop.Slug);
        }
    }

    [Fact]
    public void ThePricesAndTheWeightRulesAreValidForTheProduct_TheSameRulesAsTheOwnersForm()
    {
        var g = AtNight.Value;
        foreach (var product in g.Products)
        {
            ProductInputRules.TryNormalize(product.Name, product.Description, product.Price, product.Unit, product.PortionText,
                product.WeightStepGrams, product.MinQuantityGrams, product.CompositionAndAllergens, out var normalized, out var error)
                .Should().BeTrue($"{product.Name}: {error}");
            normalized!.Price.Should().Be(product.Price);
            product.Position.Should().BeGreaterThanOrEqualTo(0);
        }
        g.Products.Where(p => p.Unit == ProductUnit.Weight).Should().NotBeEmpty().And.OnlyContain(p => p.WeightStepGrams > 0 && p.MinQuantityGrams >= p.WeightStepGrams);
    }

    [Fact]
    public void TheWorkingHoursAreInTheCanonicalForm_TheCanteenIsClosedAtTheWeekend_TheFlowerShopHasAHoliday()
    {
        var g = AtNight.Value;
        var canteen = ShopScheduleRules.Parse(g.ShopSettings.Single(s => s.CompanyId == Shop(g, "stolovaya").Id).WorkingHoursJson)!;
        canteen.For(DayOfWeek.Saturday).Should().BeEmpty();
        canteen.For(DayOfWeek.Sunday).Should().BeEmpty();
        canteen.For(DayOfWeek.Monday).Should().Equal(new TimeInterval(11 * 60, 17 * 60));

        var coffee = ShopScheduleRules.Parse(g.ShopSettings.Single(s => s.CompanyId == Shop(g, "kofeinya").Id).WorkingHoursJson)!;
        Enum.GetValues<DayOfWeek>().Should().OnlyContain(d => coffee.For(d).Count == 1 && coffee.For(d)[0] == new TimeInterval(7 * 60, 23 * 60));

        var flowers = Shop(g, "cvety");
        var special = g.SpecialDays.Should().ContainSingle(d => d.CompanyId == flowers.Id).Subject;
        (special.Date.DayNumber - new DateOnly(2026, 10, 1).DayNumber).Should().BeInRange(3, 10);
        ShopScheduleRules.ParseIntervals(special.IntervalsJson).Should().Equal(new TimeInterval(8 * 60, 22 * 60));
    }

    [Fact]
    public void TheShopsSettingsMatchTheSpec_AcceptanceStockAndPreorders()
    {
        var g = AtNight.Value;
        ShopSettings S(string key) => g.ShopSettings.Single(s => s.CompanyId == Shop(g, key).Id);

        S("kofeinya").Should().Match<ShopSettings>(s => s.AcceptanceMode == OrderAcceptanceMode.Manual && s.AsapEnabled && s.ScheduledEnabled && s.PreorderDays == 1 && s.SlotStepMinutes == 15);
        S("pekarnya").Should().Match<ShopSettings>(s => s.AcceptanceMode == OrderAcceptanceMode.Auto && s.TrackStock && !s.AsapEnabled && s.PreorderDays == 2);
        S("stolovaya").Should().Match<ShopSettings>(s => s.AsapEnabled && !s.ScheduledEnabled);
        S("cvety").Should().Match<ShopSettings>(s => !s.AsapEnabled && s.PreorderDays == 7 && s.MinPrepMinutes == 120);
        g.ShopSettings.Should().OnlyContain(s => s.SellerInn == null && s.SellerOgrn == null && s.SellerLegalAddress == null && s.SellerLegalName!.EndsWith("(пример)"));
        g.ShopSettings.Should().OnlyContain(s => !s.CustomerMessengerEnabled && s.CustomerMode == ShopCustomerMode.Anyone);
    }

    [Fact]
    public void TheCanteenHasAMenuForTodayAndTheNextFiveWorkingDays_AndTheFridayDishIsOnFridaysOnly()
    {
        var g = AtNight.Value;
        var canteen = Shop(g, "stolovaya");
        var menus = g.DailyMenus.Where(m => m.CompanyId == canteen.Id).OrderBy(m => m.Date).ToList();

        menus.Should().HaveCount(6);
        menus[0].Date.Should().Be(new DateOnly(2026, 10, 1));
        menus.Should().OnlyContain(m => m.Date.DayOfWeek != DayOfWeek.Saturday && m.Date.DayOfWeek != DayOfWeek.Sunday);
        var fridayDish = g.Products.Single(p => p.CompanyId == canteen.Id && p.AvailableWeekdaysMask == 16);
        foreach (var menu in menus)
        {
            var items = g.DailyMenuItems.Where(i => i.DailyMenuId == menu.Id).Select(i => i.ProductId).ToList();
            items.Should().OnlyHaveUniqueItems().And.HaveCountGreaterThan(8);
            (items.Contains(fridayDish.Id)).Should().Be(menu.Date.DayOfWeek == DayOfWeek.Friday, menu.Date.ToString());
        }
    }

    // ── Orders: volume, history ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheVolumeIsAboutFiveThousandOrders_AndEveryShopHasItsOwnScale()
    {
        var g = AtNight.Value;

        g.Orders.Count.Should().BeInRange(4500, 6000);
        int Count(string key) => g.Orders.Count(o => o.CompanyId == Shop(g, key).Id);
        Count("kofeinya").Should().BeGreaterThan(Count("stolovaya")).And.BeGreaterThan(2000);
        Count("stolovaya").Should().BeGreaterThan(Count("pekarnya"));
        Count("pekarnya").Should().BeGreaterThan(Count("fermerskaya"));
        Count("fermerskaya").Should().BeGreaterThan(Count("cvety"));
        Count("cvety").Should().BeGreaterThan(50);
    }

    [Fact]
    public void ThePastEndsAreInTheRightShares_AndHaveTheirReasons()
    {
        var g = AtNight.Value;
        var today = new DateOnly(2026, 10, 1);
        var past = g.Orders.Where(o => o.PickupDate < today).ToList();
        double Share(OrderStatus s) => past.Count(o => o.Status == s) / (double)past.Count;

        Share(OrderStatus.Issued).Should().BeInRange(0.85, 0.91);
        Share(OrderStatus.Rejected).Should().BeInRange(0.01, 0.05);
        Share(OrderStatus.CancelledByCustomer).Should().BeInRange(0.02, 0.06);
        Share(OrderStatus.CancelledByShop).Should().BeInRange(0.01, 0.06);
        Share(OrderStatus.NotPickedUp).Should().BeInRange(0.015, 0.05);
        past.Should().OnlyContain(o => !OrderStateMachine.IsActive(o.Status), "no order of a past day is left active (US-35-09)");
        past.Where(o => o.Status is OrderStatus.Rejected or OrderStatus.CancelledByShop).Should().OnlyContain(o => !string.IsNullOrEmpty(o.StatusReason));
        past.Where(o => o.Status is OrderStatus.Issued or OrderStatus.CancelledByCustomer or OrderStatus.NotPickedUp).Should().OnlyContain(o => o.StatusReason == null);
        g.Orders.Count(o => o.IsModifiedByShop).Should().BeGreaterThan(g.Orders.Count / 40);
        g.Orders.Where(o => o.IsModifiedByShop).Should().OnlyContain(o => g.OrderEvents.Any(e => e.OrderId == o.Id && e.Kind == OrderEventKind.Edited));
    }

    [Fact]
    public void AnAutomaticShopNeverRejects_ItCancelsInstead()
    {
        var g = AtNight.Value;
        var bakery = Shop(g, "pekarnya");

        g.Orders.Where(o => o.CompanyId == bakery.Id).Should().NotContain(o => o.Status == OrderStatus.Rejected);
        g.OrderEvents.Where(e => e.CompanyId == bakery.Id).Should().NotContain(e => e.Kind == OrderEventKind.Rejected);
        g.OrderEvents.Where(e => e.CompanyId == bakery.Id && e.Kind == OrderEventKind.Accepted).Should().BeEmpty("an order of an automatic shop is born accepted, the journal has no separate step");
        g.Orders.Where(o => o.CompanyId == bakery.Id).Should().OnlyContain(o => o.AcceptedAtUtc == o.CreatedAtUtc);
    }

    [Fact]
    public void NoOrderExistsBeforeTheResetAndNoneIsCreatedInTheFuture()
    {
        foreach (var (g, now) in new[] { (AtNight.Value, Night), (AtNoon.Value, Noon) })
        {
            g.Orders.Should().OnlyContain(o => o.CreatedAtUtc < now, "a generated order is older than the moment of the reset");
            g.OrderEvents.Should().OnlyContain(e => e.OccurredAtUtc <= now, "a transition of the timeline is there from its own moment");
            g.OrderEvents.Should().OnlyContain(e => e.OccurredAtUtc > now.AddDays(-70));
        }
    }

    [Fact]
    public void OrdersOfTodayAndLaterAreActiveOrHandedOnlyAccordingToTheTimeline_PastOrdersAreAllDone()
    {
        var today = new DateOnly(2026, 10, 1);
        var night = AtNight.Value.Orders.Where(o => o.PickupDate >= today).ToList();
        var noon = AtNoon.Value.Orders.Where(o => o.PickupDate >= today).ToList();

        night.Should().OnlyContain(o => o.Status == OrderStatus.New || o.Status == OrderStatus.Accepted || o.Status == OrderStatus.Ready,
            "at 04:00 nothing of today has been handed over yet");
        noon.Count(o => o.Status == OrderStatus.Issued).Should().BeGreaterThan(20, "a reset at noon shows the morning already handed over");
        night.Count(o => o.Status == OrderStatus.New).Should().BeGreaterThan(10, "the board has new orders to accept");
        night.Count(o => o.Status == OrderStatus.Accepted).Should().BeGreaterThan(10);
        var coffeeShop = Shop(AtNight.Value, "kofeinya").Id;
        night.Count(o => o.CompanyId == coffeeShop && o.Status == OrderStatus.Ready).Should().Be(2, "two orders of the opening quarter of an hour are prepared the evening before");
        night.Count(o => o.Status == OrderStatus.Ready).Should().BeInRange(2, 10);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(4, 25)]
    [InlineData(6, 55)]
    [InlineData(7, 55)]
    [InlineData(9, 30)]
    [InlineData(10, 55)]
    [InlineData(12, 0)]
    [InlineData(14, 40)]
    [InlineData(16, 55)]
    [InlineData(18, 10)]
    [InlineData(19, 30)]
    [InlineData(20, 5)]
    [InlineData(21, 0)]
    public void TheCoffeeShopHasAReadyOrderRightAfterAResetAtAnyHour_AndItsJournalAgrees(int hourUtc, int minute)
    {
        var now = new DateTime(2026, 10, 1, hourUtc, minute, 0, DateTimeKind.Utc);
        var g = ShowcaseDataset.Build(ShowcaseProfile.Demo, now);
        var coffee = Shop(g, "kofeinya").Id;
        var today = DateOnly.FromDateTime(now.AddHours(3)); // the coffee shop is in Moscow

        var ready = g.Orders.Where(o => o.CompanyId == coffee && o.PickupDate == today && o.Status == OrderStatus.Ready).ToList();
        ready.Should().NotBeEmpty($"the column «Готовы к выдаче» is not empty after a reset at {now:HH:mm} UTC (US-35-02)");

        foreach (var order in ready)
        {
            var events = g.OrderEvents.Where(e => e.OrderId == order.Id).OrderBy(e => e.OccurredAtUtc).ToList();
            events.Last().ToStatus.Should().Be(OrderStatus.Ready, "the last event of the journal is the status");
            order.ReadyAtUtc.Should().NotBeNull().And.Subject.Should().BeOnOrBefore(now);
            events.Should().OnlyContain(e => e.OccurredAtUtc <= now, "nothing of the journal is in the future");
            order.CompletedAtUtc.Should().BeNull();
        }
    }

    [Fact]
    public void TheCoffeeShopHasPreordersForTomorrow_TheBakeryForTwoDays_TheCanteenHasNone()
    {
        var g = AtNight.Value;
        var today = new DateOnly(2026, 10, 1);
        int Ahead(string key, int days) => g.Orders.Count(o => o.CompanyId == Shop(g, key).Id && o.PickupDate == today.AddDays(days));

        Ahead("kofeinya", 1).Should().BeGreaterThan(5);
        Ahead("kofeinya", 2).Should().Be(0, "the coffee shop takes pre-orders one day ahead");
        Ahead("pekarnya", 2).Should().BeGreaterThan(0);
        Ahead("pekarnya", 3).Should().Be(0);
        g.Orders.Where(o => o.CompanyId == Shop(g, "stolovaya").Id).Should().OnlyContain(o => o.PickupDate <= today);
        g.Orders.Count(o => o.PickupDate > today).Should().BeGreaterThan(20);
    }

    // ── Invariants of the product ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NumbersAreOneToNInTheOrderOfCreation_InsideEveryPickupDayOfEveryShop_AndTheCounterIsTheLastOne()
    {
        var g = AtNight.Value;
        foreach (var day in g.Orders.GroupBy(o => (o.CompanyId, o.PickupDate)))
        {
            var ordered = day.OrderBy(o => o.CreatedAtUtc).ThenBy(o => o.Id).ToList();
            ordered.Select(o => o.Number).Should().Equal(Enumerable.Range(1, ordered.Count), $"{day.Key}");
            g.OrderDailyCounters.Single(c => c.CompanyId == day.Key.CompanyId && c.PickupDate == day.Key.PickupDate).LastNumber.Should().Be(ordered.Count);
        }
        g.OrderDailyCounters.Count.Should().Be(g.Orders.Select(o => (o.CompanyId, o.PickupDate)).Distinct().Count());
        g.OrderDailyCounters.Select(c => (c.CompanyId, c.PickupDate)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheMonthlyUsageIsTheNumberOfOrdersCreatedInTheMonthInTheShopsZone_NoWarningIsSet()
    {
        var g = AtNight.Value;
        foreach (var shop in Shops(g))
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId);
            var expected = g.Orders.Where(o => o.CompanyId == shop.Id)
                .GroupBy(o => { var d = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(o.CreatedAtUtc, zone)); return new DateOnly(d.Year, d.Month, 1); })
                .ToDictionary(x => x.Key, x => x.Count());
            var actual = g.OrderMonthlyUsages.Where(u => u.BillingAccountId == shop.BillingAccountId).ToDictionary(u => u.Month, u => u.Count);
            actual.Should().BeEquivalentTo(expected, shop.Slug);
        }
        g.OrderMonthlyUsages.Should().OnlyContain(u => u.Warned80AtUtc == null && u.Warned100AtUtc == null && u.Month.Day == 1);
    }

    [Fact]
    public void TheLastEventOfTheJournalIsTheStatusOfTheOrder_TheMomentsGrow_AndTheVersionCountsTheChanges()
    {
        foreach (var g in new[] { AtNight.Value, AtNoon.Value })
        {
            var events = g.OrderEvents.GroupBy(e => e.OrderId).ToDictionary(x => x.Key, x => x.OrderBy(e => e.OccurredAtUtc).ToList());
            foreach (var order in g.Orders)
            {
                var journal = events[order.Id];
                journal[0].Kind.Should().Be(OrderEventKind.Created);
                journal[0].FromStatus.Should().BeNull();
                journal[0].OccurredAtUtc.Should().Be(order.CreatedAtUtc);
                journal.Select(e => e.OccurredAtUtc).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
                journal.Last(e => e.ToStatus is not null).ToStatus.Should().Be(order.Status);
                order.Version.Should().Be(journal.Count);
                order.UpdatedAtUtc.Should().Be(journal[^1].OccurredAtUtc);
                for (var i = 1; i < journal.Count; i++)
                    journal[i].FromStatus.Should().Be(journal[i - 1].ToStatus, $"{order.Id} step {i}: a status is never skipped");
                (order.CompletedAtUtc is not null).Should().Be(!OrderStateMachine.IsActive(order.Status));
                if (order.CompletedAtUtc is not null) order.CompletedAtUtc.Should().Be(journal[^1].OccurredAtUtc);
            }
        }
    }

    [Fact]
    public void TheBoardRevisionOfEveryShopIsTheNumberOfItsEvents()
    {
        var g = AtNight.Value;
        foreach (var shop in Shops(g))
            g.ShopSettings.Single(s => s.CompanyId == shop.Id).OrdersRevision.Should().Be(g.OrderEvents.Count(e => e.CompanyId == shop.Id));
    }

    [Fact]
    public void TheJournalWritesTheChangesOnlyThroughTheProductsSerializers_AndTheEditBecomesTheLine()
    {
        var g = AtNight.Value;
        var editEvents = g.OrderEvents.Where(e => e.Kind == OrderEventKind.Edited).ToList();
        editEvents.Should().NotBeEmpty();
        foreach (var e in editEvents)
        {
            var changes = OrderChangeLog.ParseEdit(e.ChangesJson);
            changes.Should().ContainSingle();
            changes[0].Before.Should().NotBeNull();
            changes[0].After.Should().NotBeNull();
            changes[0].Before!.Qty.Should().NotBe(changes[0].After!.Qty);
            e.TotalBefore.Should().NotBe(e.TotalAfter);
            e.Comment.Should().NotBeNullOrEmpty("the shop's comment of an edit is visible to the customer");
            g.OrderItems.Where(i => i.OrderId == e.OrderId).Should().Contain(i => i.NameSnapshot == changes[0].Name && i.QuantityOrdered == changes[0].After!.Qty);
            g.Orders.Single(o => o.Id == e.OrderId).EstimatedTotal.Should().Be(e.TotalAfter, "the order carries the total after the last edit");
        }

        foreach (var e in g.OrderEvents.Where(e => e.Kind == OrderEventKind.Issued && e.ChangesJson != null))
            OrderChangeLog.ParseIssue(e.ChangesJson)!.Stock.Should().NotBeEmpty();
    }

    [Fact]
    public void ItemsAreSnapshotsOfTheProductAndTheMoneyIsTheProductsOwnArithmetic()
    {
        var g = AtNight.Value;
        var products = g.Products.ToDictionary(p => p.Id);
        var itemsByOrder = g.OrderItems.GroupBy(i => i.OrderId).ToDictionary(x => x.Key, x => x.ToList());
        foreach (var order in g.Orders)
        {
            var items = itemsByOrder[order.Id];
            items.Select(i => i.Position).Should().Equal(Enumerable.Range(0, items.Count));
            foreach (var item in items)
            {
                var product = products[item.ProductId!.Value];
                product.CompanyId.Should().Be(order.CompanyId);
                item.NameSnapshot.Should().Be(product.Name);
                item.UnitPrice.Should().Be(product.Price);
                item.Unit.Should().Be(product.Unit);
                item.LineTotalEstimated.Should().Be(OrderMoney.LineTotal(item.Unit, item.UnitPrice, item.QuantityOrdered));
                OrderQuantityRules.IsValidForEdit(item.Unit, item.QuantityOrdered, item.WeightStepGrams).Should().BeTrue($"{item.NameSnapshot} x {item.QuantityOrdered}");
                if (order.Status == OrderStatus.Issued)
                    item.LineTotalFinal.Should().Be(OrderMoney.LineTotal(item.Unit, item.UnitPrice, item.QuantityActual!.Value));
                else
                    item.QuantityActual.Should().BeNull();
            }
            order.EstimatedTotal.Should().Be(OrderMoney.Sum(items.Select(i => i.LineTotalEstimated)));
            order.HasWeightItems.Should().Be(items.Any(i => i.Unit == ProductUnit.Weight));
            if (order.Status == OrderStatus.Issued) order.FinalTotal.Should().Be(OrderMoney.Sum(items.Select(i => i.LineTotalFinal!.Value)));
            else order.FinalTotal.Should().BeNull();
        }
    }

    [Fact]
    public void WeightOrdersAreIssuedByTheActualWeight_SoTheFinalTotalIsNotTheEstimate()
    {
        var g = AtNight.Value;
        var farm = Shop(g, "fermerskaya");
        var issued = g.Orders.Where(o => o.CompanyId == farm.Id && o.Status == OrderStatus.Issued && o.HasWeightItems).ToList();

        issued.Should().NotBeEmpty();
        issued.Count(o => o.FinalTotal != o.EstimatedTotal).Should().BeGreaterThan(issued.Count * 9 / 10);
        foreach (var item in g.OrderItems.Where(i => issued.Any(o => o.Id == i.OrderId) && i.Unit == ProductUnit.Weight))
            ((double)item.QuantityActual!.Value / item.QuantityOrdered).Should().BeInRange(0.85, 1.15);
    }

    [Fact]
    public void Stock_IsAtLeastTheReserveOfActiveOrders_AndTheSoldOutProductsHaveNone()
    {
        foreach (var g in new[] { AtNight.Value, AtNoon.Value })
        {
            var bakery = Shop(g, "pekarnya");
            var activeItems = g.OrderItems.Where(i => g.Orders.Any(o => o.Id == i.OrderId && OrderStateMachine.IsActive(o.Status)) && i.ReservesStock).ToList();
            foreach (var product in g.Products.Where(p => p.CompanyId == bakery.Id))
            {
                product.StockOnHand.Should().NotBeNull(product.Name);
                var reserve = activeItems.Where(i => i.ProductId == product.Id).Sum(i => i.QuantityOrdered);
                product.StockOnHand.Should().BeGreaterThanOrEqualTo(reserve, product.Name);
            }

            var soldOut = g.Products.Where(p => p.CompanyId == bakery.Id && p.IsSoldOut).ToList();
            soldOut.Should().HaveCount(2);
            soldOut.Should().OnlyContain(p => p.StockOnHand == 0 && p.SoldOutForDate == new DateOnly(2026, 10, 1));
            activeItems.Should().NotContain(i => soldOut.Any(p => p.Id == i.ProductId), "what is shown as sold out is in no active order");
            g.Products.Where(p => p.CompanyId != bakery.Id).Should().OnlyContain(p => p.StockOnHand == null && !p.IsSoldOut, "only the bakery tracks stock");
        }

        var g2 = AtNight.Value;
        g2.OrderItems.Where(i => g2.Orders.Any(o => o.Id == i.OrderId && o.CompanyId == Shop(g2, "pekarnya").Id)).Should().OnlyContain(i => i.ReservesStock);
        g2.OrderItems.Where(i => g2.Orders.Any(o => o.Id == i.OrderId && o.CompanyId != Shop(g2, "pekarnya").Id)).Should().OnlyContain(i => !i.ReservesStock);
    }

    [Fact]
    public void TheOrdersOnSaleRespectTheWeekdayMaskAndTheMenu()
    {
        var g = AtNight.Value;
        var canteen = Shop(g, "stolovaya");
        var fridayDish = g.Products.Single(p => p.CompanyId == canteen.Id && p.AvailableWeekdaysMask == 16);
        var menus = g.DailyMenus.Where(m => m.CompanyId == canteen.Id).ToDictionary(m => m.Date);

        foreach (var item in g.OrderItems)
        {
            var order = g.Orders.Single(o => o.Id == item.OrderId);
            if (order.CompanyId != canteen.Id) continue;
            if (item.ProductId == fridayDish.Id) order.PickupDate.DayOfWeek.Should().Be(DayOfWeek.Friday);
            if (menus.TryGetValue(order.PickupDate, out var menu))
                g.DailyMenuItems.Should().Contain(i => i.DailyMenuId == menu.Id && i.ProductId == item.ProductId);
        }
    }

    [Fact]
    public void TheFieldsAreThoseOfTheLiveCreation_PublicTokenIdempotencyKeyConsentSnapshotsAndPickup()
    {
        var g = AtNight.Value;
        g.Orders.Select(o => o.PublicToken).Should().OnlyHaveUniqueItems().And.OnlyContain(t => PublicOrderToken.IsWellFormed(t));
        g.Orders.Select(o => o.IdempotencyKey).Should().OnlyHaveUniqueItems();
        g.Orders.Select(o => o.Id).Should().OnlyHaveUniqueItems();
        g.OrderEvents.Select(e => e.Id).Should().OnlyHaveUniqueItems();
        g.OrderItems.Select(i => i.Id).Should().OnlyHaveUniqueItems();

        g.Orders.Should().OnlyContain(o => o.ConsentPrivacyVersion == null && o.ConsentTermsVersion == null && o.ConsentAcceptedAtUtc == null && o.CheckoutNoticeVersion == null
            && !o.NotifyByMessenger && !o.CustomerPhoneVerified && !o.PersonalDataErased && o.MessengerConsentVersion == null);
        g.Orders.Should().OnlyContain(o => o.AllowCustomerCancelSnapshot && o.CustomerModeSnapshot == ShopCustomerMode.Anyone);
        g.Orders.Should().OnlyContain(o => (o.PickupKind == PickupKind.Asap) == (o.PickupEndUtc == null));
        g.Orders.Where(o => o.PickupKind == PickupKind.Slot).Should().OnlyContain(o => o.PickupEndUtc > o.PickupStartUtc);
        g.Orders.Should().OnlyContain(o => o.PickupStartUtc > o.CreatedAtUtc);
        foreach (var shopGroup in g.Orders.GroupBy(o => o.CompanyId))
        {
            var shop = g.Companies.Single(c => c.Id == shopGroup.Key);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId);
            shopGroup.Should().OnlyContain(o => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(o.PickupStartUtc, zone)) == o.PickupDate);
            shopGroup.Should().OnlyContain(o => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(o.CreatedAtUtc, zone)) == o.BusinessDate);
        }
    }

    [Fact]
    public void EveryPickupIsInsideTheWorkingHoursOfItsDay()
    {
        var g = AtNight.Value;
        foreach (var shop in Shops(g))
        {
            var weekly = ShopScheduleRules.Parse(g.ShopSettings.Single(s => s.CompanyId == shop.Id).WorkingHoursJson)!;
            var special = g.SpecialDays.Where(d => d.CompanyId == shop.Id).ToDictionary(d => d.Date, d => ShopScheduleRules.ParseIntervals(d.IntervalsJson));
            var zone = TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId);
            foreach (var order in g.Orders.Where(o => o.CompanyId == shop.Id))
            {
                var intervals = special.TryGetValue(order.PickupDate, out var s) ? s : weekly.For(order.PickupDate.DayOfWeek);
                intervals.Should().NotBeEmpty($"{shop.Slug} {order.PickupDate}: an order on a closed day");
                var local = TimeZoneInfo.ConvertTimeFromUtc(order.PickupStartUtc, zone);
                var minutes = local.Hour * 60 + local.Minute;
                var end = order.PickupEndUtc is { } e ? TimeZoneInfo.ConvertTimeFromUtc(e, zone) : local;
                var endMinutes = end.Hour * 60 + end.Minute;
                intervals.Should().Contain(i => minutes >= i.StartMinutes && endMinutes <= i.EndMinutes, $"{shop.Slug} {order.PickupDate} {local:HH:mm}");
            }
        }
    }

    [Fact]
    public void TheActorsOfTheJournalAreTheShopsOwnPeopleOrItsCustomer()
    {
        var g = AtNight.Value;
        foreach (var e in g.OrderEvents)
        {
            var order = g.Orders.Single(o => o.Id == e.OrderId);
            var shop = g.Companies.Single(c => c.Id == order.CompanyId);
            switch (e.ActorKind)
            {
                case OrderActorKind.Staff:
                    (e.ActorUserId == shop.OwnerUserId || g.Members.Any(m => m.CompanyId == shop.Id && m.UserId == e.ActorUserId && m.Role == UserRole.Master)).Should().BeTrue();
                    e.ActorNameSnapshot.Should().NotBeNullOrWhiteSpace();
                    break;
                case OrderActorKind.Customer:
                    e.ActorUserId.Should().Be(order.CustomerUserId);
                    break;
                case OrderActorKind.Guest:
                    e.ActorUserId.Should().BeNull();
                    e.ActorNameSnapshot.Should().Be(order.CustomerName);
                    break;
                default:
                    throw new Xunit.Sdk.XunitException($"unexpected actor {e.ActorKind}");
            }
        }
    }

    // ── Customers ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheCustomersAreAboutEightyRegisteredAndFourHundredFiftyGuests_SplitByCity()
    {
        var g = AtNight.Value;
        var orderingAccounts = g.Orders.Where(o => o.CustomerUserId != null).Select(o => o.CustomerUserId!).Distinct().ToList();
        var guestPhones = g.Orders.Where(o => o.CustomerUserId == null).Select(o => o.CustomerPhone!).Distinct().ToList();

        orderingAccounts.Count.Should().BeInRange(60, 81);
        guestPhones.Count.Should().BeInRange(300, 450);
        var clients = g.UserRoles.Where(r => r.RoleName == "Client").Select(r => r.UserId).ToHashSet();
        orderingAccounts.Should().OnlyContain(id => clients.Contains(id) && g.Users.Single(u => u.Id == id).IsShowcase);

        // a customer of Moscow does not order in Novosibirsk
        var ordersByPhone = g.Orders.GroupBy(o => o.CustomerPhone!);
        var cityOf = g.Companies.ToDictionary(c => c.Id, c => g.CityNameByCompany[c.Id]);
        ordersByPhone.Should().OnlyContain(x => x.Select(o => cityOf[o.CompanyId]).Distinct().Count() == 1, "the pools are separate per city");
    }

    [Fact]
    public void TheDistributionIsSkewed_RegularsHaveManyOrders_ManyCustomersOneToThree()
    {
        var g = AtNight.Value;
        var counts = g.Orders.Where(o => o.CompanyId == Shop(g, "kofeinya").Id).GroupBy(o => o.CustomerPhone).Select(x => x.Count()).OrderByDescending(c => c).ToList();

        counts[0].Should().BeGreaterThan(30, "a regular has a long history");
        counts.Count(c => c <= 3).Should().BeGreaterThan(counts.Count / 5, "a good share of customers ordered only one to three times");
        counts.Count(c => c >= 20).Should().BeInRange(5, counts.Count / 2, "there is a core of regulars, and it is not everybody");
    }

    [Fact]
    public void TheDemoCustomerHasFiveToTenCompletedOrdersOfThirtyDays_OneCancelledByHim_TodaysActiveOne_AndTomorrowsPreorder()
    {
        foreach (var g in new[] { AtNight.Value, AtNoon.Value })
        {
            var id = ShowcaseDemoRoles.UserIdOf(ShowcaseDemoRoles.ShopCustomer)!;
            var mine = g.Orders.Where(o => o.CustomerUserId == id).OrderBy(o => o.PickupStartUtc).ToList();
            var today = new DateOnly(2026, 10, 1);
            var kofeinya = Shop(g, "kofeinya");
            var pekarnya = Shop(g, "pekarnya");
            mine.Select(o => o.CompanyId).Distinct().Should().BeEquivalentTo(new[] { kofeinya.Id, pekarnya.Id });

            var completed = mine.Where(o => !OrderStateMachine.IsActive(o.Status)).ToList();
            completed.Count.Should().BeInRange(5, 10);
            completed.Should().OnlyContain(o => o.PickupDate >= today.AddDays(-30));
            completed.Count(o => o.Status == OrderStatus.CancelledByCustomer).Should().Be(1);
            completed.Count(o => o.CompanyId == kofeinya.Id).Should().BeGreaterThan(0);
            completed.Count(o => o.CompanyId == pekarnya.Id).Should().BeGreaterThan(0);

            var active = mine.Where(o => OrderStateMachine.IsActive(o.Status)).ToList();
            active.Should().HaveCount(2);
            var todays = active.Single(o => o.CompanyId == kofeinya.Id);
            todays.PickupDate.Should().Be(today);
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
            TimeZoneInfo.ConvertTimeFromUtc(todays.PickupStartUtc, zone).TimeOfDay.Should().Be(new TimeSpan(21, 30, 0));
            TimeZoneInfo.ConvertTimeFromUtc(todays.PickupEndUtc!.Value, zone).TimeOfDay.Should().Be(new TimeSpan(21, 45, 0));
            active.Single(o => o.CompanyId == pekarnya.Id).PickupDate.Should().Be(today.AddDays(1));
            mine.Should().OnlyContain(o => o.CustomerKind == OrderActorKind.Customer && o.CustomerName == "Анна Мельникова");
        }
    }

    [Fact]
    public void TheNotesAboutCustomersAreNeutral_NeverAboutHealth_AndKeyedByTheCanonicalPhone()
    {
        var g = AtNight.Value;
        var health = new[] { "аллерг", "диабет", "болез", "беремен", "лечен", "здоров", "диет", "лактоз", "непереносим" };
        var texts = g.ShopCustomerNotes.Select(n => n.Text)
            .Concat(ShowcaseShopSpecs.CustomerNotes).Concat(ShowcaseShopSpecs.OrderComments).Concat(ShowcaseShopSpecs.RejectReasons)
            .Concat(ShowcaseShopSpecs.ShopCancelReasons).Concat(ShowcaseShopSpecs.EditComments)
            .Concat(g.Orders.Select(o => o.Comment).OfType<string>()).ToList();

        foreach (var text in texts)
            health.Should().NotContain(word => text.Contains(word, StringComparison.OrdinalIgnoreCase), text);

        g.ShopCustomerNotes.Should().NotBeEmpty();
        g.ShopCustomerNotes.Should().OnlyContain(n => ShowcasePhones.IsShowcasePhone(n.Phone) && n.UpdatedByName != "");
        g.ShopCustomerNotes.Select(n => (n.CompanyId, n.Phone)).Should().OnlyHaveUniqueItems();
        g.ShopCustomerNotes.Should().OnlyContain(n => g.Orders.Count(o => o.CompanyId == n.CompanyId && o.CustomerPhone == n.Phone) >= 3, "a note is about a regular");
    }

    [Fact]
    public void NoOrderOrCatalogTextMentionsHealth_TheFoodInformationIsTheOnlyComposition()
    {
        var g = AtNight.Value;
        var health = new[] { "аллергия", "диабет", "болезн", "беременн" };
        foreach (var text in g.Products.SelectMany(p => new[] { p.Name, p.Description, p.CompositionAndAllergens }).Where(t => t != null))
            health.Should().NotContain(word => text!.Contains(word, StringComparison.OrdinalIgnoreCase), text);
        g.Products.Count(p => p.CompositionAndAllergens != null).Should().BeGreaterThan(g.Products.Count / 4, "part of the products carry the food information (L3)");
    }

    // ── What the generator does not write ────────────────────────────────────────────────────────────────

    [Fact]
    public void TheGraphHasNoConsentsNoPushNoMessagesAndNoPaymentNumbers()
    {
        // The graph type has no collection for them at all (§35.3.4): nothing of the outside world is ever written for a demo shop.
        var properties = typeof(ShowcaseGraph).GetProperties().Select(p => p.Name).ToList();

        properties.Should().NotContain(["ConsentRecords", "SubjectRequests", "OrderPushSubscriptions", "CustomerOrderPushNotifications", "PushSubscriptions",
            "StaffPushNotifications", "StaffMaxLinks", "StaffMaxMessages", "OutboundNotifications", "NotificationChannels", "ClientHealthNotes"]);
    }

    [Fact]
    public void ImageKeysFollowTheRolesOfTheManifest_LogoPhotosAndProducts()
    {
        var g = AtNight.Value;
        foreach (var shop in Shops(g))
        {
            g.LogoKeyByCompany[shop.Id].Should().StartWith("logo.shop.");
            g.PhotoKeysByCompany[shop.Id].Should().HaveCount(g.PhotoKeysByCompany[shop.Id].Count).And.OnlyContain(k => k.StartsWith("photo.shop."));
            g.PhotoKeysByCompany[shop.Id].Count.Should().BeInRange(3, 6);
        }
        g.ProductImageKeys.Values.Should().OnlyContain(k => k.StartsWith("product."));
        var withImage = g.ProductImageKeys.Count / (double)g.Products.Count;
        withImage.Should().BeInRange(0.45, 0.8, "a picture is not on every product (~60 %)");
    }
}
