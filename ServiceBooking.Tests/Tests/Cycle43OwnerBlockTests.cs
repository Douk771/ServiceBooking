using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Цикл 43, US-43-05 и US-43-02 (SPEC.md): блок «Сообщения клиентам в мессенджеры» в «Подписке» владельца четырёх линеек — когда он есть, строки, кнопка «Подключить/Продлить»
/// (выбирает сервер), «Сначала создайте компанию», сумма мессенджеров отдельно от суммы линейки. Тексты — API_CONTRACT_CYCLE43.md §43.31.
/// </summary>
public class Cycle43OwnerBlockTests(TestDatabaseFixture fixture) : Cycle43TestBase(fixture)
{
    private static readonly string?[] AllLines = [null, "Services", "Orders", "Stays", "Baths"];

    private static JsonElement? Block(JsonElement subscription) =>
        subscription.TryGetProperty("messengers", out var block) && block.ValueKind == JsonValueKind.Object ? block : null;

    private async Task<(Acc Account, HttpClient Owner)> OwnerAsync()
    {
        var a = await NewAccountAsync();
        return (a, AuthedClient(a.Owner.Token));
    }

    // ── когда блок есть ──────────────────────────────────────────────────────────────────

    [Fact, TestCase("CY43-70")]
    public async Task Block_IsAbsent_WhenNothingIsSoldOrConnected_AndAppearsWithTheSoldMessengersOnly()
    {
        await SetCatalogAsync("WhatsApp", 300, open: false);
        await SetCatalogAsync("Max", null, open: true);
        var (a, _) = await OwnerAsync();

        foreach (var line in AllLines)
            Block(await OwnerSubscriptionAsync(a, line)).Should().BeNull($"line {line ?? "default"}: nothing is sold and nothing is connected, so no block");

        // MAX is sold: one line, WhatsApp (closed, not connected) is not shown
        await SetCatalogAsync("Max", 490);
        foreach (var line in AllLines)
        {
            var sub = await OwnerSubscriptionAsync(a, line);
            var block = Block(sub)!.Value;
            block.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("transport").GetString()).Should().Equal("Max");
            block.GetProperty("title").GetString().Should().Be("Сообщения клиентам в мессенджеры");
            block.GetProperty("note").GetString().Should().Be("Общие для всех ваших компаний");
            block.GetProperty("conditionsUrl").GetString().Should().Be("/offer-channel");
            block.GetProperty("conditionsLabel").GetString().Should().Be("Условия");
            block.GetProperty("monthlyTotal").GetDecimal().Should().Be(0m);
            block.GetProperty("monthlyTotalText").GetString().Should().Be("Мессенджеры: 0 ₽/мес");
        }

