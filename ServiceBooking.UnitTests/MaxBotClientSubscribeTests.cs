using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.API.Services.PhoneVerification.Max;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE25.md §498.4 — the webhook subscription asks for bot_stopped and falls back to the cycle-14 list if the platform refuses.</summary>
public class MaxBotClientSubscribeTests
{
    private sealed class RecordingHandler(params HttpStatusCode[] answers) : HttpMessageHandler
    {
        public List<string[]> RequestedTypes { get; } = [];
        private int _i;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            RequestedTypes.Add(doc.RootElement.GetProperty("update_types").EnumerateArray().Select(e => e.GetString()!).ToArray());
            return new HttpResponseMessage(answers[Math.Min(_i++, answers.Length - 1)]);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static MaxBotClient Client(RecordingHandler handler)
    {
        var options = Options.Create(new PhoneVerificationOptions
        {
            Provider = "max-bot",
            Max = new MaxBotOptions { BotToken = "t", WebhookToken = new string('w', 40), PublicBaseUrl = "https://ezbook.ru", BotUsername = "bot" }
        });
        return new MaxBotClient(new Factory(handler), options, NullLogger<MaxBotClient>.Instance);
    }

    [Fact]
    public async Task Subscribe_AsksForBotStopped()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        (await Client(handler).SubscribeAsync(CancellationToken.None)).Should().BeTrue();
        handler.RequestedTypes.Should().ContainSingle().Which.Should().Equal("bot_started", "message_created", "bot_stopped");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Subscribe_WhenRefused_RetriesWithTheOldTwoTypes_InTheSameCall(HttpStatusCode refusal)
    {
        var handler = new RecordingHandler(refusal, HttpStatusCode.OK);
        (await Client(handler).SubscribeAsync(CancellationToken.None)).Should().BeTrue();
        handler.RequestedTypes.Should().HaveCount(2);
        handler.RequestedTypes[1].Should().Equal("bot_started", "message_created");
    }

    [Fact]
    public async Task Subscribe_WhenBothRefused_ReturnsFalse()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest);
        (await Client(handler).SubscribeAsync(CancellationToken.None)).Should().BeFalse();
        handler.RequestedTypes.Should().HaveCount(2);
    }
}
