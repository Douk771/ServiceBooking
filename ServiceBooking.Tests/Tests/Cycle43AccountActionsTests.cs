using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 43, US-43-03 (SPEC.md): подключить, продлить и отключить мессенджер в карточке биллинг-аккаунта отдельно от тарифа и без созданного номера; расчёт, журналы,
/// отказы 409 с причиной, гонка двух продлений. Тексты и тела — по API_CONTRACT_CYCLE43.md §43.25–§43.28.
/// </summary>
public class Cycle43AccountActionsTests(TestDatabaseFixture fixture) : Cycle43TestBase(fixture)
{
    private const string Closed = "Опция MAX закрыта для подключения. Откройте её в блоке „Подключение мессенджеров“";
    private const string Inactive = "Опция MAX неактивна. Включите её во вкладке уведомлений, блок „Подключение мессенджеров“";
    private const string NoPrice = "У MAX не задана цена. Задайте её во вкладке уведомлений, блок „Подключение мессенджеров“";

    private async Task PrepareCatalogAsync()
    {
        await SetCatalogAsync("WhatsApp", 300);
        await SetCatalogAsync("Max", 490);
    }

    private async Task<JsonElement> QuoteAsync(HttpClient admin, Guid accountId, string transport, int months) =>
        await J(await Expect(await admin.GetAsync($"/api/admin/billing-accounts/{accountId}/messengers/{transport}/quote?months={months}"), HttpStatusCode.OK));