        // WhatsApp is closed but the account HAS a row of it: the row is shown (the owner sees what he paid for)
        await SeedRowAsync(a.AccountId, "WhatsApp", NoonIn(30));
        var withRow = Block(await OwnerSubscriptionAsync(a))!.Value;
        withRow.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("transport").GetString()).Should().Equal("WhatsApp", "Max");
        withRow.GetProperty("monthlyTotal").GetDecimal().Should().Be(300m);
        withRow.GetProperty("monthlyTotalText").GetString().Should().Be("Мессенджеры: 300 ₽/мес");

        // MAX stops being sold (no price) and nothing is connected: it leaves the block, the row of WhatsApp stays
        await SetCatalogAsync("Max", null);
        Block(await OwnerSubscriptionAsync(a))!.Value.GetProperty("lines").EnumerateArray().Select(l => l.GetProperty("transport").GetString()).Should().Equal("WhatsApp");
        await SetCatalogAsync("WhatsApp", 300);
        await SetCatalogAsync("Max", 490);
    }

    [Fact, TestCase("CY43-71")]
    public async Task Lines_ShowStatusPriceAndTheButtonThatTheServerChooses()
    {
        await SetCatalogAsync("WhatsApp", 300);
        await SetCatalogAsync("Max", 490);

        async Task<JsonElement> LineOf(Acc a, string transport, string? line = null) => OwnerLine(await OwnerSubscriptionAsync(a, line), transport);

        // not connected and sold → «Подключить»
        var none = (await OwnerAsync()).Account;
        var notConnected = await LineOf(none, "Max");
        (notConnected.GetProperty("status").GetString(), notConnected.GetProperty("statusText").GetString()).Should().Be(("NotConnected", "Не подключён"));
        (notConnected.GetProperty("action").GetString(), notConnected.GetProperty("actionText").GetString()).Should().Be(("Connect", "Подключить"));
        notConnected.GetProperty("priceText").GetString().Should().Be("490 ₽/мес");
        notConnected.GetProperty("pricePerMonth").GetDecimal().Should().Be(490m);

        // footnotes
        (await LineOf(none, "WhatsApp")).GetProperty("footnote").GetString().Should().Be("Доступ к WhatsApp в России ограничен: сообщения могут не доходить");
        notConnected.GetProperty("footnote").ValueKind.Should().Be(JsonValueKind.Null);

        // ended payment (not trial) → «Продлить»
        var ended = (await OwnerAsync()).Account;
        await SeedRowAsync(ended.AccountId, "Max", NoonIn(-3));
        var expired = await LineOf(ended, "Max");
        (expired.GetProperty("action").GetString(), expired.GetProperty("actionText").GetString()).Should().Be(("Renew", "Продлить"));

        // ended trial → «Подключить»
        var trialEnded = (await OwnerAsync()).Account;
        await SeedRowAsync(trialEnded.AccountId, "Max", NoonIn(-3), trial: true);
        var trialExpired = await LineOf(trialEnded, "Max");
        (trialExpired.GetProperty("action").GetString(), trialExpired.GetProperty("actionText").GetString()).Should().Be(("Connect", "Подключить"));

        // paid, trial, ending, pending → no button
        var states = (await OwnerAsync()).Account;
        await SeedRowAsync(states.AccountId, "Max", NoonIn(30));
        await SeedRowAsync(states.AccountId, "WhatsApp", NoonIn(5), trial: true);
        foreach (var transport in new[] { "Max", "WhatsApp" })
        {
            var l = await LineOf(states, transport);
            l.GetProperty("action").ValueKind.Should().Be(JsonValueKind.Null, $"{transport} {l.GetProperty("status").GetString()}: nothing to press");
            l.GetProperty("actionText").ValueKind.Should().Be(JsonValueKind.Null);
        }
        var endingAccount = (await OwnerAsync()).Account;
        await SeedRowAsync(endingAccount.AccountId, "Max", NoonIn(30), endsAt: NoonIn(30));
        (await LineOf(endingAccount, "Max")).GetProperty("action").ValueKind.Should().Be(JsonValueKind.Null);
        var pending = (await OwnerAsync()).Account;
        await RequestChannelAsync(pending, "Max");
        var pendingLine = await LineOf(pending, "Max");
        pendingLine.GetProperty("status").GetString().Should().Be("PendingPayment");
        pendingLine.GetProperty("action").ValueKind.Should().Be(JsonValueKind.Null);

        // the price is shown only for what is sold or paid: an ended row of a messenger that is no longer sold shows no price and no button
        await SetCatalogAsync("Max", null);
        var unsold = await LineOf(ended, "Max");
        unsold.GetProperty("status").GetString().Should().Be("Expired");
        unsold.GetProperty("priceText").ValueKind.Should().Be(JsonValueKind.Null);
        unsold.GetProperty("action").ValueKind.Should().Be(JsonValueKind.Null, "it is not sold, so there is nothing to connect");
        var paidUnsold = await LineOf(states, "Max");
        paidUnsold.GetProperty("status").GetString().Should().Be("Paid", "stopping the sales does not touch what is paid");
        paidUnsold.GetProperty("monthlyPrice").GetDecimal().Should().Be(0m, "no price in the catalog: nothing to count");
        paidUnsold.GetProperty("action").ValueKind.Should().Be(JsonValueKind.Null);
        await SetCatalogAsync("Max", 490);

        // the platform stop-cock of the customer messaging: no buttons at all
        var admin = await AdminAsync();
        await SetPlatformFieldAsync(admin, "customerMessagingEnabled", false);
        try
        {
            (await LineOf(none, "Max")).GetProperty("action").ValueKind.Should().Be(JsonValueKind.Null, "platform messaging is off");
            (await LineOf(ended, "Max")).GetProperty("action").ValueKind.Should().Be(JsonValueKind.Null);
        }
        finally { await SetPlatformFieldAsync(admin, "customerMessagingEnabled", true); }
        (await LineOf(none, "Max")).GetProperty("action").GetString().Should().Be("Connect");
    }

    [Fact, TestCase("CY43-72")]
    public async Task TargetCompany_IsTheFirstCompanyOfTheLine_OrTheTextToCreateOne()
    {
        await SetCatalogAsync("Max", 490);

        // «Записи» account: first Services company by creation date
        var (a, _) = await OwnerAsync();
        var second = await CreateCompanyAsync(a.Owner.Token);
        a.Company.Id.Should().NotBe(second.Id);
        var services = OwnerLine(await OwnerSubscriptionAsync(a, "Services"), "Max");
        services.GetProperty("targetCompanyId").GetGuid().Should().Be(a.Company.Id, "the earliest company of the line");
        services.GetProperty("noCompanyText").ValueKind.Should().Be(JsonValueKind.Null);
        OwnerLine(await OwnerSubscriptionAsync(a), "Max").GetProperty("targetCompanyId").GetGuid().Should().Be(a.Company.Id, "no line = «Записи»");

        // other lines: this account has no company of such a kind
        foreach (var line in new[] { "Orders", "Stays", "Baths" })
        {
            var l = OwnerLine(await OwnerSubscriptionAsync(a, line), "Max");
            l.GetProperty("action").GetString().Should().Be("Connect", line);
            l.GetProperty("targetCompanyId").ValueKind.Should().Be(JsonValueKind.Null, line);
            l.GetProperty("noCompanyText").GetString().Should().Be("Сначала создайте компанию", line);
        }

        // an account with a «Бани» company: the Baths line leads to it, the «Записи» line has none
        var baths = await NewAccountWithoutServicesPlanAsync(); // its only company is of the Baths kind
        var onBaths = OwnerLine(await OwnerSubscriptionAsync(baths, "Baths"), "Max");
        onBaths.GetProperty("targetCompanyId").GetGuid().Should().Be(baths.Company.Id);
        onBaths.GetProperty("noCompanyText").ValueKind.Should().Be(JsonValueKind.Null);
        var onServices = OwnerLine(await OwnerSubscriptionAsync(baths, "Services"), "Max");
        onServices.GetProperty("targetCompanyId").ValueKind.Should().Be(JsonValueKind.Null);
        onServices.GetProperty("noCompanyText").GetString().Should().Be("Сначала создайте компанию");

        // no button — no «create a company» text either
        await SeedRowAsync(baths.AccountId, "Max", NoonIn(30));
        var paid = OwnerLine(await OwnerSubscriptionAsync(baths, "Services"), "Max");
        paid.GetProperty("action").ValueKind.Should().Be(JsonValueKind.Null);
        paid.GetProperty("noCompanyText").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact, TestCase("CY43-73")]
    public async Task OtherOptions_StayInTheLineTotal_MessengersAreOutsideItAndOutsideOptions()
    {
        await SetCatalogAsync("Max", 490);
        var admin = await AdminAsync();

        // a non-messenger option priced by the tariff rules as before (nothing of that kind is sold in the product, so the test creates one)
        var code = "qa43.extra." + Guid.NewGuid().ToString("N")[..8];
        var created = await admin.PostAsJsonAsync("/api/admin/options", new
        {
            code, name = "Опция теста 43", description = (string?)null, kind = "Toggle", capabilityKey = (string?)null, pricePerMonth = 100, unitName = (string?)null, maxQuantity = (int?)null,
            isPublic = false, isActive = true, sortOrder = 99,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var optionId = (await J(created)).GetProperty("id").GetGuid();

        var a = await NewAccountAsync();
        await DbAsync(async db =>
        {
            db.PlanOptionRules.Add(new PlanOptionRule { Id = Guid.NewGuid(), PlanConfigId = a.PlanId, OptionId = optionId, Availability = OptionAvailability.Extra });
            db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption { Id = Guid.NewGuid(), BillingAccountId = a.AccountId, OptionId = optionId, Quantity = 1, ActivatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });
        await SeedRowAsync(a.AccountId, "Max", NoonIn(30));

        var card = await CardAsync(a.AccountId, admin);
        card.GetProperty("options").EnumerateArray().Should().ContainSingle(o => o.GetRawText().Contains(optionId.ToString()), "the ordinary option stays in options[]");
        card.GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(1000m + 100m + 490m, "tariff + ordinary options by the tariff rules + messengers by the one rule");
        (await ListTotalAsync(a, admin)).Should().Be(1590m);
        (await ProfileTotalAsync(a)).Should().Be(1590m);

        foreach (var line in new string?[] { null, "Services" })
        {
            var sub = await OwnerSubscriptionAsync(a, line);
            sub.GetProperty("options").EnumerateArray().Should().ContainSingle(o => o.GetRawText().Contains(optionId.ToString()));
            sub.GetProperty("options").EnumerateArray().Sum(o => o.GetProperty("pricePerMonth").GetDecimal()).Should().Be(100m);
            sub.GetProperty("totalMonthlyPrice").GetDecimal().Should().Be(1100m, "cycle7 invariant: total = tariff + sum of options[]; messengers are shown apart");
            sub.GetProperty("messengers").GetProperty("monthlyTotal").GetDecimal().Should().Be(490m);
        }
    }
}
