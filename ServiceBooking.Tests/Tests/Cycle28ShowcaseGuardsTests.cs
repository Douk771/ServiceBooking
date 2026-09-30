using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 28, pass A — US-28-02 (marks and no mixing), US-28-03 (public catalog, D-1 booking rules), US-28-04 (phone-matching exclusion),
/// admin filters and stats. CY28-05…CY28-15. Written from SPEC.md and API_CONTRACT_CYCLE28.md §591–§596 (texts are quoted from the contract, not
/// imported from the implementation). A small real company is turned into a showcase one by <see cref="Cycle28ShowcaseTestBase.MakeShowcaseAsync"/>.
/// </summary>
public class Cycle28ShowcaseGuardsTests(TestDatabaseFixture fixture) : Cycle28ShowcaseTestBase(fixture)
{
    private const string MixText = "Витринную компанию и настоящие учётные записи смешивать нельзя.";
    private const string TransferText = "Витринную компанию нельзя перенести в настоящий аккаунт, а настоящую — в витринный.";
    private const string PlanText = "Служебный тариф витрины нельзя назначить настоящему аккаунту.";
    private const string SlugText = "Адрес, начинающийся с «primer-», зарезервирован. Выберите другой.";

    private sealed record Actor(string Token, string UserId, string Phone);