    private async Task<string> RefusalAsync(HttpResponseMessage response, HttpStatusCode code)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(code, text);
        return text;
    }

    private static void ShouldBeNear(DateTime? actual, DateTime expected, string because) =>
        actual.Should().NotBeNull(because).And.Subject!.Value.Should().BeCloseTo(expected, TimeSpan.FromMinutes(3), because);

    // ── расчёт ───────────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-40")]
    public async Task Connect_WithoutANumber_CreatesAPaidRowForTheAmountShownByTheQuote()
    {
        await PrepareCatalogAsync();
        var admin = await AdminAsync();
        var a = await NewAccountAsync();

        var quote = await QuoteAsync(admin, a.AccountId, "Max", 3);
        quote.GetProperty("pricePerMonth").GetDecimal().Should().Be(490m);
        quote.GetProperty("months").GetInt32().Should().Be(3);
        quote.GetProperty("amount").GetDecimal().Should().Be(1470m);
        quote.GetProperty("amountText").GetString().Should().Be("3 мес. × 490 ₽ = 1470 ₽");
        quote.GetProperty("actionText").GetString().Should().Be("Подключить на 3 мес.");
        quote.GetProperty("currentPaidUntil").ValueKind.Should().Be(JsonValueKind.Null);
        var expectedEnd = DateTime.UtcNow.AddMonths(3);
        quote.GetProperty("newPaidUntilText").GetString().Should().Be($"Будет оплачено до {D(expectedEnd)}");
        (await RowAsync(a.AccountId, "Max")).Should().BeNull("a quote writes nothing");

        var response = await ExtendAsync(admin, a.AccountId, "Max", 3, quote.GetProperty("pricePerMonth").GetDecimal(), "Счёт 17");
        var card = await J(await Expect(response, HttpStatusCode.OK));

        var row = (await RowAsync(a.AccountId, "Max"))!;
        row.Quantity.Should().Be(1);
        ShouldBeNear(row.PaidUntilUtc, expectedEnd, "now + 3 months");
        row.EndsAtUtc.Should().BeNull();
        row.GrantedByTrial.Should().BeFalse();
        (await DbAsync(db => db.NotificationChannels.CountAsync(c => c.BillingAccountId == a.AccountId))).Should().Be(0, "no number is needed to connect");

        var line = Line(card, "Max");
        line.GetProperty("status").GetString().Should().Be("Paid");
        line.GetProperty("statusText").GetString().Should().Be($"Оплачено до {D(expectedEnd)}");
        line.GetProperty("number").GetProperty("state").GetString().Should().Be("None");
        line.GetProperty("number").GetProperty("text").GetString().Should().Be("Номера нет");
        line.GetProperty("canExtend").GetBoolean().Should().BeTrue();
        line.GetProperty("extendActionText").GetString().Should().Be("Продлить");
        line.GetProperty("canDisable").GetBoolean().Should().BeTrue();
        card.GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(1490m);
        Line(card, "WhatsApp").GetProperty("status").GetString().Should().Be("NotConnected", "the other messenger is not touched");
        Line(card, "WhatsApp").GetProperty("extendActionText").GetString().Should().Be("Подключить");

        // the same on the owner's screen
        var owner = OwnerLine(await OwnerSubscriptionAsync(a), "Max");
        owner.GetProperty("statusText").GetString().Should().Be($"Оплачено до {D(expectedEnd)}");
        owner.GetProperty("action").ValueKind.Should().Be(JsonValueKind.Null, "paid: no button");
    }

    [Fact, TestCase("CY43-41")]
    public async Task Extend_CountsFromTheLaterOfNowAndTheCurrentEnd_AndAnEndedPeriodStartsFromNow()
    {
        await PrepareCatalogAsync();
        var admin = await AdminAsync();

        var a = await NewAccountAsync();
        var firstEnd = NoonIn(20);
        await SeedRowAsync(a.AccountId, "WhatsApp", firstEnd);
        var quote = await QuoteAsync(admin, a.AccountId, "WhatsApp", 2);
        quote.GetProperty("actionText").GetString().Should().Be("Продлить на 2 мес.");
        quote.GetProperty("currentPaidUntil").GetDateTime().Should().BeCloseTo(firstEnd, TimeSpan.FromSeconds(1));
        quote.GetProperty("newPaidUntil").GetDateTime().Should().BeCloseTo(firstEnd.AddMonths(2), TimeSpan.FromMinutes(1), "a paid period is extended from its end, not from now");
        quote.GetProperty("amountText").GetString().Should().Be("2 мес. × 300 ₽ = 600 ₽");

        await Expect(await ExtendAsync(admin, a.AccountId, "WhatsApp", 2, 300), HttpStatusCode.OK);
        (await RowAsync(a.AccountId, "WhatsApp"))!.PaidUntilUtc.Should().BeCloseTo(firstEnd.AddMonths(2), TimeSpan.FromMinutes(1));

        // a repeat of the same request is one more extension (as «Подтвердить оплату»)
        await Expect(await ExtendAsync(admin, a.AccountId, "WhatsApp", 2, 300), HttpStatusCode.OK);
        (await RowAsync(a.AccountId, "WhatsApp"))!.PaidUntilUtc.Should().BeCloseTo(firstEnd.AddMonths(4), TimeSpan.FromMinutes(1));

        // 24 months is the largest term
        var big = await NewAccountAsync();
        var q24 = await QuoteAsync(admin, big.AccountId, "Max", 24);
        q24.GetProperty("amount").GetDecimal().Should().Be(11760m);
        await Expect(await ExtendAsync(admin, big.AccountId, "Max", 24, 490), HttpStatusCode.OK);
        ShouldBeNear((await RowAsync(big.AccountId, "Max"))!.PaidUntilUtc, DateTime.UtcNow.AddMonths(24), "now + 24 months");

        // an ended period is not revived: the new period starts today
        var ended = await NewAccountAsync();
        await SeedRowAsync(ended.AccountId, "Max", NoonIn(-20));
        (await QuoteAsync(admin, ended.AccountId, "Max", 1)).GetProperty("actionText").GetString().Should().Be("Подключить на 1 мес.", "an ended payment is connected again");
        await Expect(await ExtendAsync(admin, ended.AccountId, "Max", 1, 490), HttpStatusCode.OK);
        ShouldBeNear((await RowAsync(ended.AccountId, "Max"))!.PaidUntilUtc, DateTime.UtcNow.AddMonths(1), "an ended period starts again from now");
        Line(await CardAsync(ended.AccountId, admin), "Max").GetProperty("status").GetString().Should().Be("Paid");
    }

    [Fact, TestCase("CY43-42")]
    public async Task Extend_RedeemsATrialRow_AndKeepsLegacyRowsPaid()
    {
        await PrepareCatalogAsync();
        var admin = await AdminAsync();

        // a trial row becomes an ordinary paid one
        var trial = await NewAccountAsync();
        await SeedRowAsync(trial.AccountId, "WhatsApp", NoonIn(6), trial: true);
        Line(await CardAsync(trial.AccountId, admin), "WhatsApp").GetProperty("status").GetString().Should().Be("Trial");
        await Expect(await ExtendAsync(admin, trial.AccountId, "WhatsApp", 2, 300), HttpStatusCode.OK);
        var row = (await RowAsync(trial.AccountId, "WhatsApp"))!;
        row.GrantedByTrial.Should().BeFalse("a redeemed trial row is an ordinary paid row");
        row.PaidUntilUtc.Should().BeOnOrAfter(DateTime.UtcNow.AddMonths(2).AddMinutes(-3));
        var card = await CardAsync(trial.AccountId, admin);
        Line(card, "WhatsApp").GetProperty("status").GetString().Should().Be("Paid");
        Line(card, "WhatsApp").GetProperty("isTrial").GetBoolean().Should().BeFalse();
        card.GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(1300m, "the redeemed row now costs the price");

        // a row without its own date (lives by the Services subscription) gets its own date from the first extension
        var legacy = await NewAccountAsync();
        await SeedRowAsync(legacy.AccountId, "Max", null);
        await Expect(await ExtendAsync(admin, legacy.AccountId, "Max", 1, 490), HttpStatusCode.OK);
        (await RowAsync(legacy.AccountId, "Max"))!.PaidUntilUtc.Should().NotBeNull().And.Subject!.Value.Should().BeOnOrAfter(DateTime.UtcNow.AddMonths(1).AddMinutes(-3));
        Line(await CardAsync(legacy.AccountId, admin), "Max").GetProperty("status").GetString().Should().Be("Paid");
    }

    // ── отказы ───────────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-43")]
    public async Task Refusals_NotSold_Are409WithTheReason_ForQuoteAndExtend_AndWriteNothing()
    {
        var admin = await AdminAsync();
        var a = await NewAccountAsync();
        var journal = await DbAsync(db => db.ChannelOptionChangeLogs.CountAsync(l => l.BillingAccountId == a.AccountId));

        async Task Check(string transport, string expected)
        {
            (await RefusalAsync(await admin.GetAsync($"/api/admin/billing-accounts/{a.AccountId}/messengers/{transport}/quote?months=1"), HttpStatusCode.Conflict)).Should().Be(expected, "quote");
            (await RefusalAsync(await ExtendAsync(admin, a.AccountId, transport, 1, 490), HttpStatusCode.Conflict)).Should().Be(expected, "extend");
            var line = Line(await CardAsync(a.AccountId, admin), transport);
            line.GetProperty("canExtend").GetBoolean().Should().BeFalse();
            line.GetProperty("extendDisabledText").GetString().Should().Be(line.GetProperty("sale").GetProperty("text").GetString(), "the card says why the button is dead");
        }

        await SetCatalogAsync("Max", 490, open: false);
        await Check("Max", Closed);
        await SetCatalogAsync("Max", 490, active: false, open: true);
        await Check("Max", Inactive);
        await SetCatalogAsync("Max", null, active: true, open: true);
        await Check("Max", NoPrice);
        // the order of the checks: closed wins over inactive and over a missing price
        await SetCatalogAsync("Max", null, active: false, open: false);
        await Check("Max", Closed);
        await SetCatalogAsync("WhatsApp", 300, open: false);
        await Check("WhatsApp", Closed.Replace("MAX", "WhatsApp"));

        (await RowAsync(a.AccountId, "Max")).Should().BeNull();
        (await RowAsync(a.AccountId, "WhatsApp")).Should().BeNull();
        (await DbAsync(db => db.ChannelOptionChangeLogs.CountAsync(l => l.BillingAccountId == a.AccountId))).Should().Be(journal, "a refusal is not journalled");
        await PrepareCatalogAsync();
    }

    [Fact, TestCase("CY43-44")]
    public async Task Extend_InputValidation_UsesTheContractTexts_AndUnknownThingsAre404()
    {
        await PrepareCatalogAsync();
        var admin = await AdminAsync();
        var a = await NewAccountAsync();
        var url = $"/api/admin/billing-accounts/{a.AccountId}/messengers/Max";

        foreach (var months in new[] { 0, -1, 25, 100 })
        {
            (await RefusalAsync(await ExtendAsync(admin, a.AccountId, "Max", months, 490), HttpStatusCode.BadRequest)).Should().Be("Срок — от 1 до 24 месяцев", $"months={months}");
            (await RefusalAsync(await admin.GetAsync($"{url}/quote?months={months}"), HttpStatusCode.BadRequest)).Should().Be("Срок — от 1 до 24 месяцев", $"quote months={months}");
        }
        (await RefusalAsync(await ExtendAsync(admin, a.AccountId, "Max", 1, 490, new string('я', 501)), HttpStatusCode.BadRequest)).Should().Be("Комментарий — не длиннее 500 символов");
        (await RefusalAsync(await admin.PostAsJsonAsync($"{url}/extend", new { months = 1, comment = "x" }), HttpStatusCode.BadRequest)).Should().Be("Не передана ожидаемая цена — обновите расчёт");
        (await RefusalAsync(await admin.PostAsJsonAsync($"{url}/extend", new { months = 1, comment = "x", expectedPricePerMonth = (decimal?)null }), HttpStatusCode.BadRequest)).Should().Be("Не передана ожидаемая цена — обновите расчёт");

        // 404 with an empty body: no such account, no such transport
        var ghost = Guid.NewGuid();
        foreach (var response in new[]
                 {
                     await ExtendAsync(admin, ghost, "Max", 1, 490), await admin.GetAsync($"/api/admin/billing-accounts/{ghost}/messengers/Max/quote?months=1"),
                     await DisableAsync(admin, ghost, "Max", "Now"), await ExtendAsync(admin, a.AccountId, "Telegram", 1, 490),
                     await admin.GetAsync($"/api/admin/billing-accounts/{a.AccountId}/messengers/Telegram/quote?months=1"), await DisableAsync(admin, a.AccountId, "Telegram", "Now"),
                 })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync()).Should().BeEmpty();
        }
        (await RowAsync(a.AccountId, "Max")).Should().BeNull("nothing of the above wrote a row");

        // the comment is optional, and 500 characters are fine
        await Expect(await ExtendAsync(admin, a.AccountId, "Max", 1, 490, null), HttpStatusCode.OK);
        await Expect(await ExtendAsync(admin, a.AccountId, "Max", 1, 490, new string('я', 500)), HttpStatusCode.OK);
    }

    [Fact, TestCase("CY43-45")]
    public async Task Extend_WithAnOutdatedPrice_Is409WithTheNewPrice_AndTheJournalAmountIsWhatWasShown()
    {
        await PrepareCatalogAsync();
        var admin = await AdminAsync();
        var a = await NewAccountAsync();
        var quote = await QuoteAsync(admin, a.AccountId, "Max", 2);
        var shownPrice = quote.GetProperty("pricePerMonth").GetDecimal();

        await SetCatalogAsync("Max", 590); // another admin changes the price between the quote and the click
        var refusal = await RefusalAsync(await ExtendAsync(admin, a.AccountId, "Max", 2, shownPrice), HttpStatusCode.Conflict);
        refusal.Should().Be("Цена MAX изменилась: теперь 590 ₽/мес. Проверьте сумму и повторите");
        (await RowAsync(a.AccountId, "Max")).Should().BeNull("nothing was sold at the stale price");
        (await DbAsync(db => db.ChannelOptionChangeLogs.CountAsync(l => l.BillingAccountId == a.AccountId))).Should().Be(0);

        // a repeat with a fresh quote goes through and the journal holds the shown price and the sum
        var fresh = await QuoteAsync(admin, a.AccountId, "Max", 2);
        fresh.GetProperty("amountText").GetString().Should().Be("2 мес. × 590 ₽ = 1180 ₽");
        await Expect(await ExtendAsync(admin, a.AccountId, "Max", 2, fresh.GetProperty("pricePerMonth").GetDecimal()), HttpStatusCode.OK);
        var log = await DbAsync(db => db.ChannelOptionChangeLogs.AsNoTracking().SingleAsync(l => l.BillingAccountId == a.AccountId));
        (log.PricePerMonth, log.Months, log.Amount).Should().Be((590m, 2, 1180m));

        // the price later changes again: the history still says at which price it was confirmed
        await SetCatalogAsync("Max", 700);
        var again = await DbAsync(db => db.ChannelOptionChangeLogs.AsNoTracking().SingleAsync(l => l.Id == log.Id));
        (again.PricePerMonth, again.Amount).Should().Be((590m, 1180m));

        // a fractional price keeps the kopecks in the amount
        await SetCatalogAsync("Max", 490.50m);
        var b = await NewAccountAsync();
        var q = await QuoteAsync(admin, b.AccountId, "Max", 3);
        q.GetProperty("amount").GetDecimal().Should().Be(1471.50m);
        q.GetProperty("amountText").GetString().Should().Be("3 мес. × 490,50 ₽ = 1471,50 ₽");
        await SetCatalogAsync("Max", 490);
    }

    // ── журналы ──────────────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-46")]
    public async Task EveryConnectAndExtend_WritesOneOptionLogRowAndOneSubscriptionHistoryRow()
    {
        await PrepareCatalogAsync();
        var admin = await AdminAsync();
        var adminId = (await LoginAsSuperAdminAsync()).UserId;
        var a = await NewAccountAsync();
        var historyBefore = await DbAsync(db => db.SubscriptionChangeLogs.CountAsync(l => l.BillingAccountId == a.AccountId));

        await Expect(await ExtendAsync(admin, a.AccountId, "Max", 3, 490, "Счёт 17"), HttpStatusCode.OK);
        var firstEnd = (await RowAsync(a.AccountId, "Max"))!.PaidUntilUtc!.Value;

        var option = await DbAsync(db => db.ChannelOptionChangeLogs.AsNoTracking().Where(l => l.BillingAccountId == a.AccountId).ToListAsync());
        option.Should().ContainSingle();
        var o = option[0];
        (o.OptionCode, o.Source, o.PricePerMonth, o.Months, o.Amount).Should().Be((MaxCode, 0, 490m, 3, 1470m));
        o.NewPaidUntilUtc.Should().BeCloseTo(firstEnd, TimeSpan.FromSeconds(1));
        o.OldPaidUntilUtc.Should().BeNull();
        o.ChangedByUserId.Should().Be(adminId);
        o.Comment.Should().Contain("Счёт 17");
        o.ChannelId.Should().BeNull("there is no number in this path");

        var history = await DbAsync(db => db.SubscriptionChangeLogs.AsNoTracking().Where(l => l.BillingAccountId == a.AccountId).OrderBy(l => l.ChangedAt).ToListAsync());
        history.Should().HaveCount(historyBefore + 1, "the connect is in the subscription history of the account (C40-7)");
        var h = history.Last();
        h.ChangeKind.Should().Be(SubscriptionChangeKindOptions);
        h.ChangedByUserId.Should().Be(adminId);
        h.Comment.Should().StartWith("Подключение MAX на 3 мес. (3 мес. × 490 ₽ = 1470 ₽).").And.Contain("Счёт 17");
        h.OldOptionsSummary.Should().Be("MAX: не подключён");
        h.NewOptionsSummary.Should().Be($"MAX: оплачено до {D(firstEnd)}");

        // the second action is an extension: one more row in each journal, the wording says «Продление»
        await Expect(await ExtendAsync(admin, a.AccountId, "Max", 1, 490, null), HttpStatusCode.OK);
        (await DbAsync(db => db.ChannelOptionChangeLogs.CountAsync(l => l.BillingAccountId == a.AccountId))).Should().Be(2);
        var second = await DbAsync(db => db.SubscriptionChangeLogs.AsNoTracking().Where(l => l.BillingAccountId == a.AccountId).OrderByDescending(l => l.ChangedAt).FirstAsync());
        second.Comment.Should().StartWith("Продление MAX на 1 мес. (1 мес. × 490 ₽ = 490 ₽)");
        second.OldOptionsSummary.Should().Be($"MAX: оплачено до {D(firstEnd)}");

        // the admin sees it in the history screen
        var screen = await TextAsync(await Expect(await admin.GetAsync($"/api/admin/billing-accounts/{a.AccountId}/subscription-history"), HttpStatusCode.OK));
        screen.Should().Contain("Options").And.Contain("MAX: не подключён").And.Contain($"MAX: оплачено до {D(firstEnd)}");
    }

    private static readonly ServiceBooking.Core.Enums.SubscriptionChangeKind SubscriptionChangeKindOptions = ServiceBooking.Core.Enums.SubscriptionChangeKind.Options;

    [Fact, TestCase("CY43-47")]
    public async Task TwoSimultaneousExtensions_BothApply_NoneIsLost()
    {
        await PrepareCatalogAsync();
        var a = await NewAccountAsync();
        var one = await AdminAsync();
        var two = await AdminAsync();

        var responses = await Task.WhenAll(ExtendAsync(one, a.AccountId, "Max", 1, 490, "А"), ExtendAsync(two, a.AccountId, "Max", 1, 490, "Б"));
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK, "no 500 from the unique index, no refusal: the second waits for the first");

        // Two one-month steps: from 31.10 that is 30.11 → 30.12, not 31.12.
        ShouldBeNear((await RowAsync(a.AccountId, "Max"))!.PaidUntilUtc, DateTime.UtcNow.AddMonths(1).AddMonths(1), "two months of one month each");
        (await DbAsync(db => db.ChannelOptionChangeLogs.CountAsync(l => l.BillingAccountId == a.AccountId))).Should().Be(2);
        (await DbAsync(db => db.SubscriptionChangeLogs.CountAsync(l => l.BillingAccountId == a.AccountId && l.Comment!.Contains("MAX")))).Should().Be(2);
        (await DbAsync(db => db.AccountSubscriptionOptions.CountAsync(r => r.BillingAccountId == a.AccountId && r.Option.Code == MaxCode))).Should().Be(1, "one row per option and account");

        // an extension and a disable at once: both are applied in some order, the row is consistent
        var b = await NewAccountAsync();
        await Expect(await ExtendAsync(one, b.AccountId, "WhatsApp", 1, 300), HttpStatusCode.OK);
        var both = await Task.WhenAll(ExtendAsync(one, b.AccountId, "WhatsApp", 1, 300), DisableAsync(two, b.AccountId, "WhatsApp", "PeriodEnd"));
        both.Select(r => (int)r.StatusCode).Should().OnlyContain(c => c == 200 || c == 409);
        both.Count(r => r.StatusCode == HttpStatusCode.OK).Should().BeGreaterThanOrEqualTo(1);
    }

    // ── подключение без номера (А8) ──────────────────────────────────────────────────────

    [Fact, TestCase("CY43-48")]
    public async Task ConnectedWithoutANumber_OwnerWizardStartsAtTerms_WithoutThePaymentStep()
    {
        await PrepareCatalogAsync();
        var admin = await AdminAsync();
        var a = await NewAccountAsync();

        var before = await OverviewTransportAsync(a, "Max");
        before.GetProperty("paid").GetBoolean().Should().BeFalse();
        before.GetProperty("wizardStep").GetString().Should().Be("Payment", "unpaid: the wizard starts with the payment step");

        await Expect(await ExtendAsync(admin, a.AccountId, "Max", 2, 490), HttpStatusCode.OK);
        var after = await OverviewTransportAsync(a, "Max");
        after.GetProperty("paid").GetBoolean().Should().BeTrue();
        after.GetProperty("isTrial").GetBoolean().Should().BeFalse();
        after.GetProperty("paymentPending").GetBoolean().Should().BeFalse();
        after.GetProperty("canRequestPayment").GetBoolean().Should().BeFalse("paid: nothing to request");
        after.GetProperty("wizardStep").GetString().Should().Be("Terms", "no number yet: the owner goes straight to the terms (the payment step is skipped)");
        after.GetProperty("channel").ValueKind.Should().Be(JsonValueKind.Null);

        // the owner accepts the terms and creates the number: it is not an «заявка на оплату», the QR comes next
        var channelId = await RequestChannelAsync(a, "Max", paymentRequest: false);
        var withChannel = await OverviewTransportAsync(a, "Max");
        withChannel.GetProperty("paymentPending").GetBoolean().Should().BeFalse("the transport is already paid, the number asks for no payment");
        withChannel.GetProperty("wizardStep").GetString().Should().BeOneOf(["Terms", "Qr", "Done"], "the payment step never comes back for a paid transport (the owner still has to accept the risk text and bind the number)");
        var channel = await DbAsync(db => db.NotificationChannels.AsNoTracking().FirstAsync(c => c.Id == channelId));
        channel.BillingAccountId.Should().Be(a.AccountId);
        channel.RequestedAtUtc.Should().BeNull("a request for payment is not created for a paid transport");

        // and the admin's card shows the bound number state
        var line = Line(await CardAsync(a.AccountId, admin), "Max");
        line.GetProperty("number").GetProperty("state").GetString().Should().BeOneOf("AwaitingQr", "Bound");
        line.GetProperty("number").GetProperty("channelId").GetGuid().Should().Be(channelId);
    }

    [Fact, TestCase("CY43-49")]
    public async Task NumberWaitingForPayment_IsClosedByTheExtensionFromTheAccountCard()
    {
        await PrepareCatalogAsync();
        var admin = await AdminAsync();
        var a = await NewAccountAsync();
        var channelId = await RequestChannelAsync(a, "Max");

        var waiting = await OverviewTransportAsync(a, "Max");
        waiting.GetProperty("paymentPending").GetBoolean().Should().BeTrue();
        Line(await CardAsync(a.AccountId, admin), "Max").GetProperty("statusText").GetString().Should().Be("Ожидает подтверждения оплаты");
        var requested = await J(await Expect(await admin.GetAsync("/api/admin/notification-channels?payment=Requested"), HttpStatusCode.OK));
        requested.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Contain(channelId);

        await Expect(await ExtendAsync(admin, a.AccountId, "Max", 1, 490), HttpStatusCode.OK);

        var paid = await OverviewTransportAsync(a, "Max");
        paid.GetProperty("paymentPending").GetBoolean().Should().BeFalse("the request is closed by the payment");
        paid.GetProperty("paid").GetBoolean().Should().BeTrue();
        Line(await CardAsync(a.AccountId, admin), "Max").GetProperty("status").GetString().Should().Be("Paid");
        (await J(await Expect(await admin.GetAsync("/api/admin/notification-channels?payment=Requested"), HttpStatusCode.OK)))
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().NotContain(channelId);
        OwnerLine(await OwnerSubscriptionAsync(a), "Max").GetProperty("status").GetString().Should().Be("Paid");
    }

    // ── отключение (P1) ──────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-50")]
    public async Task DisableNow_BehavesLikeAnEndedPayment_AndIsJournalled()
    {
        await PrepareCatalogAsync();
        var admin = await AdminAsync();
        var adminId = (await LoginAsSuperAdminAsync()).UserId;
        var a = await NewAccountAsync();
        await Expect(await ExtendAsync(admin, a.AccountId, "Max", 3, 490), HttpStatusCode.OK);
        var paidUntil = (await RowAsync(a.AccountId, "Max"))!.PaidUntilUtc!.Value;

        var line = Line(await CardAsync(a.AccountId, admin), "Max");
        line.GetProperty("canDisable").GetBoolean().Should().BeTrue();
        line.GetProperty("disableNowConfirmText").GetString().Should().Be($"Деньги не возвращаются. Сообщения клиентам в MAX перестанут уходить с {D(DateTime.UtcNow)}");
        line.GetProperty("disablePeriodEndConfirmText").GetString().Should().Be($"Деньги не возвращаются. Сообщения клиентам в MAX перестанут уходить с {D(paidUntil)}");

        var response = await Expect(await DisableAsync(admin, a.AccountId, "Max", "Now", "Просьба владельца"), HttpStatusCode.OK);
        var row = (await RowAsync(a.AccountId, "Max"))!;
        ShouldBeNear(row.EndsAtUtc, DateTime.UtcNow, "disabled right now");

        var card = await J(response);
        var after = Line(card, "Max");
        after.GetProperty("status").GetString().Should().Be("Expired");
        after.GetProperty("statusText").GetString().Should().Be($"Оплата закончилась {D(DateTime.UtcNow)}");
        after.GetProperty("monthlyPrice").GetDecimal().Should().Be(0m);
        after.GetProperty("countsInTotal").GetBoolean().Should().BeFalse();
        after.GetProperty("canDisable").GetBoolean().Should().BeFalse("nothing left to disable");
        after.GetProperty("canExtend").GetBoolean().Should().BeTrue("it can be connected again");
        card.GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(1000m);
        (await ListTotalAsync(a, admin)).Should().Be(1000m);

        // the owner sees the same: not paid, can reconnect, and the wizard does not skip the payment step any more
        var owner = OwnerLine(await OwnerSubscriptionAsync(a), "Max");
        owner.GetProperty("statusText").GetString().Should().Be($"Оплата закончилась {D(DateTime.UtcNow)}");
        owner.GetProperty("action").GetString().Should().Be("Renew");
        var overview = await OverviewTransportAsync(a, "Max");
        overview.GetProperty("paid").GetBoolean().Should().BeFalse("the transport is not paid: sending behaves as after an ended payment");
        overview.GetProperty("wizardStep").GetString().Should().Be("Payment");

        // journals
        var log = await DbAsync(db => db.ChannelOptionChangeLogs.AsNoTracking().Where(l => l.BillingAccountId == a.AccountId).OrderByDescending(l => l.ChangedAtUtc).FirstAsync());
        log.Source.Should().Be(5, "AdminOptionEnded");
        log.NewEndsAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(3));
        log.OldEndsAtUtc.Should().BeNull();
        log.ChangedByUserId.Should().Be(adminId);
        log.Comment.Should().Contain("Просьба владельца");
        var history = await DbAsync(db => db.SubscriptionChangeLogs.AsNoTracking().Where(l => l.BillingAccountId == a.AccountId).OrderByDescending(l => l.ChangedAt).FirstAsync());
        history.Comment.Should().StartWith($"Отключение MAX с {D(DateTime.UtcNow)}.").And.Contain("Просьба владельца");

        // a new connect after the disable counts from now: the disabled remainder does not come back
        await Expect(await ExtendAsync(admin, a.AccountId, "Max", 1, 490), HttpStatusCode.OK);
        var again = (await RowAsync(a.AccountId, "Max"))!;
        again.EndsAtUtc.Should().BeNull("a new payment cancels the disable");
        ShouldBeNear(again.PaidUntilUtc, DateTime.UtcNow.AddMonths(1), "counts from now, not from the old end");
    }

    [Fact, TestCase("CY43-51")]
    public async Task DisableAtPeriodEnd_KeepsTheServiceUntilThen_SecondRequestIs409_AndAnExtensionCancelsIt()
    {
        await PrepareCatalogAsync();
        var admin = await AdminAsync();
        var a = await NewAccountAsync();
        await SeedRowAsync(a.AccountId, "WhatsApp", NoonIn(45));
        var paidUntil = (await RowAsync(a.AccountId, "WhatsApp"))!.PaidUntilUtc!.Value;

        var card = await J(await Expect(await DisableAsync(admin, a.AccountId, "WhatsApp", "PeriodEnd", "Конец периода"), HttpStatusCode.OK));
        (await RowAsync(a.AccountId, "WhatsApp"))!.EndsAtUtc.Should().BeCloseTo(paidUntil, TimeSpan.FromSeconds(1));
        var line = Line(card, "WhatsApp");
        line.GetProperty("status").GetString().Should().Be("Ending");
        line.GetProperty("statusText").GetString().Should().Be($"Отключается {D(paidUntil)}");
        line.GetProperty("countsInTotal").GetBoolean().Should().BeTrue("it is paid until then");
        line.GetProperty("monthlyPrice").GetDecimal().Should().Be(300m);
        (await OverviewTransportAsync(a, "WhatsApp")).GetProperty("paid").GetBoolean().Should().BeTrue("the service continues until the end of the period");
        OwnerLine(await OwnerSubscriptionAsync(a), "WhatsApp").GetProperty("statusText").GetString().Should().Be($"Отключается {D(paidUntil)}");

        (await RefusalAsync(await DisableAsync(admin, a.AccountId, "WhatsApp", "PeriodEnd", "ещё раз"), HttpStatusCode.Conflict)).Should().Be($"Отключение WhatsApp уже назначено на {D(paidUntil)}");

        // «сейчас» overrides the schedule
        await Expect(await DisableAsync(admin, a.AccountId, "WhatsApp", "Now", "Передумали ждать"), HttpStatusCode.OK);
        ShouldBeNear((await RowAsync(a.AccountId, "WhatsApp"))!.EndsAtUtc, DateTime.UtcNow, "now");

        // an extension cancels a scheduled disable
        var b = await NewAccountAsync();
        await SeedRowAsync(b.AccountId, "Max", NoonIn(20), endsAt: NoonIn(20));
        Line(await CardAsync(b.AccountId, admin), "Max").GetProperty("status").GetString().Should().Be("Ending");
        await Expect(await ExtendAsync(admin, b.AccountId, "Max", 1, 490), HttpStatusCode.OK);
        (await RowAsync(b.AccountId, "Max"))!.EndsAtUtc.Should().BeNull("the extension takes the scheduled disable off");
        Line(await CardAsync(b.AccountId, admin), "Max").GetProperty("status").GetString().Should().Be("Paid");
    }

    [Fact, TestCase("CY43-52")]
    public async Task Disable_OfATrialRow_IsAllowedAndJournalled_AtPeriodEndOnlyWithADate()
    {
        await PrepareCatalogAsync();
        var admin = await AdminAsync();

        var withDate = await NewAccountAsync();
        await SeedRowAsync(withDate.AccountId, "Max", NoonIn(7), trial: true);
        await Expect(await DisableAsync(admin, withDate.AccountId, "Max", "Now", "Закрываем пробный"), HttpStatusCode.OK);
        ShouldBeNear((await RowAsync(withDate.AccountId, "Max"))!.EndsAtUtc, DateTime.UtcNow, "a trial row can be disabled right now");
        (await DbAsync(db => db.ChannelOptionChangeLogs.CountAsync(l => l.BillingAccountId == withDate.AccountId && l.Source == 5))).Should().Be(1);
        Line(await CardAsync(withDate.AccountId, admin), "Max").GetProperty("statusText").GetString().Should().Be($"Пробный период закончился {D(DateTime.UtcNow)}");

        var noDate = await NewAccountAsync();
        await DbAsync(async db => { (await db.BillingAccounts.FirstAsync(x => x.Id == noDate.AccountId)).TrialEndsAtUtc = DateTime.UtcNow.AddDays(10); await db.SaveChangesAsync(); });
        await SeedRowAsync(noDate.AccountId, "WhatsApp", null, trial: true);
        (await RefusalAsync(await DisableAsync(admin, noDate.AccountId, "WhatsApp", "PeriodEnd", "по окончании"), HttpStatusCode.Conflict))
            .Should().Be("У пробного периода WhatsApp нет даты окончания — отключите сейчас");
        Line(await CardAsync(noDate.AccountId, admin), "WhatsApp").GetProperty("disablePeriodEndConfirmText").ValueKind.Should().Be(JsonValueKind.Null, "the card does not offer a period-end disable it would refuse");
        await Expect(await DisableAsync(admin, noDate.AccountId, "WhatsApp", "Now", "сейчас"), HttpStatusCode.OK);
    }

    [Fact, TestCase("CY43-53")]
    public async Task Disable_Refusals_And_Validation()
    {
        await PrepareCatalogAsync();
        var admin = await AdminAsync();
        var a = await NewAccountAsync();

        (await RefusalAsync(await DisableAsync(admin, a.AccountId, "Max", "Now"), HttpStatusCode.Conflict)).Should().Be("Нечего отключать: MAX у аккаунта не подключён");
        await SeedRowAsync(a.AccountId, "Max", NoonIn(-5));
        (await RefusalAsync(await DisableAsync(admin, a.AccountId, "Max", "Now"), HttpStatusCode.Conflict)).Should().Be("Нечего отключать: оплата MAX уже закончилась");
        Line(await CardAsync(a.AccountId, admin), "Max").GetProperty("canDisable").GetBoolean().Should().BeFalse();

        await SeedRowAsync(a.AccountId, "WhatsApp", NoonIn(30));
        foreach (var comment in new string?[] { null, "", "   " })
            (await RefusalAsync(await DisableAsync(admin, a.AccountId, "WhatsApp", "Now", comment), HttpStatusCode.BadRequest)).Should().Be("Укажите причину отключения");
        (await RefusalAsync(await DisableAsync(admin, a.AccountId, "WhatsApp", "Now", new string('я', 501)), HttpStatusCode.BadRequest)).Should().Be("Причина — не длиннее 500 символов");
        (await DisableAsync(admin, a.AccountId, "WhatsApp", "Tomorrow", "x")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "an unknown moment is not a disable");
        (await RowAsync(a.AccountId, "WhatsApp"))!.EndsAtUtc.Should().BeNull("every refusal above changed nothing");
        (await DbAsync(db => db.ChannelOptionChangeLogs.CountAsync(l => l.BillingAccountId == a.AccountId && l.Source == 5))).Should().Be(0);
    }
}
