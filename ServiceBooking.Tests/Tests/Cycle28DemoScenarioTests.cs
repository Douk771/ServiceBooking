using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Ops;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// One demo host on its own database "sbtest_&lt;key&gt;_demo" after one operator reset (exactly <c>ops demo reset --yes</c>). Cycle 36 (BE-36-03): the reset
/// generates ~10 thousand bookings and takes 13-25 s, so it is no longer done before each of the 12 scenarios — <see cref="Cycle28DemoScenarioTests"/> (read-only
/// scenarios) shares one state per class, <see cref="Cycle28DemoMutationTests"/> (scenarios that book, register visitors or reset) still gets a fresh state per test.
/// </summary>
public sealed class DemoScenarioState
{
    private TestClassDatabaseLease _lease = null!;

    public DemoHostFactory Factory { get; private set; } = null!;
    public TimeSpan FirstResetTook { get; private set; }

    /// <summary>Row counts of the tables that the generator must never write — taken right after the first reset, before any visitor acted.</summary>
    public Dictionary<string, int> NeverWritten { get; private set; } = null!;

    public static async Task<DemoScenarioState> CreateAsync()
    {
        var state = new DemoScenarioState();
        try
        {
            await state.InitializeAsync();
        }
        catch
        {
            await state.DisposeAsync();
            throw;
        }
        return state;
    }

    private async Task InitializeAsync()
    {
        _lease = await TestRunEnvironment.LeaseClassDatabaseAsync("demo");
        // 00:00 local: "the nightly slot" is always already behind us, so the nightly task is due exactly when the last reset is older than today.
        Factory = new DemoHostFactory(_lease.ConnectionString, new Dictionary<string, string?> { ["DemoMode:ResetLocalTime"] = "00:00" });
        _ = Factory.Services;

        var sw = Stopwatch.StartNew();
        var writer = new StringWriter();
        var exit = await OpsCommandRunner.RunAsync(Factory.Services, OpsCommandLine.Parse(["ops", "demo", "reset", "--yes"])!, writer);
        FirstResetTook = sw.Elapsed;
        exit.Should().Be(0, writer.ToString());

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        NeverWritten = new Dictionary<string, int>
        {
            ["ClientHealthNotes"] = await db.ClientHealthNotes.CountAsync(),
            ["ClientNotePhotos"] = await db.ClientNotePhotos.CountAsync(),
            // the SuperAdmin of the instance has consents of his own; what the GENERATOR must not write is a consent of a demo account or of a phone
            ["ConsentRecords"] = await db.ConsentRecords.CountAsync(c => c.UserId == null || db.Users.Any(u => u.Id == c.UserId && u.IsShowcase)),
            ["NotificationChannels"] = await db.NotificationChannels.CountAsync(),
            ["OutboundNotifications"] = await db.OutboundNotifications.CountAsync(),
            ["StaffPushNotifications"] = await db.StaffPushNotifications.CountAsync(),
            ["MailLogs"] = await db.MailLogs.CountAsync(),
        };
    }

    public async Task DisposeAsync()
    {
        try { if (Factory is not null) await Factory.DisposeAsync(); }
        finally { if (_lease is not null) await _lease.DropAsync(); }
    }
}

/// <summary>Shared helpers of the demo scenarios (they differ only in how often the demo is reset).</summary>
public abstract class Cycle28DemoScenarioBase
{
    protected static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    protected abstract DemoScenarioState State { get; }

    protected DemoHostFactory _factory => State.Factory;
    protected TimeSpan _firstResetTook => State.FirstResetTook;
    protected Dictionary<string, int> _neverWritten => State.NeverWritten;

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    protected async Task<(int Exit, string Output)> OpsAsync(params string[] words)
    {
        var writer = new StringWriter();
        var exit = await OpsCommandRunner.RunAsync(_factory.Services, OpsCommandLine.Parse(["ops", .. words])!, writer);
        return (exit, writer.ToString());
    }

