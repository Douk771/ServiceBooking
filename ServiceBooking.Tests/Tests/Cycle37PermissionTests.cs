using System.Net;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA цикл 37, «Вызов 2»: матрица прав «разрешение × должность» (US-37-08, ARCHITECTURE_CYCLE37.md §37.9). Порядок ответов сервера:
/// не участник компании (в том числе владелец салона/магазина) — 404, неотличимо от несуществующей; участник без права — 403 пустым телом;
/// без токена — 401. Права проверяет сервер на каждый запрос, а не интерфейс.
/// </summary>
public class Cycle37PermissionTests(TestDatabaseFixture fixture) : Cycle37TestBase(fixture)
{
    private enum Who { Owner, Manager, Housekeeper }

    private sealed record Scene(
        StaysCtx Company, HouseCtx House, StaffCtx Manager, StaffCtx Housekeeper, Guid BookingId, int BookingVersion, string StrangerToken,
        string SalonOwnerToken, string ShopOwnerToken);

    private sealed record Route(string Name, StaysPermission Permission, HttpMethod Method, Func<Scene, string> Url, Func<Scene, object?> Body);

    private async Task<Scene> SceneAsync()
    {
        var company = await CreateStaysCompanyAsync();
        var house = await CreateHouseAsync(company, price: 4000);
        var manager = await AddStaffAsync(company, "Manager");
        var housekeeper = await AddStaffAsync(company, "Housekeeper");
        var booked = await BookOkAsync(house.Id, InDays(5), InDays(7));
        var bookingId = await BookingIdAsync(booked.Token);
        await AttachProofOkAsync(booked.Token); // ждёт проверки оплаты — над ним можно действовать
        var card = await StaffCardAsync(company, bookingId);
        var stranger = await RegisterAsync();
        var (salonOwner, _) = await CreateOwnerWithCompanyAsync();
        var shop = await CreateShopAsync();
        return new Scene(company, house, manager, housekeeper, bookingId, card.Version, stranger.Token, salonOwner.Token, shop.OwnerToken);
    }

    private static string Base(Scene s) => $"/api/stays/companies/{s.Company.Id}";

