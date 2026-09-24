using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.PhoneVerification;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.API.Services.PhoneVerification.Max;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 14 (SPEC.md §8.2's acceptance checklist, blocks A/B/C/E of the user stories) — functional
/// tests for the free MAX phone-verification track. Written against SPEC.md's acceptance criteria, not
/// against the implementation — see each test's own <c>TestCase</c> id / comment for which US-14-xx it
/// proves.
///
/// Every test that needs the subsystem actually ENABLED uses its own <see cref="PhoneVerificationEnabledFactory"/>
/// against this class' own database (same pattern as <c>PushEnabledFactory</c>/<c>NotificationTestFactory</c>)
/// — <see cref="Factory"/> itself stays on the Testing default (<c>PhoneVerification:Provider = "stub"</c>),
/// which is exactly what the "disabled by default" tests below need.
/// </summary>
public class PhoneVerificationTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    // ── Block B: disabled by default (§0.5, US-14-08) ───────────────────────────────

    [Fact, TestCase("PHV-001")]
    public async Task Config_SubsystemDisabledByDefault_ReportsDisabledAndNoMethods()
    {
        var response = await AnonymousClient().GetAsync("/api/phone-verification/config");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var config = await response.Content.ReadJsonAsync<PhoneVerificationConfigDto>();
        config!.Enabled.Should().BeFalse("the shipped default is Provider=stub (§0.5's \"невыпущенность\")");
        config.Methods.Should().BeEmpty();
    }

    [Fact, TestCase("PHV-002")]
    public async Task StartSession_SubsystemDisabled_Returns409WithHonestText()
    {
        var response = await AnonymousClient().PostAsJsonAsync(
            "/api/phone-verification/sessions", new StartPhoneVerificationRequestDto(UniquePhone()));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(PhoneVerificationTexts.SubsystemUnavailable);
    }

    [Fact, TestCase("PHV-003")]
    public async Task Register_WithoutPhoneVerification_WorksExactlyAsBeforeTheCycle()
    {
        // US-14-07: omitting the optional field entirely must not change existing behavior at all.
        var auth = await RegisterAsync();
        auth.PhoneVerified.Should().BeFalse();
    }

    [Fact, TestCase("PHV-004")]
    public async Task StartSession_InvalidPhoneFormat_Returns400AndExplains()
    {
        // Needs the subsystem enabled — otherwise a bad phone and a disabled subsystem would both be a
        // rejection and this test couldn't tell which one actually fired (US-14-01's own criterion:
        // "действие недоступно и объяснено почему").
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var response = await enabled.CreateClient().PostAsJsonAsync(
            "/api/phone-verification/sessions", new StartPhoneVerificationRequestDto("not-a-phone"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(PhoneVerificationTexts.InvalidPhoneFormat);
    }

    // ── Block A: starting a session, polling, cancelling (US-14-01, US-14-02, US-14-03, US-14-06) ──

    [Fact, TestCase("PHV-010")]
    public async Task StartSession_Enabled_ReturnsDeepLinkQrAndShortOpaquePayload()
    {
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var phone = UniquePhone();

        var response = await enabled.CreateClient().PostAsJsonAsync(
            "/api/phone-verification/sessions", new StartPhoneVerificationRequestDto(phone));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadJsonAsync<PhoneVerificationSessionCreatedDto>();
        created!.DeepLink.Should().StartWith("https://max.ru/qa_cycle14_bot?start=");
        created.PhoneMasked.Should().NotBe(phone, "the raw phone must never come back in the response");

        var payload = ExtractPayload(created.DeepLink);
        payload.Length.Should().BeLessOrEqualTo(128, "US-14-02's own bound on the payload length");
        created.QrPngBase64.Should().NotBeNullOrEmpty("desktop needs the QR — П8");
    }

    [Fact, TestCase("PHV-011")]
    public async Task GetSession_WrongStatusToken_Returns404NotTheRealStatus()
    {
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var client = enabled.CreateClient();
        var created = await StartAsync(client, UniquePhone());

        var response = await client.GetAsync($"/api/phone-verification/sessions/{created.SessionId}?statusToken=wrong-token");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "§164: a wrong token must read identically to an unknown id");
    }

    [Fact, TestCase("PHV-012")]
    public async Task GetSession_UnknownId_Returns404()
    {
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var response = await enabled.CreateClient().GetAsync($"/api/phone-verification/sessions/{Guid.NewGuid()}?statusToken=whatever");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("PHV-013")]
    public async Task CancelSession_AlwaysReturns204_EvenForGarbageIds()
    {
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var client = enabled.CreateClient();
        var created = await StartAsync(client, UniquePhone());

        (await client.DeleteAsync($"/api/phone-verification/sessions/{created.SessionId}?statusToken={created.StatusToken}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/phone-verification/sessions/{Guid.NewGuid()}?statusToken=garbage"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent, "§166: ALWAYS 204, including nothing-to-cancel");

        // A cancelled session no longer accepts a bot_started link — proves the cancel actually did something.
        var status = await GetStatusAsync(client, created);
        status!.Status.Should().Be(PhoneVerificationDisplayStatus.Cancelled);
    }

    [Fact, TestCase("PHV-014")]
    public async Task BotStarted_UnknownPayload_DoesNothingAndStaysSilentlySafe()
    {
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var client = enabled.CreateClient();
        var created = await StartAsync(client, UniquePhone());

        var webhookResponse = await PostWebhookAsync(client, BotStartedBody(payload: "totally-unknown-payload", senderId: "1"));
        webhookResponse.StatusCode.Should().Be(HttpStatusCode.OK, "§146.2: an unrecognized update is still always 200");

        var status = await GetStatusAsync(client, created);
        status!.Status.Should().Be(PhoneVerificationDisplayStatus.Pending, "an unrelated bot_started must not touch this session");
    }

    [Fact, TestCase("PHV-015")]
    public async Task BotStarted_SentTwice_IsIdempotent_DoesNotBreakTheSession()
    {
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var client = enabled.CreateClient();
        var created = await StartAsync(client, UniquePhone());
        var payload = ExtractPayload(created.DeepLink);

        (await PostWebhookAsync(client, BotStartedBody(payload, senderId: "42"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var afterFirst = await GetStatusAsync(client, created);
        afterFirst!.Status.Should().Be(PhoneVerificationDisplayStatus.Linked);

        (await PostWebhookAsync(client, BotStartedBody(payload, senderId: "42"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var afterSecond = await GetStatusAsync(client, created);
        afterSecond!.Status.Should().Be(PhoneVerificationDisplayStatus.Linked, "US-14-03: idempotent, does not create a second session/break the first");
    }

    [Fact, TestCase("PHV-016")]
    public async Task Webhook_WrongToken_Returns404_NeverProcessesTheBody()
    {
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var client = enabled.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/phone-verification/max/webhook/not-the-real-token", BotStartedBody("x", "1"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Block A: the security-critical checks (US-14-04, US-14-05) ─────────────────

    [Fact, TestCase("PHV-020")]
    public async Task HappyPath_LinkAndValidOwnContact_VerifiesAndFeedsRegistration()
    {
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var client = enabled.CreateClient();
        var phone = UniquePhone();
        var created = await StartAsync(client, phone);
        var payload = ExtractPayload(created.DeepLink);

        await PostWebhookAsync(client, BotStartedBody(payload, senderId: "500", chatId: "chat-500"));
        var linked = await GetStatusAsync(client, created);
        linked!.Status.Should().Be(PhoneVerificationDisplayStatus.Linked);

        var vcf = VCard(phone);
        var hash = SignHex(vcf, PhoneVerificationEnabledFactory.TestBotToken);
        await PostWebhookAsync(client, ContactBody(senderId: "500", ownerId: "500", chatId: "chat-500", vcf: vcf, hash: hash));

        var verified = await GetStatusAsync(client, created);
        verified!.Status.Should().Be(PhoneVerificationDisplayStatus.Verified);
        verified.VerifiedAtUtc.Should().NotBeNull();

        // §8.2's "ни на одном шаге пользователю не отправляется код": the recorded bot replies are all
        // fixed sentences (MaxBotTexts), never anything platform-generated for the user to type back.
        enabled.RecordingClient.SentMessages.Should().NotBeEmpty();
        enabled.RecordingClient.SentMessages.Should().OnlyContain(m => !m.Text.Contains(payload));

        // Приветствие обязано приехать С КЛАВИАТУРОЙ. Вживую 24.09.2026 бот поздоровался и позвал
        // нажать кнопку «Отправить контакт», которой не существовало: клиент отправлял только текст,
        // вложения `inline_keyboard` в коде не было вовсе, и человек упирался в тупик. Текст без
        // кнопки в этом сценарии — это несделанная работа, а не мелочь оформления.
        enabled.RecordingClient.SentMessages
            .Should().Contain(m => m.Text == MaxBotTexts.Greeting && m.RequestContact,
                "приглашение поделиться контактом без самой кнопки никуда не ведёт");

        // US-14-01's last criterion: the completed session actually feeds registration and the account
        // comes out phoneVerified:true.
        var legal = CurrentRegisterLegalDto();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterDto(
            "Verified", "Person", phone, "Password123!", null, legal,
            new PhoneVerificationRefDto(created.SessionId, created.StatusToken)));
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        auth!.PhoneVerified.Should().BeTrue();

        // Login afterwards mirrors the same account state (AuthController.Login's own note).
        var login = await client.PostAsJsonAsync("/api/auth/login", new { phone, password = "Password123!" });
        var loginAuth = await login.Content.ReadFromJsonAsync<AuthResponseDto>();
        loginAuth!.PhoneVerified.Should().BeTrue();
    }

    [Fact, TestCase("PHV-021")]
    public async Task ForeignContact_ValidSignature_ButOwnerIsNotSender_IsRejected()
    {
        // US-14-04's headline scenario: a valid signature alone must NEVER be enough — the platform also
        // proves the contact belongs to whoever sent it.
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var client = enabled.CreateClient();
        var victimPhone = UniquePhone();
        var created = await StartAsync(client, victimPhone);
        var payload = ExtractPayload(created.DeepLink);

        await PostWebhookAsync(client, BotStartedBody(payload, senderId: "attacker-1", chatId: "chat-a1"));

        var vcf = VCard(victimPhone);
        var validHash = SignHex(vcf, PhoneVerificationEnabledFactory.TestBotToken);
        // ownerId != senderId: the attacker forwarded a contact card they don't own.
        await PostWebhookAsync(client, ContactBody(senderId: "attacker-1", ownerId: "someone-else", chatId: "chat-a1", vcf: vcf, hash: validHash));

        var status = await GetStatusAsync(client, created);
        status!.Status.Should().Be(PhoneVerificationDisplayStatus.Rejected);
        status.FailureReason.Should().Be(PhoneVerificationFailureReason.ContactNotOwnedBySender);
    }

    [Fact, TestCase("PHV-022")]
    public async Task OwnContact_TamperedSignature_IsRejected()
    {
        // The second half of US-14-04: contact IS the sender's own, but the HMAC does not check out.
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var client = enabled.CreateClient();
        var phone = UniquePhone();
        var created = await StartAsync(client, phone);
        var payload = ExtractPayload(created.DeepLink);

        await PostWebhookAsync(client, BotStartedBody(payload, senderId: "700", chatId: "chat-700"));

        var vcf = VCard(phone);
        var wrongHash = SignHex(vcf, "a-completely-different-signing-key");
        await PostWebhookAsync(client, ContactBody(senderId: "700", ownerId: "700", chatId: "chat-700", vcf: vcf, hash: wrongHash));

        var status = await GetStatusAsync(client, created);
        status!.Status.Should().Be(PhoneVerificationDisplayStatus.Rejected);
        status.FailureReason.Should().Be(PhoneVerificationFailureReason.SignatureMismatch);
    }

    [Fact, TestCase("PHV-023")]
    public async Task ValidOwnContact_DifferentPhoneThanForm_IsRejected_MessageNamesBothMasked()
    {
        // US-14-05: everything checks out except the number itself does not match what was typed on
        // the site.
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var client = enabled.CreateClient();
        var formPhone = UniquePhone();
        var actualContactPhone = UniquePhone();
        var created = await StartAsync(client, formPhone);
        var payload = ExtractPayload(created.DeepLink);

        await PostWebhookAsync(client, BotStartedBody(payload, senderId: "800", chatId: "chat-800"));

        var vcf = VCard(actualContactPhone);
        var hash = SignHex(vcf, PhoneVerificationEnabledFactory.TestBotToken);
        await PostWebhookAsync(client, ContactBody(senderId: "800", ownerId: "800", chatId: "chat-800", vcf: vcf, hash: hash));

        var status = await GetStatusAsync(client, created);
        status!.Status.Should().Be(PhoneVerificationDisplayStatus.Rejected);
        status.FailureReason.Should().Be(PhoneVerificationFailureReason.PhoneMismatch);
        status.Message.Should().NotBeNull();
        status.Message.Should().NotContain(actualContactPhone, "only the MASKED number may ever appear");
        status.Message.Should().NotContain(formPhone);
    }

    [Fact, TestCase("PHV-024")]
    public async Task Contact_NoUsablePhone_IsRejectedAsNoPhoneInContact()
    {
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var client = enabled.CreateClient();
        var phone = UniquePhone();
        var created = await StartAsync(client, phone);
        var payload = ExtractPayload(created.DeepLink);

        await PostWebhookAsync(client, BotStartedBody(payload, senderId: "900", chatId: "chat-900"));

        const string vcf = "BEGIN:VCARD\nVERSION:3.0\nFN:No Phone Here\nEND:VCARD";
        var hash = SignHex(vcf, PhoneVerificationEnabledFactory.TestBotToken);
        await PostWebhookAsync(client, ContactBody(senderId: "900", ownerId: "900", chatId: "chat-900", vcf: vcf, hash: hash));

        var status = await GetStatusAsync(client, created);
        status!.Status.Should().Be(PhoneVerificationDisplayStatus.Rejected);
        status.FailureReason.Should().Be(PhoneVerificationFailureReason.NoPhoneInContact);
    }

    // ── Р5: the per-MAX-account ceiling (US-14-04's last criterion) ────────────────

    [Fact, TestCase("PHV-025")]
    public async Task Ceiling_FourthDistinctPhoneFromSameMaxAccount_IsRejected_TextDoesNotRevealOthers()
    {
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var client = enabled.CreateClient();
        const string sameMaxSenderId = "ceiling-account-1";

        // Three DIFFERENT registered accounts each verify their OWN phone from-profile, all through the
        // same MAX account — this is exactly the shape Р5 caps ("не более 3 разных номеров на один
        // MAX-аккаунт"), and using Profile-purpose sessions (not Registration) means VerifiedPhone rows
        // are written immediately by the webhook handler, no extra /auth/register round trip needed.
        for (var i = 0; i < 3; i++)
        {
            var phone = UniquePhone();
            var auth = await RegisterAsync(phone);
            var authedClient = enabled.CreateClient();
            authedClient.DefaultRequestHeaders.Authorization = new("Bearer", auth.Token);

            var created = await StartAsync(authedClient, phone: null); // authenticated: falls back to the account's own number
            var payload = ExtractPayload(created.DeepLink);
            var chatId = $"chat-{sameMaxSenderId}-{i}";

            await PostWebhookAsync(client, BotStartedBody(payload, senderId: sameMaxSenderId, chatId: chatId));
            var vcf = VCard(phone);
            var hash = SignHex(vcf, PhoneVerificationEnabledFactory.TestBotToken);
            await PostWebhookAsync(client, ContactBody(senderId: sameMaxSenderId, ownerId: sameMaxSenderId, chatId: chatId, vcf: vcf, hash: hash));

            var status = await GetStatusAsync(authedClient, created);
            status!.Status.Should().Be(PhoneVerificationDisplayStatus.Verified, $"phone #{i + 1} is within the ceiling of 3");
        }

        // Fourth distinct number, same MAX account — must be rejected.
        var fourthPhone = UniquePhone();
        var fourthAuth = await RegisterAsync(fourthPhone);
        var fourthClient = enabled.CreateClient();
        fourthClient.DefaultRequestHeaders.Authorization = new("Bearer", fourthAuth.Token);

        var fourthSession = await StartAsync(fourthClient, phone: null);
        var fourthPayload = ExtractPayload(fourthSession.DeepLink);
        const string fourthChatId = "chat-ceiling-account-1-fourth";
        await PostWebhookAsync(client, BotStartedBody(fourthPayload, senderId: sameMaxSenderId, chatId: fourthChatId));
        var fourthVcf = VCard(fourthPhone);
        var fourthHash = SignHex(fourthVcf, PhoneVerificationEnabledFactory.TestBotToken);
        await PostWebhookAsync(client, ContactBody(senderId: sameMaxSenderId, ownerId: sameMaxSenderId, chatId: fourthChatId, vcf: fourthVcf, hash: fourthHash));

        var fourthStatus = await GetStatusAsync(fourthClient, fourthSession);
        fourthStatus!.Status.Should().Be(PhoneVerificationDisplayStatus.Rejected);
        fourthStatus.FailureReason.Should().Be(PhoneVerificationFailureReason.MaxAccountLimitReached);
        fourthStatus.Message.Should().NotBeNullOrEmpty();
        // The already-verified numbers (masked or not) must never leak through this text — only the
        // fact that the ceiling was hit, never which numbers occupy it.
        fourthStatus.Message.Should().Be(PhoneVerificationTexts.ForFailureReason(PhoneVerificationFailureReason.MaxAccountLimitReached));
        var recordedTexts = enabled.RecordingClient.SentMessages.Select(m => m.Text).ToList();
        recordedTexts.Should().NotContain(t => t.Contains(fourthPhone));
    }

    // ── Block C: profile view, change-phone gate (US-14-16, US-14-17, R6/§6.4) ─────

    [Fact, TestCase("PHV-030")]
    public async Task ChangePhone_NewNumberHasNoGuestBookings_WorksWithoutVerification_EvenSubsystemDisabled()
    {
        // Р1's hard constraint, proven on the DEFAULT (disabled) factory: a person without MAX must never
        // lose the ability to change to a "clean" number.
        var user = await RegisterAsync();
        var newPhone = UniquePhone();
        var client = AuthedClient(user.Token);

        var response = await client.PostAsJsonAsync("/api/profile/change-phone", new
        {
            currentPassword = "Password123!",
            newPhone
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("PHV-031")]
    public async Task ChangePhone_NewNumberHasGuestBookings_SubsystemDisabled_HonestRefusal_NotHang()
    {
        // US-14-17's explicit "not a hang, not a silent failure" criterion for the disabled state.
        var user = await RegisterAsync();
        var guestPhone = UniquePhone();
        await CreateGuestBookingOnAsync(guestPhone);

        var client = AuthedClient(user.Token);
        var response = await client.PostAsJsonAsync("/api/profile/change-phone", new
        {
            currentPassword = "Password123!",
            newPhone = guestPhone
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(PhoneVerificationTexts.ChangePhoneSubsystemDisabled);
    }

    [Fact, TestCase("PHV-032")]
    public async Task ChangePhone_NewNumberHasGuestBookings_SubsystemEnabled_RequiresVerification_ThenSucceeds()
    {
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);

        var userPhone = UniquePhone();
        var registerResponse = await enabled.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterDto(
            "Gate", "User", userPhone, "Password123!", null, CurrentRegisterLegalDto()));
        registerResponse.EnsureSuccessStatusCode();
        var user = (await registerResponse.Content.ReadFromJsonAsync<AuthResponseDto>())!;

        var targetPhone = UniquePhone();
        await CreateGuestBookingOnAsync(targetPhone);

        var client = enabled.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", user.Token);

        // Without a verification session at all: rejected, honest reason.
        var withoutVerification = await client.PostAsJsonAsync("/api/profile/change-phone", new
        {
            currentPassword = "Password123!",
            newPhone = targetPhone
        });
        withoutVerification.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await withoutVerification.Content.ReadAsStringAsync()).Should().Contain(PhoneVerificationTexts.ChangePhoneNeedsVerification);

        // Verify the target number through the bot (Purpose=Profile, since the caller is authenticated).
        var created = await StartAsync(client, targetPhone);
        var payload = ExtractPayload(created.DeepLink);
        await PostWebhookAsync(client, BotStartedBody(payload, senderId: "gate-1000", chatId: "chat-gate-1000"));
        var vcf = VCard(targetPhone);
        var hash = SignHex(vcf, PhoneVerificationEnabledFactory.TestBotToken);
        await PostWebhookAsync(client, ContactBody(senderId: "gate-1000", ownerId: "gate-1000", chatId: "chat-gate-1000", vcf: vcf, hash: hash));

        var withVerification = await client.PostAsJsonAsync("/api/profile/change-phone", new
        {
            currentPassword = "Password123!",
            newPhone = targetPhone,
            verification = new { sessionId = created.SessionId, statusToken = created.StatusToken }
        });

        withVerification.StatusCode.Should().Be(HttpStatusCode.OK,
            "US-14-17: a verified session for the target number lets the gate through");
    }

    [Fact, TestCase("PHV-033")]
    public async Task PhoneVerified_ResetsOnChangePhone_BadgeAndPersonnelViewUpdateImmediately()
    {
        // US-14-11 + US-14-14/US-14-15: verify from profile, confirm the badge/personnel signal is on,
        // then change to a fresh (no guest bookings) number and confirm the mark is gone immediately —
        // not "eventually", and visible both to the user's own profile and to company staff.
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);

        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        await SetSubscriptionAsync(company.Id);

        var clientPhone = UniquePhone();
        var registerResponse = await enabled.CreateClient().PostAsJsonAsync("/api/auth/register", new RegisterDto(
            "Badge", "Client", clientPhone, "Password123!", null, CurrentRegisterLegalDto()));
        registerResponse.EnsureSuccessStatusCode();
        var clientAuth = (await registerResponse.Content.ReadFromJsonAsync<AuthResponseDto>())!;

        var authedClient = enabled.CreateClient();
        authedClient.DefaultRequestHeaders.Authorization = new("Bearer", clientAuth.Token);

        // Book with this master so they show up in MastersController.GetClients.
        var bookingResponse = await authedClient.PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(9, 0), null, null, null, null, null));
        bookingResponse.EnsureSuccessStatusCode();

        // Verify the client's own phone from profile.
        var created = await StartAsync(authedClient, phone: null);
        var payload = ExtractPayload(created.DeepLink);
        await PostWebhookAsync(authedClient, BotStartedBody(payload, senderId: "badge-1", chatId: "chat-badge-1"));
        var vcf = VCard(clientPhone);
        var hash = SignHex(vcf, PhoneVerificationEnabledFactory.TestBotToken);
        await PostWebhookAsync(authedClient, ContactBody(senderId: "badge-1", ownerId: "badge-1", chatId: "chat-badge-1", vcf: vcf, hash: hash));

        var profileResponse = await authedClient.GetAsync("/api/profile");
        var profileJson = await profileResponse.Content.ReadAsStringAsync();
        profileJson.Should().Contain("\"phoneVerified\":true");

        var clientsBeforeResponse = await AuthedClient(master.Token).GetAsync($"/api/masters/clients?companyId={company.Id}");
        var clientsBefore = await clientsBeforeResponse.Content.ReadAsStringAsync();
        clientsBefore.Should().Contain("\"phoneVerified\":true");

        // Now change to a fresh number with no guest bookings (no gate involved) — the badge must
        // disappear immediately, on both surfaces.
        var newPhone = UniquePhone();
        var changeResponse = await authedClient.PostAsJsonAsync("/api/profile/change-phone", new
        {
            currentPassword = "Password123!",
            newPhone
        });
        changeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // The bearer token issued before the change no longer validates afterward (its SecurityStamp
        // claim is checked against the live account on every request, ProfileController/Program.cs's own
        // JWT bearer handler) — expected, security-relevant behavior, not something this test is about.
        // change-phone's own 200 response body already carries the post-change ProfileDto.
        var profileAfter = await changeResponse.Content.ReadAsStringAsync();
        profileAfter.Should().Contain("\"phoneVerified\":false");

        var clientsAfter = await (await AuthedClient(master.Token).GetAsync($"/api/masters/clients?companyId={company.Id}")).Content.ReadAsStringAsync();
        clientsAfter.Should().Contain("\"phoneVerified\":false");
    }

    // ── R15: an all-unverified company must not look alarming ──────────────────────

    [Fact, TestCase("PHV-040")]
    public async Task Personnel_ClientList_AllUnverified_LooksNormal_NoWarningNoSortHint()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync();
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        await SetSubscriptionAsync(company.Id);

        var clientUser = await RegisterAsync();
        var bookingResponse = await AuthedClient(clientUser.Token).PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(10, 0), null, null, null, null, null));
        bookingResponse.EnsureSuccessStatusCode();

        var response = await AuthedClient(master.Token).GetAsync($"/api/masters/clients?companyId={company.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();

        // R15: no negative wording anywhere on the endpoint's output for an all-unverified company.
        json.Should().NotContain("не подтвержд", "an unverified client must not be shown as a warning");
        json.Should().Contain("\"phoneVerified\":false", "a registered client with no verification simply reads false, not a scary label");
    }

    [Fact, TestCase("PHV-041")]
    public async Task Personnel_ClientList_GuestWithNoAccount_PhoneVerifiedIsNull_NotFalse()
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        await SetSubscriptionAsync(company.Id);

        var guestPhone = UniquePhone();
        var response = await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(11, 0), null, "Guest Name", guestPhone, null, null));
        response.EnsureSuccessStatusCode();

        var clientsResponse = await AuthedClient(master.Token).GetAsync($"/api/masters/clients?companyId={company.Id}");
        var json = await clientsResponse.Content.ReadAsStringAsync();
        json.Should().Contain("\"phoneVerified\":null", "US-14-15: 'neither applicable' must not read as false/'not verified'");
    }

    // ── Block B: no dependency on billing/tariff/company (US-14-10) ────────────────

    [Fact, TestCase("PHV-050")]
    public async Task RegistrationVerification_HappensBeforeAnyCompanyOrPlanExists()
    {
        // US-14-10: the whole happy path above (PHV-020) already proves this implicitly — verifying at
        // registration necessarily happens before the account (let alone a company/plan) exists. This
        // test names the property directly: the diagnostics endpoint (SuperAdmin-only, unrelated to
        // billing) is reachable without any billing wiring, and Register's phoneVerified field does not
        // require EffectivePlan/LegalOptionGuards to be evaluated.
        using var enabled = new PhoneVerificationEnabledFactory(ConnectionString);
        var phone = UniquePhone();
        var client = enabled.CreateClient();
        var created = await StartAsync(client, phone);
        var payload = ExtractPayload(created.DeepLink);
        await PostWebhookAsync(client, BotStartedBody(payload, senderId: "no-billing-1", chatId: "chat-nb-1"));
        var vcf = VCard(phone);
        var hash = SignHex(vcf, PhoneVerificationEnabledFactory.TestBotToken);
        await PostWebhookAsync(client, ContactBody(senderId: "no-billing-1", ownerId: "no-billing-1", chatId: "chat-nb-1", vcf: vcf, hash: hash));

        var legal = CurrentRegisterLegalDto();
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterDto(
            "No", "Billing", phone, "Password123!", null, legal, new PhoneVerificationRefDto(created.SessionId, created.StatusToken)));

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK, "no company/plan exists yet for this brand-new account");
        var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponseDto>();
        auth!.PhoneVerified.Should().BeTrue();
    }

    // ── Startup fail-fast (mirrors NotificationTransportStartupTests' MAX-005) ─────

    [Fact, TestCase("PHV-060")]
    public void UnrecognizedProvider_ThrowsAtStartup_BeforeHostBecomesHealthy()
    {
        const string unusedConnectionString =
            "Host=127.0.0.1;Port=1;Database=sb_startup_phv_unused;Username=postgres;Password=postgres";

        using var factory = new BadPhoneVerificationProviderFactory(unusedConnectionString);
        var act = () => _ = factory.Services;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PhoneVerification:Provider*",
                "an unrecognized PhoneVerification:Provider value must fail loud at startup (§150.1)");
    }

    private sealed class BadPhoneVerificationProviderFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestHostSettings.Apply(builder, "startup-qa9", connectionString);
            builder.UseSetting("PhoneVerification:Provider", "definitely-not-a-real-provider");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static async Task<PhoneVerificationSessionCreatedDto> StartAsync(HttpClient client, string? phone)
    {
        var response = await client.PostAsJsonAsync("/api/phone-verification/sessions", new StartPhoneVerificationRequestDto(phone));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadJsonAsync<PhoneVerificationSessionCreatedDto>())!;
    }

    private static async Task<PhoneVerificationSessionStatusDto?> GetStatusAsync(HttpClient client, PhoneVerificationSessionCreatedDto created)
    {
        var response = await client.GetAsync($"/api/phone-verification/sessions/{created.SessionId}?statusToken={created.StatusToken}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadJsonAsync<PhoneVerificationSessionStatusDto>();
    }

    private static string ExtractPayload(string deepLink)
    {
        var uri = new Uri(deepLink);
        var query = HttpUtility.ParseQueryString(uri.Query);
        return query["start"]!;
    }

    private static Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync($"/api/phone-verification/max/webhook/{PhoneVerificationEnabledFactory.TestWebhookToken}", body);

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

    private static string SignHex(string vcfInfo, string botToken) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(botToken), Encoding.UTF8.GetBytes(vcfInfo)));

    /// <summary>Creates a guest (no account) booking on <paramref name="guestPhone"/> through the ordinary
    /// booking flow — the exact shape <c>GuestBookingLookup.HasGuestBookingsAsync</c> looks for
    /// (<c>ClientId == null &amp;&amp; GuestPhone == canonicalPhone</c>), needed to exercise US-14-17's gate.</summary>
    private async Task CreateGuestBookingOnAsync(string guestPhone)
    {
        var (owner, company) = await CreateOwnerWithCompanyAsync(allowSelfBooking: true);
        var master = await AddMasterAsync(owner.Token, company.Id);
        var service = await CreateServiceAsync(owner.Token, company.Id);
        var date = NextWeekday();
        await SetWorkingDayAsync(owner.Token, master.UserId, company.Id, date);
        await SetSubscriptionAsync(company.Id);

        var response = await AnonymousClient().PostAsJsonAsync("/api/bookings", new CreateBookingDto(
            company.Id, service.Id, master.UserId, date, new TimeOnly(12, 0), null, "Guest Name", guestPhone, null, null));
        response.EnsureSuccessStatusCode();
    }

    private static DateOnly NextWeekday()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            date = date.AddDays(1);
        return date;
    }
}
