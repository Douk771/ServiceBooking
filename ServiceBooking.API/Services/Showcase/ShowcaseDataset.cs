using ServiceBooking.API.Services.Showcase.Tariffs;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Showcase;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.2–§575.4 — builds the showcase graph. A pure function of <c>(profile, nowUtc)</c>: no database, no clock, no global state.
/// Names, companies, services, masters, schedules, slugs and ids depend on the PROFILE only (never on the date); the dates of schedules and bookings depend on
/// "today" in each company's own time zone. Two calls with the same arguments give the same graph, field by field.
/// </summary>
public static class ShowcaseDataset
{
    private const int GridMinutes = 30;

    public static ShowcaseGraph Build(ShowcaseProfile profile, DateTime nowUtc)
    {
        var graph = new ShowcaseGraph();
        var phones = new ShowcasePhones();
        var p = profile.Name;

        // ── People shared by the whole showcase: owners come with their companies; clients are a pool split between the companies. ──
        var owners = new Dictionary<string, (AppUser User, BillingAccount Account)>();
        var companiesByKey = new Dictionary<string, Company>();
        var schedulesByKey = new Dictionary<string, List<MasterSchedule>>();
        foreach (var spec in ShowcaseSpecs.Companies)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(spec.TimeZoneId);
            var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz));
            var todayStartUtc = LocalToUtc(localToday, TimeOnly.MinValue, tz);
            var rng = new ShowcaseRandom($"{p}:company:{spec.Key}");

            // Owner and billing account (one account per owner: the network of two points shares it).
            if (!owners.TryGetValue(spec.OwnerKey, out var owner))
            {
                var ownerUser = NewUser(graph, phones, p, spec.OwnerKey, spec.OwnerFirstName, spec.OwnerLastName, todayStartUtc.AddDays(-rng.Next(200, 500)), "CompanyOwner");
                var account = new BillingAccount
                {
                    Id = ShowcaseIds.For(p, "billing-account", spec.OwnerKey),
                    OwnerUserId = ownerUser.Id,
                    Name = $"Витрина: {spec.OwnerKey}",
                    IsShowcase = true,
                    CreatedAtUtc = ownerUser.CreatedAt,
                    UpdatedAtUtc = ownerUser.CreatedAt,
                };
                graph.BillingAccounts.Add(account);
                // The demo owner sits on the PUBLIC "Салон" tariff, paid 30 days ahead, so that "Ваша подписка" shows a real tariff (§579.4); every other
                // owner sits on the hidden service tariff of the showcase with no end date.
                var isDemoOwner = profile.DemoRoles && spec.OwnerKey == ShowcaseDemoRoles.OwnerUserKey;
                graph.Subscriptions.Add(new AccountSubscription
                {
                    Id = ShowcaseIds.For(p, "subscription", spec.OwnerKey),
                    OwnerUserId = ownerUser.Id,
                    BillingAccountId = account.Id,
                    PlanConfigId = isDemoOwner ? ZapisTariffCatalog.SalonId : ShowcaseCatalog.ShowcasePlanId,
                    PaidUntil = isDemoOwner ? todayStartUtc.AddDays(30) : null,
                    IsActive = true,
                    CreatedAt = ownerUser.CreatedAt,
                    UpdatedAt = ownerUser.CreatedAt,
                });
                owner = (ownerUser, account);
                owners[spec.OwnerKey] = owner;
            }

            var company = new Company
            {
                Id = ShowcaseIds.For(p, "company", spec.Key),
                Name = spec.Name,
                Slug = ShowcaseCatalog.SlugPrefix + spec.SlugBase,
                Description = spec.Description,
                Address = spec.Street,
                // §574.4: no phone, no e-mail, no map links — a fictional company must not publish a contact that could ring somewhere.
                Phone = null,
                Email = null,
                YandexMapsUrl = null,
                TwoGisUrl = null,
                AllowSelfBooking = true,
                RequirePrepayment = false,
                ShowInPublicListing = true,
                IsActive = true,
                BookingHorizonDays = 30,
                ClientRescheduleMinHours = 2,
                TimeZoneId = spec.TimeZoneId,
                TimeZoneIsManual = false,
                OwnerUserId = owner.User.Id,
                BillingAccountId = owner.Account.Id,
                Kind = CompanyKind.Services,
                IsShowcase = true,
                ShowcaseBookingOpen = spec.BookingOpen,
                CreatedAt = todayStartUtc.AddDays(-rng.Next(120, 400)),
            };
            graph.Companies.Add(company);
            companiesByKey[spec.Key] = company;
            graph.CityNameByCompany[company.Id] = spec.City;
            graph.LogoKeyByCompany[company.Id] = $"logo.{spec.Category}";
            graph.PhotoKeysByCompany[company.Id] = Enumerable.Range(1, rng.Next(3, 7)).Select(n => $"photo.{spec.Category}.{n}").ToList();

            graph.Members.Add(new CompanyMember
            {
                Id = ShowcaseIds.For(p, "member", $"{spec.Key}:{spec.OwnerKey}"),
                CompanyId = company.Id,
                UserId = owner.User.Id,
                Role = UserRole.CompanyOwner,
                ProvidesServices = spec.OwnerProvidesServices,
                Bio = spec.OwnerProvidesServices ? ShowcaseSpecs.Bio(spec.Category, rng.Next(6, 15)) : null,
                CommissionPercent = 0,
                JoinedAt = company.CreatedAt,
            });

            // Services.
            var services = new List<Service>();
            for (var i = 0; i < spec.Services.Count; i++)
            {
                var s = spec.Services[i];
                var service = new Service
                {
                    Id = ShowcaseIds.For(p, "service", $"{spec.Key}:{i}"),
                    CompanyId = company.Id,
                    Name = s.Name,
                    Description = s.Description,
                    Price = s.Price,
                    DurationMinutes = s.Minutes,
                    IsActive = true,
                    CreatedAt = company.CreatedAt,
                };
                services.Add(service);
                graph.Services.Add(service);
                graph.ServiceImageKeys[service.Id] = s.Image;
            }

            // Masters (the owner of a one-person company is the master).
            var masters = new List<MasterCtx>();
            if (spec.OwnerProvidesServices)
                masters.Add(new MasterCtx(owner.User, $"{spec.Key}:m0", null));
            for (var i = masters.Count; i < spec.MasterCount; i++)
            {
                var nameRng = new ShowcaseRandom($"{p}:master-name:{spec.Key}:{i}");
                var female = spec.Category != "barber" ? nameRng.Chance(0.85) : nameRng.Chance(0.05);
                var (first, last) = PersonName(nameRng, female);
                var user = NewUser(graph, phones, p, $"{spec.Key}:m{i}", first, last, company.CreatedAt.AddDays(nameRng.Next(1, 60)), "Master");
                var bio = ShowcaseSpecs.Bio(spec.Category, nameRng.Next(2, 16));
                var commission = 30 + 5 * nameRng.Next(0, 5); // 30..50
                graph.Members.Add(new CompanyMember
                {
                    Id = ShowcaseIds.For(p, "member", $"{spec.Key}:m{i}"),
                    CompanyId = company.Id,
                    UserId = user.Id,
                    Role = UserRole.Master,
                    ProvidesServices = true,
                    Bio = bio,
                    CommissionPercent = commission,
                    JoinedAt = company.CreatedAt.AddDays(nameRng.Next(1, 60)),
                });
                masters.Add(new MasterCtx(user, $"{spec.Key}:m{i}", commission));
            }

            AssignServices(graph, p, spec, company, masters, services);
            schedulesByKey[spec.Key] = BuildSchedules(graph, profile, spec, company, tz, localToday, masters);
        }

        // ── Clients: the pool is built after the staff so that adding a client does not shift a master's phone. ──
        var registered = new List<AppUser>();
        for (var i = 0; i < profile.RegisteredClients; i++)
        {
            var nameRng = new ShowcaseRandom($"{p}:client-name:{i}");
            var (first, last) = PersonName(nameRng, nameRng.Chance(0.75));
            var created = LocalToUtc(DateOnly.FromDateTime(nowUtc), TimeOnly.MinValue, TimeZoneInfo.Utc).AddDays(-nameRng.Next(20, 400));
            registered.Add(NewUser(graph, phones, p, $"client:{i}", first, last, created, "Client"));
        }
        var guests = new List<(string Name, string Phone)>();
        for (var i = 0; i < profile.GuestClients; i++)
        {
            var nameRng = new ShowcaseRandom($"{p}:guest-name:{i}");
            var (first, last) = PersonName(nameRng, nameRng.Chance(0.75));
            guests.Add(($"{first} {last}", phones.Next()));
        }

        // The demo client (§579.4) is made AFTER the pool, so no other phone shifts; his visits are taken from existing bookings below.
        AppUser? demoClient = null;
        if (profile.DemoRoles)
            demoClient = NewUser(graph, phones, p, ShowcaseDemoRoles.ClientUserKey, "Мария", "Климова",
                LocalToUtc(DateOnly.FromDateTime(nowUtc), TimeOnly.MinValue, TimeZoneInfo.Utc).AddDays(-240), "Client");

        // ── Bookings: per master and day, deterministic in (profile, date). ──
        var ci = 0;
        foreach (var spec in ShowcaseSpecs.Companies)
        {
            var company = companiesByKey[spec.Key];
            var tz = TimeZoneInfo.FindSystemTimeZoneById(spec.TimeZoneId);
            var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz));
            var pool = new ClientPool(
                registered.Where((_, idx) => idx % ShowcaseSpecs.Companies.Count == ci).ToList(),
                guests.Where((_, idx) => idx % ShowcaseSpecs.Companies.Count == ci).ToList());
            BuildBookings(graph, profile, spec, company, tz, localToday, pool, schedulesByKey[spec.Key]);
            ci++;
        }

        if (demoClient is not null) AttachDemoClient(graph, profile, demoClient, companiesByKey, nowUtc);
        if (profile.Reviews) BuildReviews(graph, profile, nowUtc, demoClient);
        if (profile.ClientNotes) BuildClientNotes(graph, profile, nowUtc);

        // ARCHITECTURE_CYCLE35.md §35.9.1 — the shops of «Заказы» come LAST: their people take the phones after every salon one and their rows are
        // appended after every salon row, so the salon part of the demo (and the whole production showcase) is byte-for-byte what it was before.
        if (profile.Shops) ShowcaseShopsDataset.Build(graph, profile, phones, nowUtc);

        return graph;
    }

    private sealed record MasterCtx(AppUser User, string Key, decimal? Commission);

    private sealed record ClientPool(List<AppUser> Registered, List<(string Name, string Phone)> Guests);

    private static DateTime LocalToUtc(DateOnly date, TimeOnly time, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified), tz);

    private static (string First, string Last) PersonName(ShowcaseRandom rng, bool female)
    {
        var first = female ? rng.Pick(ShowcaseSpecs.FemaleNames) : rng.Pick(ShowcaseSpecs.MaleNames);
        var surname = rng.Pick(ShowcaseSpecs.Surnames);
        return (first, female ? surname.Female : surname.Male);
    }

    internal static AppUser NewUser(ShowcaseGraph graph, ShowcasePhones phones, string profile, string key, string first, string last, DateTime createdAtUtc, string role)
    {
        var phone = phones.Next();
        var id = ShowcaseIds.For(profile, "user", key).ToString();
        var user = new AppUser
        {
            Id = id,
            UserName = phone,
            NormalizedUserName = phone,
            PhoneNumber = phone,
            PhoneNumberConfirmed = false,
            Email = null,
            NormalizedEmail = null,
            EmailConfirmed = false,
            PasswordHash = null, // §574.3: no password — a showcase account never signs in on production
            SecurityStamp = ShowcaseIds.For(profile, "security-stamp", key).ToString("N"),
            ConcurrencyStamp = ShowcaseIds.For(profile, "concurrency-stamp", key).ToString("N"),
            LockoutEnabled = true,
            FirstName = first,
            LastName = last,
            IsShowcase = true,
            CreatedAt = DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc),
        };
        graph.Users.Add(user);
        graph.UserRoles.Add((id, role));
        return user;
    }

    private static void AssignServices(ShowcaseGraph graph, string p, ShowcaseCompanySpec spec, Company company, List<MasterCtx> masters, List<Service> services)
    {
        for (var m = 0; m < masters.Count; m++)
        {
            var rng = new ShowcaseRandom($"{p}:master-services:{masters[m].Key}");
            var chosen = new HashSet<int>();
            for (var s = 0; s < services.Count; s++)
                if (s % masters.Count == m || rng.Chance(0.4)) chosen.Add(s); // every service is covered; masters also share many
            while (chosen.Count < Math.Min(3, services.Count)) chosen.Add(rng.Next(services.Count));
            foreach (var s in chosen.OrderBy(x => x))
                graph.MasterServices.Add(new MasterService
                {
                    Id = ShowcaseIds.For(p, "master-service", $"{masters[m].Key}:{s}"),
                    MasterId = masters[m].User.Id,
                    ServiceId = services[s].Id,
                });
        }
    }

    // Weekly patterns as ISO weekdays (1 = Monday … 7 = Sunday).
    private static readonly int[][] WorkPatterns =
    [
        [1, 2, 3, 4, 5], [2, 3, 4, 5, 6], [1, 2, 4, 5, 6], [3, 4, 5, 6, 7], [1, 3, 5, 6, 7],
    ];

    private sealed record MasterSchedule(MasterCtx Master, HashSet<int> WorkDays, TimeOnly Start, TimeOnly End, TimeOnly BreakStart, TimeOnly BreakEnd);

    private static List<MasterSchedule> ScheduleOf(string p, ShowcaseCompanySpec spec, List<MasterCtx> masters) =>
        masters.Select(m =>
        {
            var rng = new ShowcaseRandom($"{p}:schedule:{m.Key}");
            var days = WorkPatterns[rng.Next(WorkPatterns.Length)].ToHashSet();
            var start = new TimeOnly(rng.Next(9, 12), 0);
            var end = start.AddHours(rng.Next(9, 11)); // 9–10 hours, ends by 21:00
            var breakStart = new TimeOnly(rng.Next(13, 15), 0);
            return new MasterSchedule(m, days, start, end, breakStart, breakStart.AddHours(1));
        }).ToList();

    private static List<MasterSchedule> BuildSchedules(
        ShowcaseGraph graph, ShowcaseProfile profile, ShowcaseCompanySpec spec, Company company, TimeZoneInfo tz, DateOnly localToday, List<MasterCtx> masters)
    {
        var p = profile.Name;
        var schedules = ScheduleOf(p, spec, masters);
        var vacationRng = new ShowcaseRandom($"{p}:vacation:{spec.Key}");
        var vacationMaster = masters.Count > 1 ? vacationRng.Next(masters.Count) : -1; // a one-person company has no vacation
        var vacationStart = localToday.AddDays(vacationRng.Next(2, 9));
        var vacationDays = vacationRng.Next(3, 6);

        for (var m = 0; m < schedules.Count; m++)
        {
            var sc = schedules[m];
            for (var dow = 1; dow <= 7; dow++)
                graph.ScheduleTemplates.Add(new WeeklyScheduleTemplate
                {
                    Id = ShowcaseIds.For(p, "template", $"{sc.Master.Key}:{dow}"),
                    MasterId = sc.Master.User.Id,
                    CompanyId = company.Id,
                    DayOfWeek = dow,
                    IsWorking = sc.WorkDays.Contains(dow),
                    StartTime = sc.Start,
                    EndTime = sc.End,
                });

            for (var offset = -profile.PastDays; offset <= profile.ScheduleDaysAhead; offset++)
            {
                var date = localToday.AddDays(offset);
                var isoDow = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
                var onVacation = m == vacationMaster && date >= vacationStart && date < vacationStart.AddDays(vacationDays);
                var working = sc.WorkDays.Contains(isoDow) && !onVacation;
                var wh = new WorkingHours
                {
                    Id = ShowcaseIds.For(p, "working-hours", $"{sc.Master.Key}:{date:yyyyMMdd}"),
                    MasterId = sc.Master.User.Id,
                    CompanyId = company.Id,
                    Date = date,
                    StartTime = sc.Start,
                    EndTime = sc.End,
                    IsWorking = working,
                };
                graph.WorkingHours.Add(wh);
                if (working)
                    graph.ScheduleBreaks.Add(new ScheduleBreak
                    {
                        Id = ShowcaseIds.For(p, "break", $"{sc.Master.Key}:{date:yyyyMMdd}"),
                        WorkingHoursId = wh.Id,
                        StartTime = sc.BreakStart,
                        EndTime = sc.BreakEnd,
                    });
            }
        }
        return schedules;
    }

    private static void BuildBookings(
        ShowcaseGraph graph, ShowcaseProfile profile, ShowcaseCompanySpec spec, Company company, TimeZoneInfo tz, DateOnly localToday, ClientPool pool,
        List<MasterSchedule> schedules)
    {
        var p = profile.Name;
        var services = graph.Services.Where(s => s.CompanyId == company.Id).ToList();
        var masterServices = graph.MasterServices.Where(ms => schedules.Any(sc => sc.Master.User.Id == ms.MasterId)).ToLookup(ms => ms.MasterId);
        var workingHours = graph.WorkingHours.Where(w => w.CompanyId == company.Id && w.IsWorking).ToLookup(w => (w.MasterId, w.Date));
        var todayStartUtc = LocalToUtc(localToday, TimeOnly.MinValue, tz);

        // Per company: which master-days are booked solid within the next week (§575.3 "у 1–2 мастеров есть дни, занятые целиком").
        var fullRng = new ShowcaseRandom($"{p}:full-days:{spec.Key}:{localToday:yyyyMMdd}");
        var fullDays = new HashSet<(int Master, int Offset)>();
        for (var k = 0; k < Math.Min(2, schedules.Count); k++)
            fullDays.Add((fullRng.Next(schedules.Count), fullRng.Next(1, 8)));

        for (var m = 0; m < schedules.Count; m++)
        {
            var sc = schedules[m];
            var master = sc.Master;
            var masterServiceIds = masterServices[master.User.Id].Select(ms => ms.ServiceId).ToHashSet();
            var offered = services.Where(s => masterServiceIds.Contains(s.Id)).ToList();
            if (offered.Count == 0) continue;

            for (var offset = -profile.PastDays; offset <= 14; offset++)
            {
                var date = localToday.AddDays(offset);
                if (!workingHours[(master.User.Id, date)].Any()) continue;

                var rng = new ShowcaseRandom($"{p}:bookings:{master.Key}:{date:yyyyMMdd}");
                var isoDow = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
                double fill;
                if (offset < 0) fill = profile.PastOccupancy * (isoDow >= 6 ? 1.15 : 0.95) * (0.85 + 0.3 * rng.NextDouble());
                else if (offset <= 7) fill = fullDays.Contains((m, offset)) ? 1.0 : 0.30 + 0.30 * rng.NextDouble();
                else fill = 0.10 + 0.20 * rng.NextDouble();
                fill = Math.Min(fill, 1.0);

                var cursor = sc.Start;
                while (cursor < sc.End)
                {
                    if (cursor >= sc.BreakStart && cursor < sc.BreakEnd) { cursor = RoundUp(sc.BreakEnd); continue; }
                    if (!rng.Chance(fill)) { cursor = cursor.AddMinutes(GridMinutes); continue; }

                    var chosen = new List<Service> { rng.Pick(offered) };
                    if (offered.Count > 1 && rng.Chance(0.15))
                    {
                        var second = rng.Pick(offered);
                        if (second.Id != chosen[0].Id) chosen.Add(second);
                    }
                    var minutes = chosen.Sum(s => s.DurationMinutes);
                    var end = cursor.AddMinutes(minutes);
                    var wraps = end < cursor; // crossed midnight
                    if (wraps || end > sc.End || (cursor < sc.BreakStart && end > sc.BreakStart))
                    {
                        cursor = cursor.AddMinutes(GridMinutes);
                        continue;
                    }

                    AddBooking(graph, profile, spec, company, tz, todayStartUtc, master, sc, date, offset, cursor, end, chosen, pool, rng);
                    cursor = RoundUp(end);
                }
            }
        }
    }

    private static TimeOnly RoundUp(TimeOnly t)
    {
        var rem = t.Minute % GridMinutes;
        return rem == 0 ? t : t.AddMinutes(GridMinutes - rem);
    }

    private static void AddBooking(
        ShowcaseGraph graph, ShowcaseProfile profile, ShowcaseCompanySpec spec, Company company, TimeZoneInfo tz, DateTime todayStartUtc,
        MasterCtx master, MasterSchedule sc, DateOnly date, int offset, TimeOnly start, TimeOnly end, List<Service> chosen, ClientPool pool, ShowcaseRandom rng)
    {
        var p = profile.Name;
        var key = $"{master.Key}:{date:yyyyMMdd}:{start:HHmm}";
        var visitStartUtc = LocalToUtc(date, start, tz);
        var visitEndUtc = LocalToUtc(date, end, tz);

        // Who: a registered client of the showcase or a guest (a walk-in recorded by staff, or an online guest).
        AppUser? client = null;
        (string Name, string Phone)? guest = null;
        if (pool.Registered.Count > 0 && (pool.Guests.Count == 0 || rng.Chance(0.35))) client = rng.Pick(pool.Registered);
        else guest = rng.Pick(pool.Guests);

        // Status: the past is settled, the future is confirmed (a few cancelled).
        var statusRoll = rng.NextDouble();
        BookingStatus status;
        if (offset < 0) status = statusRoll < 0.80 ? BookingStatus.Completed : statusRoll < 0.92 ? BookingStatus.Cancelled : BookingStatus.NoShow;
        else status = statusRoll < 0.96 ? BookingStatus.Confirmed : BookingStatus.Cancelled;

        var createdAt = offset < 0
            ? visitStartUtc.AddHours(-rng.Next(2, 240))
            : todayStartUtc.AddHours(-rng.Next(1, 96));
        if (createdAt >= visitStartUtc) createdAt = visitStartUtc.AddHours(-1);

        var booking = new Booking
        {
            Id = ShowcaseIds.For(p, "booking", key),
            CompanyId = company.Id,
            ServiceId = chosen[0].Id,
            MasterId = master.User.Id,
            ClientId = client?.Id,
            GuestName = guest?.Name,
            GuestPhone = guest?.Phone,
            Date = date,
            StartTime = start,
            EndTime = end,
            Price = chosen.Sum(s => s.Price),
            CommissionPercent = master.Commission ?? 0,
            Status = status,
            PaymentStatus = PaymentStatus.NotRequired,
            CancellationReason = status == BookingStatus.Cancelled ? rng.Pick(ShowcaseSpecs.CancellationReasons) : null,
            CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc),
            ShowcaseKind = ShowcaseBookingKind.Seeded,
        };
        graph.Bookings.Add(booking);

        for (var i = 0; i < chosen.Count; i++)
            graph.BookingServices.Add(new BookingService
            {
                Id = ShowcaseIds.For(p, "booking-service", $"{key}:{i}"),
                BookingId = booking.Id,
                ServiceId = chosen[i].Id,
                Position = i,
                NameSnapshot = chosen[i].Name,
                DurationMinutes = chosen[i].DurationMinutes,
                Price = chosen[i].Price,
            });

        // Journal (§575.3): Created by the person who made the booking, then what happened to it.
        var masterName = $"{master.User.FirstName} {master.User.LastName}";
        var createdByStaff = guest is not null && rng.Chance(0.4);
        var lastEventAt = createdAt;
        var eventIndex = 0;
        void Append(BookingEventKind kind, BookingActorKind actor, string? actorId, string? actorName, UserRole? role, DateTime at,
            string? reason = null, DateOnly? prevDate = null, TimeOnly? prevTime = null, DateOnly? newDate = null, TimeOnly? newTime = null)
        {
            var atUtc = DateTime.SpecifyKind(at, DateTimeKind.Utc);
            if (atUtc > lastEventAt) lastEventAt = atUtc;
            graph.BookingEvents.Add(new BookingEvent
            {
                Id = ShowcaseIds.For(p, "booking-event", $"{key}:{eventIndex++}"),
                BookingId = booking.Id,
                CompanyId = company.Id,
                Kind = kind,
                OccurredAtUtc = atUtc,
                ActorKind = actor,
                ActorUserId = actorId,
                ActorNameSnapshot = actorName,
                ActorRoleSnapshot = role,
                CancellationReason = reason,
                PreviousDate = prevDate,
                PreviousStartTime = prevTime,
                NewDate = newDate,
                NewStartTime = newTime,
            });
        }

        if (client is not null)
            Append(BookingEventKind.Created, BookingActorKind.Client, client.Id, $"{client.FirstName} {client.LastName}", UserRole.Client, createdAt);
        else if (createdByStaff)
            Append(BookingEventKind.Created, BookingActorKind.Staff, master.User.Id, masterName, UserRole.Master, createdAt);
        else
            Append(BookingEventKind.Created, BookingActorKind.Guest, null, guest!.Value.Name, null, createdAt);

        // One booking in twenty was moved once (the demo profile: more of them, and some twice — US-28-13).
        if (rng.Chance(profile.RescheduleChance) && status != BookingStatus.Cancelled)
        {
            var movedAt = createdAt.AddHours(Math.Max(1, (visitStartUtc - createdAt).TotalHours * rng.NextDouble() * 0.5));
            var previousDate = date.AddDays(-rng.Next(1, 4));
            var previousTime = new TimeOnly(rng.Next(10, 19), rng.Chance(0.5) ? 0 : 30);
            var twice = profile.RichHistory && rng.Chance(0.25);
            var midDate = date;
            var midTime = start;
            if (twice)
            {
                midDate = date.AddDays(-rng.Next(0, 2));
                midTime = new TimeOnly(rng.Next(10, 19), rng.Chance(0.5) ? 0 : 30);
                // A move that moves nothing would be a lie in the journal: shift the intermediate slot by half-hours until it differs from both its neighbours.
                for (var attempt = 0; attempt < 4 && ((midDate, midTime) == (previousDate, previousTime) || (midDate, midTime) == (date, start)); attempt++)
                    midTime = midTime.AddMinutes(30);
            }

            void AppendMove(DateTime at, DateOnly fromDate, TimeOnly fromTime, DateOnly toDate, TimeOnly toTime)
            {
                if (client is not null)
                    Append(BookingEventKind.Rescheduled, BookingActorKind.Client, client.Id, $"{client.FirstName} {client.LastName}", UserRole.Client, at,
                        prevDate: fromDate, prevTime: fromTime, newDate: toDate, newTime: toTime);
                else
                    Append(BookingEventKind.Rescheduled, BookingActorKind.Staff, master.User.Id, masterName, UserRole.Master, at,
                        prevDate: fromDate, prevTime: fromTime, newDate: toDate, newTime: toTime);
            }

            AppendMove(movedAt, previousDate, previousTime, midDate, midTime);
            if (twice)
            {
                var movedAgainAt = movedAt.AddHours(Math.Max(1, (visitStartUtc - movedAt).TotalHours * (0.2 + 0.4 * rng.NextDouble())));
                if (movedAgainAt > visitStartUtc) movedAgainAt = visitStartUtc;
                AppendMove(movedAgainAt, midDate, midTime, date, start);
            }
        }

        switch (status)
        {
            case BookingStatus.Completed:
                Append(BookingEventKind.Completed, BookingActorKind.Staff, master.User.Id, masterName, UserRole.Master, visitEndUtc);
                break;
            case BookingStatus.NoShow:
                Append(BookingEventKind.NoShow, BookingActorKind.Staff, master.User.Id, masterName, UserRole.Master, visitEndUtc.AddMinutes(30));
                break;
            case BookingStatus.Cancelled:
                var cancelledAt = createdAt.AddHours(Math.Max(1, (visitStartUtc - createdAt).TotalHours * (0.2 + 0.7 * rng.NextDouble())));
                if (client is not null && rng.Chance(0.7))
                    Append(BookingEventKind.Cancelled, BookingActorKind.Client, client.Id, $"{client.FirstName} {client.LastName}", UserRole.Client, cancelledAt,
                        reason: booking.CancellationReason);
                else
                    Append(BookingEventKind.Cancelled, BookingActorKind.Staff, master.User.Id, masterName, UserRole.Master, cancelledAt, reason: booking.CancellationReason);
                break;
        }

        booking.UpdatedAt = lastEventAt;
    }

    // ── Demo profile only (§579.4, US-28-13) ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gives the demo client his visits: a few completed ones in the past and a few confirmed ones ahead in each of the companies of
    /// <see cref="ShowcaseDemoRoles.ClientCompanyKeys"/>. They are NOT new bookings: existing walk-in/guest bookings are re-assigned to him (the slots stay valid
    /// and nothing is booked twice); the "created" event of the journal is rewritten to say that the client made them himself.
    /// </summary>
    private static void AttachDemoClient(ShowcaseGraph graph, ShowcaseProfile profile, AppUser client, IReadOnlyDictionary<string, Company> companies, DateTime nowUtc)
    {
        var createdEvents = graph.BookingEvents.Where(e => e.Kind == BookingEventKind.Created).ToDictionary(e => e.BookingId);
        var clientName = $"{client.FirstName} {client.LastName}";

        foreach (var key in ShowcaseDemoRoles.ClientCompanyKeys)
        {
            var company = companies[key];
            var tz = TimeZoneInfo.FindSystemTimeZoneById(company.TimeZoneId);
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz));
            var rng = new ShowcaseRandom($"{profile.Name}:demo-client:{key}");

            var guestBookings = graph.Bookings
                .Where(b => b.CompanyId == company.Id && b.ClientId == null)
                .OrderBy(b => b.Date).ThenBy(b => b.StartTime).ThenBy(b => b.Id)
                .ToList();
            var past = guestBookings.Where(b => b.Status == BookingStatus.Completed && b.Date < today).ToList();
            var future = guestBookings.Where(b => b.Status == BookingStatus.Confirmed && b.Date > today).ToList();
            var flagship = key == ShowcaseDemoRoles.FlagshipCompanyKey;

            foreach (var booking in Spread(past, flagship ? 4 : 3, rng).Concat(Spread(future, flagship ? 2 : 1, rng)))
            {
                booking.ClientId = client.Id;
                booking.GuestName = null;
                booking.GuestPhone = null;
                var created = createdEvents[booking.Id];
                created.ActorKind = BookingActorKind.Client;
                created.ActorUserId = client.Id;
                created.ActorNameSnapshot = clientName;
                created.ActorRoleSnapshot = UserRole.Client;
            }
        }
    }

    /// <summary>Takes <paramref name="count"/> items spread over the whole list (one from each equal slice), deterministically.</summary>
    private static List<T> Spread<T>(List<T> items, int count, ShowcaseRandom rng)
    {
        var result = new List<T>();
        if (items.Count == 0 || count <= 0) return result;
        count = Math.Min(count, items.Count);
        var slice = items.Count / count;
        for (var i = 0; i < count; i++)
            result.Add(items[i * slice + rng.Next(slice)]);
        return result;
    }

    private static readonly IReadOnlyList<string> ReviewComments5 =
    [
        "Всё понравилось, обязательно вернусь.", "Отличный мастер, результат превзошёл ожидания.", "Очень аккуратная работа и приятная атмосфера.",
        "Записалась онлайн, приняли точно в назначенное время. Спасибо!", "Уже не первый раз, всегда на высоте.",
    ];

    private static readonly IReadOnlyList<string> ReviewComments4 =
    [
        "Хорошо, всё сделали аккуратно. Немного подождала в начале.", "Результатом доволен, приду ещё.", "Приятный сервис, цены адекватные.",
        "Мастер внимательный, есть небольшие пожелания по времени записи.",
    ];

    private static readonly IReadOnlyList<string> ReviewComments3 =
    [
        "В целом нормально, но ожидала чуть большего.", "Работа выполнена, по времени вышло дольше обещанного.", "Обычный визит, без восторга, но и без претензий.",
    ];

    private static readonly IReadOnlyList<string> ReviewComments2 =
    [
        "Пришлось ждать, результат средний.", "Не совсем то, что обсуждали заранее.",
    ];

    private static readonly IReadOnlyList<string> ReviewComments1 =
    [
        "Визит не оправдал ожиданий.", "Остались вопросы по качеству, надеюсь, администрация отреагирует.",
    ];

    private static (int Rating, string? Comment) RollReview(ShowcaseRandom rng)
    {
        var roll = rng.NextDouble();
        var rating = roll < 0.55 ? 5 : roll < 0.83 ? 4 : roll < 0.93 ? 3 : roll < 0.98 ? 2 : 1;
        var pool = rating switch { 5 => ReviewComments5, 4 => ReviewComments4, 3 => ReviewComments3, 2 => ReviewComments2, _ => ReviewComments1 };
        var comment = rng.Chance(0.6) ? rng.Pick(pool) : null;
        return (rating, comment);
    }

    /// <summary>
    /// Reviews on a share of completed visits of registered clients (only those can review, like in the product: the review names the client's own booking).
    /// The demo client keeps his earliest completed visit unreviewed, so "Оставить отзыв" has something to offer.
    /// </summary>
    private static void BuildReviews(ShowcaseGraph graph, ShowcaseProfile profile, DateTime nowUtc, AppUser? demoClient)
    {
        var users = graph.Users.ToDictionary(u => u.Id);
        var zones = graph.Companies.ToDictionary(c => c.Id, c => TimeZoneInfo.FindSystemTimeZoneById(c.TimeZoneId));
        Guid? keepForDemoClient = demoClient is null ? null : graph.Bookings
            .Where(b => b.ClientId == demoClient.Id && b.Status == BookingStatus.Completed)
            .OrderBy(b => b.Date).ThenBy(b => b.StartTime).Select(b => (Guid?)b.Id).FirstOrDefault();

        foreach (var booking in graph.Bookings)
        {
            if (booking.Status != BookingStatus.Completed || booking.ClientId is not { } clientId) continue;
            if (booking.Id == keepForDemoClient) continue;

            var rng = new ShowcaseRandom($"{profile.Name}:review:{booking.Id}");
            var chance = demoClient is not null && clientId == demoClient.Id ? 0.5 : 0.30;
            if (!rng.Chance(chance)) continue;

            var (rating, comment) = RollReview(rng);
            var visitEndUtc = LocalToUtc(booking.Date, booking.EndTime, zones[booking.CompanyId]);
            var createdAt = visitEndUtc.AddHours(rng.Next(2, 72));
            if (createdAt > nowUtc) createdAt = nowUtc;
            if (createdAt < visitEndUtc) createdAt = visitEndUtc;

            var author = users[clientId];
            graph.Reviews.Add(new Review
            {
                Id = ShowcaseIds.For(profile.Name, "review", booking.Id.ToString()),
                BookingId = booking.Id,
                CompanyId = booking.CompanyId,
                MasterId = booking.MasterId,
                ClientId = clientId,
                ReviewerName = $"{author.FirstName} {author.LastName}",
                Rating = rating,
                Comment = comment,
                CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc),
            });
        }
    }

    // Neutral work notes. No health details, no photos, nothing a client would not expect a salon to write down (US-28-04).
    private static readonly IReadOnlyList<string> ClientNoteTexts =
    [
        "Предпочитает утреннее время, приходит вовремя.", "Любит спокойную обстановку без лишних разговоров.", "Обычно приходит с подругой, записывать на соседние окна.",
        "Постоянный клиент, оставлять за ним ближайшее свободное окно.", "В прошлый раз понравился выбранный оттенок, повторить.", "Просила напомнить о коррекции через три недели.",
        "Предпочитает оплату картой.", "Интересуется новинками, показать каталог процедур.", "Приходит после работы, лучше вечерние окна.", "Любит чай без сахара.",
        "Просит сразу озвучивать итоговую стоимость.", "Опаздывает на 5–10 минут, закладывать запас в записи.",
    ];

    /// <summary>
    /// Notes of masters about clients, written just after a completed visit: about one visit in fourteen of a registered client and one in thirty of a guest
    /// (a guest's note is keyed by his phone, like in the product). The demo master gets noticeably more, so his "Клиенты" is not empty.
    /// </summary>
    private static void BuildClientNotes(ShowcaseGraph graph, ShowcaseProfile profile, DateTime nowUtc)
    {
        var zones = graph.Companies.ToDictionary(c => c.Id, c => TimeZoneInfo.FindSystemTimeZoneById(c.TimeZoneId));
        var demoMasterId = ShowcaseDemoRoles.UserIdOf(ShowcaseDemoRoles.Master);

        foreach (var booking in graph.Bookings)
        {
            if (booking.Status != BookingStatus.Completed) continue;
            var registered = booking.ClientId is not null;
            if (!registered && booking.GuestPhone is null) continue;

            var rng = new ShowcaseRandom($"{profile.Name}:client-note:{booking.Id}");
            var chance = booking.MasterId == demoMasterId ? 0.30 : registered ? 1.0 / 14 : 1.0 / 30;
            if (!rng.Chance(chance)) continue;

            var visitEndUtc = LocalToUtc(booking.Date, booking.EndTime, zones[booking.CompanyId]);
            var createdAt = visitEndUtc.AddMinutes(rng.Next(5, 90));
            if (createdAt > nowUtc) createdAt = visitEndUtc;

            graph.ClientNotes.Add(new ClientNote
            {
                Id = ShowcaseIds.For(profile.Name, "client-note", booking.Id.ToString()),
                CompanyId = booking.CompanyId,
                MasterId = booking.MasterId,
                ClientId = booking.ClientId,
                GuestPhone = registered ? null : booking.GuestPhone,
                Note = rng.Pick(ClientNoteTexts),
                BookingId = booking.Id,
                CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc),
            });
        }
    }
}
