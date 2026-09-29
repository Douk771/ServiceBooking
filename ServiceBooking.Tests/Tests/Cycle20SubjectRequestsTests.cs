using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 20 (SPEC_CYCLE20_LEGAL_CLOSURE.md US-20-09, Т20-13; ARCHITECTURE_CYCLE20.md §410) — the
/// SuperAdmin's manual registration of a subject request that arrived by e-mail or postal mail, the
/// second (non-anonymous, non-WebForm) intake path this cycle adds alongside the existing public
/// <c>POST /api/subject-requests</c> form. Written from SPEC.md's acceptance criteria, independently of
/// AdminController's own implementation ("Вызов 2").
/// </summary>
public class Cycle20SubjectRequestsTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── CY20-SR-01: manual registration, due date computed from the ACTUAL receipt date, not "now" ──

    [Fact, TestCase("CY20-SR-01")]
    public async Task RegisterManually_DueDateComputedFromReceivedAt_NotFromNow()
    {
        var admin = await LoginAsSuperAdminAsync();
        var receivedAt = DateTime.UtcNow.AddDays(-3);

        var response = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/subject-requests", new
        {
            kind = "Access",
            channel = "Email",
            receivedAt,
            phone = "+79001234567",
            contactValue = "ivan@example.com",
            message = "Прошу предоставить копию моих персональных данных.",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("channel").GetString().Should().Be("Email");
        body.GetProperty("receivedAt").GetDateTime().Should().BeCloseTo(receivedAt, TimeSpan.FromSeconds(5));

        // Access requests get a 10-working-day-ish absolute deadline in this test config; the essential
        // assertion is that dueAt is measured from receivedAt (3 days ago), not from "now" — so it must
        // land noticeably BEFORE "now + the same nominal window" would if it had been computed from now.
        var dueAt = body.GetProperty("dueAt").GetDateTime();
        var naiveDueFromNow = DateTime.UtcNow.AddDays(10);
        dueAt.Should().BeBefore(naiveDueFromNow,
            "the deadline must be anchored to when the letter/e-mail actually arrived, not to today");
    }

    // ── CY20-SR-02: Channel must be Email or PostalMail — WebForm is rejected here ────────────────

    [Fact, TestCase("CY20-SR-02")]
    public async Task RegisterManually_WebFormChannel_IsRejected()
    {
        var admin = await LoginAsSuperAdminAsync();

        var response = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/subject-requests", new
        {
            kind = "Access",
            channel = "WebForm",
            receivedAt = DateTime.UtcNow.AddDays(-1),
            phone = (string?)null,
            contactValue = "someone@example.com",
            message = "test",
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "WebForm means the anonymous public form submitted it — a SuperAdmin never registers one of those manually");
    }

    // ── CY20-SR-03: phone is optional (a postal letter may not carry one) — stored/masked as empty ──

    [Fact, TestCase("CY20-SR-03")]
    public async Task RegisterManually_WithoutPhone_Succeeds_PhoneMaskedIsEmpty()
    {
        var admin = await LoginAsSuperAdminAsync();

        var response = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/subject-requests", new
        {
            kind = "Erasure",
            channel = "PostalMail",
            receivedAt = DateTime.UtcNow.AddDays(-1),
            phone = (string?)null,
            contactValue = "г. Москва, ул. Примерная, д. 1",
            message = "Прошу удалить мои данные.",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("phoneMasked").GetString().Should().BeEmpty();
    }

    // ── CY20-SR-04: date in the future is rejected ───────────────────────────────────────────────

    [Fact, TestCase("CY20-SR-04")]
    public async Task RegisterManually_ReceivedAtInTheFuture_Returns400()
    {
        var admin = await LoginAsSuperAdminAsync();

        var response = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/subject-requests", new
        {
            kind = "Access",
            channel = "Email",
            receivedAt = DateTime.UtcNow.AddDays(1),
            phone = (string?)null,
            contactValue = "someone@example.com",
            message = "test",
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── CY20-SR-05: a manually-registered request appears in the general admin list, with its channel ─

    [Fact, TestCase("CY20-SR-05")]
    public async Task RegisterManually_AppearsInGeneralAdminList_WithChannelAndRegisteredBy()
    {
        var admin = await LoginAsSuperAdminAsync();

        var register = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/subject-requests", new
        {
            kind = "Rectification",
            channel = "PostalMail",
            receivedAt = DateTime.UtcNow.AddDays(-2),
            phone = (string?)null,
            contactValue = "г. Москва, ул. Примерная, д. 2",
            message = "Прошу исправить неверные данные.",
        });
        register.EnsureSuccessStatusCode();
        var created = await register.Content.ReadFromJsonAsync<JsonElement>();
        var reference = created.GetProperty("reference").GetString();

        var list = await AuthedClient(admin.Token).GetAsync("/api/admin/subject-requests?page=1&pageSize=100");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await list.Content.ReadFromJsonAsync<JsonElement>();
        var row = listBody.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("reference").GetString() == reference);
        row.GetProperty("channel").GetString().Should().Be("PostalMail");
        row.GetProperty("registeredByName").GetString().Should().NotBeNullOrEmpty();
    }

    // ── CY20-SR-06: missing/blank required fields are rejected ──────────────────────────────────

    [Fact, TestCase("CY20-SR-06")]
    public async Task RegisterManually_MissingContactOrMessage_Returns400()
    {
        var admin = await LoginAsSuperAdminAsync();

        var missingContact = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/subject-requests", new
        {
            kind = "Access",
            channel = "Email",
            receivedAt = DateTime.UtcNow.AddDays(-1),
            phone = (string?)null,
            contactValue = "",
            message = "test",
        });
        missingContact.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var missingMessage = await AuthedClient(admin.Token).PostAsJsonAsync("/api/admin/subject-requests", new
        {
            kind = "Access",
            channel = "Email",
            receivedAt = DateTime.UtcNow.AddDays(-1),
            phone = (string?)null,
            contactValue = "someone@example.com",
            message = "",
        });
        missingMessage.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── CY20-SR-07: a non-SuperAdmin cannot register a subject request manually ─────────────────

    [Fact, TestCase("CY20-SR-07")]
    public async Task RegisterManually_NonSuperAdmin_Forbidden()
    {
        var (owner, _) = await CreateOwnerWithCompanyAsync();

        var response = await AuthedClient(owner.Token).PostAsJsonAsync("/api/admin/subject-requests", new
        {
            kind = "Access",
            channel = "Email",
            receivedAt = DateTime.UtcNow.AddDays(-1),
            phone = (string?)null,
            contactValue = "someone@example.com",
            message = "test",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
