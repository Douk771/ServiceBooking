using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 43, US-43-04 (SPEC.md): «Подтвердить оплату» в карточке номера проверяет то же, что подключение из карточки аккаунта (опция активна, цена задана), даёт тот же
/// результат и те же записи в журналах, сохраняет цену и сумму на момент подтверждения. Тексты — API_CONTRACT_CYCLE43.md §43.25.4, §43.30.
/// </summary>
public class Cycle43ConfirmPaymentTests(TestDatabaseFixture fixture) : Cycle43TestBase(fixture)
{
    private async Task PrepareCatalogAsync()
    {
        await SetCatalogAsync("WhatsApp", 300);
        await SetCatalogAsync("Max", 490);
    }

    private async Task<(Acc Account, Guid ChannelId)> WithRequestedNumberAsync(string transport = "Max")
    {
        await PrepareCatalogAsync();
        var a = await NewAccountAsync();
        return (a, await RequestChannelAsync(a, transport));
    }

    private static Task<HttpResponseMessage> Confirm(HttpClient admin, Guid channelId, object body) =>
        admin.PostAsJsonAsync($"/api/admin/notification-channels/{channelId}/confirm-payment", body);

    [Fact, TestCase("CY43-60")]
    public async Task Confirm_IsRefused409_WhenTheOptionIsInactiveHasNoPriceOrIsClosed_AndNothingIsWritten()
    {
        var admin = await AdminAsync();
        var (a, channelId) = await WithRequestedNumberAsync();
        var logsBefore = await DbAsync(db => db.ChannelOptionChangeLogs.CountAsync(l => l.BillingAccountId == a.AccountId));
        var paymentLogsBefore = await DbAsync(db => db.ChannelPaymentLogs.CountAsync(l => l.ChannelId == channelId));

        async Task Refused(string text)
        {
            var response = await Confirm(admin, channelId, new { months = 1, comment = "Счёт 17" });
            response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
            (await response.Content.ReadAsStringAsync()).Should().Be(text);
            (await RowAsync(a.AccountId, "Max")).Should().BeNull("a refused confirmation sells nothing");
        }

        await SetCatalogAsync("Max", null); // no price
        await Refused("У MAX не задана цена. Задайте её во вкладке уведомлений, блок „Подключение мессенджеров“");
        await SetCatalogAsync("Max", 490, active: false); // inactive
        await Refused("Опция MAX неактивна. Включите её во вкладке уведомлений, блок „Подключение мессенджеров“");
        await SetCatalogAsync("Max", 490, active: true, open: false); // closed
        await Refused("Опция MAX закрыта для подключения. Откройте её в блоке „Подключение мессенджеров“");

        (await DbAsync(db => db.ChannelOptionChangeLogs.CountAsync(l => l.BillingAccountId == a.AccountId))).Should().Be(logsBefore);
        (await DbAsync(db => db.ChannelPaymentLogs.CountAsync(l => l.ChannelId == channelId))).Should().Be(paymentLogsBefore);

        // the cause removed: the very same request goes through
        await SetCatalogAsync("Max", 490);
        await Expect(await Confirm(admin, channelId, new { months = 1, comment = "Счёт 17" }), HttpStatusCode.OK);

        // the earlier checks of the contract keep their place and texts
        var bad = await Confirm(admin, channelId, new { months = 13 });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).Should().Be("Срок — от 1 до 12 месяцев");
        (await Confirm(admin, Guid.NewGuid(), new { months = 1 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY43-61")]
    public async Task Confirm_AndTheAccountCardConnect_GiveTheSameResultAndTheSameJournals()
    {
        var admin = await AdminAsync();
        var (viaCard, channelId) = await WithRequestedNumberAsync();
        var viaAccount = await NewAccountAsync();

        await Expect(await Confirm(admin, channelId, new { months = 2, comment = "Счёт 17" }), HttpStatusCode.OK);
        await Expect(await ExtendAsync(admin, viaAccount.AccountId, "Max", 2, 490, "Счёт 17"), HttpStatusCode.OK);

        var cardRow = (await RowAsync(viaCard.AccountId, "Max"))!;
        var accountRow = (await RowAsync(viaAccount.AccountId, "Max"))!;
        cardRow.PaidUntilUtc.Should().BeCloseTo(accountRow.PaidUntilUtc!.Value, TimeSpan.FromMinutes(2), "the same date");
        (cardRow.Quantity, cardRow.EndsAtUtc, cardRow.GrantedByTrial).Should().Be((accountRow.Quantity, accountRow.EndsAtUtc, accountRow.GrantedByTrial));
        cardRow.ActivatedAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(3));

        var cardView = Line(await CardAsync(viaCard.AccountId, admin), "Max");
        var accountView = Line(await CardAsync(viaAccount.AccountId, admin), "Max");
        cardView.GetProperty("status").GetString().Should().Be(accountView.GetProperty("status").GetString());
        cardView.GetProperty("statusText").GetString().Should().Be(accountView.GetProperty("statusText").GetString(), "the same status text, the same date");
        cardView.GetProperty("monthlyPrice").GetDecimal().Should().Be(accountView.GetProperty("monthlyPrice").GetDecimal());

        var cardLog = await DbAsync(db => db.ChannelOptionChangeLogs.AsNoTracking().SingleAsync(l => l.BillingAccountId == viaCard.AccountId));
        var accountLog = await DbAsync(db => db.ChannelOptionChangeLogs.AsNoTracking().SingleAsync(l => l.BillingAccountId == viaAccount.AccountId));
        (cardLog.OptionCode, cardLog.PricePerMonth, cardLog.Months, cardLog.Amount).Should().Be((accountLog.OptionCode, accountLog.PricePerMonth, accountLog.Months, accountLog.Amount));
        (cardLog.PricePerMonth, cardLog.Months, cardLog.Amount).Should().Be((490m, 2, 980m));
        cardLog.Source.Should().Be(1, "the number card");
        accountLog.Source.Should().Be(0, "the account card");
        cardLog.ChannelId.Should().Be(channelId);
        accountLog.ChannelId.Should().BeNull();

        var cardHistory = await DbAsync(db => db.SubscriptionChangeLogs.AsNoTracking().SingleAsync(l => l.BillingAccountId == viaCard.AccountId && l.ChangeKind == SubscriptionChangeKind.Options));
        var accountHistory = await DbAsync(db => db.SubscriptionChangeLogs.AsNoTracking().SingleAsync(l => l.BillingAccountId == viaAccount.AccountId && l.ChangeKind == SubscriptionChangeKind.Options));
        cardHistory.Comment.Should().Be(accountHistory.Comment, "the same wording in the subscription history of both accounts");
        cardHistory.Comment.Should().StartWith("Подключение MAX на 2 мес. (2 мес. × 490 ₽ = 980 ₽).").And.Contain("Счёт 17");
        cardHistory.OldOptionsSummary.Should().Be("MAX: не подключён");
        cardHistory.NewOptionsSummary.Should().Be(accountHistory.NewOptionsSummary);

        // the owner sees the payment as made
        var overview = await OverviewTransportAsync(viaCard, "Max");
        overview.GetProperty("paymentPending").GetBoolean().Should().BeFalse();
        overview.GetProperty("paid").GetBoolean().Should().BeTrue();
    }

    [Fact, TestCase("CY43-62")]
    public async Task Confirm_StoresThePriceAndTheAmountOfTheMoment_InBothJournals_EvenWhenThePriceChangesLater()
    {
        var admin = await AdminAsync();
        var (a, channelId) = await WithRequestedNumberAsync();

        // the window shows the calculation (the same one the account card uses)
        var quote = await J(await Expect(await admin.GetAsync($"/api/admin/billing-accounts/{a.AccountId}/messengers/Max/quote?months=3"), HttpStatusCode.OK));
        quote.GetProperty("amountText").GetString().Should().Be("3 мес. × 490 ₽ = 1470 ₽");

        await Expect(await Confirm(admin, channelId, new { months = 3, comment = "Счёт 18", expectedPricePerMonth = quote.GetProperty("pricePerMonth").GetDecimal() }), HttpStatusCode.OK);
        await SetCatalogAsync("Max", 800); // later the price grows

        var option = await DbAsync(db => db.ChannelOptionChangeLogs.AsNoTracking().SingleAsync(l => l.BillingAccountId == a.AccountId));
        (option.PricePerMonth, option.Months, option.Amount).Should().Be((490m, 3, 1470m));
        var payment = await DbAsync(db => db.ChannelPaymentLogs.AsNoTracking().Where(l => l.ChannelId == channelId).OrderByDescending(l => l.ChangedAtUtc).ToListAsync());
        payment.Should().Contain(l => l.Amount == 1470m, "the confirmation row of the payment journal carries the amount");

        // the card of the number still shows the confirmation, the history says what it was
        var card = await J(await Expect(await admin.GetAsync($"/api/admin/notification-channels/{channelId}"), HttpStatusCode.OK));
        card.GetProperty("paymentEvents").EnumerateArray().Should().Contain(e => e.GetProperty("kind").GetString() == "PaymentConfirmed");
        await SetCatalogAsync("Max", 490);
    }

    [Fact, TestCase("CY43-63")]
    public async Task Confirm_WithOutdatedExpectedPrice_Is409_AndWithoutItTheFieldIsOptional()
    {
        var admin = await AdminAsync();
        var (a, channelId) = await WithRequestedNumberAsync();

        await SetCatalogAsync("Max", 590);
        var stale = await Confirm(admin, channelId, new { months = 1, comment = "x", expectedPricePerMonth = 490 });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await stale.Content.ReadAsStringAsync()).Should().Be("Цена MAX изменилась: теперь 590 ₽/мес. Проверьте сумму и повторите");
        (await RowAsync(a.AccountId, "Max")).Should().BeNull();

        // the old clients do not send the field at all, or send null: the current price is used
        var old = await Expect(await Confirm(admin, channelId, new { months = 2 }), HttpStatusCode.OK);
        (await DbAsync(db => db.ChannelOptionChangeLogs.AsNoTracking().SingleAsync(l => l.BillingAccountId == a.AccountId))).Amount.Should().Be(1180m);
        _ = old;
        await Expect(await Confirm(admin, channelId, new { months = 1, expectedPricePerMonth = (decimal?)null }), HttpStatusCode.OK);
        await Expect(await Confirm(admin, channelId, new { months = 1, expectedPricePerMonth = 590 }), HttpStatusCode.OK);
        await SetCatalogAsync("Max", 490);
    }

    [Fact, TestCase("CY43-64")]
    public async Task Confirm_IsInTheSubscriptionHistoryOfTheAccount_ForEveryTransport()
    {
        var admin = await AdminAsync();
        foreach (var transport in new[] { "Max", "WhatsApp" })
        {
            var (a, channelId) = await WithRequestedNumberAsync(transport);
            var before = await DbAsync(db => db.SubscriptionChangeLogs.CountAsync(l => l.BillingAccountId == a.AccountId));
            await Expect(await Confirm(admin, channelId, new { months = 1, comment = "Счёт 19" }), HttpStatusCode.OK);

            var after = await DbAsync(db => db.SubscriptionChangeLogs.AsNoTracking().Where(l => l.BillingAccountId == a.AccountId).OrderByDescending(l => l.ChangedAt).ToListAsync());
            after.Should().HaveCount(before + 1, $"{transport}: «Подтвердить оплату» now writes the history of the subscription (C40-7)");
            after[0].ChangeKind.Should().Be(SubscriptionChangeKind.Options);
            var name = transport == "Max" ? "MAX" : "WhatsApp";
            after[0].Comment.Should().Contain($"{name} на 1 мес.").And.Contain("Счёт 19");
            after[0].NewOptionsSummary.Should().Contain($"{name}: оплачено до");
            var screen = await TextAsync(await Expect(await admin.GetAsync($"/api/admin/billing-accounts/{a.AccountId}/subscription-history"), HttpStatusCode.OK));
            screen.Should().Contain($"{name}: оплачено до");
        }
    }

    [Fact, TestCase("CY43-65")]
    public async Task ConfirmAndAccountExtend_AtTheSameTime_BothApply()
    {
        var one = await AdminAsync();
        var two = await AdminAsync();
        var (a, channelId) = await WithRequestedNumberAsync();

        var responses = await Task.WhenAll(Confirm(one, channelId, new { months = 1 }), ExtendAsync(two, a.AccountId, "Max", 1, 490));
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK, "one lock of the account serves both paths");
        // Two one-month steps, not one two-month step: from 31.10 that is 30.11 → 30.12, not 31.12.
        (await RowAsync(a.AccountId, "Max"))!.PaidUntilUtc.Should().BeCloseTo(DateTime.UtcNow.AddMonths(1).AddMonths(1), TimeSpan.FromMinutes(3));
        (await DbAsync(db => db.ChannelOptionChangeLogs.CountAsync(l => l.BillingAccountId == a.AccountId))).Should().Be(2);
    }

    [Fact, TestCase("CY43-66")]
    public async Task Confirm_AtZeroPrice_IsAllowed_AndTheAmountIsZero()
    {
        var admin = await AdminAsync();
        var (a, channelId) = await WithRequestedNumberAsync();
        await SetCatalogAsync("Max", 0);
        await Expect(await Confirm(admin, channelId, new { months = 3, comment = "Бесплатно по договорённости" }), HttpStatusCode.OK);
        var log = await DbAsync(db => db.ChannelOptionChangeLogs.AsNoTracking().SingleAsync(l => l.BillingAccountId == a.AccountId));
        (log.PricePerMonth, log.Months, log.Amount).Should().Be((0m, 3, 0m), "zero is a legal price, not an absent one");
        Line(await CardAsync(a.AccountId, admin), "Max").GetProperty("status").GetString().Should().Be("Paid");
        await SetCatalogAsync("Max", 490);
    }
}
