using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Showcase;

/// <summary>
/// ARCHITECTURE_CYCLE35.md §35.9 (A35-2) — builds the demo shops of «Заказы» into a <see cref="ShowcaseGraph"/>: five shops with their people, catalogs, daily menus,
/// special days, ~5 thousand orders with a journal, the counters the product keeps and the notes of shops about regular customers. A pure function of
/// <c>(profile, nowUtc)</c>, like <see cref="ShowcaseDataset"/>: no database, no clock. It is called LAST by <c>ShowcaseDataset.Build</c> and takes its phones after all
/// the salon ones, so not a byte of the salon part moves (R35-6, the frozen fingerprints of <c>ShowcaseDatasetTests</c>).
///
/// The generator does NOT call the live rules (capture, hours, horizon, limit, stock): it writes entities, like <see cref="ShowcaseGenerator"/> writes bookings. The
/// consistency the product relies on is held by deriving every row from ONE plan of the order: the number inside the day of the pickup, the counter of that day, the
/// monthly usage, the stock (at least the reserve of active orders), the journal (last event = status) and the revision of the board (§35.9.4).
/// Names, catalogs, prices, slugs, ids and phones depend on the profile only; the dates and the statuses of orders depend on the date (and, for today's orders,
/// on the moment of the reset, which decides how far the live timeline of <see cref="ShowcaseOrderTimeline"/> has come).
/// </summary>
public static class ShowcaseShopsDataset
{
    public const int PastDays = 60;
    public const int FutureDays = 3;

    /// <summary>Shares of the ends of a PAST order, in order: handed over, refused, cancelled by the customer, cancelled by the shop, not collected (§35.9.3).</summary>
    private const double IssuedShare = 0.88, RejectedShare = 0.03, CancelledByCustomerShare = 0.04, CancelledByShopShare = 0.02;

    private const double EditedShare = 0.05;
    private const double CommentShare = 0.10;

    public static void Build(ShowcaseGraph graph, ShowcaseProfile profile, ShowcasePhones phones, DateTime nowUtc)
    {
        var p = profile.Name;

        var shops = new List<ShopCtx>();
        foreach (var spec in ShowcaseShopSpecs.Shops) shops.Add(BuildShop(graph, phones, p, spec, nowUtc));

        var pools = BuildCustomerPools(graph, phones, p, nowUtc);
        var demoCustomer = ShowcaseDataset.NewUser(graph, phones, p, ShowcaseDemoRoles.ShopCustomerUserKey, "Анна", "Мельникова",
            nowUtc.Date.AddDays(-150), "Client");
        var demoCustomerCtx = new CustomerCtx(demoCustomer.Id, $"{demoCustomer.FirstName} {demoCustomer.LastName}", demoCustomer.PhoneNumber!);

        var orders = new List<OrderCtx>();
        foreach (var shop in shops) orders.AddRange(BuildOrders(p, shop, pools[shop.Spec.City], nowUtc));
        orders.AddRange(BuildDemoCustomerOrders(p, shops, demoCustomerCtx, nowUtc));

        Number(orders);
        MarkPreparedEarly(shops, orders, nowUtc);
        EnsureReadyOrder(shops, orders, nowUtc);
        foreach (var order in orders) WriteOrder(graph, p, order, nowUtc);
        FinishShops(graph, p, shops, orders, nowUtc);
    }

    // ── Context types ────────────────────────────────────────────────────────────────────────────────────────

    private sealed record CustomerCtx(string? UserId, string Name, string Phone)
    {
        public OrderActorKind Kind => UserId is null ? OrderActorKind.Guest : OrderActorKind.Customer;
    }

    private sealed class ProductCtx(Product product, ShowcaseProductSpec spec)
    {
        public Product Product { get; } = product;
        public ShowcaseProductSpec Spec { get; } = spec;
    }

    private sealed class ShopCtx(ShowcaseShopSpec spec, Company company, BillingAccount account, TimeZoneInfo zone, DateOnly localToday)
    {
        public ShowcaseShopSpec Spec { get; } = spec;
        public Company Company { get; } = company;
        public BillingAccount Account { get; } = account;
        public TimeZoneInfo Zone { get; } = zone;
        public DateOnly LocalToday { get; } = localToday;
        public AppUser Owner { get; set; } = null!;
        public List<AppUser> Staff { get; } = [];
        public ShopSettings Settings { get; set; } = null!;
        public List<ProductCtx> Products { get; } = [];
        public Dictionary<DateOnly, HashSet<Guid>> MenuByDate { get; } = [];
        public Dictionary<DateOnly, (int Start, int End)> SpecialHours { get; } = [];
    }

    private sealed class LineCtx(ProductCtx product, int quantity)
    {
        public ProductCtx Product { get; } = product;
        public int Quantity { get; set; } = quantity;
        public int? Actual { get; set; }
    }

    private enum PastFate { Issued, Rejected, CancelledByCustomer, CancelledByShop, NotPickedUp }

    private sealed class OrderCtx(string key, ShopCtx shop)
    {
        public string Key { get; } = key;
        public ShopCtx Shop { get; } = shop;
        public Guid Id { get; } = ShowcaseIds.For("demo", "order", key);
        public DateTime CreatedUtc { get; set; }
        public DateOnly PickupDate { get; set; }
        public DateTime StartUtc { get; set; }
        public DateTime? EndUtc { get; set; }
        public CustomerCtx Customer { get; set; } = null!;
        public List<LineCtx> Lines { get; set; } = [];
        public string? Comment { get; set; }
        public PastFate Fate { get; set; } = PastFate.Issued;
        public bool Edited { get; set; }

        /// <summary>One of the two first orders of today, due at the opening: "собран с вечера", already Ready at the moment of the reset (§35.10.2).</summary>
        public bool PreparedEarly { get; set; }

        /// <summary>
        /// A reset after the last pickup of the day: every order of today is already over by its plan, so one of them is left "Ready, not collected yet" (it is closed by the
        /// demo task on its next pass). Keeps the column «Готовы к выдаче» from being empty at any hour of the reset.
        /// </summary>
        public bool HeldReady { get; set; }

        public int Number { get; set; }
        public PickupKind Kind => EndUtc is null ? PickupKind.Asap : PickupKind.Slot;
    }

    private sealed record EventDraft(
        OrderEventKind Kind, DateTime AtUtc, OrderStatus? From, OrderStatus? To, OrderActorKind Actor, string? ActorUserId, string? ActorName,
        string? Reason = null, string? Comment = null, string? ChangesJson = null, decimal? TotalBefore = null, decimal? TotalAfter = null);

    // ── Time helpers ─────────────────────────────────────────────────────────────────────────────────────────