    private static IReadOnlyList<Route> Routes() =>
    [
        // ManageCompany — только владелец
        new("settings", StaysPermission.ManageCompany, HttpMethod.Put, s => Base(s) + "/settings", s => s.Company.Company.Settings),
        new("payment-details", StaysPermission.ManageCompany, HttpMethod.Put, s => Base(s) + "/payment-details", _ => new PaymentDetailsDto(PaymentDetailsText, PaymentPurposeText)),
        new("provider", StaysPermission.ManageCompany, HttpMethod.Put, s => Base(s) + "/provider",
            _ => new ProviderInput(StayProviderStatus.SelfEmployed, "Иванов Иван", ValidPersonInn, null, "г. Новокузнецк")),
        new("slug", StaysPermission.ManageCompany, HttpMethod.Put, s => Base(s) + "/slug", s => new SlugInput(s.Company.Slug)),
        new("notification-settings PUT", StaysPermission.ManageCompany, HttpMethod.Put, s => Base(s) + "/notification-settings",
            _ => new StaysNotificationSettingsInput(true, true, true, false, null, null)),
        // ManageHouses — только владелец
        new("house create", StaysPermission.ManageHouses, HttpMethod.Post, s => Base(s) + "/houses", _ => new HouseCreateInput("Новый", 2)),
        new("house setup", StaysPermission.ManageHouses, HttpMethod.Put, s => Base(s) + $"/houses/{s.House.Id}/setup",
            s => new HouseSetupInput(s.House.House.Name, s.House.Slug, 4, false, 0, 0, false, false)),
        new("house pricing", StaysPermission.ManageHouses, HttpMethod.Put, s => Base(s) + $"/houses/{s.House.Id}/pricing", _ => new HousePricingInput(HousePriceMode.Constant, 4000)),
        new("price periods list", StaysPermission.ManageHouses, HttpMethod.Get, s => Base(s) + $"/houses/{s.House.Id}/price-periods", _ => null),
        new("price period create", StaysPermission.ManageHouses, HttpMethod.Post, s => Base(s) + $"/houses/{s.House.Id}/price-periods", _ => new PricePeriodInput(InDays(100), InDays(110), 5000)),
        new("house registry", StaysPermission.ManageHouses, HttpMethod.Put, s => Base(s) + $"/houses/{s.House.Id}/registry",
            s => new HouseRegistryInput(HouseObjectKind.Residential, null, null, new AttestationInput(true, s.House.House.RegistryNotice.Version))),
        new("house unpublish", StaysPermission.ManageHouses, HttpMethod.Post, s => Base(s) + $"/houses/{s.House.Id}/unpublish", _ => new { }),
        new("house order", StaysPermission.ManageHouses, HttpMethod.Put, s => Base(s) + "/houses/order", s => new IdsOrderInput([s.House.Id])),
        // EditHouseContent — владелец и управляющий
        new("house content", StaysPermission.EditHouseContent, HttpMethod.Put, s => Base(s) + $"/houses/{s.House.Id}/content",
            _ => new HouseContentInput("Описание", [], "Шерегеш", null, null, null)),
        new("house get", StaysPermission.EditHouseContent, HttpMethod.Get, s => Base(s) + $"/houses/{s.House.Id}", _ => null),
        // ViewCabinet — владелец и управляющий
        new("houses list", StaysPermission.ViewCabinet, HttpMethod.Get, s => Base(s) + "/houses", _ => null),
        new("company qr", StaysPermission.ViewCabinet, HttpMethod.Get, s => Base(s) + "/qr", _ => null),
        new("notification-settings GET", StaysPermission.ViewCabinet, HttpMethod.Get, s => Base(s) + "/notification-settings", _ => null),
        // ViewBookings / ManageBookings / ManageBlocks — владелец и управляющий
        new("board", StaysPermission.ViewBookings, HttpMethod.Get, s => Base(s) + "/board", _ => null),
        new("bookings list", StaysPermission.ViewBookings, HttpMethod.Get, s => Base(s) + "/bookings", _ => null),
        new("booking card", StaysPermission.ViewBookings, HttpMethod.Get, s => Base(s) + $"/bookings/{s.BookingId}", _ => null),
        new("blocks create", StaysPermission.ManageBlocks, HttpMethod.Post, s => Base(s) + "/blocks", s => new HouseBlockInput(s.House.Id, InDays(200), InDays(201), HouseBlockKind.Repair, null)),
        new("manual quote", StaysPermission.ManageBookings, HttpMethod.Post, s => Base(s) + "/bookings/quote", s => new StaffStayQuoteInput(s.House.Id, InDays(60), InDays(61), 2, 0, 0, false)),
        new("manual create", StaysPermission.ManageBookings, HttpMethod.Post, s => Base(s) + "/bookings",
            s => new ManualStayBookingInput(s.House.Id, InDays(70), InDays(71), 2, 0, 0, false, "Звонок", null, false, null, null)),
        new("confirm-payment", StaysPermission.ManageBookings, HttpMethod.Post, s => Base(s) + $"/bookings/{s.BookingId}/confirm-payment", _ => new ExpectedVersionInput(9999)),
        new("reject-payment", StaysPermission.ManageBookings, HttpMethod.Post, s => Base(s) + $"/bookings/{s.BookingId}/reject-payment", _ => new ExpectedVersionReasonInput(9999, "нет денег")),
        new("owner cancel", StaysPermission.ManageBookings, HttpMethod.Post, s => Base(s) + $"/bookings/{s.BookingId}/cancel", _ => new ExpectedVersionReasonInput(9999, "причина")),
        // ViewSchedule — все три должности
        new("schedule", StaysPermission.ViewSchedule, HttpMethod.Get, s => Base(s) + "/schedule", _ => null),
    ];