    protected async Task<T> Db<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected async Task Db(Func<AppDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected HttpClient Client(string? token = null)
    {
        var http = _factory.CreateClient();
        if (token is not null) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    protected async Task<AuthResponseDto> DemoLoginAsync(string role)
    {
        var response = await Client().PostAsJsonAsync("/api/demo/login", new { role });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>(Web))!;
    }

    protected static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    protected static JsonElement TokenPayload(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.Clone();
    }

    protected string VisitorPhone() => "79" + Random.Shared.NextInt64(100_000_000, 999_999_999);

    /// <summary>A self-registered visitor (an ordinary client, no demo claim).</summary>
    protected async Task<AuthResponseDto> RegisterVisitorAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var snapshot = scope.ServiceProvider.GetRequiredService<LegalDocumentProvider>().Current!;
        var legal = new RegisterLegalDto(snapshot.Get(LegalDocumentType.Privacy)!.Version, snapshot.Get(LegalDocumentType.TermsClient)!.Version);
        var response = await Client().PostAsJsonAsync("/api/auth/register", new RegisterDto("Посетитель", "Проверочный", VisitorPhone(), "Password123!", null, legal));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>(Web))!;
    }

    protected record Slot(Guid CompanyId, string MasterId, Guid ServiceId, DateOnly Date, string Start, string NextStart);

    /// <summary>Two free slots of an open demo company on one day, found through the public slots API (like a visitor's browser does).</summary>
    protected async Task<Slot> FindFreeSlotsAsync(int fromDay)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var pick = await Db(db => db.CompanyMembers.AsNoTracking()
            .Where(m => m.Company.IsShowcase && m.Company.ShowcaseBookingOpen && m.ProvidesServices)
            .OrderBy(m => m.Company.Name)
            .Select(m => new
            {
                m.CompanyId, MasterId = m.UserId,
                ServiceId = db.MasterServices.Where(ms => ms.MasterId == m.UserId && ms.Service.CompanyId == m.CompanyId && ms.Service.IsActive)
                    .OrderBy(ms => ms.Service.DurationMinutes).Select(ms => ms.ServiceId).First(),
            }).FirstAsync());
        for (var d = fromDay; d < fromDay + 20; d++)
        {
            var date = today.AddDays(d);
            var response = await Client().GetAsync($"/api/bookings/slots?companyId={pick.CompanyId}&masterId={pick.MasterId}&serviceId={pick.ServiceId}&date={date:yyyy-MM-dd}");
            if (response.StatusCode != HttpStatusCode.OK) continue;
            var slots = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            if (slots.GetArrayLength() < 2) continue;
            string Start(int i) => (slots[i].TryGetProperty("start", out var s) ? s : slots[i].GetProperty("startTime")).GetString()!;
            return new Slot(pick.CompanyId, pick.MasterId, pick.ServiceId, date, Start(0), Start(slots.GetArrayLength() - 1));
        }
        throw new InvalidOperationException("no day with two free slots in the generated demo schedule");
    }

    protected static int RoleCount(JsonElement roles, string role) => roles.EnumerateArray().Count(r => r.GetString() == role);

}

/// <summary>
/// QA cycle 28, pass B, "Вызов 2" — the demo stand (US-28-09…US-28-11, US-28-13) as a visitor and an operator see it. Written from SPEC.md and
/// API_CONTRACT_CYCLE28.md (§596–§600a, §602), not from the implementation. Scenarios that only READ the generated demo share the one reset of their class.
/// </summary>
[Collection("Cycle28Demo")]
public class Cycle28DemoScenarioTests(DemoScenarioFixture fixture) : Cycle28DemoScenarioBase, IClassFixture<DemoScenarioFixture>
{
    protected override DemoScenarioState State => fixture.State;

    // ── US-28-10: entering under a ready role ────────────────────────────────────────────────────