    /// <summary>Owner + company + master + service + one working day. The token stays valid after the marks are set (only login is refused).</summary>
    private async Task<(Actor Owner, Guid CompanyId, string Slug, string MasterId, Guid ServiceId, DateOnly Date)> BuildAsync(bool showcase, bool open)
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        if (showcase) await MakeShowcaseAsync(company.Id, open, master.UserId);
        return (new Actor(owner.Token, owner.UserId, owner.Phone), company.Id, company.Slug, master.UserId, service.Id, date);
    }

    private object GuestBody(Guid companyId, Guid serviceId, string masterId, DateOnly date, string time, string name = "Гость Проверочный", string? phone = null) => new
    {
        companyId, serviceId, masterId, date = date.ToString("yyyy-MM-dd"), startTime = time, guestName = name, guestPhone = phone ?? UniquePhone(),
    };

    // ── CY28-06: mixing forbidden (US-28-02) ──────────────────────────────────────────────────────

    [Fact, TestCase("CY28-06")]
    public async Task AddMember_RealUserToShowcaseCompany_And_ShowcaseUserToRealCompany_Are409WithContractText()
    {
        var admin = await LoginAsSuperAdminAsync();
        var showcase = await BuildAsync(showcase: true, open: false);
        var real = await BuildAsync(showcase: false, open: false);

        var realUser = await RegisterAsync();
        var toShowcase = await AuthedClient(admin.Token).PostAsJsonAsync($"/api/companies/{showcase.CompanyId}/members",
            new { phone = realUser.Phone, firstName = "Реальный", lastName = "Человек", role = "Master", bio = (string?)null, email = (string?)null });
        toShowcase.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await toShowcase.Content.ReadAsStringAsync()).Should().Be(MixText);

        var showcaseUser = await RegisterAsync();
        await DbAsync(async db =>
        {
            (await db.Users.FirstAsync(u => u.Id == showcaseUser.UserId)).IsShowcase = true;
            await db.SaveChangesAsync();
        });
        var toReal = await AuthedClient(real.Owner.Token).PostAsJsonAsync($"/api/companies/{real.CompanyId}/members",
            new { phone = showcaseUser.Phone, firstName = "Витринный", lastName = "Человек", role = "Master", bio = (string?)null, email = (string?)null });
        toReal.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await toReal.Content.ReadAsStringAsync()).Should().Be(MixText);

        await DbAsync(async db =>
        {
            (await db.CompanyMembers.AnyAsync(m => m.CompanyId == showcase.CompanyId && m.UserId == realUser.UserId)).Should().BeFalse("nothing was written");
            (await db.CompanyMembers.AnyAsync(m => m.CompanyId == real.CompanyId && m.UserId == showcaseUser.UserId)).Should().BeFalse();
        });
    }

    [Fact, TestCase("CY28-07")]
    public async Task Admin_OwnerChange_Transfer_AndServicePlan_RefuseToMixRealAndShowcase()
    {
        var admin = await LoginAsSuperAdminAsync();
        var showcase = await BuildAsync(showcase: true, open: false);
        var real = await BuildAsync(showcase: false, open: false);

        // Owner change: a real company, a showcase user as the new responsible (made a member first so the cycle-20 link check cannot be the refusal).
        await DbAsync(async db =>
        {
            db.CompanyMembers.Add(new CompanyMember { Id = Guid.NewGuid(), CompanyId = real.CompanyId, UserId = showcase.MasterId, Role = UserRole.Master });
            await db.SaveChangesAsync();
        });
        var owner = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/companies/{real.CompanyId}/owner", new { newOwnerUserId = showcase.MasterId });
        owner.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await owner.Content.ReadAsStringAsync()).Should().Be(MixText);

        // Transfer: a real company into the showcase billing account.
        var showcaseAccount = await DbAsync(db => db.BillingAccounts.Where(a => a.OwnerUserId == showcase.Owner.UserId).Select(a => a.Id).FirstAsync());
        var transfer = await AuthedClient(admin.Token).PostAsJsonAsync($"/api/admin/companies/{real.CompanyId}/transfer",
            new { targetBillingAccountId = showcaseAccount, newOwnerUserId = showcase.Owner.UserId, confirmRightsTransfer = true });
        transfer.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await transfer.Content.ReadAsStringAsync()).Should().Be(TransferText);

        // ... and a showcase company into a real account.
        var realAccount = await DbAsync(db => db.BillingAccounts.Where(a => a.OwnerUserId == real.Owner.UserId).Select(a => a.Id).FirstAsync());
        var transfer2 = await AuthedClient(admin.Token).PostAsJsonAsync($"/api/admin/companies/{showcase.CompanyId}/transfer",
            new { targetBillingAccountId = realAccount, newOwnerUserId = real.Owner.UserId, confirmRightsTransfer = true });
        transfer2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await transfer2.Content.ReadAsStringAsync()).Should().Be(TransferText);

        // The hidden tariff cannot go to a real account.
        var assign = await AuthedClient(admin.Token).PutAsJsonAsync($"/api/admin/billing-accounts/{realAccount}/subscription", new
        {
            planId = ShowcaseCatalog.ShowcasePlanId, isActive = true, paidUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(1)).ToString("yyyy-MM-dd"), options = Array.Empty<object>(),
            reasonCode = "OperatorErrorCorrection", reasonDetails = "Проверка запрета смешивания",
        });
        assign.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await assign.Content.ReadAsStringAsync()).Should().Be(PlanText);

        await DbAsync(async db =>
        {
            var c = await db.Companies.AsNoTracking().FirstAsync(x => x.Id == real.CompanyId);
            c.OwnerUserId.Should().Be(real.Owner.UserId, "refusals leave the data untouched");
            c.BillingAccountId.Should().Be(realAccount);
            (await db.Companies.AsNoTracking().FirstAsync(x => x.Id == showcase.CompanyId)).BillingAccountId.Should().Be(showcaseAccount);
            (await db.AccountSubscriptions.AsNoTracking().Where(s => s.BillingAccountId == realAccount).ToListAsync())
                .Should().NotContain(s => s.PlanConfigId == ShowcaseCatalog.ShowcasePlanId);
        });
    }

    [Fact, TestCase("CY28-08")]
    public async Task ReservedSlug_PrimerPrefix_IsRefusedForRealOwners_AnyCase_ButSimilarSlugsAreFine()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();
        var cityId = await AnyCityIdAsync();
        async Task<HttpResponseMessage> Create(string slug) => await AuthedClient(owner.Token).PostAsJsonAsync("/api/companies",
            new CreateCompanyDto("Проверка адреса", slug, null, null, null, null, cityId, null, true, OwnerTerms: CurrentOwnerTermsDto()));

        foreach (var slug in new[] { "primer-salon", "PRIMER-Salon", "primer-" })
        {
            var refused = await Create(slug);
            refused.StatusCode.Should().Be(HttpStatusCode.Conflict, slug);
            (await refused.Content.ReadAsStringAsync()).Should().Be(SlugText);
        }
        (await Create(Unique("primerose-"))).StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created],
            "only the exact reserved prefix «primer-» is blocked");
    }

    [Fact, TestCase("CY28-09")]
    public async Task Login_ShowcaseAccount_Gets401_IdenticalToWrongPassword()
    {
        var user = await RegisterAsync(password: "Password123!");
        var wrong = await LoginRawAsync(user.Phone, "definitely-wrong-Password1!");
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var wrongBody = await wrong.Content.ReadAsStringAsync();

        (await LoginRawAsync(user.Phone, "Password123!")).StatusCode.Should().Be(HttpStatusCode.OK, "control: a real account with the right password logs in");

        await DbAsync(async db =>
        {
            (await db.Users.FirstAsync(u => u.Id == user.UserId)).IsShowcase = true;
            await db.SaveChangesAsync();
        });
        var showcase = await LoginRawAsync(user.Phone, "Password123!");
        showcase.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a showcase account never logs in on a production configuration, even with the right password");
        (await showcase.Content.ReadAsStringAsync()).Should().Be(wrongBody, "the answer must not be an oracle: same status and body as a wrong password");
    }

    // ── CY28-10: phone-matching exclusion (§574.5) ────────────────────────────────────────────────

    [Fact, TestCase("CY28-10")]
    public async Task RealUserWithConfirmedShowcasePhone_DoesNotSeeOrDeleteShowcaseBookings()
    {
        const string phone = "72005550001";
        var showcase = await BuildAsync(showcase: true, open: false);
        var real = await BuildAsync(showcase: false, open: false);

        Guid showcaseBooking = Guid.NewGuid(), realBooking = Guid.NewGuid();
        var showcaseCompanyName = await DbAsync(db => db.Companies.Where(c => c.Id == showcase.CompanyId).Select(c => c.Name).FirstAsync());
        var realCompanyName = await DbAsync(db => db.Companies.Where(c => c.Id == real.CompanyId).Select(c => c.Name).FirstAsync());
        await DbAsync(async db =>
        {
            Booking Make(Guid id, Guid company, Guid service, string master, string name, ShowcaseBookingKind kind) => new()
            {
                Id = id, CompanyId = company, ServiceId = service, MasterId = master, GuestName = name, GuestPhone = phone,
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)), StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0),
                Price = 1000, Status = BookingStatus.Completed, ShowcaseKind = kind,
            };
            db.Bookings.Add(Make(showcaseBooking, showcase.CompanyId, showcase.ServiceId, showcase.MasterId, "ВитринныйГостьЭкспорта", ShowcaseBookingKind.Seeded));
            db.Bookings.Add(Make(realBooking, real.CompanyId, real.ServiceId, real.MasterId, "НастоящийГостьЭкспорта", ShowcaseBookingKind.None));
            await db.SaveChangesAsync();
        });

        var user = await RegisterAsync(phone: "+" + phone);
        await MarkPhoneVerifiedAsync(phone, user.UserId);

        var export = await AuthedClient(user.Token).GetAsync("/api/profile/export");
        export.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await export.Content.ReadAsStringAsync();
        text.Should().Contain(realBooking.ToString(), "control: the gated phone matching does find real guest bookings on the confirmed number");
        text.Should().Contain(realCompanyName);
        text.Should().NotContain(showcaseBooking.ToString(), "§574.5: fictional bookings on a real number are not the subject's data");
        text.Should().NotContain(showcaseCompanyName, "the showcase company is not listed as an operator holding the subject's data");

        var deletion = await AuthedClient(user.Token).PostAsJsonAsync("/api/profile/delete-account", new { currentPassword = "Password123!" });
        deletion.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await DbAsync(async db =>
        {
            var shown = await db.Bookings.AsNoTracking().FirstAsync(b => b.Id == showcaseBooking);
            shown.GuestName.Should().Be("ВитринныйГостьЭкспорта", "deleting a real account must not touch showcase rows");
            shown.ClientDeleted.Should().BeFalse();
        });
    }

    // ── CY28-11/12: booking into a showcase (D-1) ─────────────────────────────────────────────────

    [Fact, TestCase("CY28-11")]
    public async Task ClosedShowcase_GuestGets409Json_StaffCanStillBook_SlotsStayVisible()
    {
        var s = await BuildAsync(showcase: true, open: false);

        var refused = await AnonymousClient().PostAsJsonAsync("/api/bookings", GuestBody(s.CompanyId, s.ServiceId, s.MasterId, s.Date, "10:00:00"));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        refused.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        using (var doc = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("ShowcaseBookingClosed");
            doc.RootElement.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
        }

        // A signed-in client is refused the same way.
        var client = await RegisterAsync();
        var refusedClient = await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings", new
        {
            companyId = s.CompanyId, serviceId = s.ServiceId, masterId = s.MasterId, date = s.Date.ToString("yyyy-MM-dd"), startTime = "10:00:00",
        });
        refusedClient.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // The calendar still works (only the confirmation is refused).
        var slots = await AnonymousClient().GetAsync($"/api/workinghours?companyId={s.CompanyId}&masterId={s.MasterId}&from={s.Date:yyyy-MM-dd}&to={s.Date:yyyy-MM-dd}");
        slots.StatusCode.Should().NotBe(HttpStatusCode.Conflict);

        // The company's own staff (the demo owner) is not refused.
        var staff = await AuthedClient(s.Owner.Token).PostAsJsonAsync("/api/bookings", GuestBody(s.CompanyId, s.ServiceId, s.MasterId, s.Date, "11:00:00"));
        staff.StatusCode.Should().Be(HttpStatusCode.Created, await staff.Content.ReadAsStringAsync());

        await DbAsync(async db =>
            (await db.Bookings.CountAsync(b => b.CompanyId == s.CompanyId && b.StartTime == new TimeOnly(10, 0))).Should().Be(0, "a refused booking leaves nothing"));
    }

    [Fact, TestCase("CY28-12")]
    public async Task OpenShowcase_VisitorBooks_MarkedAsVisitor_DtoTellsShowcase_RescheduleAndCancelWork()
    {
        var s = await BuildAsync(showcase: true, open: true);

        var created = await AnonymousClient().PostAsJsonAsync("/api/bookings", GuestBody(s.CompanyId, s.ServiceId, s.MasterId, s.Date, "10:00:00"));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        using var dto = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        dto.RootElement.GetProperty("companyIsShowcase").GetBoolean().Should().BeTrue();
        var id = dto.RootElement.GetProperty("id").GetGuid();

        await DbAsync(async db =>
            (await db.Bookings.AsNoTracking().FirstAsync(b => b.Id == id)).ShowcaseKind.Should().Be(ShowcaseBookingKind.Visitor));

        // Staff of the company moves and cancels it (a visitor without an account cannot); the DTO keeps the mark.
        var moved = await AuthedClient(s.Owner.Token).PatchAsJsonAsync($"/api/bookings/{id}/reschedule", new { date = s.Date.ToString("yyyy-MM-dd"), startTime = "12:00:00" });
        moved.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.NoContent], await moved.Content.ReadAsStringAsync());
        var cancelled = await AuthedClient(s.Owner.Token).PatchAsJsonAsync($"/api/bookings/{id}/cancel", "проверка");
        cancelled.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        // A signed-in client: the marked booking shows the flag in «Мои визиты».
        var client = await RegisterAsync();
        var second = await AuthedClient(client.Token).PostAsJsonAsync("/api/bookings", new
        {
            companyId = s.CompanyId, serviceId = s.ServiceId, masterId = s.MasterId, date = s.Date.ToString("yyyy-MM-dd"), startTime = "14:00:00",
        });
        second.StatusCode.Should().Be(HttpStatusCode.Created, await second.Content.ReadAsStringAsync());
        var mine = await AuthedClient(client.Token).GetAsync("/api/bookings/client");
        using var list = JsonDocument.Parse(await mine.Content.ReadAsStringAsync());
        list.RootElement.EnumerateArray().Should().ContainSingle().Which.GetProperty("companyIsShowcase").GetBoolean().Should().BeTrue();

        // A real company's booking says false.
        var real = await BuildAsync(showcase: false, open: false);
        var ordinary = await AnonymousClient().PostAsJsonAsync("/api/bookings", GuestBody(real.CompanyId, real.ServiceId, real.MasterId, real.Date, "10:00:00"));
        ordinary.StatusCode.Should().Be(HttpStatusCode.Created);
        using var ordinaryDto = JsonDocument.Parse(await ordinary.Content.ReadAsStringAsync());
        ordinaryDto.RootElement.GetProperty("companyIsShowcase").GetBoolean().Should().BeFalse();
    }

    // ── CY28-13: retention of visitor bookings (24 h) ────────────────────────────────────────────

    [Fact, TestCase("CY28-13")]
    public async Task Retention_DeletesVisitorBookingsOlderThan24h_WithChildren_AndNeverSeededOrRealOnes()
    {
        var s = await BuildAsync(showcase: true, open: true);
        var real = await BuildAsync(showcase: false, open: false);

        Guid oldVisitor, freshVisitor, oldSeeded, oldReal;
        (oldVisitor, freshVisitor, oldSeeded, oldReal) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await DbAsync(async db =>
        {
            Booking Make(Guid id, Guid company, Guid service, string master, ShowcaseBookingKind kind, TimeSpan age, int hour)
            {
                var b = new Booking
                {
                    Id = id, CompanyId = company, ServiceId = service, MasterId = master, GuestName = "Гость ретенции", GuestPhone = "70000000000",
                    Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), StartTime = new TimeOnly(hour, 0), EndTime = new TimeOnly(hour + 1, 0),
                    Price = 1000, Status = BookingStatus.Confirmed, ShowcaseKind = kind,
                    CreatedAt = DateTime.UtcNow - age, UpdatedAt = DateTime.UtcNow - age,
                };
                b.BookingServices.Add(new BookingService { Id = Guid.NewGuid(), BookingId = id, ServiceId = service, Position = 0, Price = 1000, DurationMinutes = 60, NameSnapshot = "Услуга" });
                return b;
            }
            db.Bookings.Add(Make(oldVisitor, s.CompanyId, s.ServiceId, s.MasterId, ShowcaseBookingKind.Visitor, TimeSpan.FromHours(25), 10));
            db.Bookings.Add(Make(freshVisitor, s.CompanyId, s.ServiceId, s.MasterId, ShowcaseBookingKind.Visitor, TimeSpan.FromHours(2), 12));
            db.Bookings.Add(Make(oldSeeded, s.CompanyId, s.ServiceId, s.MasterId, ShowcaseBookingKind.Seeded, TimeSpan.FromDays(3), 14));
            db.Bookings.Add(Make(oldReal, real.CompanyId, real.ServiceId, real.MasterId, ShowcaseBookingKind.None, TimeSpan.FromDays(3), 10));
            await db.SaveChangesAsync();
        });

        await using var dry = Factory.WithWebHostBuilder(b => b.UseSetting("ScheduledTasks:data-retention:DryRun", "true"));
        using (var scope = dry.Services.CreateScope())
            await scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "data-retention").ExecuteAsync(CancellationToken.None);
        await DbAsync(async db => (await db.Bookings.CountAsync(b => new[] { oldVisitor, freshVisitor, oldSeeded, oldReal }.Contains(b.Id))).Should().Be(4, "dry run deletes nothing"));

        await using var live = Factory.WithWebHostBuilder(b => b.UseSetting("ScheduledTasks:data-retention:DryRun", "false"));
        using (var scope = live.Services.CreateScope())
            await scope.ServiceProvider.GetServices<IScheduledTask>().Single(t => t.Name == "data-retention").ExecuteAsync(CancellationToken.None);

        await DbAsync(async db =>
        {
            (await db.Bookings.AnyAsync(b => b.Id == oldVisitor)).Should().BeFalse("a visitor booking older than 24 h is purged");
            (await db.BookingServices.AnyAsync(x => x.BookingId == oldVisitor)).Should().BeFalse("with its service rows");
            (await db.BookingEvents.AnyAsync(x => x.BookingId == oldVisitor)).Should().BeFalse("and its journal");
            (await db.Bookings.AnyAsync(b => b.Id == freshVisitor)).Should().BeTrue("younger than 24 h stays");
            (await db.Bookings.AnyAsync(b => b.Id == oldSeeded)).Should().BeTrue("generated bookings are not touched");
            (await db.Bookings.AnyAsync(b => b.Id == oldReal)).Should().BeTrue("real bookings are not touched");
        });
    }

    // ── CY28-14/15: public catalog, admin filter and stats ───────────────────────────────────────

    [Fact, TestCase("CY28-14")]
    public async Task PublicCatalog_ShowsShowcaseWithFlags_RealCompanyHasBothFalse_NoContactsLeaked()
    {
        var open = await BuildAsync(showcase: true, open: true);
        var closed = await BuildAsync(showcase: true, open: false);
        var real = await BuildAsync(showcase: false, open: false);

        async Task<JsonElement> BySlug(string slug)
        {
            var r = await AnonymousClient().GetAsync($"/api/companies/{slug}");
            r.StatusCode.Should().Be(HttpStatusCode.OK, slug);
            return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        var o = await BySlug(open.Slug);
        o.GetProperty("isShowcase").GetBoolean().Should().BeTrue();
        o.GetProperty("showcaseBookingOpen").GetBoolean().Should().BeTrue();
        o.GetProperty("onlineBookingEnabled").GetBoolean().Should().BeTrue("the hidden tariff allows online booking, the calendar is shown");
        var c = await BySlug(closed.Slug);
        c.GetProperty("isShowcase").GetBoolean().Should().BeTrue();
        c.GetProperty("showcaseBookingOpen").GetBoolean().Should().BeFalse();
        var r = await BySlug(real.Slug);
        r.GetProperty("isShowcase").GetBoolean().Should().BeFalse();
        r.GetProperty("showcaseBookingOpen").GetBoolean().Should().BeFalse();

        // The catalog listing carries the same fields and includes the showcase companies (visibility comes from the hidden tariff).
        var list = await AnonymousClient().GetAsync("/api/companies/public?pageSize=100");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await list.Content.ReadAsStringAsync();
        body.Should().Contain(open.Slug).And.Contain(closed.Slug);
    }

    [Fact, TestCase("CY28-15")]
    public async Task Admin_ShowcaseFilter_ThreeLists_400OnUnknown_StatsExcludeShowcase()
    {
        var admin = await LoginAsSuperAdminAsync();
        var s = await BuildAsync(showcase: true, open: true);
        var real = await BuildAsync(showcase: false, open: false);
        await DbAsync(async db =>
        {
            db.Bookings.Add(new Booking
            {
                Id = Guid.NewGuid(), CompanyId = s.CompanyId, ServiceId = s.ServiceId, MasterId = s.MasterId, GuestName = "Витрина", GuestPhone = "72005550002",
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 0),
                Price = 5000, Status = BookingStatus.Completed, ShowcaseKind = ShowcaseBookingKind.Seeded,
            });
            await db.SaveChangesAsync();
        });
        var client = AuthedClient(admin.Token);

        async Task<List<JsonElement>> Items(string url)
        {
            var response = await client.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.OK, url);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var items = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("items");
            return items.EnumerateArray().Select(e => e.Clone()).ToList();
        }

        // Companies.
        var only = await Items($"/api/admin/companies?showcase=only&search={s.Slug}");
        only.Should().ContainSingle().Which.GetProperty("isShowcase").GetBoolean().Should().BeTrue();
        (await Items($"/api/admin/companies?showcase=exclude&search={s.Slug}")).Should().BeEmpty();
        (await Items($"/api/admin/companies?showcase=exclude&search={real.Slug}")).Should().ContainSingle();
        (await Items($"/api/admin/companies?showcase=only&search={real.Slug}")).Should().BeEmpty();
        (await Items($"/api/admin/companies?showcase=all&search={real.Slug}")).Should().ContainSingle();
        (await Items($"/api/admin/companies?search={real.Slug}")).Should().ContainSingle().Which.GetProperty("isShowcase").GetBoolean().Should().BeFalse();

        // Users.
        (await Items($"/api/admin/users?showcase=only&search={s.Owner.Phone.TrimStart('+')}")).Should().ContainSingle().Which.GetProperty("isShowcase").GetBoolean().Should().BeTrue();
        (await Items($"/api/admin/users?showcase=exclude&search={s.Owner.Phone.TrimStart('+')}")).Should().BeEmpty();
        (await Items($"/api/admin/users?showcase=exclude&search={real.Owner.Phone.TrimStart('+')}")).Should().ContainSingle();

        // Billing accounts.
        var accounts = await Items("/api/admin/billing-accounts?showcase=only&pageSize=100");
        accounts.Should().NotBeEmpty().And.OnlyContain(a => a.GetProperty("isShowcase").GetBoolean());
        (await Items("/api/admin/billing-accounts?showcase=exclude&pageSize=100")).Should().OnlyContain(a => !a.GetProperty("isShowcase").GetBoolean());

        // Unknown value → 400 with the contract text, on all three routes.
        foreach (var route in new[] { "users", "companies", "billing-accounts" })
        {
            var bad = await client.GetAsync($"/api/admin/{route}?showcase=maybe");
            bad.StatusCode.Should().Be(HttpStatusCode.BadRequest, route);
            (await bad.Content.ReadAsStringAsync()).Should().Be("showcase должен быть одним из: all, only, exclude.");
        }

        // Stats: totals do not include the showcase; the showcase counters do.
        var stats = JsonDocument.Parse(await (await client.GetAsync("/api/admin/stats")).Content.ReadAsStringAsync()).RootElement;
        stats.GetProperty("showcaseCompanies").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        stats.GetProperty("showcaseUsers").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        stats.GetProperty("showcaseBookings").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        var totals = await DbAsync(async db => (
            Companies: await db.Companies.CountAsync(c => !c.IsShowcase),
            Users: await db.Users.CountAsync(u => !u.IsShowcase),
            Bookings: await db.Bookings.CountAsync(b => b.ShowcaseKind == ShowcaseBookingKind.None),
            Revenue: await db.Bookings.Where(b => b.ShowcaseKind == ShowcaseBookingKind.None && b.Status == BookingStatus.Completed).SumAsync(b => (decimal?)b.Price) ?? 0m));
        stats.GetProperty("totalCompanies").GetInt32().Should().Be(totals.Companies);
        stats.GetProperty("totalBookings").GetInt32().Should().Be(totals.Bookings);
        stats.GetProperty("totalRevenue").GetDecimal().Should().Be(totals.Revenue, "the 5000 of the showcase booking must not be counted");
    }

    // ── mailing ───────────────────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY28-05C")]
    public async Task Mailing_ToShowcaseCompany_Is409_BeforeAnythingIsWritten()
    {
        var s = await BuildAsync(showcase: true, open: true);
        var response = await AuthedClient(s.Owner.Token).PostAsJsonAsync($"/api/companies/{s.CompanyId}/mail",
            new { subject = "Проверка", message = "Текст рассылки" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Be("По витринной компании рассылка недоступна.");
        await DbAsync(async db => (await db.MailLogs.CountAsync(m => m.CompanyId == s.CompanyId)).Should().Be(0));
    }
}
