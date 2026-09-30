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
                graph.Subscriptions.Add(new AccountSubscription
                {
                    Id = ShowcaseIds.For(p, "subscription", spec.OwnerKey),
                    OwnerUserId = ownerUser.Id,
                    BillingAccountId = account.Id,
                    PlanConfigId = ShowcaseCatalog.ShowcasePlanId,
                    PaidUntil = null,
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

    private static AppUser NewUser(ShowcaseGraph graph, ShowcasePhones phones, string profile, string key, string first, string last, DateTime createdAtUtc, string role)
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

        // One booking in twenty was moved once.
        if (rng.Chance(0.05) && status != BookingStatus.Cancelled)
        {
            var movedAt = createdAt.AddHours(Math.Max(1, (visitStartUtc - createdAt).TotalHours * rng.NextDouble() * 0.5));
            var previousDate = date.AddDays(-rng.Next(1, 4));
            var previousTime = new TimeOnly(rng.Next(10, 19), rng.Chance(0.5) ? 0 : 30);
            if (client is not null)
                Append(BookingEventKind.Rescheduled, BookingActorKind.Client, client.Id, $"{client.FirstName} {client.LastName}", UserRole.Client, movedAt,
                    prevDate: previousDate, prevTime: previousTime, newDate: date, newTime: start);
            else
                Append(BookingEventKind.Rescheduled, BookingActorKind.Staff, master.User.Id, masterName, UserRole.Master, movedAt,
                    prevDate: previousDate, prevTime: previousTime, newDate: date, newTime: start);
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
}