    [Fact, TestCase("CY28-40")]
    public async Task FirstFill_IsWithinBudget_AndStatusListsThreeRoles()
    {
        _firstResetTook.Should().BeLessThan(TimeSpan.FromMinutes(3), "NFR: a demo reset fits into 3 minutes");

        var status = await JsonAsync(await Client().GetAsync("/api/demo/status"));
        status.GetProperty("demoMode").GetBoolean().Should().BeTrue();
        status.GetProperty("resetting").GetBoolean().Should().BeFalse();
        status.GetProperty("lastResetAtUtc").ValueKind.Should().Be(JsonValueKind.String, "the time of the reset is recorded");
        status.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("role").GetString())
            .Should().BeEquivalentTo(["owner", "master", "client"]);
        status.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("label").GetString())
            .Should().Contain(["Войти как владелец салона", "Войти как мастер", "Войти как клиент"]);
    }

    [Fact, TestCase("CY28-41")]
    public async Task RoleLogin_GivesRightRoleAndDemoClaim_AndPasswordLoginStaysClosed()
    {
        var expected = new Dictionary<string, string> { ["owner"] = "CompanyOwner", ["master"] = "Master", ["client"] = "Client" };
        foreach (var (role, systemRole) in expected)
        {
            var auth = await DemoLoginAsync(role);
            auth.Roles.Should().Contain(systemRole, role);
            auth.Roles.Should().NotContain("SuperAdmin");
            TokenPayload(auth.Token).TryGetProperty("sb_demo", out _).Should().BeTrue($"{role}: the token carries the demo claim");

            // SPEC US-28-10: the showcase accounts never enter by password, in the demo too.
            var byPassword = await Client().PostAsJsonAsync("/api/auth/login", new LoginDto(auth.Phone, "Password123!"));
            byPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"{role} by phone and password");
        }

        // Case and padding of the role name are not a way around the list; garbage and an absent body are a 400, not a 500.
        (await Client().PostAsJsonAsync("/api/demo/login", new { role = "superadmin" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client().PostAsJsonAsync("/api/demo/login", new { role = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client().PostAsync("/api/demo/login", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client().PostAsync("/api/demo/login", new StringContent("", Encoding.UTF8, "application/json"))).StatusCode
            .Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact, TestCase("CY28-42")]
    public async Task OwnerCabinet_IsFull_SubscriptionShowsPaidSalonPlan()
    {
        var owner = await DemoLoginAsync("owner");
        var http = Client(owner.Token);

        var companies = await JsonAsync(await http.GetAsync("/api/companies/my"));
        companies.GetArrayLength().Should().BeGreaterThan(0);
        var company = companies[0];
        var companyId = company.GetProperty("id").GetGuid();
        company.GetProperty("isShowcase").GetBoolean().Should().BeTrue();

        (await JsonAsync(await http.GetAsync($"/api/companies/{companyId}/masters"))).GetArrayLength().Should().BeGreaterThanOrEqualTo(3, "a salon with its team");
        var services = await JsonAsync(await http.GetAsync($"/api/services?companyId={companyId}"));
        services.GetArrayLength().Should().BeGreaterThanOrEqualTo(6);
        (await http.GetAsync($"/api/companies/{companyId}/stats?from={DateTime.UtcNow.AddDays(-30):yyyy-MM-dd}&to={DateTime.UtcNow:yyyy-MM-dd}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await http.GetAsync($"/api/bookings/master?date={DateTime.UtcNow:yyyy-MM-dd}&to={DateTime.UtcNow.AddDays(14):yyyy-MM-dd}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // «Ваша подписка»: the real public plan "Салон", paid for about a month ahead
        var subscription = await JsonAsync(await http.GetAsync("/api/billing/subscription"));
        subscription.GetProperty("plan").GetProperty("name").GetString().Should().Be("Салон");
        subscription.GetProperty("paidUntil").GetDateTime().Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromDays(2));
        subscription.GetProperty("status").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact, TestCase("CY28-43")]
    public async Task MasterAndClientCabinets_AreFilled()
    {
        // master: «Мои записи», clients with notes
        var master = await DemoLoginAsync("master");
        var asMaster = Client(master.Token);
        var companyId = await Db(db => db.CompanyMembers.Where(m => m.UserId == master.UserId).Select(m => m.CompanyId).FirstAsync());
        var bookings = await JsonAsync(await asMaster.GetAsync($"/api/bookings/master?date={DateTime.UtcNow.AddDays(-30):yyyy-MM-dd}&to={DateTime.UtcNow.AddDays(14):yyyy-MM-dd}"));
        bookings.GetArrayLength().Should().BeGreaterThan(10);
        var clients = await JsonAsync(await asMaster.GetAsync($"/api/masters/clients?companyId={companyId}&pageSize=50"));
        var items = clients.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);
        items.EnumerateArray().Any(c => c.GetProperty("notes").GetArrayLength() > 0).Should().BeTrue("the demo master's clients carry notes (US-28-13)");

        // client: «Мои визиты» in several companies, something to review
        var client = await DemoLoginAsync("client");
        var asClient = Client(client.Token);
        var visits = await JsonAsync(await asClient.GetAsync("/api/bookings/client"));
        visits.EnumerateArray().Select(v => v.GetProperty("companyId").GetGuid()).Distinct().Count().Should().BeGreaterThanOrEqualTo(3);
        (await JsonAsync(await asClient.GetAsync("/api/reviews/can-review"))).GetArrayLength().Should().BeGreaterThan(0, "an unrated visit is left for the visitor to rate");
    }

    // ── US-28-10: what demo roles cannot do ──────────────────────────────────────────────────────

    [Fact, TestCase("CY28-44")]
    public async Task RestrictedActions_AreRefusedForEveryDemoRole_NotForSelfRegisteredVisitors_NotForAnonymous()
    {
        const string text = "В демо-версии это действие недоступно.";
        var routes = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Post, "/api/profile/change-password"), (HttpMethod.Post, "/api/profile/change-phone"),
            (HttpMethod.Post, "/api/profile/delete-account"), (HttpMethod.Post, "/api/billing/subscription/request"),
        };

        foreach (var role in new[] { "owner", "master", "client" })
        {
            var auth = await DemoLoginAsync(role);
            foreach (var (method, url) in routes)
            {
                // a body that would pass validation, no body at all, and a body of the wrong shape: the refusal comes first in every case
                foreach (HttpContent? body in new HttpContent?[]
                         {
                             JsonContent.Create(new { currentPassword = "x", newPassword = "Password123!2", phone = "79001234567", planId = Guid.NewGuid() }),
                             null,
                             new StringContent("not json", Encoding.UTF8, "application/json"),
                         })
                {
                    using var request = new HttpRequestMessage(method, url) { Content = body };
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
                    var response = await _factory.CreateClient().SendAsync(request);
                    var answer = await response.Content.ReadAsStringAsync();
                    response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{role} {method} {url}: {answer}");
                    answer.Should().Be(text, $"{role} {url}");
                    response.Headers.GetValues("X-Demo-Restricted").Should().ContainSingle().Which.Should().Be("1");
                }
            }
        }

        // Anonymous: the ordinary 401, nothing demo-specific to learn.
        var anonymous = await Client().PostAsJsonAsync("/api/profile/change-password", new { });
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        anonymous.Headers.Contains("X-Demo-Restricted").Should().BeFalse();

        // A visitor who registered himself gets the old behaviour (his data is erased at night anyway): no demo refusal, no demo header.
        var visitor = await RegisterVisitorAsync();
        var own = await Client(visitor.Token).PostAsJsonAsync("/api/profile/change-password", new { currentPassword = "wrong-password", newPassword = "Password123!2" });
        own.Headers.Contains("X-Demo-Restricted").Should().BeFalse();
        own.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        (await own.Content.ReadAsStringAsync()).Should().NotBe(text);
    }

    // ── US-28-09: nothing leaves, nothing is indexed ─────────────────────────────────────────────

    [Fact, TestCase("CY28-45")]
    public async Task EveryApiAnswer_CarriesNoIndexHeader()
    {
        var owner = await DemoLoginAsync("owner");
        var answers = new[]
        {
            await Client().GetAsync("/api/demo/status"),                       // 200
            await Client().GetAsync("/api/companies/public?pageSize=5"),       // 200
            await Client().GetAsync("/api/companies/my"),                      // 401
            await Client().GetAsync("/api/no-such-route"),                     // 404
            await Client(owner.Token).PostAsJsonAsync("/api/profile/change-password", new { }), // 403
            await Client().GetAsync("/api/health/ready"),
        };
        foreach (var response in answers)
        {
            response.Headers.TryGetValues("X-Robots-Tag", out var values).Should().BeTrue($"{response.RequestMessage!.RequestUri!.AbsolutePath} ({(int)response.StatusCode})");
            values!.Single().Should().Contain("noindex").And.Contain("nofollow");
        }
    }

    [Fact, TestCase("CY28-47")]
    public async Task PhoneVerificationViaMaxBot_IsOff_AndPricingIsPublic()
    {
        var config = await JsonAsync(await Client().GetAsync("/api/phone-verification/config"));
        config.GetProperty("enabled").GetBoolean().Should().BeFalse("the production bot is not connected to the demo");

        var session = await Client().PostAsJsonAsync("/api/phone-verification/sessions", new { phone = VisitorPhone() });
        session.IsSuccessStatusCode.Should().BeFalse("no verification session can be started");
        (await session.Content.ReadAsStringAsync()).Should().NotBeNullOrWhiteSpace("the refusal is explained to the visitor");

        // the webhook of the bot is not reachable either
        (await Client().PostAsync("/api/phone-verification/max/webhook/any-token", new StringContent("{}", Encoding.UTF8, "application/json"))).IsSuccessStatusCode.Should().BeFalse();

        // the price list page is public on the demo (pricing.public-enabled = true)
        var pricing = await JsonAsync(await Client().GetAsync("/api/pricing"));
        pricing.GetProperty("plans").GetArrayLength().Should().BeGreaterThanOrEqualTo(5);
    }

    // ── US-28-13: richer demo data ───────────────────────────────────────────────────────────────

    [Fact, TestCase("CY28-48")]
    public async Task DemoData_HasReviewsRatingsNotesAndHistory_ButNothingHealthNothingPhotoNothingConsent()
    {
        var reviews = await Db(db => db.Reviews.CountAsync());
        reviews.Should().BeInRange(400, 900, "about 650 reviews");
        (await Db(db => db.ClientNotes.CountAsync())).Should().BeInRange(150, 450, "about 290 notes");
        (await Db(db => db.BookingEvents.CountAsync(e => e.Kind == BookingEventKind.Rescheduled))).Should().BeGreaterThan(0, "change history contains reschedules");

        // every company of the catalog shows a rating, and a believable one
        var catalog = await JsonAsync(await Client().GetAsync("/api/companies/public?pageSize=100"));
        var items = catalog.GetProperty("items").EnumerateArray().ToList();
        items.Should().NotBeEmpty();
        foreach (var company in items)
        {
            company.GetProperty("reviewCount").GetInt32().Should().BeGreaterThan(0, company.GetProperty("name").GetString());
            company.GetProperty("averageRating").GetDouble().Should().BeInRange(3.8, 4.5, company.GetProperty("name").GetString());
        }

        // SPEC US-28-13: health data and client photos are not generated even in the demo; nor consents, channels, outgoing queues
        foreach (var (table, count) in _neverWritten)
            count.Should().Be(0, $"{table} must stay empty after a reset");

        // reviews belong to completed visits of registered clients only (the generator does not invent reviewers)
        (await Db(db => db.Reviews.CountAsync(r => r.Booking.Status != BookingStatus.Completed))).Should().Be(0);
    }

    // ── US-28-11: the reset ──────────────────────────────────────────────────────────────────────

}

public sealed class DemoScenarioFixture : IAsyncLifetime
{
    public DemoScenarioState State { get; private set; } = null!;

    public async Task InitializeAsync() => State = await DemoScenarioState.CreateAsync();

    public async Task DisposeAsync() => await State.DisposeAsync();
}

/// <summary>
/// The scenarios of <see cref="Cycle28DemoScenarioTests"/> that change the demo (guest and visitor bookings, registered visitors, resets): each starts from its own
/// fresh reset, exactly as before the cycle-36 split. Order of the tests is random, so every scenario that creates visitor data cleans up by itself or tolerates it.
/// </summary>
[Collection("Cycle28Demo")]
public class Cycle28DemoMutationTests : Cycle28DemoScenarioBase, IAsyncLifetime
{
    private DemoScenarioState _state = null!;

    protected override DemoScenarioState State => _state;

    public async Task InitializeAsync() => _state = await DemoScenarioState.CreateAsync();

    public async Task DisposeAsync() => await _state.DisposeAsync();

    [Fact, TestCase("CY28-46")]
    public async Task VisitorBooksReschedulesCancels_AndNothingIsSent_AnywhereIncludingMailing()
    {
        var owner = await DemoLoginAsync("owner");
        var ownerHttp = Client(owner.Token);
        var flagshipId = (await JsonAsync(await ownerHttp.GetAsync("/api/companies/my")))[0].GetProperty("id").GetGuid();

        // guest booking (no account), booking of a registered visitor, and the staff moving and cancelling both — in a company the generator made
        var slot = await FindFreeSlotsAsync(8);
        var guest = await Client().PostAsJsonAsync("/api/bookings", new
        {
            companyId = slot.CompanyId, serviceId = slot.ServiceId, masterId = slot.MasterId, date = slot.Date.ToString("yyyy-MM-dd"), startTime = slot.Start,
            guestName = "Проверочный Посетитель", guestPhone = "+" + VisitorPhone(),
        });
        guest.StatusCode.Should().Be(HttpStatusCode.Created, await guest.Content.ReadAsStringAsync());
        var guestId = (await JsonAsync(guest)).GetProperty("id").GetGuid();

        var visitor = await RegisterVisitorAsync();
        var slot2 = await FindFreeSlotsAsync(9);
        var own = await Client(visitor.Token).PostAsJsonAsync("/api/bookings", new
        {
            companyId = slot2.CompanyId, serviceId = slot2.ServiceId, masterId = slot2.MasterId, date = slot2.Date.ToString("yyyy-MM-dd"), startTime = slot2.Start,
        });
        own.StatusCode.Should().Be(HttpStatusCode.Created, await own.Content.ReadAsStringAsync());
        var ownId = (await JsonAsync(own)).GetProperty("id").GetGuid();

        // the visitor moves and cancels his own booking himself, the owner of that company (whichever it is) may be another account — use the visitor's rights
        var moved = await Client(visitor.Token).PatchAsJsonAsync($"/api/bookings/{ownId}/reschedule", new { date = slot2.Date.ToString("yyyy-MM-dd"), startTime = slot2.NextStart });
        moved.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.BadRequest, HttpStatusCode.Conflict); // the company's reschedule window may refuse; what matters is: nothing is sent
        (await Client(visitor.Token).PatchAsJsonAsync($"/api/bookings/{ownId}/cancel", "передумал")).StatusCode
            .Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent, HttpStatusCode.BadRequest, HttpStatusCode.Conflict);

        // Staff of the flagship may touch the guest booking only if it is in the flagship; otherwise a 403/404 is fine — the outbound checks below are the point.
        await ownerHttp.PatchAsJsonAsync($"/api/bookings/{guestId}/reschedule", new { date = slot.Date.ToString("yyyy-MM-dd"), startTime = slot.NextStart });
        await ownerHttp.PatchAsJsonAsync($"/api/bookings/{guestId}/cancel", "проверка");

        await Db(async db =>
        {
            (await db.OutboundNotifications.CountAsync()).Should().Be(_neverWritten["OutboundNotifications"], "no WhatsApp/MAX message in the demo");
            (await db.StaffPushNotifications.CountAsync()).Should().Be(_neverWritten["StaffPushNotifications"], "no push to staff in the demo");
            (await db.MailLogs.CountAsync()).Should().Be(_neverWritten["MailLogs"]);
        });

        // «Рассылка»: refused with the demo sentence, nothing queued
        var mail = await ownerHttp.PostAsJsonAsync($"/api/companies/{flagshipId}/mail", new { subject = "Проверка", message = "Текст письма" });
        mail.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await mail.Content.ReadAsStringAsync()).Should().Be("В демо-версии рассылка не отправляется.");
        (await Db(db => db.MailLogs.CountAsync())).Should().Be(_neverWritten["MailLogs"]);
    }

    [Fact, TestCase("CY28-49")]
    public async Task Reset_RemovesEverythingVisitorsDid_KeepsDirectoriesAndSettings_KeepsRoleTokensAlive()
    {
        var ownerBefore = await DemoLoginAsync("owner");
        var visitor = await RegisterVisitorAsync();
        var slot = await FindFreeSlotsAsync(10);
        (await Client(visitor.Token).PostAsJsonAsync("/api/bookings", new
        {
            companyId = slot.CompanyId, serviceId = slot.ServiceId, masterId = slot.MasterId, date = slot.Date.ToString("yyyy-MM-dd"), startTime = slot.Start,
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        var identity = _factory.Identity;
        var publicFile = Path.Combine(identity.PublicRoot, "visitor-upload", "a.jpg");
        var privateFile = Path.Combine(identity.PrivateRoot, "visitor-upload", "b.jpg");
        foreach (var file in new[] { publicFile, privateFile })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllBytesAsync(file, [1, 2, 3]);
        }

        var before = await Db(async db => (Cities: await db.Cities.CountAsync(), Plans: await db.SubscriptionPlanConfigs.CountAsync(),
            Users: await db.Users.CountAsync(u => u.Id == visitor.UserId)));
        before.Users.Should().Be(1);

        // without --yes: a plan, nothing changes
        var (planExit, plan) = await OpsAsync("demo", "reset");
        planExit.Should().Be(0, plan);
        plan.Should().Contain("только показать");
        (await Db(db => db.Users.CountAsync(u => u.Id == visitor.UserId))).Should().Be(1);
        File.Exists(publicFile).Should().BeTrue();

        var sw = Stopwatch.StartNew();
        var (exit, output) = await OpsAsync("demo", "reset", "--yes");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(3));
        exit.Should().Be(0, output);

        await Db(async db =>
        {
            (await db.Users.CountAsync(u => u.Id == visitor.UserId)).Should().Be(0, "the self-registered visitor is gone");
            (await db.Bookings.CountAsync(b => b.ShowcaseKind == ShowcaseBookingKind.Visitor || b.ClientId == visitor.UserId)).Should().Be(0);
            (await db.Bookings.CountAsync(b => b.ShowcaseKind == ShowcaseBookingKind.None)).Should().Be(0, "nothing but generated demo rows is left");
            (await db.Companies.CountAsync(c => !c.IsShowcase)).Should().Be(0);
            (await db.Cities.CountAsync()).Should().Be(before.Cities, "the city directory stays");
            (await db.SubscriptionPlanConfigs.CountAsync()).Should().Be(before.Plans, "tariffs stay");
            (await db.PlatformSettings.FirstAsync(s => s.Key == "pricing.public-enabled")).Value.Should().BeOneOf("true", "True", "1");
            (await db.PlatformSettings.FirstAsync(s => s.Key == "demo.last-reset-utc")).UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(5));
        });
        File.Exists(publicFile).Should().BeFalse("visitor files are erased in the public root");
        File.Exists(privateFile).Should().BeFalse("and in the private root");

        // the operator (SuperAdmin) can still enter, the demo role token issued BEFORE the reset still works, the visitor's token does not
        var admin = await Client().PostAsJsonAsync("/api/auth/login", new LoginDto(identity.SuperAdminPhone, identity.SuperAdminPassword));
        admin.StatusCode.Should().Be(HttpStatusCode.OK, "SuperAdmin survives the reset");
        (await Client(ownerBefore.Token).GetAsync("/api/billing/subscription")).StatusCode.Should().Be(HttpStatusCode.OK, "same account id and stamp after the reset");
        (await Client(visitor.Token).GetAsync("/api/bookings/client")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the visitor's account no longer exists");
        (await DemoLoginAsync("owner")).UserId.Should().Be(ownerBefore.UserId);
    }

    [Fact, TestCase("CY28-50")]
    public async Task DuringReset_VisitorSeesFriendlyWaitMessage_NotAnError_ThenTheDemoIsBack_AndSecondRunIsRefusedWithoutBreakingTheFirst()
    {
        var first = Task.Run(() => OpsAsync("demo", "reset", "--yes"));
        var http = Client();

        string? busyText = null;
        HttpResponseMessage? busy = null;
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (!first.IsCompleted && DateTime.UtcNow < deadline)
        {
            var probe = await http.GetAsync("/api/companies/public?pageSize=5");
            if (probe.StatusCode == HttpStatusCode.ServiceUnavailable) { busy = probe; busyText = await probe.Content.ReadAsStringAsync(); break; }
            await Task.Delay(100);
        }
        busy.Should().NotBeNull("the visitor must see the maintenance answer while the reset runs");
        busyText.Should().Be("Демо обновляется, зайдите через минуту");
        busy!.Headers.GetValues("Retry-After").Should().ContainSingle().Which.Should().Be("60");
        busy.Headers.GetValues("X-Demo-Resetting").Should().ContainSingle().Which.Should().Be("1");

        // Status and health answer meanwhile; the status says «resetting»
        var status = await JsonAsync(await http.GetAsync("/api/demo/status"));
        status.GetProperty("resetting").GetBoolean().Should().BeTrue();
        (await http.GetAsync("/api/health/ready")).StatusCode.Should().NotBe(HttpStatusCode.ServiceUnavailable);
        (await http.PostAsJsonAsync("/api/demo/login", new { role = "owner" })).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        // A second operator run at the same moment is refused (exit 4), and it does NOT lift the flag of the first one
        var second = await OpsAsync("demo", "reset", "--yes");
        second.Exit.Should().Be(4, second.Output);
        if (!first.IsCompleted)
            (await http.GetAsync("/api/companies/public?pageSize=5")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "the first run still holds the flag");

        var (exit, output) = await first;
        exit.Should().Be(0, output);

        // back to normal for the visitor
        (await http.GetAsync("/api/companies/public?pageSize=5")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(await http.GetAsync("/api/demo/status"))).GetProperty("resetting").GetBoolean().Should().BeFalse();
        (await DemoLoginAsync("client")).Roles.Should().Contain("Client");
    }

    [Fact, TestCase("CY28-51")]
    public async Task NightlyTask_IsListedOnlyInDemo_SkipsWithoutStamp_ResetsWhenStale_ThenIsNotDue()
    {
        var superAdmin = await (await Client().PostAsJsonAsync("/api/auth/login", new LoginDto(_factory.Identity.SuperAdminPhone, _factory.Identity.SuperAdminPassword)))
            .Content.ReadFromJsonAsync<AuthResponseDto>(Web);
        var tasks = await JsonAsync(await Client(superAdmin!.Token).GetAsync("/api/admin/scheduled-tasks"));
        tasks.EnumerateArray().Select(t => t.GetProperty("name").GetString()).Should().Contain("demo-reset");

        async Task<ScheduledTaskOutcome> RunTaskAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var task = scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "demo-reset");
            return await task.ExecuteAsync(CancellationToken.None);
        }

        // fresh reset from InitializeAsync: nothing is due
        var idle = await RunTaskAsync();
        idle.Affected.Should().Be(0);
        idle.Summary.Should().Contain("not due");

        // the first fill of a demo is the operator's job: no stamp, no reset from the schedule
        string? stamp = null;
        await Db(async db =>
        {
            var row = await db.PlatformSettings.FirstAsync(s => s.Key == "demo.last-reset-utc");
            stamp = row.Value;
            db.PlatformSettings.Remove(row);
            await db.SaveChangesAsync();
        });
        var noStamp = await RunTaskAsync();
        noStamp.Affected.Should().Be(0);
        (await Db(db => db.Companies.CountAsync())).Should().BeGreaterThan(0, "the data was not touched");

        // a stale stamp (the machine missed the night): the task runs the reset once, then it is not due any more
        var visitor = await RegisterVisitorAsync();
        await Db(async db =>
        {
            db.PlatformSettings.Add(new PlatformSetting { Key = "demo.last-reset-utc", Value = DateTime.UtcNow.AddDays(-2).ToString("O"), UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        var due = await RunTaskAsync();
        due.Affected.Should().Be(1, due.Summary);
        (await Db(db => db.Users.CountAsync(u => u.Id == visitor.UserId))).Should().Be(0, "the nightly reset removes what the visitor created");
        (await RunTaskAsync()).Summary.Should().Contain("not due");
        stamp.Should().NotBeNull();
    }
}

/// <summary>
/// QA cycle 28, pass B — the two locks that keep the demo away from production (US-28-09, NFR «Безопасность»). Every production-looking setting, one at a time, stops
/// the start with a message naming it; a database with real data or another mark is refused; an empty database gets the mark and a restart on it is fine.
/// </summary>
[Collection("Cycle28Demo")]
public class Cycle28DemoLocksTests : IAsyncLifetime
{
    private TestClassDatabaseLease _lease = null!;

    public async Task InitializeAsync() => _lease = await TestRunEnvironment.LeaseClassDatabaseAsync("demo");

    public async Task DisposeAsync() => await _lease.DropAsync();

    private static string Everything(Exception exception)
    {
        var sb = new StringBuilder();
        for (var e = exception; e is not null; e = e.InnerException!) { sb.AppendLine(e.Message); if (e.InnerException is null) break; }
        return sb.ToString();
    }

    public static IEnumerable<object[]> ProductionShapes() =>
    [
        ["PublicSites:ServicesBaseUrl", "https://visit.ezbook.ru", "ServicesBaseUrl"],
        ["PublicSites:ServicesBaseUrl", "https://ezbook.ru", "ServicesBaseUrl"],
        ["AllowedOrigins", "https://demo.visit.ezbook.ru,https://visit.ezbook.ru", "AllowedOrigins"],
        ["AllowedOrigins", "https://ezbook.ru", "AllowedOrigins"],
        ["Jwt:Issuer", "ServiceBooking", "Jwt:Issuer"],
        ["Notifications:Provider", "greenapi", "Notifications:Provider"],
        ["Notifications:StaffPush:Provider", "web-push", "StaffPush"],
        ["Notifications:StaffMax:Enabled", "true", "StaffMax"],
        ["PhoneVerification:Provider", "max-bot", "PhoneVerification:Provider"],
        ["Showcase:Reseed:Enabled", "true", "Reseed"],
    ];

    [Theory, TestCase("CY28-52")]
    [MemberData(nameof(ProductionShapes))]
    public void Start_IsRefused_ByEveryProductionLookingSetting(string key, string value, string mustBeNamed)
    {
        using var factory = new DemoHostFactory(_lease.ConnectionString, new Dictionary<string, string?> { [key] = value });
        var start = () => { _ = factory.Services; };
        var thrown = start.Should().Throw<Exception>().Which;
        Everything(thrown).Should().Contain(mustBeNamed).And.Contain("demo", "the message says what a demo instance needs");
    }

    [Fact, TestCase("CY28-52")]
    public async Task Start_IsRefused_ByAProductionDatabaseName_AndAllProblemsAreReportedAtOnce()
    {
        // the name of the real database: the lock 1 looks at the name before anything touches the database
        var real = new Npgsql.NpgsqlConnectionStringBuilder(_lease.ConnectionString) { Database = "servicebooking" }.ConnectionString;
        using var factory = new DemoHostFactory(real, new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = real,
            ["PublicSites:ServicesBaseUrl"] = "https://ezbook.ru",
            ["Jwt:Issuer"] = "ServiceBooking",
        });
        var start = () => { _ = factory.Services; };
        var message = Everything(start.Should().Throw<Exception>().Which);
        message.Should().Contain("_demo").And.Contain("ServicesBaseUrl").And.Contain("Jwt:Issuer", "all violations in one message, not one by one");
        await Task.CompletedTask;
    }

    [Fact, TestCase("CY28-53")]
    public async Task DataLock_ForeignMark_Refuses_EmptyDatabaseGetsMark_RestartIsFine()
    {
        // 1. an empty database: the start succeeds and the database is marked as a demo one
        await using (var fresh = new DemoHostFactory(_lease.ConnectionString))
        {
            _ = fresh.Services;
            using var scope = fresh.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.PlatformSettings.AsNoTracking().Where(s => s.Key == "instance.kind").Select(s => s.Value).SingleAsync()).Should().Be("demo");

        }
        await using (var again = new DemoHostFactory(_lease.ConnectionString))
        {
            var restart = () => { _ = again.Services; };
            restart.Should().NotThrow("the mark instance.kind = demo lets the instance start again");
        }

        // 2. another mark: refused. Switch the mark directly and start once more.
        await using (var direct = new DemoHostFactory(_lease.ConnectionString))
        {
            _ = direct.Services;
            using var scope = direct.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.PlatformSettings.Where(s => s.Key == "instance.kind").ExecuteUpdateAsync(u => u.SetProperty(s => s.Value, "prod"));
        }
        await using (var foreign = new DemoHostFactory(_lease.ConnectionString))
        {
            var start = () => { _ = foreign.Services; };
            Everything(start.Should().Throw<Exception>().Which).Should().Contain("instance.kind");
        }
    }

    [Fact, TestCase("CY28-53")]
    public async Task DataLock_DatabaseWithRealCompany_IsRefused_AndTheMarkIsNotWritten()
    {
        // Build a "production" database: the same database, booted WITHOUT demo mode (production shape), with a real company and no mark.
        await using (var production = new DemoHostFactory(_lease.ConnectionString, new Dictionary<string, string?>
        {
            ["DemoMode:Enabled"] = "false", ["Jwt:Issuer"] = "ServiceBooking", ["AllowedOrigins"] = "http://localhost:5173",
        }))
        {
            _ = production.Services;
            using var scope = production.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.PlatformSettings.CountAsync(s => s.Key == "instance.kind")).Should().Be(0, "a production-shaped instance neither writes nor reads the mark");

            var owner = await db.Users.FirstAsync(); // the SuperAdmin seeded at startup is enough as a formal owner for a raw row
            var billing = new BillingAccount { Id = Guid.NewGuid(), OwnerUserId = owner.Id };
            db.BillingAccounts.Add(billing);
            db.Companies.Add(new Company
            {
                Id = Guid.NewGuid(), Name = "Настоящий салон", Slug = "real-" + Guid.NewGuid().ToString("N")[..8], OwnerUserId = owner.Id, BillingAccountId = billing.Id,
            });
            await db.SaveChangesAsync();
        }

        await using var demo = new DemoHostFactory(_lease.ConnectionString);
        var start = () => { _ = demo.Services; };
        Everything(start.Should().Throw<Exception>().Which).Should().Contain("настоящие данные");

        // and the failed start has not marked the database
        await using var probe = new DemoHostFactory(_lease.ConnectionString, new Dictionary<string, string?> { ["DemoMode:Enabled"] = "false", ["Jwt:Issuer"] = "ServiceBooking", ["AllowedOrigins"] = "http://localhost:5173" });
        _ = probe.Services;
        using var scope2 = probe.Services.CreateScope();
        (await scope2.ServiceProvider.GetRequiredService<AppDbContext>().PlatformSettings.CountAsync(s => s.Key == "instance.kind")).Should().Be(0);
    }
}
