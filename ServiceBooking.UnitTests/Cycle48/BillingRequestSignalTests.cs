using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceBooking.API.Services.Signals;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests.Cycle48;

/// <summary>CY48-B6 (unit half): the text of the operator signal and the due date (API_CONTRACT_CYCLE48_STOREFRONT.md §48.25).</summary>
public class BillingRequestSignalTests
{
    private static readonly Guid AccountId = Guid.Parse("1a2b3c4d-1111-2222-3333-444455556666");

    [Fact]
    public void SubscriptionRequest_HasTheContractedShape()
    {
        var text = BillingRequestSignal.ForSubscriptionRequest(CompanyKind.Services, "Салон", 2, AccountId, new DateOnly(2026, 10, 27));

        text.Should().Be("Новая заявка на подписку: линейка=Services, тариф=«Салон», опций=2, аккаунт=1a2b3c4d, счёт выставить до 27.10.2026");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void SubscriptionRequest_WithoutPlan_SaysNoChange(string? plan) =>
        BillingRequestSignal.ForSubscriptionRequest(CompanyKind.Baths, plan, 0, AccountId, new DateOnly(2026, 1, 5))
            .Should().Be("Новая заявка на подписку: линейка=Baths, тариф=без смены, опций=0, аккаунт=1a2b3c4d, счёт выставить до 05.01.2026");

    [Theory]
    [InlineData(NotificationTransport.WhatsApp, "WhatsApp")]
    [InlineData(NotificationTransport.Max, "MAX")]
    public void MessengerRequest_HasTheContractedShape(NotificationTransport transport, string name) =>
        BillingRequestSignal.ForMessengerPaymentRequest(transport, AccountId, new DateOnly(2026, 10, 27))
            .Should().Be($"Новая заявка на опцию: мессенджер={name}, аккаунт=1a2b3c4d, счёт выставить до 27.10.2026");

    [Fact]
    public void Messages_CarryNoPersonalData()
    {
        // Only the catalog plan name is free text; the account is reduced to 8 hex digits, so the full id never leaks.
        var text = BillingRequestSignal.ForSubscriptionRequest(CompanyKind.Orders, "Лавка", 1, AccountId, new DateOnly(2026, 10, 27));

        text.Should().NotContain(AccountId.ToString()).And.NotContain("@").And.NotContain("+7");
    }

    [Theory]
    // 2026-10-23 is a Friday; 12:00 UTC = 19:00 in Barnaul (UTC+7): same day.
    [InlineData("2026-10-23T12:00:00", "2026-10-26")]
    // Thursday evening in Barnaul -> Friday.
    [InlineData("2026-10-22T12:00:00", "2026-10-23")]
    // Saturday -> Monday.
    [InlineData("2026-10-24T08:00:00", "2026-10-26")]
    // Sunday -> Monday.
    [InlineData("2026-10-25T08:00:00", "2026-10-26")]
    // Friday 20:00 UTC is already Saturday 03:00 in Barnaul -> Monday, not Tuesday-style drift.
    [InlineData("2026-10-23T20:00:00", "2026-10-26")]
    // Sunday 20:00 UTC is Monday 03:00 in Barnaul -> Tuesday.
    [InlineData("2026-10-25T20:00:00", "2026-10-27")]
    public void InvoiceDueBy_IsTheNextWorkingDayInBarnaul(string utc, string expected) =>
        BillingRequestSignal.InvoiceDueBy(DateTime.SpecifyKind(DateTime.Parse(utc), DateTimeKind.Utc))
            .Should().Be(DateOnly.Parse(expected));

    [Fact]
    public async Task Dispatcher_SwallowsAnExceptionOfTheSignalService()
    {
        var signals = new ThrowingSignals();

        var act = () => OperatorSignalDispatcher.FireAndForget(signals, NullLogger.Instance, "x");

        act.Should().NotThrow();
        await signals.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Dispatcher_PassesTheMessageThrough()
    {
        var signals = new RecordingSignals();

        OperatorSignalDispatcher.FireAndForget(signals, NullLogger.Instance, "сообщение");

        (await signals.Message.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("сообщение");
    }

    private sealed class ThrowingSignals : IGlitchTipSignalService
    {
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> SendAsync(string message, CancellationToken ct)
        {
            Called.TrySetResult();
            throw new InvalidOperationException("signal channel is down");
        }
    }

    private sealed class RecordingSignals : IGlitchTipSignalService
    {
        public TaskCompletionSource<string> Message { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> SendAsync(string message, CancellationToken ct)
        {
            Message.TrySetResult(message);
            return Task.FromResult(true);
        }
    }
}