    private static Task<HttpResponseMessage> Send(HttpClient client, Route route, Scene s)
    {
        var request = new HttpRequestMessage(route.Method, route.Url(s));
        if (route.Body(s) is { } body) request.Content = JsonContentFor(body);
        return client.SendAsync(request);
    }

    private static HttpContent JsonContentFor(object body) =>
        new StringContent(JsonSerializer.Serialize(body, JsonHelpers.Options), System.Text.Encoding.UTF8, "application/json");

    [Fact, TestCase("CY37-20")]
    public async Task PermissionMatrix_RoleByPermission()
    {
        var s = await SceneAsync();
        var clients = new Dictionary<Who, HttpClient>
        {
            [Who.Owner] = AuthedClient(s.Company.OwnerToken),
            [Who.Manager] = AuthedClient(s.Manager.Token),
            [Who.Housekeeper] = AuthedClient(s.Housekeeper.Token),
        };
        var failures = new List<string>();

        foreach (var route in Routes())
        {
            foreach (var who in Enum.GetValues<Who>())
            {
                var role = who switch { Who.Owner => StaysMyRole.Owner, Who.Manager => StaysMyRole.Manager, _ => StaysMyRole.Housekeeper };
                // ожидание — из таблицы SPEC US-37-08, записанной здесь независимо от StaysAccess
                var allowed = (route.Permission, who) switch
                {
                    (StaysPermission.ViewSchedule, _) => true,
                    (StaysPermission.ManageCompany or StaysPermission.ManageHouses, Who.Owner) => true,
                    (StaysPermission.ManageCompany or StaysPermission.ManageHouses, _) => false,
                    (_, Who.Housekeeper) => false,
                    _ => true,
                };
                var response = await Send(clients[who], route, s);
                var code = response.StatusCode;
                if (allowed && code is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
                    failures.Add($"{route.Name} / {who}: ожидалось «разрешено», получено {(int)code}");
                if (!allowed)
                {
                    if (code != HttpStatusCode.Forbidden) failures.Add($"{route.Name} / {who}: ожидалось 403, получено {(int)code}");
                    else if ((await response.Content.ReadAsStringAsync()).Length != 0) failures.Add($"{route.Name} / {who}: 403 должен быть с пустым телом");
                }
                _ = role;
            }
        }
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact, TestCase("CY37-21")]
    public async Task NonMembers_Get404_NotAnyOtherCode_AnonymousGets401()
    {
        var s = await SceneAsync();
        var failures = new List<string>();
        foreach (var route in Routes())
        {
            foreach (var (label, token) in new[] { ("посторонний", s.StrangerToken), ("владелец салона", s.SalonOwnerToken), ("владелец магазина", s.ShopOwnerToken) })
            {
                var code = (await Send(AuthedClient(token), route, s)).StatusCode;
                if (code != HttpStatusCode.NotFound) failures.Add($"{route.Name} / {label}: ожидалось 404, получено {(int)code}");
            }
            var anon = (await Send(AnonymousClient(), route, s)).StatusCode;
            if (anon != HttpStatusCode.Unauthorized) failures.Add($"{route.Name} / аноним: ожидалось 401, получено {(int)anon}");
        }
        failures.Should().BeEmpty(string.Join("\n", failures));
    }

    [Fact, TestCase("CY37-22")]
    public async Task OwnerOnlyDestructiveRoutes_ManagerAndHousekeeperGet403_AndNothingChanges()
    {
        var s = await SceneAsync();
        var spare = await CreateHouseAsync(s.Company, publish: false);
        foreach (var token in new[] { s.Manager.Token, s.Housekeeper.Token })
        {
            var c = AuthedClient(token);
            (await c.DeleteAsync(Base(s) + $"/houses/{spare.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await c.PostJsonAsync(Base(s) + $"/houses/{spare.Id}/archive", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await c.PostJsonAsync(Base(s) + $"/houses/{spare.Id}/publish", new HousePublishInput(new AttestationInput(true, spare.House.RegistryNotice.Version))))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        var untouched = await GetHouseAsync(s.Company, spare.Id);
        untouched.IsArchived.Should().BeFalse();
        untouched.IsPublished.Should().BeFalse();

        // владелец архивирует и удаляет (дом без броней и без заверений — удаляется)
        (await AuthedClient(s.Company.OwnerToken).DeleteAsync(Base(s) + $"/houses/{spare.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact, TestCase("CY37-23")]
    public async Task Housekeeper_SeesOnlyScheduleDataInCompanyCard_NoSettingsPaymentOrProvider()
    {
        var s = await SceneAsync();
        var card = await GetCompanyAsync(s.Company, s.Housekeeper.Token);
        card.MyRole.Should().Be(StaysMyRole.Housekeeper);
        card.MyPermissions.Should().BeEquivalentTo([StaysPermission.ViewSchedule], "горничная не получает ни одного права сверх графика");
        card.Settings.Should().BeNull();
        card.PaymentDetails.Should().BeNull();
        card.Provider.Should().BeNull();
        card.AwaitingPaymentCount.Should().BeNull();

        var manager = await GetCompanyAsync(s.Company, s.Manager.Token);
        manager.MyRole.Should().Be(StaysMyRole.Manager);
        manager.PaymentDetails.Should().BeNull("реквизиты и исполнитель — только владельцу (ManageCompany)");
        manager.Provider.Should().BeNull();
        manager.MyPermissions.Should().NotContain([StaysPermission.ManageCompany, StaysPermission.ManageHouses]);

        var owner = await GetCompanyAsync(s.Company);
        owner.MyRole.Should().Be(StaysMyRole.Owner);
        owner.PaymentDetails!.PaymentDetails.Should().Be(PaymentDetailsText);
        owner.MyPermissions.Should().Contain([StaysPermission.ManageCompany, StaysPermission.ManageHouses, StaysPermission.ViewSchedule]);
    }

    [Fact, TestCase("CY37-24")]
    public async Task RemovedStaff_LosesAccessAtOnce_WithTheSameToken()
    {
        var s = await SceneAsync();
        var manager = AuthedClient(s.Manager.Token);
        (await manager.GetAsync(Base(s) + "/board")).StatusCode.Should().Be(HttpStatusCode.OK);

        var removed = await AuthedClient(s.Company.OwnerToken).DeleteAsync($"/api/Companies/{s.Company.Id}/members/{s.Manager.MemberId}");
        removed.IsSuccessStatusCode.Should().BeTrue(await removed.Content.ReadAsStringAsync());

        (await manager.GetAsync(Base(s) + "/board")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await manager.GetAsync(Base(s) + $"/bookings/{s.BookingId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await manager.GetAsync(Base(s) + "/schedule")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact, TestCase("CY37-25")]
    public async Task DemotedManager_BecomesHousekeeper_AndLosesBookingRightsAtOnce()
    {
        var s = await SceneAsync();
        var manager = AuthedClient(s.Manager.Token);
        (await manager.GetAsync(Base(s) + $"/bookings/{s.BookingId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var change = await AuthedClient(s.Company.OwnerToken).PutJsonAsync($"/api/Companies/{s.Company.Id}/members/{s.Manager.MemberId}/position", new { position = "Housekeeper" });
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await manager.GetAsync(Base(s) + $"/bookings/{s.BookingId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.GetAsync(Base(s) + "/schedule")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact, TestCase("CY37-26")]
    public async Task OwnerTermsGate_StaysOwnerRoutes_RequireCurrentTerms()
    {
        // владелец без принятого соглашения не создаёт компанию (400/409/503 строкой — тексты существующие)
        var user = await RegisterAsync();
        var r = await AuthedClient(user.Token).PostJsonAsync("/api/stays/companies",
            new StaysCompanyCreateInput("Без соглашения", Unique("dom-t-"), null, "+79001112233", null, null));
        r.StatusCode.Should().NotBe(HttpStatusCode.Created);
        r.IsSuccessStatusCode.Should().BeFalse();
    }
}