    private static DateTime LocalToUtc(TimeZoneInfo zone, DateOnly date, int minutes, int seconds = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).AddMinutes(minutes).AddSeconds(seconds), DateTimeKind.Unspecified), zone);

    private static DateOnly LocalDate(TimeZoneInfo zone, DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));

    private static (string First, string Last) PersonName(ShowcaseRandom rng, bool female)
    {
        var first = female ? rng.Pick(ShowcaseSpecs.FemaleNames) : rng.Pick(ShowcaseSpecs.MaleNames);
        var surname = rng.Pick(ShowcaseSpecs.Surnames);
        return (first, female ? surname.Female : surname.Male);
    }

    private static string FormatPhone(string canonical) =>
        $"+7 ({canonical[1..4]}) {canonical[4..7]}-{canonical[7..9]}-{canonical[9..11]}";

    // ── Shops: owner, staff, company, settings, catalog, menus, special days ─────────────────────────────────

    private static ShopCtx BuildShop(ShowcaseGraph graph, ShowcasePhones phones, string p, ShowcaseShopSpec spec, DateTime nowUtc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(spec.TimeZoneId);
        var localToday = LocalDate(zone, nowUtc);
        var todayStartUtc = LocalToUtc(zone, localToday, 0);
        var rng = new ShowcaseRandom($"{p}:shop:{spec.Key}");

        // For the coffee shop this is the key of the demo role «владелец» (ShowcaseDemoRoles.ShopOwnerUserKey).
        var ownerKey = $"shop:{spec.Key}-owner";
        var owner = ShowcaseDataset.NewUser(graph, phones, p, ownerKey, spec.OwnerFirstName, spec.OwnerLastName, todayStartUtc.AddDays(-rng.Next(200, 500)), "CompanyOwner");
        var account = new BillingAccount
        {
            Id = ShowcaseIds.For(p, "billing-account", ownerKey),
            OwnerUserId = owner.Id,
            Name = $"Витрина: {ownerKey}",
            IsShowcase = true,
            CreatedAtUtc = owner.CreatedAt,
            UpdatedAtUtc = owner.CreatedAt,
        };
        graph.BillingAccounts.Add(account);
        graph.OrdersSubscriptions.Add(new OrdersSubscription
        {
            Id = ShowcaseIds.For(p, "orders-subscription", spec.Key),
            BillingAccountId = account.Id,
            PlanConfigId = ShowcaseCatalog.OrdersShowcasePlanId,
            PaidUntil = null,
            IsActive = true,
            CreatedAtUtc = owner.CreatedAt,
            UpdatedAtUtc = owner.CreatedAt,
        });

        var createdAt = todayStartUtc.AddDays(-rng.Next(120, 400));
        var staff = new List<AppUser>();
        var staffMembers = new List<CompanyMember>();
        var companyId = ShowcaseIds.For(p, "shop", spec.Key);
        for (var i = 0; i < spec.StaffCount; i++)
        {
            var nameRng = new ShowcaseRandom($"{p}:shop-staff-name:{spec.Key}:{i}");
            var (first, last) = PersonName(nameRng, nameRng.Chance(0.6));
            // For the coffee shop, index 0 is the key of the demo role «сотрудник» (ShowcaseDemoRoles.ShopStaffUserKey).
            var key = $"shop:{spec.Key}:s{i}";
            var joined = createdAt.AddDays(nameRng.Next(1, 60));
            var user = ShowcaseDataset.NewUser(graph, phones, p, key, first, last, joined, "Master");
            staff.Add(user);
            staffMembers.Add(new CompanyMember
            {
                Id = ShowcaseIds.For(p, "member", $"shop:{spec.Key}:s{i}"),
                CompanyId = companyId,
                UserId = user.Id,
                Role = UserRole.Master,
                ProvidesServices = false,
                CommissionPercent = 0,
                JoinedAt = joined,
            });
        }

        var company = new Company
        {
            Id = companyId,
            Name = spec.Name,
            Slug = ShowcaseCatalog.SlugPrefix + spec.SlugBase,
            Description = spec.Description,
            Address = spec.Street,
            // §35.9.2: a shop has a phone (US-35-02) — from the same block as every showcase phone, outside the numbering plan; no e-mail, no map links.
            Phone = FormatPhone(phones.Next()),
            Email = null,
            YandexMapsUrl = null,
            TwoGisUrl = null,
            AllowSelfBooking = false,
            RequirePrepayment = false,
            ShowInPublicListing = true,
            IsActive = true,
            TimeZoneId = spec.TimeZoneId,
            TimeZoneIsManual = false,
            OwnerUserId = owner.Id,
            BillingAccountId = account.Id,
            Kind = CompanyKind.Orders,
            IsShowcase = true,
            CreatedAt = createdAt,
        };
        graph.Companies.Add(company);
        graph.CityNameByCompany[company.Id] = spec.City;
        graph.LogoKeyByCompany[company.Id] = $"logo.shop.{spec.AssetCategory}";
        graph.PhotoKeysByCompany[company.Id] = Enumerable.Range(1, rng.Next(3, 7)).Select(n => $"photo.shop.{spec.AssetCategory}.{n}").ToList();
        graph.Members.Add(new CompanyMember
        {
            Id = ShowcaseIds.For(p, "member", $"shop:{spec.Key}:{ownerKey}"),
            CompanyId = company.Id,
            UserId = owner.Id,
            Role = UserRole.CompanyOwner,
            ProvidesServices = false,
            CommissionPercent = 0,
            JoinedAt = createdAt,
        });
        graph.Members.AddRange(staffMembers);

        var ctx = new ShopCtx(spec, company, account, zone, localToday) { Owner = owner };
        ctx.Staff.AddRange(staff);

        var weekly = new WeeklyHours(spec.Hours.GroupBy(h => h.Day).ToDictionary(
            g => g.Key, g => (IReadOnlyList<TimeInterval>)g.Select(h => new TimeInterval(h.StartMinutes, h.EndMinutes)).ToList()));
        ctx.Settings = new ShopSettings
        {
            CompanyId = company.Id,
            CustomerMode = ShopCustomerMode.Anyone,
            AcceptanceMode = spec.Acceptance,
            AllowCustomerCancel = true,
            TrackStock = spec.TrackStock,
            OrdersRevision = 1,
            WorkingHoursJson = ShopScheduleRules.Serialize(weekly),
            OrdersStopped = false,
            AsapEnabled = spec.AsapEnabled,
            ScheduledEnabled = spec.ScheduledEnabled,
            SlotStepMinutes = spec.SlotStepMinutes,
            PreorderDays = spec.PreorderDays,
            MinPrepMinutes = spec.MinPrepMinutes,
            CustomerWebPushEnabled = true,
            CustomerMessengerEnabled = false,
            // Only the invented name of the seller (§35.9.2): no INN, no OGRN, no legal address, no form — the legal review of the demo is open (L35-2).
            SellerLegalName = spec.SellerLegalName,
            UpdatedAtUtc = createdAt,
            UpdatedByUserId = owner.Id,
        };
        graph.ShopSettings.Add(ctx.Settings);

        BuildCatalog(graph, p, ctx, createdAt, nowUtc);
        BuildMenus(graph, p, ctx, nowUtc);
        BuildSpecialDays(graph, p, ctx, nowUtc);
        return ctx;
    }

    private static void BuildCatalog(ShowcaseGraph graph, string p, ShopCtx shop, DateTime createdAt, DateTime nowUtc)
    {
        var spec = shop.Spec;
        for (var c = 0; c < spec.Categories.Count; c++)
        {
            var categorySpec = spec.Categories[c];
            var category = new ProductCategory
            {
                Id = ShowcaseIds.For(p, "category", $"{spec.Key}:{c}"),
                CompanyId = shop.Company.Id,
                Name = categorySpec.Name,
                Position = c,
                IsHidden = false,
                CreatedAtUtc = createdAt,
                UpdatedAtUtc = createdAt,
            };
            graph.ProductCategories.Add(category);

            for (var i = 0; i < categorySpec.Products.Count; i++)
            {
                var s = categorySpec.Products[i];
                var product = new Product
                {
                    Id = ShowcaseIds.For(p, "product", $"{spec.Key}:{c}:{i}"),
                    CompanyId = shop.Company.Id,
                    CategoryId = category.Id,
                    Name = s.Name,
                    Description = s.Description,
                    Unit = s.Unit,
                    Price = s.Price,
                    PortionText = s.Unit == ProductUnit.Piece ? s.Portion : null,
                    WeightStepGrams = s.Unit == ProductUnit.Weight ? s.StepGrams : null,
                    MinQuantityGrams = s.Unit == ProductUnit.Weight ? s.MinGrams : null,
                    Position = i,
                    IsPublished = true,
                    IsSoldOut = s.SoldOutToday,
                    SoldOutForDate = s.SoldOutToday ? shop.LocalToday : null,
                    AvailableWeekdaysMask = s.WeekdaysMask,
                    CompositionAndAllergens = s.Composition,
                    // The stock is set after the orders are known (FinishShops): at least the reserve of the active ones.
                    StockOnHand = shop.Spec.TrackStock && s.Stock is not null ? 0 : null,
                    CreatedAtUtc = createdAt,
                    UpdatedAtUtc = createdAt,
                };
                graph.Products.Add(product);
                shop.Products.Add(new ProductCtx(product, s));
                if (s.Image is not null) graph.ProductImageKeys[product.Id] = $"product.{spec.AssetCategory}.{s.Image}";
            }
        }
    }

    /// <summary>The canteen: a menu for today and the next five working days (§35.9.2). A date with a menu sells exactly its products.</summary>
    private static void BuildMenus(ShowcaseGraph graph, string p, ShopCtx shop, DateTime nowUtc)
    {
        if (shop.Spec.Key != ShowcaseShopSpecs.Stolovaya.Key) return;

        var dates = new List<DateOnly>();
        for (var date = shop.LocalToday; dates.Count < 6; date = date.AddDays(1))
            if (shop.Spec.Hours.Any(h => h.Day == date.DayOfWeek)) dates.Add(date);

        var perCategory = new[] { 2, 3, 2, 2, 2, 1 }; // soups, mains, sides, salads, drinks, bakery
        foreach (var date in dates)
        {
            var rng = new ShowcaseRandom($"{p}:menu:{shop.Spec.Key}:{date:yyyy-MM-dd}");
            var chosen = new List<ProductCtx>();
            for (var c = 0; c < shop.Spec.Categories.Count; c++)
            {
                var inCategory = shop.Products
                    .Where(pc => pc.Product.CategoryId == graph.ProductCategories.First(x => x.CompanyId == shop.Company.Id && x.Position == c).Id)
                    // Friday's dish is on Friday's menu only; the other days' menus never carry it.
                    .Where(pc => pc.Spec.WeekdaysMask == ShowcaseProductSpec.WeekdayMaskAll || WeekdayMask.Allows(pc.Spec.WeekdaysMask, date))
                    .ToList();
                var take = Math.Min(perCategory[Math.Min(c, perCategory.Length - 1)], inCategory.Count);
                rng.Shuffle(inCategory);
                chosen.AddRange(inCategory.Take(take));
            }

            // The Friday dish goes in on Fridays in addition to the regular picks.
            if (date.DayOfWeek == DayOfWeek.Friday)
                foreach (var fridayDish in shop.Products.Where(pc => pc.Spec.WeekdaysMask != ShowcaseProductSpec.WeekdayMaskAll && WeekdayMask.Allows(pc.Spec.WeekdaysMask, date)))
                    if (!chosen.Contains(fridayDish)) chosen.Add(fridayDish);

            var menu = new ShopDailyMenu
            {
                Id = ShowcaseIds.For(p, "menu", $"{shop.Spec.Key}:{date:yyyy-MM-dd}"),
                CompanyId = shop.Company.Id,
                Date = date,
                UpdatedAtUtc = nowUtc,
                UpdatedByUserId = shop.Owner.Id,
            };
            graph.DailyMenus.Add(menu);
            foreach (var product in chosen)
                graph.DailyMenuItems.Add(new ShopDailyMenuItem { DailyMenuId = menu.Id, ProductId = product.Product.Id });
            shop.MenuByDate[date] = chosen.Select(pc => pc.Product.Id).ToHashSet();
        }
    }

    /// <summary>The flower shop: a holiday in 3–10 days with longer hours (§35.9.2).</summary>
    private static void BuildSpecialDays(ShowcaseGraph graph, string p, ShopCtx shop, DateTime nowUtc)
    {
        if (shop.Spec.Key != ShowcaseShopSpecs.Cvety.Key) return;
        var rng = new ShowcaseRandom($"{p}:special-day:{shop.Spec.Key}");
        var date = shop.LocalToday.AddDays(3 + rng.Next(0, 8));
        var interval = new TimeInterval(8 * 60, 22 * 60);
        graph.SpecialDays.Add(new ShopSpecialDay
        {
            CompanyId = shop.Company.Id,
            Date = date,
            IsClosed = false,
            IntervalsJson = ShopScheduleRules.SerializeIntervals([interval]),
            UpdatedAtUtc = nowUtc,
            UpdatedByUserId = shop.Owner.Id,
        });
        shop.SpecialHours[date] = (interval.StartMinutes, interval.EndMinutes);
    }

    // ── Customers ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Per city: registered customers first (they order more often), then guests. The demo customer is made separately.</summary>
    private static Dictionary<string, List<CustomerCtx>> BuildCustomerPools(ShowcaseGraph graph, ShowcasePhones phones, string p, DateTime nowUtc)
    {
        var pools = new Dictionary<string, List<CustomerCtx>>();
        foreach (var cityPool in ShowcaseShopSpecs.CityPools)
        {
            var list = new List<CustomerCtx>();
            for (var i = 0; i < cityPool.Registered; i++)
            {
                var nameRng = new ShowcaseRandom($"{p}:shop-client-name:{cityPool.City}:{i}");
                var (first, last) = PersonName(nameRng, nameRng.Chance(0.7));
                var user = ShowcaseDataset.NewUser(graph, phones, p, $"shop-client:{cityPool.City}:{i}", first, last, nowUtc.Date.AddDays(-nameRng.Next(20, 400)), "Client");
                list.Add(new CustomerCtx(user.Id, $"{first} {last}", user.PhoneNumber!));
            }

            for (var i = 0; i < cityPool.Guests; i++)
            {
                var nameRng = new ShowcaseRandom($"{p}:shop-guest-name:{cityPool.City}:{i}");
                var (first, last) = PersonName(nameRng, nameRng.Chance(0.7));
                list.Add(new CustomerCtx(null, $"{first} {last}", phones.Next()));
            }
            pools[cityPool.City] = list;
        }
        return pools;
    }

    // ── Orders of the day ────────────────────────────────────────────────────────────────────────────────────

    private static (int Start, int End)? HoursFor(ShopCtx shop, DateOnly date)
    {
        if (shop.SpecialHours.TryGetValue(date, out var special)) return special;
        foreach (var h in shop.Spec.Hours)
            if (h.Day == date.DayOfWeek) return (h.StartMinutes, h.EndMinutes);
        return null;
    }

    private static bool SoldOn(ShopCtx shop, ProductCtx product, DateOnly date) =>
        shop.MenuByDate.TryGetValue(date, out var menu) ? menu.Contains(product.Product.Id) : WeekdayMask.Allows(product.Spec.WeekdaysMask, date);

    private static List<OrderCtx> BuildOrders(string p, ShopCtx shop, List<CustomerCtx> pool, DateTime nowUtc)
    {
        var spec = shop.Spec;
        var result = new List<OrderCtx>();
        var today = shop.LocalToday;
        var lastDay = today.AddDays(Math.Min(FutureDays, spec.ScheduledEnabled ? spec.PreorderDays : 0));

        for (var date = today.AddDays(-PastDays); date <= lastDay; date = date.AddDays(1))
        {
            if (HoursFor(shop, date) is not { } hours) continue;
            var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var range = weekend ? spec.WeekendOrders : spec.WeekdayOrders;
            if (range.Max == 0) continue;
            var dayRng = new ShowcaseRandom($"{p}:shop:{spec.Key}:day:{date:yyyy-MM-dd}");
            var count = dayRng.Next(range.Min, range.Max + 1);
            if (date > today)
            {
                var distance = date.DayNumber - today.DayNumber;
                count = Math.Max(1, (int)Math.Round(count * (distance == 1 ? 0.35 : distance == 2 ? 0.2 : 0.12)));
            }

            for (var i = 0; i < count; i++)
            {
                var order = BuildOrder(p, shop, pool, date, hours, i, nowUtc);
                if (order is not null) result.Add(order);
            }
        }
        return result;
    }

    private static OrderCtx? BuildOrder(string p, ShopCtx shop, List<CustomerCtx> pool, DateOnly date, (int Start, int End) hours, int index, DateTime nowUtc)
    {
        var spec = shop.Spec;
        var today = shop.LocalToday;
        var key = $"{spec.Key}:{date:yyyyMMdd}:{index}";
        var rng = new ShowcaseRandom($"{p}:order:{key}");

        // Every draw is made in a fixed order whatever the branch below takes, so changing one rule never shifts the numbers of another.
        var rKind = rng.NextDouble();
        var rPeak = rng.Next(spec.Peaks.Sum(pk => pk.Weight));
        var rMinute = rng.NextDouble();
        var rNear = rng.NextDouble();
        var rLead = rng.Next(0, 181);
        var rPrevDay = rng.NextDouble();
        var rPrevMinute = rng.Next(0, 781);
        var rSeconds = rng.Next(60);
        var rCustomer = rng.NextDouble();
        var rFate = rng.NextDouble();
        var rEdit = rng.NextDouble();
        var rComment = rng.NextDouble();
        var rCommentPick = rng.Next(ShowcaseShopSpecs.OrderComments.Count);

        // The pickup moment, inside the day's hours, in one of the busy parts of the day.
        var remaining = rPeak;
        var peak = spec.Peaks[^1];
        foreach (var candidate in spec.Peaks)
        {
            if (remaining < candidate.Weight) { peak = candidate; break; }
            remaining -= candidate.Weight;
        }
        var minute = peak.StartMinutes + (int)(rMinute * Math.Max(1, peak.EndMinutes - peak.StartMinutes));

        var asap = spec.AsapEnabled && date <= today && (!spec.ScheduledEnabled || rKind < spec.AsapShare);
        DateTime created, start;
        DateTime? end;
        if (asap)
        {
            var t = Math.Clamp(minute, hours.Start + spec.MinPrepMinutes, hours.End - 5);
            created = LocalToUtc(shop.Zone, date, t - spec.MinPrepMinutes, rSeconds);
            start = created.AddMinutes(spec.MinPrepMinutes);
            end = null;
        }
        else
        {
            var step = spec.SlotStepMinutes;
            var latestStart = hours.End - step;
            var t = Math.Clamp(minute, hours.Start, Math.Max(hours.Start, latestStart));
            t = hours.Start + (t - hours.Start) / step * step;
            start = LocalToUtc(shop.Zone, date, t);
            end = start.AddMinutes(step);
            if (spec.PreorderDays >= 1 && rNear >= 0.62)
            {
                var daysAhead = 1 + Math.Min(spec.PreorderDays - 1, (int)(rPrevDay * spec.PreorderDays));
                created = LocalToUtc(shop.Zone, date.AddDays(-daysAhead), 8 * 60 + rPrevMinute, rSeconds);
            }
            else
            {
                created = start.AddMinutes(-(spec.MinPrepMinutes + rLead)).AddSeconds(-rSeconds);
            }
        }

        // An order of today or later exists already at the moment of the reset: it cannot have been created in the future. One that naturally would have been
        // (an "as soon as possible" order of the afternoon seen from the morning) becomes a pre-order of the same pickup time, or does not exist in a shop without pre-orders.
        if (date >= today && created > nowUtc.AddMinutes(-1))
        {
            if (!spec.ScheduledEnabled) return null;
            if (asap)
            {
                var step = spec.SlotStepMinutes;
                var local = TimeZoneInfo.ConvertTimeFromUtc(start, shop.Zone);
                var t = local.Hour * 60 + local.Minute;
                t = hours.Start + Math.Max(0, t - hours.Start) / step * step;
                start = LocalToUtc(shop.Zone, date, Math.Min(t, Math.Max(hours.Start, hours.End - step)));
                end = start.AddMinutes(step);
            }
            var ago = new ShowcaseRandom($"{p}:order-created:{key}").Next(2, 241);
            created = nowUtc.AddMinutes(-ago);
            if (created >= start) created = start.AddMinutes(-1);
            if (created > nowUtc.AddMinutes(-1)) return null; // a pickup that is itself at the very moment of the reset: no order
        }

        var customer = PickCustomer(pool, rCustomer);

        var lines = PickLines(p, shop, key, date, date >= today);
        if (lines.Count == 0) return null;

        var order = new OrderCtx(key, shop)
        {
            CreatedUtc = created,
            PickupDate = date,
            StartUtc = start,
            EndUtc = end,
            Customer = customer,
            Lines = lines,
            Comment = rComment < CommentShare ? ShowcaseShopSpecs.OrderComments[rCommentPick] : null,
            Edited = rEdit < EditedShare,
        };
        if (date < today)
        {
            order.Fate = rFate switch
            {
                _ when rFate < IssuedShare => PastFate.Issued,
                _ when rFate < IssuedShare + RejectedShare => spec.Acceptance == OrderAcceptanceMode.Manual ? PastFate.Rejected : PastFate.CancelledByShop,
                _ when rFate < IssuedShare + RejectedShare + CancelledByCustomerShare => PastFate.CancelledByCustomer,
                _ when rFate < IssuedShare + RejectedShare + CancelledByCustomerShare + CancelledByShopShare => PastFate.CancelledByShop,
                _ => PastFate.NotPickedUp,
            };
            if (order.Fate != PastFate.Issued) order.Edited = false;
        }
        return order;
    }

    /// <summary>
    /// The distribution of orders over customers is skewed (§35.9.2): a tenth of the pool are regulars with half of the orders, a quarter order from time to time with
    /// a third of them, the rest order once in a while (one to a few times in two months). Registered customers come first in the pool, so they are the regulars:
    /// the customer card of the owner shows a believable history (the lesson of C28-5).
    /// </summary>
    private static CustomerCtx PickCustomer(List<CustomerCtx> pool, double roll)
    {
        var regulars = Math.Max(1, (int)Math.Ceiling(pool.Count * 0.10));
        var occasional = Math.Max(1, (int)Math.Ceiling(pool.Count * 0.25));
        var rest = Math.Max(1, pool.Count - regulars - occasional);
        int index;
        if (roll < 0.50) index = (int)(regulars * Math.Pow(roll / 0.50, 1.6));
        else if (roll < 0.85) index = regulars + (int)(occasional * Math.Pow((roll - 0.50) / 0.35, 1.3));
        else index = regulars + occasional + (int)(rest * ((roll - 0.85) / 0.15));
        return pool[Math.Min(pool.Count - 1, index)];
    }

    private static List<LineCtx> PickLines(string p, ShopCtx shop, string key, DateOnly date, bool active)
    {
        var rng = new ShowcaseRandom($"{p}:order-items:{key}");
        var candidates = shop.Products
            .Where(pc => SoldOn(shop, pc, date))
            // What the demo shows as sold out is in no active order: its stock is zero (§35.9.4).
            .Where(pc => !(active && pc.Spec.SoldOutToday))
            .ToList();
        var roll = rng.NextDouble();
        var wanted = roll < 0.45 ? 1 : roll < 0.78 ? 2 : roll < 0.93 ? 3 : 4;
        var lines = new List<LineCtx>();
        while (lines.Count < wanted && candidates.Count > 0)
        {
            var total = candidates.Sum(c => c.Spec.Popularity);
            var pick = rng.Next(total);
            var chosen = candidates[^1];
            foreach (var candidate in candidates)
            {
                if (pick < candidate.Spec.Popularity) { chosen = candidate; break; }
                pick -= candidate.Spec.Popularity;
            }
            candidates.Remove(chosen);

            int quantity;
            if (chosen.Spec.Unit == ProductUnit.Weight)
                quantity = Math.Min(OrderQuantityRules.MaxGrams, chosen.Spec.MinGrams + chosen.Spec.StepGrams * rng.Next(0, 4));
            else
            {
                var q = rng.NextDouble();
                quantity = q < 0.70 ? 1 : q < 0.92 ? 2 : 3;
            }
            lines.Add(new LineCtx(chosen, quantity));
        }
        return lines;
    }

    // ── The orders of the demo customer (§35.9.3) ────────────────────────────────────────────────────────────

    private static List<OrderCtx> BuildDemoCustomerOrders(string p, List<ShopCtx> shops, CustomerCtx customer, DateTime nowUtc)
    {
        var result = new List<OrderCtx>();
        // The shops of the demo customer (ShowcaseDemoRoles.CustomerShopKeys): the coffee shop (the flagship) and the bakery.
        var kofeinya = shops.Single(s => s.Spec.Key == ShowcaseDemoRoles.CustomerShopKeys[0]);
        var pekarnya = shops.Single(s => s.Spec.Key == ShowcaseDemoRoles.CustomerShopKeys[1]);

        // Completed orders of the last 30 days: six in the coffee shop (one of them cancelled by the customer), three in the bakery.
        var history = new (ShopCtx Shop, int DaysAgo, int StartMinutes, PastFate Fate)[]
        {
            (kofeinya, 2, 8 * 60 + 15, PastFate.Issued), (kofeinya, 5, 13 * 60, PastFate.Issued), (kofeinya, 9, 8 * 60 + 30, PastFate.CancelledByCustomer),
            (kofeinya, 14, 9 * 60, PastFate.Issued), (kofeinya, 20, 17 * 60 + 15, PastFate.Issued), (kofeinya, 27, 8 * 60, PastFate.Issued),
            (pekarnya, 4, 17 * 60 + 30, PastFate.Issued), (pekarnya, 11, 8 * 60, PastFate.Issued), (pekarnya, 23, 18 * 60, PastFate.Issued),
        };
        for (var i = 0; i < history.Length; i++)
        {
            var (shop, daysAgo, startMinutes, fate) = history[i];
            var date = shop.LocalToday.AddDays(-daysAgo);
            var key = $"demo-customer:{shop.Spec.Key}:{i}";
            var start = LocalToUtc(shop.Zone, date, startMinutes);
            // The coffee shop alternates "as soon as possible" and pre-orders; the bakery has pre-orders only.
            var slot = !shop.Spec.AsapEnabled || i % 2 == 1;
            var order = NewDemoOrder(p, shop, customer, key, date, start, slot, nowUtc);
            order.Fate = fate;
            result.Add(order);
        }

        // Today: one active order of the coffee shop with a slot before closing (21:30–22:00), so it is active the whole day.
        var todayKofeinya = NewDemoOrder(p, kofeinya, customer, "demo-customer:kofeinya:today", kofeinya.LocalToday,
            LocalToUtc(kofeinya.Zone, kofeinya.LocalToday, 21 * 60 + 30), slot: true, nowUtc);
        result.Add(todayKofeinya);

        // Tomorrow: one pre-order of the bakery (it accepts by itself).
        var tomorrow = pekarnya.LocalToday.AddDays(1);
        result.Add(NewDemoOrder(p, pekarnya, customer, "demo-customer:pekarnya:tomorrow", tomorrow,
            LocalToUtc(pekarnya.Zone, tomorrow, 10 * 60), slot: true, nowUtc));
        return result;
    }

    private static OrderCtx NewDemoOrder(string p, ShopCtx shop, CustomerCtx customer, string key, DateOnly date, DateTime start, bool slot, DateTime nowUtc)
    {
        var spec = shop.Spec;
        var rng = new ShowcaseRandom($"{p}:order:{key}");
        DateTime created;
        DateTime? end;
        if (slot)
        {
            end = start.AddMinutes(spec.SlotStepMinutes);
            created = date < shop.LocalToday
                ? start.AddMinutes(-(spec.MinPrepMinutes + rng.Next(20, 240)))
                : nowUtc.AddMinutes(-rng.Next(60, 181));
        }
        else
        {
            end = null;
            created = start.AddMinutes(-spec.MinPrepMinutes);
        }
        if (created >= start) created = start.AddMinutes(-spec.MinPrepMinutes);

        var lines = PickLines(p, shop, key, date, date >= shop.LocalToday);
        if (lines.Count == 0) lines.Add(new LineCtx(shop.Products[0], 1));
        return new OrderCtx(key, shop)
        {
            CreatedUtc = created,
            PickupDate = date,
            StartUtc = start,
            EndUtc = end,
            Customer = customer,
            Lines = lines,
        };
    }

    // ── Numbers, journal, entities ───────────────────────────────────────────────────────────────────────────

    /// <summary>The number of an order is unique inside (shop, pickup day) and grows with the creation moment (C24-11): 1…n in the order of creation.</summary>
    private static void Number(List<OrderCtx> orders)
    {
        foreach (var group in orders.GroupBy(o => (o.Shop.Company.Id, o.PickupDate)))
        {
            var number = 0;
            foreach (var order in group.OrderBy(o => o.CreatedUtc).ThenBy(o => o.Id))
                order.Number = ++number;
        }
    }

    /// <summary>
    /// §35.10.2: at the reset (04:00) all of today's orders are New or Accepted, except two whose slot is at the opening of the shop (the first quarter of an hour):
    /// they were "prepared the evening before" and are already Ready. The task continues from there (the next step of their plan is the handover).
    /// </summary>
    private static void MarkPreparedEarly(List<ShopCtx> shops, List<OrderCtx> orders, DateTime nowUtc)
    {
        foreach (var shop in shops)
        {
            if (HoursFor(shop, shop.LocalToday) is not { } hours) continue;
            var atOpening = orders
                .Where(o => o.Shop == shop && o.PickupDate == shop.LocalToday && o.EndUtc is not null && o.CreatedUtc < nowUtc.AddMinutes(-4))
                .Where(o =>
                {
                    var local = TimeZoneInfo.ConvertTimeFromUtc(o.StartUtc, shop.Zone);
                    var minutes = local.Hour * 60 + local.Minute;
                    return minutes >= hours.Start && minutes <= hours.Start + 15;
                })
                .OrderBy(o => o.StartUtc).ThenBy(o => o.Key, StringComparer.Ordinal)
                .Take(2);
            foreach (var order in atOpening) order.PreparedEarly = true;
        }
    }

    /// <summary>
    /// US-35-02: the column «Готовы к выдаче» is never empty right after a reset, whatever the hour of it (a manual reset before a meeting is made by day, §35.10.2). The
    /// planned timeline gives a Ready order only for a pickup in the next ~5–15 minutes, so a shop that has none at this moment (and none prepared at the opening) gets
    /// one: the nearest upcoming order of today is "prepared in advance"; when the day is already over, the last simple order of today is "not collected yet". The reset
    /// at 04:00 is not touched: there the two orders at the opening are already marked. The orders of the demo customer are never chosen: their scenario is fixed.
    /// </summary>
    private static void EnsureReadyOrder(List<ShopCtx> shops, List<OrderCtx> orders, DateTime nowUtc)
    {
        foreach (var shop in shops)
        {
            var today = orders
                .Where(o => o.Shop == shop && o.PickupDate == shop.LocalToday && !o.Key.StartsWith("demo-customer:", StringComparison.Ordinal))
                .Select(o => (Order: o, Plan: ShowcaseOrderTimeline.Plan(o.Id, o.CreatedUtc, o.StartUtc, shop.Spec.Acceptance)))
                .ToList();

            // Ready already by the timeline, or prepared in advance (the mark of the opening only counts while the planned moment of readiness is still ahead).
            if (today.Any(t => t.Plan.ReadyAtUtc > nowUtc ? t.Order.PreparedEarly : t.Plan.ClosedAtUtc > nowUtc)) continue;

            var upcoming = today
                .Where(t => t.Plan.ReadyAtUtc > nowUtc && t.Order.CreatedUtc < nowUtc.AddMinutes(-4))
                .OrderBy(t => t.Order.StartUtc).ThenBy(t => t.Order.Key, StringComparer.Ordinal)
                .Select(t => t.Order)
                .FirstOrDefault();
            if (upcoming is not null)
            {
                upcoming.PreparedEarly = true;
                continue;
            }

            // Only what the demo task can close later: no weighed lines and no stock to write off (it leaves such an order for a visitor).
            var finished = today
                .Where(t => t.Plan.ClosedAtUtc <= nowUtc && t.Order.Lines.All(l => l.Product.Spec.Unit != ProductUnit.Weight && !ReservesStock(shop, l)))
                .OrderByDescending(t => t.Order.StartUtc).ThenBy(t => t.Order.Key, StringComparer.Ordinal)
                .Select(t => t.Order)
                .FirstOrDefault();
            if (finished is not null) finished.HeldReady = true;
        }
    }

    private static AppUser StaffOf(OrderCtx order, ShowcaseRandom rng, bool live)
    {
        var all = new List<AppUser> { order.Shop.Owner };
        all.AddRange(order.Shop.Staff);
        // The moves of today's orders are made by the demo employee, like the task that continues them later (§35.10.3).
        if (live) return order.Shop.Staff.Count > 0 ? order.Shop.Staff[0] : order.Shop.Owner;
        return all[rng.Next(all.Count)];
    }

    private static EventDraft StaffEvent(
        OrderEventKind kind, DateTime at, OrderStatus from, OrderStatus to, AppUser staff, string? reason = null, string? changesJson = null,
        decimal? totalBefore = null, decimal? totalAfter = null) =>
        new(kind, at, from, to, OrderActorKind.Staff, staff.Id, $"{staff.FirstName} {staff.LastName}".Trim(), reason, null, changesJson, totalBefore, totalAfter);

    private static EventDraft CustomerEvent(OrderEventKind kind, DateTime at, OrderStatus? from, OrderStatus? to, CustomerCtx customer) =>
        new(kind, at, from, to, customer.Kind, customer.UserId, customer.Name);

    private static decimal LineEstimate(LineCtx line) => OrderMoney.LineTotal(line.Product.Spec.Unit, line.Product.Product.Price, line.Quantity);

    private static decimal Total(IEnumerable<LineCtx> lines) => OrderMoney.Sum(lines.Select(LineEstimate));

    /// <summary>Makes the journal of the order, changing its lines on the way when the shop edited it.</summary>
    private static List<EventDraft> BuildJournal(OrderCtx order, string p, DateTime nowUtc, OrderAcceptanceMode mode)
    {
        var today = order.Shop.LocalToday;
        var live = order.PickupDate >= today;
        var initial = mode == OrderAcceptanceMode.Auto ? OrderStatus.Accepted : OrderStatus.New;
        var fateRng = new ShowcaseRandom($"{p}:order-fate:{order.Key}");
        var events = new List<EventDraft> { CustomerEvent(OrderEventKind.Created, order.CreatedUtc, null, initial, order.Customer) };

        if (!live && order.Fate is PastFate.Rejected or PastFate.CancelledByCustomer or PastFate.CancelledByShop)
        {
            BuildEarlyEnd(order, events, fateRng, mode, initial);
            return events;
        }

        // Handed over or not collected (past), or whatever the timeline has reached at this moment (today and later).
        ShowcaseOrderFate? forced = live ? null : order.Fate == PastFate.NotPickedUp ? ShowcaseOrderFate.NotPickedUp : ShowcaseOrderFate.Issued;
        var plan = ShowcaseOrderTimeline.Plan(order.Id, order.CreatedUtc, order.StartUtc, mode, forced);
        IReadOnlyList<ShowcaseOrderStep> steps = live ? ShowcaseOrderTimeline.StateAt(plan, nowUtc).Steps : ShowcaseOrderTimeline.AllSteps(plan);
        if (live && order.PreparedEarly && plan.ReadyAtUtc > nowUtc) steps = PreparedEarlySteps(order, plan, nowUtc);
        else if (live && order.HeldReady) steps = [.. steps.Where(s => s.To is not (OrderStatus.Issued or OrderStatus.NotPickedUp))];

        if (order.Edited && steps.Count > 0 && plan.Fate == ShowcaseOrderFate.Issued)
        {
            var editAt = order.CreatedUtc + (steps[0].AtUtc - order.CreatedUtc) / 2;
            var editRng = new ShowcaseRandom($"{p}:order-edit:{order.Key}");
            var before = Total(order.Lines);
            var change = EditOneLine(order, editRng);
            var staff = StaffOf(order, editRng, live);
            events.Add(StaffEvent(OrderEventKind.Edited, editAt, initial, initial, staff, changesJson: OrderChangeLog.SerializeEdit([change]),
                totalBefore: before, totalAfter: Total(order.Lines)) with { Comment = ShowcaseShopSpecs.EditComments[editRng.Next(ShowcaseShopSpecs.EditComments.Count)] });
        }

        var stepRng = new ShowcaseRandom($"{p}:order-staff:{order.Key}");
        foreach (var step in steps)
        {
            var staff = StaffOf(order, stepRng, live);
            if (step.Kind == OrderEventKind.Issued)
            {
                var (changesJson, final) = IssueLines(order, p);
                events.Add(StaffEvent(OrderEventKind.Issued, step.AtUtc, step.From, step.To, staff, changesJson: changesJson,
                    totalBefore: Total(order.Lines), totalAfter: final));
            }
            else
            {
                events.Add(StaffEvent(step.Kind, step.AtUtc, step.From, step.To, staff));
            }
        }
        return events;
    }

    /// <summary>Accepted soon after creation and ready well before the moment of the reset: the order that was "prepared the evening before".</summary>
    private static IReadOnlyList<ShowcaseOrderStep> PreparedEarlySteps(OrderCtx order, ShowcaseOrderPlan plan, DateTime nowUtc)
    {
        var steps = new List<ShowcaseOrderStep>(2);
        var span = nowUtc - order.CreatedUtc;
        var acceptedAt = order.CreatedUtc + span / 4;
        var readyAt = order.CreatedUtc + span / 2;
        if (plan.Mode != OrderAcceptanceMode.Auto)
            steps.Add(new ShowcaseOrderStep(OrderEventKind.Accepted, OrderStatus.New, OrderStatus.Accepted, acceptedAt));
        steps.Add(new ShowcaseOrderStep(OrderEventKind.MarkedReady, OrderStatus.Accepted, OrderStatus.Ready, readyAt));
        return steps;
    }

    /// <summary>Refused by the shop, cancelled by the customer or by the shop — a short life that ends before anything is prepared.</summary>
    private static void BuildEarlyEnd(OrderCtx order, List<EventDraft> events, ShowcaseRandom rng, OrderAcceptanceMode mode, OrderStatus initial)
    {
        var staff = StaffOf(order, rng, live: false);
        var current = initial;
        var at = order.CreatedUtc;
        var maxBeforeStart = Math.Max(2, (int)(order.StartUtc - order.CreatedUtc).TotalMinutes - 1);

        if (order.Fate == PastFate.Rejected)
        {
            at = at.AddMinutes(Math.Min(rng.Next(1, 11), maxBeforeStart));
            events.Add(StaffEvent(OrderEventKind.Rejected, at, current, OrderStatus.Rejected, staff,
                reason: ShowcaseShopSpecs.RejectReasons[rng.Next(ShowcaseShopSpecs.RejectReasons.Count)]));
            return;
        }

        // The shop accepts first (half of the customer cancellations, all cancellations by the shop), unless acceptance is automatic.
        if (mode == OrderAcceptanceMode.Manual && (order.Fate == PastFate.CancelledByShop || rng.Chance(0.5)))
        {
            at = at.AddMinutes(Math.Min(rng.Next(1, 5), maxBeforeStart));
            events.Add(StaffEvent(OrderEventKind.Accepted, at, OrderStatus.New, OrderStatus.Accepted, staff));
            current = OrderStatus.Accepted;
        }

        if (order.Fate == PastFate.CancelledByCustomer)
        {
            var cancelAt = at.AddMinutes(Math.Max(1, Math.Min(rng.Next(3, 46), maxBeforeStart)));
            events.Add(CustomerEvent(OrderEventKind.CancelledByCustomer, cancelAt, current, OrderStatus.CancelledByCustomer, order.Customer));
            return;
        }

        var shopCancelAt = at.AddMinutes(rng.Next(3, 26));
        events.Add(StaffEvent(OrderEventKind.CancelledByShop, shopCancelAt, current, OrderStatus.CancelledByShop, staff,
            reason: ShowcaseShopSpecs.ShopCancelReasons[rng.Next(ShowcaseShopSpecs.ShopCancelReasons.Count)]));
    }

    /// <summary>The shop changed one line by one step (one piece, or one weight step): the journal keeps "was → became", the order keeps the new quantity.</summary>
    private static ChangeEntry EditOneLine(OrderCtx order, ShowcaseRandom rng)
    {
        var line = order.Lines[rng.Next(order.Lines.Count)];
        var spec = line.Product.Spec;
        var product = line.Product.Product;
        var before = new ChangeSide(line.Quantity, product.Price, spec.Unit);
        if (spec.Unit == ProductUnit.Weight)
            line.Quantity = line.Quantity - spec.StepGrams >= spec.MinGrams ? line.Quantity - spec.StepGrams : line.Quantity + spec.StepGrams;
        else
            line.Quantity = line.Quantity > 1 ? line.Quantity - 1 : line.Quantity + 1;
        return new ChangeEntry(product.Name, before, new ChangeSide(line.Quantity, product.Price, spec.Unit));
    }

    /// <summary>Fixes the actual quantities at the issue (a weight line: ±10 % of the ordered, in steps of 10 g) and returns the journal's stock write-off and the final total.</summary>
    private static (string? ChangesJson, decimal FinalTotal) IssueLines(OrderCtx order, string p)
    {
        var rng = new ShowcaseRandom($"{p}:order-weight:{order.Key}");
        var writeOffs = new List<StockWriteOff>();
        var finals = new List<decimal>();
        foreach (var line in order.Lines)
        {
            var spec = line.Product.Spec;
            if (spec.Unit == ProductUnit.Weight)
            {
                var factor = 0.9 + 0.2 * rng.NextDouble();
                var actual = Math.Max(10, (int)Math.Round(line.Quantity * factor / 10.0) * 10);
                if (actual == line.Quantity) actual += 10; // "by the actual weight" must be visibly not the ordered one
                line.Actual = actual;
            }
            else
            {
                line.Actual = line.Quantity;
            }
            finals.Add(OrderMoney.LineTotal(spec.Unit, line.Product.Product.Price, line.Actual.Value));
            if (ReservesStock(order.Shop, line)) writeOffs.Add(new StockWriteOff(line.Product.Product.Name, spec.Unit, line.Actual.Value, line.Actual.Value, Zeroed: false));
        }
        return (writeOffs.Count > 0 ? OrderChangeLog.SerializeIssue(new IssueLog(writeOffs)) : null, OrderMoney.Sum(finals));
    }

    private static bool ReservesStock(ShopCtx shop, LineCtx line) => shop.Spec.TrackStock && line.Product.Spec.Stock is not null;

    private static void WriteOrder(ShowcaseGraph graph, string p, OrderCtx o, DateTime nowUtc)
    {
        var shop = o.Shop;
        var mode = shop.Spec.Acceptance;
        var journal = BuildJournal(o, p, nowUtc, mode);

        var order = new Order
        {
            Id = o.Id,
            CompanyId = shop.Company.Id,
            Number = o.Number,
            BusinessDate = LocalDate(shop.Zone, o.CreatedUtc),
            PickupKind = o.Kind,
            PickupDate = o.PickupDate,
            PickupStartUtc = o.StartUtc,
            PickupEndUtc = o.EndUtc,
            NotifyByMessenger = false,
            PublicToken = PublicOrderToken.Encode(
                [.. ShowcaseIds.For(p, "order-token", $"{o.Key}:a").ToByteArray(), .. ShowcaseIds.For(p, "order-token", $"{o.Key}:b").ToByteArray()]),
            Status = mode == OrderAcceptanceMode.Auto ? OrderStatus.Accepted : OrderStatus.New,
            CustomerKind = o.Customer.Kind,
            CustomerUserId = o.Customer.UserId,
            CustomerName = o.Customer.Name,
            CustomerPhone = o.Customer.Phone,
            CustomerPhoneVerified = false,
            Comment = o.Comment,
            AcceptanceModeSnapshot = mode,
            AllowCustomerCancelSnapshot = shop.Settings.AllowCustomerCancel,
            CustomerModeSnapshot = shop.Settings.CustomerMode,
            IdempotencyKey = ShowcaseIds.For(p, "order-idem", o.Key),
            CreatedAtUtc = o.CreatedUtc,
        };

        // The fields come from the journal, so the status and the last event can never disagree (§35.9.4).
        for (var i = 0; i < journal.Count; i++)
        {
            var e = journal[i];
            graph.OrderEvents.Add(new OrderEvent
            {
                Id = ShowcaseIds.For(p, "order-event", $"{o.Key}:{i}"),
                OrderId = order.Id,
                CompanyId = order.CompanyId,
                Kind = e.Kind,
                OccurredAtUtc = e.AtUtc,
                ActorKind = e.Actor,
                ActorUserId = e.ActorUserId,
                ActorNameSnapshot = e.ActorName,
                FromStatus = e.From,
                ToStatus = e.To,
                Reason = e.Reason,
                Comment = e.Comment,
                ChangesJson = e.ChangesJson,
                TotalBefore = e.TotalBefore,
                TotalAfter = e.TotalAfter,
                VisibleToCustomer = true,
            });

            if (e.To is { } to) order.Status = to;
            switch (e.Kind)
            {
                case OrderEventKind.Accepted: order.AcceptedAtUtc = e.AtUtc; break;
                case OrderEventKind.MarkedReady: order.ReadyAtUtc = e.AtUtc; break;
                case OrderEventKind.Rejected or OrderEventKind.CancelledByShop: order.StatusReason = e.Reason; break;
                case OrderEventKind.Issued: order.FinalTotal = e.TotalAfter; break;
            }
            if (OrderStateMachine.IsTerminal(order.Status) && e.Kind != OrderEventKind.Created && e.Kind != OrderEventKind.Edited) order.CompletedAtUtc = e.AtUtc;
            order.UpdatedAtUtc = e.AtUtc;
        }
        if (mode == OrderAcceptanceMode.Auto) order.AcceptedAtUtc = o.CreatedUtc;
        order.IsModifiedByShop = journal.Any(e => e.Kind == OrderEventKind.Edited);
        order.Version = journal.Count;

        for (var i = 0; i < o.Lines.Count; i++)
        {
            var line = o.Lines[i];
            var spec = line.Product.Spec;
            var product = line.Product.Product;
            var estimate = LineEstimate(line);
            graph.OrderItems.Add(new OrderItem
            {
                Id = ShowcaseIds.For(p, "order-item", $"{o.Key}:{i}"),
                OrderId = order.Id,
                Position = i,
                ProductId = product.Id,
                NameSnapshot = product.Name,
                Unit = spec.Unit,
                UnitPrice = product.Price,
                PortionTextSnapshot = product.PortionText,
                WeightStepGrams = product.WeightStepGrams,
                QuantityOrdered = line.Quantity,
                QuantityActual = line.Actual,
                LineTotalEstimated = estimate,
                LineTotalFinal = line.Actual is { } actual ? OrderMoney.LineTotal(spec.Unit, product.Price, actual) : null,
                ReservesStock = ReservesStock(shop, line),
            });
        }
        order.EstimatedTotal = Total(o.Lines);
        order.HasWeightItems = o.Lines.Any(l => l.Product.Spec.Unit == ProductUnit.Weight);
        graph.Orders.Add(order);
    }

    // ── What depends on all the orders: stock, counters, usage, revision, notes ──────────────────────────────

    private static void FinishShops(ShowcaseGraph graph, string p, List<ShopCtx> shops, List<OrderCtx> orders, DateTime nowUtc)
    {
        var itemsByOrder = graph.OrderItems.GroupBy(i => i.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        var eventsByShop = graph.OrderEvents.GroupBy(e => e.CompanyId).ToDictionary(g => g.Key, g => g.Count());

        foreach (var shop in shops)
        {
            var shopOrders = graph.Orders.Where(o => o.CompanyId == shop.Company.Id).ToList();

            // Stock: at least the reserve of the active orders (more, so that the shop is not "at the limit" at once); the products shown as sold out have none.
            if (shop.Spec.TrackStock)
            {
                var reserved = new Dictionary<Guid, int>();
                foreach (var order in shopOrders.Where(o => OrderStateMachine.IsActive(o.Status)))
                    foreach (var item in itemsByOrder[order.Id].Where(i => i.ReservesStock && i.ProductId is not null))
                        reserved[item.ProductId!.Value] = reserved.GetValueOrDefault(item.ProductId.Value) + item.QuantityOrdered;
                foreach (var product in shop.Products.Where(pc => pc.Product.StockOnHand is not null))
                    product.Product.StockOnHand = product.Spec.SoldOutToday
                        ? 0
                        : reserved.GetValueOrDefault(product.Product.Id) + (product.Spec.Stock ?? 0);
            }

            foreach (var day in shopOrders.GroupBy(o => o.PickupDate))
                graph.OrderDailyCounters.Add(new OrderDailyCounter { CompanyId = shop.Company.Id, PickupDate = day.Key, LastNumber = day.Max(o => o.Number) });

            foreach (var month in shopOrders.GroupBy(o => { var d = LocalDate(shop.Zone, o.CreatedAtUtc); return new DateOnly(d.Year, d.Month, 1); }))
                graph.OrderMonthlyUsages.Add(new OrderMonthlyUsage { BillingAccountId = shop.Account.Id, Month = month.Key, Count = month.Count() });

            shop.Settings.OrdersRevision = Math.Max(1, eventsByShop.GetValueOrDefault(shop.Company.Id));

            // Notes about regular customers: a tenth of those with several orders.
            var regulars = shopOrders.Where(o => o.CustomerPhone is not null).GroupBy(o => o.CustomerPhone!)
                .Select(g => (Phone: g.Key, Count: g.Count())).Where(t => t.Count >= 3)
                .OrderByDescending(t => t.Count).ThenBy(t => t.Phone, StringComparer.Ordinal).ToList();
            var noteCount = regulars.Count / 10;
            for (var i = 0; i < noteCount; i++)
            {
                var noteRng = new ShowcaseRandom($"{p}:customer-note:{shop.Spec.Key}:{regulars[i].Phone}");
                var when = nowUtc.AddDays(-noteRng.Next(1, 40));
                graph.ShopCustomerNotes.Add(new ShopCustomerNote
                {
                    Id = ShowcaseIds.For(p, "customer-note", $"{shop.Spec.Key}:{regulars[i].Phone}"),
                    CompanyId = shop.Company.Id,
                    Phone = regulars[i].Phone,
                    Text = ShowcaseShopSpecs.CustomerNotes[noteRng.Next(ShowcaseShopSpecs.CustomerNotes.Count)],
                    CreatedAtUtc = when,
                    UpdatedAtUtc = when,
                    UpdatedByUserId = shop.Owner.Id,
                    UpdatedByName = $"{shop.Owner.FirstName} {shop.Owner.LastName}".Trim(),
                });
            }
        }
    }
}
