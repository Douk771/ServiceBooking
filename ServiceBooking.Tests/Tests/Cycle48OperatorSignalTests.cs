using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ServiceBooking.API.Services.Signals;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// CY48-B6, functional part for the messenger number (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.15, US-48-08): POST /api/notification-channels
/// with <c>paymentRequest: true</c> sends exactly one operator signal; <c>paymentRequest: false</c> sends none; a failing signal channel
/// does not change the answer. IGlitchTipSignalService is replaced by a recording fake.
/// </summary>
public class Cycle48OperatorSignalTests(TestDatabaseFixture fixture) : Cycle43TestBase(fixture)
{
    private sealed class RecordingSignals(bool fail) : IGlitchTipSignalService
    {
        public List<string> Messages { get; } = [];

        public Task<bool> SendAsync(string message, CancellationToken ct)
        {
            lock (Messages) Messages.Add(message);
            if (fail) throw new InvalidOperationException("GlitchTip is down");
            return Task.FromResult(true);
        }
    }

    private async Task<(HttpResponseMessage Response, string Body)> PostChannelAsync(RecordingSignals signals, bool paymentRequest, bool paid)
    {
        await SetCatalogAsync("WhatsApp", 300);
        await SetCatalogAsync("Max", 490);
        var account = await NewAccountAsync();
        if (paid) await SeedRowAsync(account.AccountId, "Max", DateTime.UtcNow.AddDays(20)); // the Terms step needs a paid transport

        await using var factory = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IGlitchTipSignalService>();
            s.AddSingleton<IGlitchTipSignalService>(signals);
        }));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", account.Owner.Token);
        var response = await client.PostAsJsonAsync("/api/notification-channels",
            new { legalEntityForm = "Company", inn = "7707083893", offerAccepted = new { version = CurrentOfferVersion() }, transport = "Max", paymentRequest });
        var body = await response.Content.ReadAsStringAsync();
        for (var i = 0; i < 30 && signals.Messages.Count == 0; i++) await Task.Delay(100);
        await Task.Delay(300); // a second, wrong signal would arrive in this window
        return (response, body);
    }

    [Fact, TestCase("CY48-B6")]
    public async Task PaymentRequestTrue_SendsExactlyOneSignal_WithoutPersonalData()
    {
        var signals = new RecordingSignals(fail: false);

        var (response, body) = await PostChannelAsync(signals, paymentRequest: true, paid: false);

        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        signals.Messages.Should().HaveCount(1);
        signals.Messages[0].Should().NotContain("7707083893", "the signal carries no personal data (the ИНН is the owner's)").And.NotContain("@").And.NotContain("+7");
    }

    [Fact, TestCase("CY48-B6")]
    public async Task PaymentRequestFalse_SendsNoSignal()
    {
        var signals = new RecordingSignals(fail: false);

        var (response, body) = await PostChannelAsync(signals, paymentRequest: false, paid: true);

        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        signals.Messages.Should().BeEmpty("the Terms step is not a payment request");
    }

    [Fact, TestCase("CY48-B6")]
    public async Task FailingSignalChannel_DoesNotChangeTheAnswer()
    {
        var healthy = new RecordingSignals(fail: false);
        var (okResponse, okBody) = await PostChannelAsync(healthy, paymentRequest: true, paid: false);
        var failing = new RecordingSignals(fail: true);

        var (response, body) = await PostChannelAsync(failing, paymentRequest: true, paid: false);

        okResponse.StatusCode.Should().Be(HttpStatusCode.Created, okBody);
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        failing.Messages.Should().HaveCount(1, "the signal was attempted");
        var shape = (string s) => JsonDocument.Parse(s).RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();
        shape(body).Should().Equal(shape(okBody), "the body has the same fields as without a failing channel");
    }
}
