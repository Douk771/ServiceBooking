using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Cycle 22, package P3 (ARCHITECTURE_CYCLE22.md §375 F1–F3, §381; SPEC.md US-22-04/05, NFR
/// "оптимизации SQL покрыты тестами на равенство"). Characterization tests written and run green
/// against the PRE-rewrite code — <c>GET /api/masters/clients</c> grouped in memory and
/// <c>GET /api/companies/{id}/stats</c> aggregated in memory — and kept unchanged across the SQL
/// rewrite: the responses must stay exactly the same (composition, order, totals, money formatting).
///
/// Data is seeded straight into the database (fixed past dates, fixed note timestamps, controlled user
/// ids) so the expected JSON is literal: the only run-dependent values (company/master/service/booking
/// ids, the one API-created future booking's date) are replaced by placeholders before comparison.
/// Registered-client ids share one random prefix starting with 'f', so the "equal last visit → order by
/// client key" tie-break is deterministic against both each other (suffix -01, -02 …) and guest phones
/// (digits sort before 'f').
/// </summary>
public class Cycle22RefactorEquivalenceTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private async Task DbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(db);
    }

    private static AppUser User(string id, string first, string last, string phone, string? email, bool confirmed) => new()
    {
        Id = id,
        UserName = id,
        NormalizedUserName = id.ToUpperInvariant(),
        FirstName = first,
        LastName = last,
        PhoneNumber = phone,
        PhoneNumberConfirmed = confirmed,
        Email = email,
        NormalizedEmail = email?.ToUpperInvariant(),
        SecurityStamp = Guid.NewGuid().ToString(),
        CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    private static Service Svc(Guid companyId, string name, decimal price) => new()
    {
        Id = Guid.NewGuid(), CompanyId = companyId, Name = name, DurationMinutes = 60, Price = price,
    };

    /// <summary>A booking with its BookingServices rows (one per service, the first one also being
    /// Booking.ServiceId — the same shape the product writes).</summary>
    private static Booking Bk(
        Guid companyId, string masterId, string? clientId, DateOnly date, int startHour, BookingStatus status,
        decimal price, Service[] services, string? guestName = null, string? guestPhone = null, string? guestEmail = null,
        int durationMinutes = 60)
    {
        var id = Guid.NewGuid();
        var start = new TimeOnly(startHour, 0);
        var booking = new Booking
        {
            Id = id, CompanyId = companyId, ServiceId = services[0].Id, MasterId = masterId, ClientId = clientId,
            GuestName = guestName, GuestPhone = guestPhone, GuestEmail = guestEmail,
            Date = date, StartTime = start, EndTime = start.AddMinutes(durationMinutes),
            Price = price, Status = status,
            CreatedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        for (var i = 0; i < services.Length; i++)
        {
            booking.BookingServices.Add(new Core.Entities.BookingService
            {
                Id = Guid.NewGuid(), BookingId = id, ServiceId = services[i].Id, Position = i,
                NameSnapshot = services[i].Name, DurationMinutes = 60, Price = services[i].Price,
            });
        }
        return booking;
    }

    private static string Canon(string json, params (string Value, string Label)[] replacements)
    {
        foreach (var (value, label) in replacements.OrderByDescending(r => r.Value.Length))
            json = json.Replace(value, label, StringComparison.Ordinal);
        return json;
    }

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    // ── GET /api/masters/clients — dataset ───────────────────────────────────────────────────────

    private sealed record ClientsWorld(
        HttpClient Master, Guid CompanyId, string MasterId, string ColleagueId, string Prefix,
        Guid[] ServiceIds, Guid[] BookingIds, DateOnly FutureDate, (string Value, string Label)[] Placeholders);

    /// <summary>
    /// Company K, the calling master M (not the owner), a colleague M2. Registered clients C1–C4 (ids
    /// "{P}-01"…), guests G1–G3, one booking with neither client nor guest phone, a booking of C1 with
    /// the COLLEAGUE (must not count), notes by both masters (one linked to a booking, one with a photo
    /// uploaded by M on M2's note), a note in ANOTHER company about C1 (must not leak), cancelled/no-show
    /// /pending/completed statuses, a multi-service booking, three last-visit ties.
    /// </summary>
    private async Task<ClientsWorld> SeedClientsWorldAsync()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var colleague = await AddMasterAsync(owner.Token, company.Id);
        var (_, otherCompany) = await CreateOwnerWithCompanyAsync();

        var prefix = "f" + Guid.NewGuid().ToString("N")[..11];
        string Cid(int n) => $"{prefix}-0{n}";
        // Phones unique to this test run, in this class' own database; "78…" never collides with
        // TestData.Phone()'s "+79…" shape.
        var tag = Math.Abs(prefix.GetHashCode() % 900) + 100; // 3 digits
        string Ph(int n) => $"78{tag}{n:000000}"; // 11 digits

        var s1 = Svc(company.Id, "Стрижка", 1500.50m);
        var s2 = Svc(company.Id, "Окрашивание", 3200.00m);
        var future = NextWeekday();
        var futureService = await CreateServiceAsync(owner.Token, company.Id, name: "Укладка " + prefix, durationMinutes: 60, price: 900m);
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, future);

        var bookings = new List<Booking>();
        Guid[] noteIds = [];
        await DbAsync(async db =>
        {
            db.Users.AddRange(
                User(Cid(1), "Анна", "Иванова", Ph(1), $"anna.{prefix}@test.local", confirmed: true),
                User(Cid(2), "Борис", "Петров", Ph(2), null, confirmed: false),
                User(Cid(3), "анна", "Смирнова", Ph(3), $"smirnova.{prefix}@test.local", confirmed: true),
                User(Cid(4), "Пётр", "", Ph(4), null, confirmed: false));
            db.Services.AddRange(s1, s2);

            var k = company.Id;
            var m = master.UserId;
            bookings.AddRange([
                // C1: three visits with M (one multi-service, one cancelled) + one with the colleague.
                Bk(k, m, Cid(1), D(2025, 3, 10), 9, BookingStatus.Completed, 1500.50m, [s1]),
                Bk(k, m, Cid(1), D(2025, 3, 10), 11, BookingStatus.Cancelled, 4700.50m, [s1, s2], durationMinutes: 90),
                Bk(k, m, Cid(1), D(2025, 1, 5), 10, BookingStatus.Completed, 3200.00m, [s2]),
                Bk(k, colleague.UserId, Cid(1), D(2025, 4, 1), 10, BookingStatus.Completed, 1500.50m, [s1]),
                // C2: ties with C1 and G2 on 2025-03-10.
                Bk(k, m, Cid(2), D(2025, 3, 10), 14, BookingStatus.Completed, 1500.50m, [s1]),
                // C3: ties with G3 on 2025-02-01.
                Bk(k, m, Cid(3), D(2025, 2, 1), 9, BookingStatus.NoShow, 3200.00m, [s2]),
                // C4: oldest, pending.
                Bk(k, m, Cid(4), D(2024, 12, 1), 12, BookingStatus.Pending, 1500.50m, [s1]),
                // G1: an old visit under an old name; the future API booking below renames them.
                Bk(k, m, null, D(2025, 2, 1), 15, BookingStatus.Completed, 1500.50m, [s1],
                    guestName: "Гость Старый", guestPhone: Ph(501), guestEmail: $"old.{prefix}@test.local"),
                // G2: ties with C1/C2 on 2025-03-10; two visits the same day (last one wins the name).
                Bk(k, m, null, D(2025, 3, 10), 16, BookingStatus.Completed, 3200.00m, [s2],
                    guestName: "Мария", guestPhone: Ph(502), guestEmail: null),
                Bk(k, m, null, D(2025, 3, 10), 8, BookingStatus.Cancelled, 1500.50m, [s1],
                    guestName: "Маша (ранняя)", guestPhone: Ph(502), guestEmail: $"early.{prefix}@test.local"),
                // G3.
                Bk(k, m, null, D(2025, 2, 1), 17, BookingStatus.Completed, 1500.50m, [s1],
                    guestName: null, guestPhone: Ph(503), guestEmail: null),
                // Neither client nor guest phone — never listed.
                Bk(k, m, null, D(2025, 3, 20), 9, BookingStatus.Completed, 1500.50m, [s1], guestName: "Аноним"),
            ]);
            db.Bookings.AddRange(bookings);
            await db.SaveChangesAsync();

            var n1 = new ClientNote
            {
                Id = Guid.NewGuid(), CompanyId = k, MasterId = m, ClientId = Cid(1), Note = "Любит короткие стрижки",
                BookingId = bookings[0].Id, CreatedAt = new DateTime(2025, 3, 10, 10, 5, 0, DateTimeKind.Utc),
            };
            var n2 = new ClientNote
            {
                Id = Guid.NewGuid(), CompanyId = k, MasterId = colleague.UserId, ClientId = Cid(1), Note = "Аллергия на аммиак",
                CreatedAt = new DateTime(2025, 1, 5, 11, 0, 0, DateTimeKind.Utc),
            };
            var n3 = new ClientNote
            {
                Id = Guid.NewGuid(), CompanyId = k, MasterId = m, GuestPhone = Ph(501), Note = "Гость, просил перезвонить",
                CreatedAt = new DateTime(2025, 2, 1, 16, 0, 0, DateTimeKind.Utc),
            };
            var leaked = new ClientNote
            {
                Id = Guid.NewGuid(), CompanyId = otherCompany.Id, MasterId = colleague.UserId, ClientId = Cid(1),
                Note = "ЧУЖАЯ компания", CreatedAt = new DateTime(2025, 3, 11, 0, 0, 0, DateTimeKind.Utc),
            };
            db.ClientNotes.AddRange(n1, n2, n3, leaked);
            db.ClientNotePhotos.Add(new ClientNotePhoto
            {
                Id = Guid.NewGuid(), ClientNoteId = n2.Id, CompanyId = k, StoragePath = "x/full.jpg", ThumbnailPath = "x/thumb.jpg",
                SizeBytes = 12345, Width = 800, Height = 600, ContentHash = "hash-" + prefix, UploadedByUserId = m,
                CreatedAt = new DateTime(2025, 1, 5, 11, 1, 0, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
            noteIds = [n1.Id, n2.Id, n3.Id];
        });

        // G1's newest visit goes through the product: a formatted phone that normalizes onto G1's key.
        var formatted = $"8 ({Ph(501)[1..4]}) {Ph(501)[4..7]}-{Ph(501)[7..9]}-{Ph(501)[9..]}";
        var created = await AuthedClient(owner.Token).PostAsJsonAsync("/api/bookings",
            new CreateBookingDto(company.Id, futureService.Id, master.UserId, future, new TimeOnly(15, 0), null,
                "Гость Новый", formatted, $"new.{prefix}@test.local", null));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var placeholders = new List<(string, string)>
        {
            (prefix, "{P}"), (master.UserId, "{M}"), (colleague.UserId, "{M2}"), (company.Id.ToString(), "{K}"),
            (future.ToString("yyyy-MM-dd"), "{FUTURE}"), ($"78{tag}", "78{T}"),
            (bookings[0].Id.ToString(), "{B_C1_0310}"),
            (noteIds[0].ToString(), "{N1}"), (noteIds[1].ToString(), "{N2}"), (noteIds[2].ToString(), "{N3}"),
        };
        await DbAsync(async db =>
        {
            var photoId = db.ClientNotePhotos.Where(p => p.ClientNoteId == noteIds[1]).Select(p => p.Id).Single();
            placeholders.Add((photoId.ToString(), "{PH1}"));
            await Task.CompletedTask;
        });

        return new ClientsWorld(AuthedClient(master.Token), company.Id, master.UserId, colleague.UserId, prefix,
            [s1.Id, s2.Id, futureService.Id], bookings.Select(b => b.Id).ToArray(), future, placeholders.ToArray());
    }

    private static async Task<string> GetRawAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return body;
    }

    /// <summary>Compact "key|name|total" projection of a page, for the paging/search assertions.</summary>
    private static string Keys(string json, ClientsWorld w)
    {
        var node = JsonNode.Parse(json)!;
        var items = node["items"]!.AsArray()
            .Select(i => (string?)i!["clientId"] ?? (string?)i["guestPhone"])
            .Select(k => Canon(k!, w.Placeholders));
        return $"total={(int)node["total"]!};page={(int)node["page"]!};size={(int)node["pageSize"]!};next={(bool)node["hasNext"]!};[{string.Join(",", items)}]";
    }

    // ── CY22-06 — full list, exact JSON ─────────────────────────────────────────────────────────

    [Fact, TestCase("CY22-06")]
    public async Task MasterClients_FullList_ExactJson()
    {
        var w = await SeedClientsWorldAsync();

        var raw = await GetRawAsync(w.Master, $"/api/masters/clients?companyId={w.CompanyId}");

        Canon(raw, w.Placeholders).Should().Be(ExpectedFullList);
    }

    private const string ExpectedFullList =
        """
        {"items":[{"clientId":null,"guestPhone":"78{T}000501","name":"Гость Новый","phone":"78{T}000501","email":"new.{P}@test.local","lastVisitDate":"{FUTURE}","totalVisits":2,"notes":[{"id":"{N3}","note":"Гость, просил перезвонить","createdAt":"2025-02-01T16:00:00Z","authorId":"{M}","authorName":"Test User","bookingId":null,"bookingDate":null,"bookingServiceName":null,"canDelete":true,"photos":[]}],"bookingSummaries":[{"date":"{FUTURE}","serviceName":"Укладка {P}","status":"Confirmed"},{"date":"2025-02-01","serviceName":"Стрижка","status":"Completed"}],"phoneVerified":null},{"clientId":null,"guestPhone":"78{T}000502","name":"Мария","phone":"78{T}000502","email":null,"lastVisitDate":"2025-03-10","totalVisits":2,"notes":[],"bookingSummaries":[{"date":"2025-03-10","serviceName":"Окрашивание","status":"Completed"},{"date":"2025-03-10","serviceName":"Стрижка","status":"Cancelled"}],"phoneVerified":null},{"clientId":"{P}-01","guestPhone":null,"name":"Анна Иванова","phone":"78{T}000001","email":"anna.{P}@test.local","lastVisitDate":"2025-03-10","totalVisits":3,"notes":[{"id":"{N1}","note":"Любит короткие стрижки","createdAt":"2025-03-10T10:05:00Z","authorId":"{M}","authorName":"Test User","bookingId":"{B_C1_0310}","bookingDate":"2025-03-10","bookingServiceName":"Стрижка","canDelete":true,"photos":[]},{"id":"{N2}","note":"Аллергия на аммиак","createdAt":"2025-01-05T11:00:00Z","authorId":"{M2}","authorName":"Test User","bookingId":null,"bookingDate":null,"bookingServiceName":null,"canDelete":false,"photos":[{"id":"{PH1}","url":"/api/client-notes/photos/{PH1}","thumbnailUrl":"/api/client-notes/photos/{PH1}/thumb","width":800,"height":600,"sizeBytes":12345,"createdAt":"2025-01-05T11:01:00Z","uploadedByName":"Test User","canDelete":true}]}],"bookingSummaries":[{"date":"2025-03-10","serviceName":"Стрижка","status":"Cancelled"},{"date":"2025-03-10","serviceName":"Стрижка","status":"Completed"},{"date":"2025-01-05","serviceName":"Окрашивание","status":"Completed"}],"phoneVerified":true},{"clientId":"{P}-02","guestPhone":null,"name":"Борис Петров","phone":"78{T}000002","email":null,"lastVisitDate":"2025-03-10","totalVisits":1,"notes":[],"bookingSummaries":[{"date":"2025-03-10","serviceName":"Стрижка","status":"Completed"}],"phoneVerified":false},{"clientId":null,"guestPhone":"78{T}000503","name":"Guest","phone":"78{T}000503","email":null,"lastVisitDate":"2025-02-01","totalVisits":1,"notes":[],"bookingSummaries":[{"date":"2025-02-01","serviceName":"Стрижка","status":"Completed"}],"phoneVerified":null},{"clientId":"{P}-03","guestPhone":null,"name":"анна Смирнова","phone":"78{T}000003","email":"smirnova.{P}@test.local","lastVisitDate":"2025-02-01","totalVisits":1,"notes":[],"bookingSummaries":[{"date":"2025-02-01","serviceName":"Окрашивание","status":"NoShow"}],"phoneVerified":true},{"clientId":"{P}-04","guestPhone":null,"name":"Пётр","phone":"78{T}000004","email":null,"lastVisitDate":"2024-12-01","totalVisits":1,"notes":[],"bookingSummaries":[{"date":"2024-12-01","serviceName":"Стрижка","status":"Pending"}],"phoneVerified":false}],"page":1,"pageSize":20,"total":7,"hasNext":false}
        """;

    // ── CY22-07 — page boundaries and ordering ties ─────────────────────────────────────────────

    [Fact, TestCase("CY22-07")]
    public async Task MasterClients_Paging_SameSlicesTotalsAndTieOrder()
    {
        var w = await SeedClientsWorldAsync();
        var url = $"/api/masters/clients?companyId={w.CompanyId}";

        var p1 = Keys(await GetRawAsync(w.Master, url + "&page=1&pageSize=3"), w);
        var p2 = Keys(await GetRawAsync(w.Master, url + "&page=2&pageSize=3"), w);
        var p3 = Keys(await GetRawAsync(w.Master, url + "&page=3&pageSize=3"), w);
        var p4 = Keys(await GetRawAsync(w.Master, url + "&page=4&pageSize=3"), w);
        var exact = Keys(await GetRawAsync(w.Master, url + "&page=1&pageSize=7"), w);
        var clamped = Keys(await GetRawAsync(w.Master, url + "&page=0&pageSize=500"), w);

        string.Join("\n", p1, p2, p3, p4, exact, clamped).Should().Be(
            """
            total=7;page=1;size=3;next=True;[78{T}000501,78{T}000502,{P}-01]
            total=7;page=2;size=3;next=True;[{P}-02,78{T}000503,{P}-03]
            total=7;page=3;size=3;next=False;[{P}-04]
            total=7;page=4;size=3;next=False;[]
            total=7;page=1;size=7;next=False;[78{T}000501,78{T}000502,{P}-01,{P}-02,78{T}000503,{P}-03,{P}-04]
            total=7;page=1;size=100;next=False;[78{T}000501,78{T}000502,{P}-01,{P}-02,78{T}000503,{P}-03,{P}-04]
            """);
    }

    // ── CY22-08 — search by name (case-insensitive) and by phone digits ─────────────────────────

    [Fact, TestCase("CY22-08")]
    public async Task MasterClients_Search_SameMatchesAndOrder()
    {
        var w = await SeedClientsWorldAsync();
        var url = $"/api/masters/clients?companyId={w.CompanyId}&search=";
        var tag = w.Placeholders.Single(p => p.Label == "78{T}").Value[2..];

        // (label, search) — the label is what the expected text shows, with this run's phone tag as {T}.
        var searches = new (string Label, string Value)[]
        {
            ("анна", "анна"), ("АННА", "АННА"), ("Иванова", "Иванова"), ("Анна Иванова", "Анна Иванова"),
            ("Гость", "Гость"), ("Guest", "Guest"), ("пётр", "пётр"), ("Мария", "Мария"),
            ("8 ({T0}", $"8 ({tag[..1]}"),              // < 5 digits → name search, matches nothing
            ("+7 8{T}-000-501", $"+7 8{tag}-000-501"),   // phone-like → normalized onto G1
            ("8{T}000", $"8{tag}000"),                   // phone-like partial digits → every phone
            ("000002", "000002"),                        // ≥ 5 digits, partial phone
            ("zzz", "zzz"),
        };

        var lines = new List<string>();
        foreach (var (label, value) in searches)
        {
            var raw = await GetRawAsync(w.Master, url + Uri.EscapeDataString(value) + "&pageSize=100");
            lines.Add(label + " => " + Keys(raw, w));
        }
        // Search + paging together.
        lines.Add("анна p2/1 => " + Keys(await GetRawAsync(w.Master, url + Uri.EscapeDataString("анна") + "&page=2&pageSize=1"), w));

        string.Join("\n", lines).Should().Be(
            """
            анна => total=2;page=1;size=100;next=False;[{P}-01,{P}-03]
            АННА => total=2;page=1;size=100;next=False;[{P}-01,{P}-03]
            Иванова => total=1;page=1;size=100;next=False;[{P}-01]
            Анна Иванова => total=1;page=1;size=100;next=False;[{P}-01]
            Гость => total=1;page=1;size=100;next=False;[78{T}000501]
            Guest => total=1;page=1;size=100;next=False;[78{T}000503]
            пётр => total=1;page=1;size=100;next=False;[{P}-04]
            Мария => total=1;page=1;size=100;next=False;[78{T}000502]
            8 ({T0} => total=0;page=1;size=100;next=False;[]
            +7 8{T}-000-501 => total=1;page=1;size=100;next=False;[78{T}000501]
            8{T}000 => total=7;page=1;size=100;next=False;[78{T}000501,78{T}000502,{P}-01,{P}-02,78{T}000503,{P}-03,{P}-04]
            000002 => total=1;page=1;size=100;next=False;[{P}-02]
            zzz => total=0;page=1;size=100;next=False;[]
            анна p2/1 => total=2;page=2;size=1;next=False;[{P}-03]
            """);
    }

    // ── CY22-07b/08b — page-scoped reads: notes and booking summaries, exact JSON (cycle 22 review) ──

    /// <summary>
    /// Review of cycle 22: CY22-07/08 compare only keys and totals, while the rewrite reads notes and
    /// booking summaries for the REQUESTED PAGE's clients only — so those are asserted here on page
    /// slices, as exact JSON. On top of the shared dataset (which CY22-06 keeps exactly as it was) one more
    /// note is seeded carrying BOTH a ClientId (C4, last page) and a GuestPhone (G2, first page) — the
    /// data model allows it (no constraint ties the two). The pre-rewrite code (a08c6ca) loaded every
    /// note of the company and matched registered clients on <c>n.ClientId == key</c> and guests on
    /// <c>n.GuestPhone == key</c> independently, so such a note showed under BOTH; the rewrite's page
    /// filter (<c>ClientId = ANY(page ids) OR GuestPhone = ANY(page phones)</c>) plus the same two lookups
    /// must keep that, whichever of the two lands on the page. Every other client's notes/summaries are
    /// exactly CY22-06's.
    /// </summary>
    private async Task<ClientsWorld> SeedClientsWorldWithDualNoteAsync()
    {
        var w = await SeedClientsWorldAsync();
        var dualId = Guid.NewGuid();
        var g2Phone = w.Placeholders.Single(p => p.Label == "78{T}").Value + "000502";
        await DbAsync(async db =>
        {
            db.ClientNotes.Add(new ClientNote
            {
                Id = dualId, CompanyId = w.CompanyId, MasterId = w.ColleagueId, ClientId = $"{w.Prefix}-04", GuestPhone = g2Phone,
                Note = "И клиент, и телефон гостя", CreatedAt = new DateTime(2025, 3, 12, 9, 0, 0, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
        });
        return w with { Placeholders = [.. w.Placeholders, (dualId.ToString(), "{N4}")] };
    }

    [Fact, TestCase("CY22-07b")]
    public async Task MasterClients_PageSlices_NotesAndSummaries_ExactJson()
    {
        var w = await SeedClientsWorldWithDualNoteAsync();
        var url = $"/api/masters/clients?companyId={w.CompanyId}";

        // First page: two guests (G2 carries the dual note) and C1 (booking-linked note + colleague's note with a photo).
        Canon(await GetRawAsync(w.Master, url + "&page=1&pageSize=3"), w.Placeholders).Should().Be(ExpectedPage1Of3);
        // Last page: registered clients only (C4 — the dual note again, no guest on the page).
        Canon(await GetRawAsync(w.Master, url + "&page=3&pageSize=3"), w.Placeholders).Should().Be(ExpectedPage3Of3);
        // A page of guests only (G1, G2).
        Canon(await GetRawAsync(w.Master, url + "&page=1&pageSize=2"), w.Placeholders).Should().Be(ExpectedPage1Of2);
    }

    [Fact, TestCase("CY22-08b")]
    public async Task MasterClients_SearchSlices_NotesAndSummaries_ExactJson()
    {
        var w = await SeedClientsWorldWithDualNoteAsync();
        var url = $"/api/masters/clients?companyId={w.CompanyId}&search=";

        // Name search + paging: the first of two matches (C1), full notes and summaries.
        Canon(await GetRawAsync(w.Master, url + Uri.EscapeDataString("анна") + "&page=1&pageSize=1"), w.Placeholders)
            .Should().Be(ExpectedSearchAnnaPage1Of1);
        // A guest alone (by name), and a registered client alone (by phone digits) — each gets the dual note.
        Canon(await GetRawAsync(w.Master, url + Uri.EscapeDataString("Мария")), w.Placeholders).Should().Be(ExpectedSearchMaria);
        Canon(await GetRawAsync(w.Master, url + "000004"), w.Placeholders).Should().Be(ExpectedSearchC4Phone);
    }

    private const string ExpectedPage1Of3 =
        """
        {"items":[{"clientId":null,"guestPhone":"78{T}000501","name":"Гость Новый","phone":"78{T}000501","email":"new.{P}@test.local","lastVisitDate":"{FUTURE}","totalVisits":2,"notes":[{"id":"{N3}","note":"Гость, просил перезвонить","createdAt":"2025-02-01T16:00:00Z","authorId":"{M}","authorName":"Test User","bookingId":null,"bookingDate":null,"bookingServiceName":null,"canDelete":true,"photos":[]}],"bookingSummaries":[{"date":"{FUTURE}","serviceName":"Укладка {P}","status":"Confirmed"},{"date":"2025-02-01","serviceName":"Стрижка","status":"Completed"}],"phoneVerified":null},{"clientId":null,"guestPhone":"78{T}000502","name":"Мария","phone":"78{T}000502","email":null,"lastVisitDate":"2025-03-10","totalVisits":2,"notes":[{"id":"{N4}","note":"И клиент, и телефон гостя","createdAt":"2025-03-12T09:00:00Z","authorId":"{M2}","authorName":"Test User","bookingId":null,"bookingDate":null,"bookingServiceName":null,"canDelete":false,"photos":[]}],"bookingSummaries":[{"date":"2025-03-10","serviceName":"Окрашивание","status":"Completed"},{"date":"2025-03-10","serviceName":"Стрижка","status":"Cancelled"}],"phoneVerified":null},{"clientId":"{P}-01","guestPhone":null,"name":"Анна Иванова","phone":"78{T}000001","email":"anna.{P}@test.local","lastVisitDate":"2025-03-10","totalVisits":3,"notes":[{"id":"{N1}","note":"Любит короткие стрижки","createdAt":"2025-03-10T10:05:00Z","authorId":"{M}","authorName":"Test User","bookingId":"{B_C1_0310}","bookingDate":"2025-03-10","bookingServiceName":"Стрижка","canDelete":true,"photos":[]},{"id":"{N2}","note":"Аллергия на аммиак","createdAt":"2025-01-05T11:00:00Z","authorId":"{M2}","authorName":"Test User","bookingId":null,"bookingDate":null,"bookingServiceName":null,"canDelete":false,"photos":[{"id":"{PH1}","url":"/api/client-notes/photos/{PH1}","thumbnailUrl":"/api/client-notes/photos/{PH1}/thumb","width":800,"height":600,"sizeBytes":12345,"createdAt":"2025-01-05T11:01:00Z","uploadedByName":"Test User","canDelete":true}]}],"bookingSummaries":[{"date":"2025-03-10","serviceName":"Стрижка","status":"Cancelled"},{"date":"2025-03-10","serviceName":"Стрижка","status":"Completed"},{"date":"2025-01-05","serviceName":"Окрашивание","status":"Completed"}],"phoneVerified":true}],"page":1,"pageSize":3,"total":7,"hasNext":true}
        """;

    private const string ExpectedPage3Of3 =
        """
        {"items":[{"clientId":"{P}-04","guestPhone":null,"name":"Пётр","phone":"78{T}000004","email":null,"lastVisitDate":"2024-12-01","totalVisits":1,"notes":[{"id":"{N4}","note":"И клиент, и телефон гостя","createdAt":"2025-03-12T09:00:00Z","authorId":"{M2}","authorName":"Test User","bookingId":null,"bookingDate":null,"bookingServiceName":null,"canDelete":false,"photos":[]}],"bookingSummaries":[{"date":"2024-12-01","serviceName":"Стрижка","status":"Pending"}],"phoneVerified":false}],"page":3,"pageSize":3,"total":7,"hasNext":false}
        """;

    private const string ExpectedPage1Of2 =
        """
        {"items":[{"clientId":null,"guestPhone":"78{T}000501","name":"Гость Новый","phone":"78{T}000501","email":"new.{P}@test.local","lastVisitDate":"{FUTURE}","totalVisits":2,"notes":[{"id":"{N3}","note":"Гость, просил перезвонить","createdAt":"2025-02-01T16:00:00Z","authorId":"{M}","authorName":"Test User","bookingId":null,"bookingDate":null,"bookingServiceName":null,"canDelete":true,"photos":[]}],"bookingSummaries":[{"date":"{FUTURE}","serviceName":"Укладка {P}","status":"Confirmed"},{"date":"2025-02-01","serviceName":"Стрижка","status":"Completed"}],"phoneVerified":null},{"clientId":null,"guestPhone":"78{T}000502","name":"Мария","phone":"78{T}000502","email":null,"lastVisitDate":"2025-03-10","totalVisits":2,"notes":[{"id":"{N4}","note":"И клиент, и телефон гостя","createdAt":"2025-03-12T09:00:00Z","authorId":"{M2}","authorName":"Test User","bookingId":null,"bookingDate":null,"bookingServiceName":null,"canDelete":false,"photos":[]}],"bookingSummaries":[{"date":"2025-03-10","serviceName":"Окрашивание","status":"Completed"},{"date":"2025-03-10","serviceName":"Стрижка","status":"Cancelled"}],"phoneVerified":null}],"page":1,"pageSize":2,"total":7,"hasNext":true}
        """;

    private const string ExpectedSearchAnnaPage1Of1 =
        """
        {"items":[{"clientId":"{P}-01","guestPhone":null,"name":"Анна Иванова","phone":"78{T}000001","email":"anna.{P}@test.local","lastVisitDate":"2025-03-10","totalVisits":3,"notes":[{"id":"{N1}","note":"Любит короткие стрижки","createdAt":"2025-03-10T10:05:00Z","authorId":"{M}","authorName":"Test User","bookingId":"{B_C1_0310}","bookingDate":"2025-03-10","bookingServiceName":"Стрижка","canDelete":true,"photos":[]},{"id":"{N2}","note":"Аллергия на аммиак","createdAt":"2025-01-05T11:00:00Z","authorId":"{M2}","authorName":"Test User","bookingId":null,"bookingDate":null,"bookingServiceName":null,"canDelete":false,"photos":[{"id":"{PH1}","url":"/api/client-notes/photos/{PH1}","thumbnailUrl":"/api/client-notes/photos/{PH1}/thumb","width":800,"height":600,"sizeBytes":12345,"createdAt":"2025-01-05T11:01:00Z","uploadedByName":"Test User","canDelete":true}]}],"bookingSummaries":[{"date":"2025-03-10","serviceName":"Стрижка","status":"Cancelled"},{"date":"2025-03-10","serviceName":"Стрижка","status":"Completed"},{"date":"2025-01-05","serviceName":"Окрашивание","status":"Completed"}],"phoneVerified":true}],"page":1,"pageSize":1,"total":2,"hasNext":true}
        """;

    private const string ExpectedSearchMaria =
        """
        {"items":[{"clientId":null,"guestPhone":"78{T}000502","name":"Мария","phone":"78{T}000502","email":null,"lastVisitDate":"2025-03-10","totalVisits":2,"notes":[{"id":"{N4}","note":"И клиент, и телефон гостя","createdAt":"2025-03-12T09:00:00Z","authorId":"{M2}","authorName":"Test User","bookingId":null,"bookingDate":null,"bookingServiceName":null,"canDelete":false,"photos":[]}],"bookingSummaries":[{"date":"2025-03-10","serviceName":"Окрашивание","status":"Completed"},{"date":"2025-03-10","serviceName":"Стрижка","status":"Cancelled"}],"phoneVerified":null}],"page":1,"pageSize":20,"total":1,"hasNext":false}
        """;

    private const string ExpectedSearchC4Phone =
        """
        {"items":[{"clientId":"{P}-04","guestPhone":null,"name":"Пётр","phone":"78{T}000004","email":null,"lastVisitDate":"2024-12-01","totalVisits":1,"notes":[{"id":"{N4}","note":"И клиент, и телефон гостя","createdAt":"2025-03-12T09:00:00Z","authorId":"{M2}","authorName":"Test User","bookingId":null,"bookingDate":null,"bookingServiceName":null,"canDelete":false,"photos":[]}],"bookingSummaries":[{"date":"2024-12-01","serviceName":"Стрижка","status":"Pending"}],"phoneVerified":false}],"page":1,"pageSize":20,"total":1,"hasNext":false}
        """;

    // ── GET /api/companies/{id}/stats — dataset ─────────────────────────────────────────────────

    private sealed record StatsWorld(HttpClient Owner, Guid CompanyId, (string Value, string Label)[] Placeholders);

    /// <summary>
    /// Period March 2025. C1: history before the period (not new) + a multi-service completed visit in
    /// it. C2: first ever visit in the period (new) with a second, cancelled one. C3: only a no-show on
    /// the period's last day (new — any status counts). C4: only after the period (not new). C5: a
    /// cancelled visit the day before the period + a completed one on its first day (not new). C6:
    /// confirmed, multi-service, in the period (new). A guest multi-service visit (never "new client").
    /// Prices carry kopecks; two masters split the completed revenue.
    /// </summary>
    private async Task<StatsWorld> SeedStatsWorldAsync()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var m1 = await AddMasterAsync(owner.Token, company.Id);
        var m2 = await AddMasterAsync(owner.Token, company.Id);
        var prefix = "f" + Guid.NewGuid().ToString("N")[..11];
        var tag = Math.Abs(prefix.GetHashCode() % 900) + 100;
        string Cid(int n) => $"{prefix}-0{n}";

        var s1 = Svc(company.Id, "Стрижка", 1000.25m);
        var s2 = Svc(company.Id, "Укладка", 500.25m);
        var s3 = Svc(company.Id, "Маникюр", 300.00m);

        await DbAsync(async db =>
        {
            for (var i = 1; i <= 6; i++)
                db.Users.Add(User(Cid(i), $"Клиент{i}", "Тестовый", $"77{tag}{i:000000}", null, confirmed: false));
            db.Services.AddRange(s1, s2, s3);
            var k = company.Id;
            db.Bookings.AddRange(
                Bk(k, m1.UserId, Cid(1), D(2025, 2, 15), 10, BookingStatus.Completed, 1000.00m, [s1]),
                Bk(k, m1.UserId, Cid(1), D(2025, 3, 5), 10, BookingStatus.Completed, 1500.50m, [s1, s2]),
                Bk(k, m2.UserId, Cid(2), D(2025, 3, 5), 12, BookingStatus.Completed, 99.99m, [s1]),
                Bk(k, m2.UserId, Cid(2), D(2025, 3, 20), 12, BookingStatus.Cancelled, 700.00m, [s2]),
                Bk(k, m1.UserId, Cid(3), D(2025, 3, 31), 18, BookingStatus.NoShow, 300.00m, [s3]),
                Bk(k, m1.UserId, Cid(4), D(2025, 4, 1), 9, BookingStatus.Completed, 5000.00m, [s1]),
                Bk(k, m2.UserId, Cid(5), D(2025, 2, 28), 9, BookingStatus.Cancelled, 1000.25m, [s1]),
                Bk(k, m2.UserId, Cid(5), D(2025, 3, 1), 9, BookingStatus.Completed, 0.01m, [s3]),
                Bk(k, m1.UserId, Cid(6), D(2025, 3, 15), 11, BookingStatus.Confirmed, 1300.25m, [s1, s3]),
                Bk(k, m1.UserId, null, D(2025, 3, 10), 14, BookingStatus.Completed, 250.75m, [s1],
                    guestName: "Гость", guestPhone: $"78{tag}999001"));
            await db.SaveChangesAsync();
        });

        return new StatsWorld(AuthedClient(owner.Token), company.Id,
        [
            (m1.UserId, "{M1}"), (m2.UserId, "{M2}"), (s1.Id.ToString(), "{S1}"), (s2.Id.ToString(), "{S2}"),
            (s3.Id.ToString(), "{S3}"),
        ]);
    }

    /// <summary>masterStats has no ORDER BY in either implementation (group order is whatever the rows
    /// came back in) — sorted by the placeholder label here so the comparison is order-stable; every
    /// other array keeps its response order.</summary>
    private static string CanonStats(string raw, StatsWorld w)
    {
        var node = JsonNode.Parse(Canon(raw, w.Placeholders))!.AsObject();
        var masters = node["masterStats"]!.AsArray().Select(n => n!.DeepClone())
            .OrderBy(n => (string)n["masterId"]!, StringComparer.Ordinal).ToList();
        node["masterStats"] = new JsonArray(masters.ToArray());
        return node.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    // ── CY22-09 — stats over a busy period, exact JSON ──────────────────────────────────────────

    [Fact, TestCase("CY22-09")]
    public async Task CompanyStats_BusyPeriod_ExactJson()
    {
        var w = await SeedStatsWorldAsync();

        var raw = await GetRawAsync(w.Owner, $"/api/companies/{w.CompanyId}/stats?from=2025-03-01&to=2025-03-31");

        CanonStats(raw, w).Should().Be(
            """
            {"totalRevenue":1851.25,"bookingsCount":7,"completedCount":4,"cancelledCount":1,"newClientsCount":3,"masterStats":[{"masterId":"{M1}","masterName":"Test User","bookingsCount":2,"revenue":1751.25},{"masterId":"{M2}","masterName":"Test User","bookingsCount":2,"revenue":100.00}],"popularServices":[{"serviceId":"{S1}","serviceName":"Стрижка","count":4},{"serviceId":"{S3}","serviceName":"Маникюр","count":3},{"serviceId":"{S2}","serviceName":"Укладка","count":2}],"dailyRevenue":[{"date":"2025-03-01","revenue":0.01},{"date":"2025-03-05","revenue":1600.49},{"date":"2025-03-10","revenue":250.75}]}
            """);
    }

    // ── CY22-10 — edge periods: nothing, cancelled only, a single day ───────────────────────────

    [Fact, TestCase("CY22-10")]
    public async Task CompanyStats_EdgePeriods_ExactJson()
    {
        var w = await SeedStatsWorldAsync();
        var url = $"/api/companies/{w.CompanyId}/stats";

        var empty = CanonStats(await GetRawAsync(w.Owner, url + "?from=2030-01-01&to=2030-01-31"), w);
        var cancelledOnly = CanonStats(await GetRawAsync(w.Owner, url + "?from=2025-02-28&to=2025-02-28"), w);
        var singleDay = CanonStats(await GetRawAsync(w.Owner, url + "?from=2025-03-05T00:00:00&to=2025-03-05T23:59:59"), w);
        var wide = CanonStats(await GetRawAsync(w.Owner, url + "?from=2020-01-01&to=2030-12-31"), w);

        string.Join("\n", empty, cancelledOnly, singleDay, wide).Should().Be(
            """
            {"totalRevenue":0,"bookingsCount":0,"completedCount":0,"cancelledCount":0,"newClientsCount":0,"masterStats":[],"popularServices":[],"dailyRevenue":[]}
            {"totalRevenue":0,"bookingsCount":1,"completedCount":0,"cancelledCount":1,"newClientsCount":1,"masterStats":[],"popularServices":[{"serviceId":"{S1}","serviceName":"Стрижка","count":1}],"dailyRevenue":[]}
            {"totalRevenue":1600.49,"bookingsCount":2,"completedCount":2,"cancelledCount":0,"newClientsCount":1,"masterStats":[{"masterId":"{M1}","masterName":"Test User","bookingsCount":1,"revenue":1500.50},{"masterId":"{M2}","masterName":"Test User","bookingsCount":1,"revenue":99.99}],"popularServices":[{"serviceId":"{S1}","serviceName":"Стрижка","count":2},{"serviceId":"{S2}","serviceName":"Укладка","count":1}],"dailyRevenue":[{"date":"2025-03-05","revenue":1600.49}]}
            {"totalRevenue":7851.25,"bookingsCount":10,"completedCount":6,"cancelledCount":2,"newClientsCount":6,"masterStats":[{"masterId":"{M1}","masterName":"Test User","bookingsCount":4,"revenue":7751.25},{"masterId":"{M2}","masterName":"Test User","bookingsCount":2,"revenue":100.00}],"popularServices":[{"serviceId":"{S1}","serviceName":"Стрижка","count":7},{"serviceId":"{S3}","serviceName":"Маникюр","count":3},{"serviceId":"{S2}","serviceName":"Укладка","count":2}],"dailyRevenue":[{"date":"2025-02-15","revenue":1000.00},{"date":"2025-03-01","revenue":0.01},{"date":"2025-03-05","revenue":1600.49},{"date":"2025-03-10","revenue":250.75},{"date":"2025-04-01","revenue":5000.00}]}
            """);
    }

    // ── CY22-11 — roles are re-read on every request (F20) ──────────────────────────────────────

    /// <summary>
    /// Revocation through membership removal is already covered by SEC-050
    /// (<see cref="IdentityRoleSyncTests.RemovingSoleMembership_RevokesTheRole_Immediately"/>) and
    /// token revocation by a SecurityStamp change by AuthTests (US-17). This adds the direct case the
    /// F20 single-query rewrite of OnTokenValidated must keep: a role granted and then revoked straight
    /// in AspNetUserRoles is honoured on the very next request with the SAME token — no caching.
    /// </summary>
    [Fact, TestCase("CY22-11")]
    public async Task RoleGrantAndRevoke_TakeEffectOnTheNextRequest_SameToken()
    {
        var user = await RegisterAsync();
        var client = AuthedClient(user.Token);
        var url = $"/api/bookings/master?date={NextWeekday():yyyy-MM-dd}";

        (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var entity = await users.FindByIdAsync(user.UserId);
            (await users.AddToRoleAsync(entity!, "Master")).Succeeded.Should().BeTrue();
        }
        (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var entity = await users.FindByIdAsync(user.UserId);
            (await users.RemoveFromRoleAsync(entity!, "Master")).Succeeded.Should().BeTrue();
        }
        (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
