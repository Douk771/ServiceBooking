using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.Controllers;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.DTOs.Legal;
using ServiceBooking.API.DTOs.PhoneVerification;
using ServiceBooking.API.Services.PhoneVerification.Max;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 16 (SPEC_CYCLE16_TECH_DEBT.md §5.1, TD-03/TD-03-bis/TD-03-ter/TD-03-quater) — written against
/// the acceptance criteria in the spec, independently of the implementation (the same convention
/// PhoneVerificationTests follows for cycle 14). A confirmed phone is produced end-to-end through the
/// same MAX webhook flow PhoneVerificationTests (PHV-020) exercises — there is no shortcut/test-only
/// hook for flipping PhoneNumberConfirmed, on purpose, so this suite proves the real path.
/// </summary>
public class GuestDataGateCycle16Tests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── TD-03: the full gate on guest-matched data (Export / DeleteAccount) ─────────────────────

    [Fact, TestCase("TD03-001")]
    public async Task Export_UnconfirmedPhone_ExcludesGuestMatchedBookingsAndHealthNotes()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var phone = UniquePhone();

        // A guest visit exists on this phone before any account registers on it.
        var guestBooking = await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, "Guest Before Signup", phone, null, null));
        guestBooking.StatusCode.Should().Be(HttpStatusCode.Created);
        var guestBookingId = (await guestBooking.Content.ReadJsonAsync<BookingDto>())!.Id;

        // A stranger who merely knows this phone number registers an account on it — the account's phone
        // is NEVER confirmed (RegisterAsync's ordinary path, no MAX flow run).
        var attacker = await RegisterAsync(phone: phone);

        var raw = await (await AuthedClient(attacker.Token).GetAsync("/api/profile/export")).Content.ReadAsStringAsync();
        var exported = JsonSerializer.Deserialize<JsonElement>(raw);

        var bookingIds = exported.GetProperty("bookings").EnumerateArray()
            .Select(b => b.GetProperty("id").GetString()).ToList();
        bookingIds.Should().NotContain(guestBookingId.ToString(),
            "TD-03: an unconfirmed phone must not disclose a stranger's guest visit made on that number");
    }

    [Fact, TestCase("TD03-002")]
    public async Task Export_ConfirmedPhone_StillIncludesGuestMatchedBookings_BehaviorUnchanged()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var phone = UniquePhone();
        var guestBooking = await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, "Guest Before Signup", phone, null, null));
        guestBooking.StatusCode.Should().Be(HttpStatusCode.Created);
        var guestBookingId = (await guestBooking.Content.ReadJsonAsync<BookingDto>())!.Id;

        var user = await RegisterWithConfirmedPhoneAsync(phone);

        var raw = await (await AuthedClient(user.Token).GetAsync("/api/profile/export")).Content.ReadAsStringAsync();
        var exported = JsonSerializer.Deserialize<JsonElement>(raw);
        var bookingIds = exported.GetProperty("bookings").EnumerateArray()
            .Select(b => b.GetProperty("id").GetString()).ToList();
        bookingIds.Should().Contain(guestBookingId.ToString(),
            "TD-03: the gate must not affect an account whose own phone IS confirmed — same-subject reunification stays intact");
    }

    [Fact, TestCase("TD03-003")]
    public async Task DeleteAccount_UnconfirmedPhone_DoesNotAnonymizeGuestMatchedBooking()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var phone = UniquePhone();
        var guestBooking = await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, "Guest Before Signup", phone, null, null));
        guestBooking.StatusCode.Should().Be(HttpStatusCode.Created);
        var guestBookingId = (await guestBooking.Content.ReadJsonAsync<BookingDto>())!.Id;

        var attacker = await RegisterAsync(phone: phone);
        var deleteResponse = await AuthedClient(attacker.Token).PostAsJsonAsync("/api/profile/delete-account",
            new { currentPassword = "Password123!" });
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var bookingAfter = await (await AuthedClient(owner.Token).GetAsync($"/api/bookings/{guestBookingId}"))
            .Content.ReadJsonAsync<BookingDto>();
        bookingAfter!.ClientDeleted.Should().BeFalse(
            "TD-03: an unconfirmed account's own delete-account request must not be able to reach — let alone erase — a stranger's guest visit");
        bookingAfter.ClientId.Should().BeNull("the booking was always guest-only, never tied to this account");
    }

    [Fact, TestCase("TD03-004")]
    public async Task Export_UnconfirmedPhoneWithGuestData_ResponseCarriesGuestDataGateAppliedFlag()
    {
        // API_CONTRACT_CYCLE16.md §273.1/§276.2 (frontend/src/hooks/useExportData.ts already reads this
        // field from the export blob) — the export response is contractually required to carry a
        // `guestDataGate: { applied, explanation }` section so the UI can show GuestDataGateNotice
        // without inferring anything from empty sections (§272's own anti-oracle rule).
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var phone = UniquePhone();
        (await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(13, 0), null, "Guest Before Signup", phone, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var attacker = await RegisterAsync(phone: phone);
        var raw = await (await AuthedClient(attacker.Token).GetAsync("/api/profile/export")).Content.ReadAsStringAsync();
        var exported = JsonSerializer.Deserialize<JsonElement>(raw);

        exported.TryGetProperty("guestDataGate", out var gate).Should().BeTrue(
            "API_CONTRACT_CYCLE16.md §273.1 requires a guestDataGate section whenever the gate could apply");
        gate.GetProperty("applied").GetBoolean().Should().BeTrue();
    }

    // ── TD-03-bis: response deadline computed per request kind ─────────────────────────────────

    [Theory, TestCase("TD03BIS-001")]
    [InlineData("Access", 10)]
    [InlineData("Rectification", 7)]
    [InlineData("Erasure", 7)]
    public async Task Submit_WorkingDayKinds_DueDateMatchesLegallyRequiredWorkingDays(string kind, int expectedWorkingDays)
    {
        var reference = await SubmitSubjectRequestAsync(kind);
        var (dueAtUtc, receivedAtUtc) = await GetDueAndReceivedAsync(reference);

        var expectedDueUtc = ServiceBooking.API.Services.WorkingDays.Add(receivedAtUtc, expectedWorkingDays);
        dueAtUtc.Date.Should().Be(expectedDueUtc.Date,
            $"LEGAL_REVIEW_CYCLE16.md О5: kind={kind} must get {expectedWorkingDays} working days, not the single " +
            "SubjectRequests:ResponseWorkingDays value used for every kind today");
    }

    [Fact, TestCase("TD03BIS-002")]
    public async Task Submit_ConsentWithdrawal_DueIn30CalendarDays_NotTheDefaultWorkingDayCount()
    {
        var reference = await SubmitSubjectRequestAsync("ConsentWithdrawal");
        var (dueAtUtc, receivedAtUtc) = await GetDueAndReceivedAsync(reference);

        var expectedDueUtc = receivedAtUtc.AddDays(30);
        dueAtUtc.Date.Should().Be(expectedDueUtc.Date,
            "ч. 5 ст. 21 152-ФЗ: ConsentWithdrawal gets 30 CALENDAR days, distinct from the working-day kinds");
    }

    [Fact, TestCase("TD03BIS-003")]
    public async Task Submit_AccessedVsErasure_GetDifferentDueDates()
    {
        // A same-instant differential check that doesn't depend on WorkingDays arithmetic being right in
        // the test too — Access (10 working days) and Erasure (7 working days) must simply differ.
        var accessRef = await SubmitSubjectRequestAsync("Access");
        var erasureRef = await SubmitSubjectRequestAsync("Erasure");

        var (accessDue, _) = await GetDueAndReceivedAsync(accessRef);
        var (erasureDue, _) = await GetDueAndReceivedAsync(erasureRef);

        // Compares CALENDAR DATE only, not the full timestamp: two requests submitted moments apart get
        // slightly different ReceivedAtUtc values regardless of this bug, so comparing full DateTime
        // equality would pass even when both kinds use the exact same working-day count (false negative
        // for the very regression this test exists to catch).
        accessDue.Date.Should().NotBe(erasureDue.Date,
            "SubjectRequestsController.cs currently computes DueAtUtc identically for every kind (SPEC_CYCLE16_TECH_DEBT.md TD-03-bis)");
    }

    [Fact, TestCase("TD03BIS-004")]
    public async Task Submit_AcceptedResponse_ResponseDueByWorkingDaysMatchesRecordedDueDate()
    {
        var response = await AnonymousClient().PostAsJsonAsync("/api/subject-requests", new SubmitSubjectRequestDto(
            "Erasure", UniquePhone(), "contact@example.com", "Please delete my data.", null));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await response.Content.ReadFromJsonAsync<SubjectRequestAcceptedDto>();

        var (dueAtUtc, receivedAtUtc) = await GetDueAndReceivedAsync(accepted!.Reference);
        var impliedDueUtc = ServiceBooking.API.Services.WorkingDays.Add(receivedAtUtc, accepted.ResponseDueByWorkingDays);
        dueAtUtc.Date.Should().Be(impliedDueUtc.Date,
            "the number told to the subject in the Accepted response must match what was actually recorded — a mismatch misleads the subject about their own deadline");
    }

    // ── TD-03-ter: the gate also covers writing a revoke into ConsentLedger ────────────────────

    [Fact, TestCase("TD03TER-001")]
    public async Task RevokeSalonConsent_UnconfirmedPhone_CannotRevokeAStranger_SConsent()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var victimPhone = UniquePhone();
        // The victim visited this salon as a guest and the salon recorded a HealthDataConsent for them.
        (await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(14, 0), null, "Guest Victim", victimPhone, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        await GrantHealthConsentAsync(owner.Token, company.Id, $"phone:{victimPhone}");

        // An attacker registers on the victim's phone — never confirmed.
        var attacker = await RegisterAsync(phone: victimPhone);

        var revoke = await AuthedClient(attacker.Token).PostAsJsonAsync("/api/profile/consents/revoke",
            new { documentKey = "HealthDataConsent", purpose = (string?)null, reason = "attacker", companyId = company.Id });

        // TD-03-ter: the attacker must not be able to write a revoke into the victim's ConsentLedger at
        // all — success here (revoked > 0) means the gate is holding only by the accidental order of an
        // earlier BadRequest check, not by a real property, exactly what LEGAL_REVIEW_CYCLE16.md Н1 warns about.
        if (revoke.StatusCode == HttpStatusCode.OK)
        {
            var body = await revoke.Content.ReadFromJsonAsync<RevokeConsentResponseDto>();
            body!.Revoked.Should().Be(0,
                "TD-03-ter: an unconfirmed account must never manage to revoke a consent belonging to a phone it hasn't proven ownership of (ч. 3 ст. 9 152-ФЗ)");
        }
        else
        {
            revoke.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Forbidden);
        }
    }

    [Fact, TestCase("TD03TER-002")]
    public async Task RevokeSalonConsent_ConfirmedPhone_StillWorks_BehaviorUnchanged()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);

        var phone = UniquePhone();
        (await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(15, 0), null, "Guest Self", phone, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        await GrantHealthConsentAsync(owner.Token, company.Id, $"phone:{phone}");

        var user = await RegisterWithConfirmedPhoneAsync(phone);

        var revoke = await AuthedClient(user.Token).PostAsJsonAsync("/api/profile/consents/revoke",
            new { documentKey = "HealthDataConsent", purpose = (string?)null, reason = "my own data", companyId = company.Id });

        revoke.StatusCode.Should().Be(HttpStatusCode.OK,
            "TD-03-ter must not regress the legitimate case: an account that genuinely owns the confirmed phone can still withdraw its own salon-recorded consent");
        var body = await revoke.Content.ReadFromJsonAsync<RevokeConsentResponseDto>();
        body!.Revoked.Should().Be(1);
    }

    // ── TD-03-quater: intake keeps its unconditional response shape regardless of the signal ───

    [Fact, TestCase("TD03QUATER-001")]
    public async Task Submit_NewRequest_StillReturnsAcceptedUnconditionally_SignalNeverBlocksIntake()
    {
        // §50.1's own guarantee must survive the new signal call added by TD-03-quater: DSN is empty in
        // Testing (SentryDsn unset), so GlitchTipSignalService.SendAsync is a no-op — but the endpoint's
        // success/shape must not depend on that at all, exactly the "no fail-fast on this one" contract
        // the option carries (.env.production.example's own comment).
        var response = await AnonymousClient().PostAsJsonAsync("/api/subject-requests", new SubmitSubjectRequestDto(
            "Access", UniquePhone(), "contact-td03quater@example.com", "Прошу предоставить мои данные.", null));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var accepted = await response.Content.ReadFromJsonAsync<SubjectRequestAcceptedDto>();
        accepted!.Reference.Should().NotBeNullOrWhiteSpace();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private async Task<string> SubmitSubjectRequestAsync(string kind)
    {
        var response = await AnonymousClient().PostAsJsonAsync("/api/subject-requests", new SubmitSubjectRequestDto(
            kind, UniquePhone(), "contact@example.com", $"Обращение вида {kind}.", null));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, $"submitting a well-formed '{kind}' request must be accepted");
        var accepted = await response.Content.ReadFromJsonAsync<SubjectRequestAcceptedDto>();
        return accepted!.Reference;
    }

    private async Task<(DateTime DueAtUtc, DateTime ReceivedAtUtc)> GetDueAndReceivedAsync(string reference)
    {
        var admin = await LoginAsSuperAdminAsync();
        var page = await (await AuthedClient(admin.Token).GetAsync("/api/admin/subject-requests?pageSize=200"))
            .Content.ReadJsonAsync<PagedResult<SubjectRequestDto>>();
        var row = page!.Items.Should().ContainSingle(r => r.Reference == reference).Subject;
        return (row.DueAt, row.ReceivedAt);
    }

    /// <summary>Produces an account whose OWN phone is confirmed, through the real MAX webhook flow
    /// (same steps as PhoneVerificationTests.HappyPath_LinkAndValidOwnContact_VerifiesAndFeedsRegistration,
    /// PHV-020) — there is deliberately no test-only shortcut for PhoneNumberConfirmed, since TD-03's gate
    /// exists to check exactly this flag and a shortcut would prove nothing about the real path.</summary>
    private async Task<AuthResponseDto> RegisterWithConfirmedPhoneAsync(string phone)
    {
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var client = enabled.CreateClient();

        var start = await client.PostAsJsonAsync(
            "/api/phone-verification/sessions", new StartPhoneVerificationRequestDto(phone));
        start.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await start.Content.ReadJsonAsync<PhoneVerificationSessionCreatedDto>();

        var deepLink = created!.DeepLink;
        var uri = new Uri(deepLink);
        var payload = System.Web.HttpUtility.ParseQueryString(uri.Query)["start"]!;
        var senderId = $"confirm-{Guid.NewGuid():N}";
        var chatId = $"chat-{senderId}";

        var started = await client.PostAsJsonAsync(
            $"/api/phone-verification/max/webhook/{PhoneVerificationEnabledFactory.TestWebhookToken}",
            BotStartedBody(payload, senderId, chatId));
        started.StatusCode.Should().Be(HttpStatusCode.OK);

        var vcf = VCard(phone);
        var hash = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(PhoneVerificationEnabledFactory.TestBotToken), Encoding.UTF8.GetBytes(vcf)));
        var contact = await client.PostAsJsonAsync(
            $"/api/phone-verification/max/webhook/{PhoneVerificationEnabledFactory.TestWebhookToken}",
            ContactBody(senderId, senderId, chatId, vcf, hash));
        contact.StatusCode.Should().Be(HttpStatusCode.OK);

        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterDto(
            "Verified", "Person", phone, "Password123!", null, CurrentRegisterLegalDto(),
            new ServiceBooking.API.DTOs.PhoneVerification.PhoneVerificationRefDto(created.SessionId, created.StatusToken)));
        register.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await register.Content.ReadFromJsonAsync<AuthResponseDto>())!;
    }

    private static object BotStartedBody(string payload, string senderId, string chatId = "chat-default") => new
    {
        update_type = "bot_started",
        payload,
        user = new { user_id = senderId },
        chat = new { chat_id = chatId }
    };

    private static object ContactBody(string senderId, string ownerId, string chatId, string vcf, string hash) => new
    {
        update_type = "message_created",
        message = new
        {
            sender = new { user_id = senderId },
            recipient = new { chat_id = chatId },
            body = new
            {
                attachments = new object[]
                {
                    new
                    {
                        type = "contact",
                        payload = new
                        {
                            vcf_info = vcf,
                            hash,
                            max_info = new { user_id = ownerId }
                        }
                    }
                }
            }
        }
    };

    private static string VCard(string canonicalPhone) =>
        $"BEGIN:VCARD\nVERSION:3.0\nFN:Test Contact\nTEL;TYPE=CELL:{canonicalPhone}\nEND:VCARD";
}
