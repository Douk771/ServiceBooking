using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 4 (API_CONTRACT_CYCLE4.md §32-33, ARCHITECTURE_CYCLE4.md §32, SPEC.md §8) — the provider's
/// delivery-status webhook and the client-facing unsubscribe link, exercised end to end against a REAL
/// rate-limit policy (the priority scenario the code reviewer called out first: the webhook used to
/// return 500 on every call because it referenced an unregistered policy).
/// </summary>
public class NotificationWebhookUnsubscribeTests(TestDatabaseFixture fixture) : NotificationTestBase(fixture)
{
    // ── Webhook end-to-end (priority scenario 1) ─────────────────────────────────────────────────

    [Fact, TestCase("NTF-W001")]
    public async Task Webhook_ValidToken_DeliveryStatus_UpdatesRowAndReturns200()
    {
        var (_, _, channel) = await CreateConnectedChannelAsync();
        // Unique() (not a literal like "msg-1") — a second line of defense against ever updating a
        // different run's row by the same id, on top of this suite now correctly wiping the database
        // between runs via the "Api" collection (see NotificationTestBase's doc comment).
        var messageId = Unique("msg-w001-");
        var row = await SeedRowAsync(channel.Id, NotificationStatus.Sent, providerMessageId: messageId);

        var response = await PostWebhookAsync(BuildDeliveryStatusBody(channel, messageId, "delivered"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.OutboundNotifications.AsNoTracking().FirstAsync(n => n.Id == row.Id);
        reloaded.Status.Should().Be(NotificationStatus.Delivered);
    }

    [Fact, TestCase("NTF-W002")]
    public async Task Webhook_InvalidToken_Returns401WithEmptyBody_NoRowChanged()
    {
        var (_, _, channel) = await CreateConnectedChannelAsync();
        var messageId = Unique("msg-w002-");
        var row = await SeedRowAsync(channel.Id, NotificationStatus.Sent, providerMessageId: messageId);

        var content = new StringContent(BuildDeliveryStatusBody(channel, messageId, "delivered"), Encoding.UTF8, "application/json");
        var response = await AnonymousClient().PostAsync("/api/notifications/provider-webhook/WRONG-TOKEN", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().BeEmpty("§19.2: webhook 401 body must be empty");

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.OutboundNotifications.AsNoTracking().FirstAsync(n => n.Id == row.Id);
        reloaded.Status.Should().Be(NotificationStatus.Sent, "an unauthenticated webhook must not change any row");
    }

    [Fact, TestCase("NTF-W003")]
    public async Task Webhook_UnknownIdMessage_Returns200_NotAnError()
    {
        var (_, _, channel) = await CreateConnectedChannelAsync();
        var response = await PostWebhookAsync(BuildDeliveryStatusBody(channel, "never-queued-id", "delivered"));
        response.StatusCode.Should().Be(HttpStatusCode.OK, "§33: an unknown idMessage must still be 200, or the provider retries forever");
    }

    [Fact, TestCase("NTF-W004")]
    public async Task Webhook_StatusMonotonic_LateSentAfterDelivered_DoesNotRegress()
    {
        var (_, _, channel) = await CreateConnectedChannelAsync();
        var messageId = Unique("msg-w004-");
        var row = await SeedRowAsync(channel.Id, NotificationStatus.Sent, providerMessageId: messageId);

        (await PostWebhookAsync(BuildDeliveryStatusBody(channel, messageId, "delivered"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostWebhookAsync(BuildDeliveryStatusBody(channel, messageId, "sent"))).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.OutboundNotifications.AsNoTracking().FirstAsync(n => n.Id == row.Id);
        reloaded.Status.Should().Be(NotificationStatus.Delivered, "a late/out-of-order 'sent' event must never move a row backward");
    }

    [Fact, TestCase("NTF-W005")]
    public async Task Webhook_RealRateLimitPolicy_TripsAfterQuota()
    {
        // Priority scenario 1: exercises the ACTUAL "notifications-webhook" policy end to end — not a
        // mock, not a unit test of the limiter in isolation. This is the exact class of bug the reviewer
        // flagged: the endpoint referenced a rate-limit policy name that was never registered, so EVERY
        // call 500'd instead of the intended 401 (wrong token) or 429 (over quota) — a policy-lookup
        // exception, not a business-logic bug. The test's job is proving the policy is registered and
        // reachable, NOT reproducing its production quota (600/min, API_CONTRACT_CYCLE4.md §33) — see
        // this cycle's fix below.
        //
        // Fixed (post-cycle-18 CI red, NTF-W005): the original version drove 601 REAL sequential requests
        // against the production 600/min default to force a real clock-window trip. On a loaded CI
        // runner (MaxParallelThreads=2, 62 tests added by cycle 18) those 601 calls took ~62 real
        // seconds, long enough for the fixed 1-minute window to roll over mid-loop and reset the counter
        // — every response came back a legitimate 401 and 429 never arrived, a false failure caused by
        // machine speed, not product behavior. Using RateLimitTestFactory's existing
        // notificationsWebhookPermitLimit override (same pattern RateLimitingTests already uses for
        // auth-login/auth-register) shrinks the PERMIT COUNT instead of racing the WINDOW, so the same
        // real policy trips deterministically in a handful of calls regardless of how fast the host is.
        const int permitLimit = 5;
        await using var factory = new RateLimitTestFactory(ConnectionString, notificationsWebhookPermitLimit: permitLimit);
        var client = factory.CreateClient();
        HttpResponseMessage? last = null;
        for (var i = 0; i < permitLimit + 1; i++)
        {
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            last = await client.PostAsync("/api/notifications/provider-webhook/WRONG-TOKEN", content);
            if (last.StatusCode == (HttpStatusCode)429) break;
            last.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"call #{i + 1} must be a clean 401, never a 500 from a missing policy");
        }
        last!.StatusCode.Should().Be((HttpStatusCode)429, $"{permitLimit + 1} calls against a {permitLimit}/min policy must trip the real limiter");
    }

    // ── Unsubscribe ───────────────────────────────────────────────────────────────────────────────

    [Fact, TestCase("NTF-U001")]
    public async Task Unsubscribe_GetPage_ValidToken_ReturnsMaskedPhone()
    {
        var phone = "79990001234";
        var token = UnsubscribeTokens.Build(phone, Encoding.UTF8.GetBytes(NotificationTestFactory.TestUnsubscribeKey));

        var response = await AnonymousClient().GetAsync($"/api/notifications/unsubscribe/{token}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadJsonAsync<UnsubscribePageDto>())!;
        page.AlreadyOptedOut.Should().BeFalse();
        page.PhoneMasked.Should().NotContain(phone, "the raw phone number must never appear in an unsubscribe response");
    }

    [Fact, TestCase("NTF-U002")]
    public async Task Unsubscribe_InvalidToken_Returns404_NotDistinguishableFromMissing()
    {
        var response = await AnonymousClient().GetAsync("/api/notifications/unsubscribe/not-a-valid-token-at-all");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("NTF-U003")]
    public async Task Unsubscribe_Post_Idempotent_SecondCallStillNoContent()
    {
        var phone = "79990005555";
        var token = UnsubscribeTokens.Build(phone, Encoding.UTF8.GetBytes(NotificationTestFactory.TestUnsubscribeKey));

        var first = await AnonymousClient().PostAsync($"/api/notifications/unsubscribe/{token}", null);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var second = await AnonymousClient().PostAsync($"/api/notifications/unsubscribe/{token}", null);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.NotificationOptOuts.CountAsync(o => o.Phone == phone)).Should().Be(1, "must not create a duplicate opt-out row");

        var getResponse = await AnonymousClient().GetAsync($"/api/notifications/unsubscribe/{token}");
        (await getResponse.Content.ReadJsonAsync<UnsubscribePageDto>())!.AlreadyOptedOut.Should().BeTrue();
    }

    // ── §37: no secret in application logs ───────────────────────────────────────────────────────

    [Fact, TestCase("NTF-L001")]
    public async Task UnsubscribeAndWebhookTokens_NeverAppearInApplicationLogs()
    {
        // SPEC §37 / §13 п.6 — grepped by fact, per the QA brief, not read from the source of the fix.
        //
        // Every WebApplicationFactory in this process (every test class, every collection) writes to the
        // SAME physical "logs/app-*.json" — Serilog's file path is relative to the process' working
        // directory, which every in-process host shares. That file also never gets cleared between
        // separate `dotnet test` invocations on a dev machine, so it accumulates for days (confirmed
        // during this QA pass: eight dated files, ~85,000 lines total, going back to before this cycle
        // even started). Reading the WHOLE file/directory the way this test originally did is therefore
        // unreliable in BOTH directions: unrelated historical content can make an unrelated substring
        // match look like today's leak, and — more importantly — it is simply the wrong scope. This
        // version snapshots each file's current LENGTH before acting, then only inspects the bytes
        // APPENDED after that point, exactly like a `tail -f`/`wc -c` diff — the same fix in spirit as
        // NotificationTestBase's `[Collection("Api")]` (this class's own DB-side version of "never assume
        // a shared resource starts empty").
        var phone = "79993334455";
        var unsubscribeToken = UnsubscribeTokens.Build(phone, Encoding.UTF8.GetBytes(NotificationTestFactory.TestUnsubscribeKey));

        // ARCHITECTURE_CYCLE8.md §71.4: logs move to a run+factory-scoped temp directory
        // (Factory.Identity.LogDirectory), so this no longer needs to search upward from
        // AppContext.BaseDirectory for a directory every host in the process used to share.
        var logsDir = Factory.Identity.LogDirectory;
        Directory.Exists(logsDir).Should().BeTrue("expected TestHostSettings to have created this host's own log directory");
        var baselineLengths = Directory.GetFiles(logsDir, "app-*.json").ToDictionary(f => f, f => new FileInfo(f).Length);

        // Hit BOTH the unsubscribe link (phone+signature — PII) and the provider webhook (shared secret)
        // at least once each, including an error path (wrong webhook token) so a failure log line is
        // produced too — Information-level logs go to GlitchTip on any error per the reviewer's brief.
        await AnonymousClient().GetAsync($"/api/notifications/unsubscribe/{unsubscribeToken}");
        await AnonymousClient().PostAsync($"/api/notifications/unsubscribe/{unsubscribeToken}", null);
        await AnonymousClient().PostAsync($"/api/notifications/provider-webhook/{NotificationTestFactory.TestWebhookToken}",
            new StringContent("{}", Encoding.UTF8, "application/json"));
        await AnonymousClient().PostAsync("/api/notifications/provider-webhook/some-wrong-token",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        // Force a flush: Serilog's file sink batches writes; give it a moment before grepping.
        await Task.Delay(500);

        var newLogText = string.Join("\n", ReadAppendedText(logsDir!, baselineLengths));
        newLogText.Should().NotBeEmpty("this test's own 4 requests must have produced at least one new log line to check");
        newLogText.Should().NotContain(unsubscribeToken, "the unsubscribe token (phone+signature) must never be logged in any form");
        newLogText.Should().NotContain(phone, "the raw phone number encoded in the token must never be logged");
        newLogText.Should().NotContain(NotificationTestFactory.TestWebhookToken, "the shared webhook token must never be logged");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Reads only the bytes written to each "app-*.json" file (including any NEW file that
    /// didn't exist at baseline — e.g. a run crossing midnight on this test machine's fake 2026 clock)
    /// since <paramref name="baselineLengths"/> was captured. Opened with <see cref="FileShare.ReadWrite"/>
    /// because Serilog's own file sink holds the file open for writing for the lifetime of the process.</summary>
    private static IEnumerable<string> ReadAppendedText(string logsDir, IReadOnlyDictionary<string, long> baselineLengths)
    {
        foreach (var file in Directory.GetFiles(logsDir, "app-*.json"))
        {
            var startOffset = baselineLengths.GetValueOrDefault(file, 0L);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length <= startOffset) continue;
            stream.Seek(startOffset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            yield return reader.ReadToEnd();
        }
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string rawBody)
    {
        var content = new StringContent(rawBody, Encoding.UTF8, "application/json");
        return await AnonymousClient().PostAsync($"/api/notifications/provider-webhook/{NotificationTestFactory.TestWebhookToken}", content);
    }

    private static string BuildDeliveryStatusBody(NotificationChannel channel, string idMessage, string status) =>
        $$"""
        {
          "typeWebhook": "outgoingMessageStatus",
          "instanceData": { "idInstance": "{{channel.ProviderInstanceId}}" },
          "timestamp": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
          "idMessage": "{{idMessage}}",
          "status": "{{status}}"
        }
        """;

    /// <summary>
    /// Developer note (not a defect — flagged during QA, kept here as the assumption's one place to
    /// live): <c>NotificationsController.ApplyDeliveryStatusAsync</c> looks up the row to update by
    /// <c>ProviderMessageId</c> ALONE, not scoped to a channel — correct in production, where the
    /// provider's own message ids are globally unique, but it means this SUITE'S <c>providerMessageId</c>
    /// values must be unique per test run too (<see cref="Unique"/>, not a literal like "msg-1") or a
    /// webhook call in one test can update a same-named row seeded by a DIFFERENT test/run instead of its
    /// own. Confirmed by fact during this QA pass: a literal id picked up a stale row from an EARLIER run
    /// of this same suite against a database that hadn't been wiped between invocations (the actual root
    /// cause, now fixed — see <see cref="NotificationTestBase"/>'s <c>[Collection("Api")]</c>) and updated
    /// the wrong row, which looked exactly like a product bug until traced to its actual cause.
    /// </summary>
    private async Task<OutboundNotification> SeedRowAsync(Guid channelId, NotificationStatus status, string providerMessageId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companyId = await db.ChannelCompanyAssignments.AsNoTracking()
            .Where(a => a.ChannelId == channelId).Select(a => a.CompanyId).FirstAsync();
        var row = new OutboundNotification
        {
            Id = Guid.NewGuid(), CompanyId = companyId, ChannelId = channelId,
            Type = NotificationType.BookingConfirmed, RecipientPhone = "79990001111", Body = "test",
            DueAtUtc = DateTime.UtcNow.AddMinutes(-5), VisitStartUtc = DateTime.UtcNow.AddHours(2),
            Status = status, SentAtUtc = DateTime.UtcNow.AddMinutes(-4),
            ProviderMessageId = providerMessageId,
            IdempotencyKey = $"webhook-test:{Guid.NewGuid()}",
        };
        db.OutboundNotifications.Add(row);
        await db.SaveChangesAsync();
        return row;
    }
}
