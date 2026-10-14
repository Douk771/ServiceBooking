using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE42.md §42.7–§42.9 — ONE vectors file contracts/cycle42/bani-vectors.json, shared with the bani frontend.</summary>
public class BaniVectorsTests
{
    private static JsonElement Root => ContractFiles.Load("cycle42", "bani-vectors.json").RootElement.Clone();

    private static IEnumerable<object[]> Ids(string section, string array = "cases") =>
        Root.GetProperty(section).GetProperty(array).EnumerateArray().Select(c => new object[] { c.GetProperty("id").GetString()! });

    private static JsonElement Case(string section, string array, string id) =>
        Root.GetProperty(section).GetProperty(array).EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);

    private static DateTime T(JsonElement e, string name) => DateTime.ParseExact(e.GetProperty(name).GetString()!, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    public static IEnumerable<object[]> MomentIds() => Ids("sessionReminder", "moment");
    public static IEnumerable<object[]> DecideIds() => Ids("sessionReminder", "decide");
    public static IEnumerable<object[]> GuestIds() => Ids("guests");
    public static IEnumerable<object[]> RestrictedIds() => Ids("restrictedItems");
    public static IEnumerable<object[]> OwnerTextIds() => Ids("ownerText");
    public static IEnumerable<object[]> GateIds() => Ids("gate");

    [Theory, MemberData(nameof(MomentIds))]
    public void ReminderMoment_MatchesVector(string id)
    {
        var c = Case("sessionReminder", "moment", id);
        SessionReminderPolicy.Moment(T(c, "start"), c.GetProperty("hoursBefore").GetInt32()).Should().Be(T(c, "expected"));
    }

    [Theory, MemberData(nameof(DecideIds))]
    public void ReminderDecide_MatchesVector(string id)
    {
        var c = Case("sessionReminder", "decide", id);
        var grace = Root.GetProperty("sessionReminder").GetProperty("graceMinutes").GetInt32();
        SessionReminderPolicy.Decide(T(c, "moment"), T(c, "created"), T(c, "now"), T(c, "start"), grace)
            .ToString().Should().Be(c.GetProperty("expected").GetString());
    }

    [Fact]
    public void ReminderGrace_DefaultMatchesFile() =>
        SessionReminderPolicy.DefaultGraceMinutes.Should().Be(Root.GetProperty("sessionReminder").GetProperty("graceMinutes").GetInt32());

    [Theory, MemberData(nameof(GuestIds))]
    public void GuestsCount_MatchesVector(string id)
    {
        var c = Case("guests", "cases", id);
        int? Num(string n) => c.GetProperty(n).ValueKind == JsonValueKind.Null ? null : c.GetProperty(n).GetInt32();
        var r = GuestsCountRules.Validate(Num("guestsCount"), Num("capacity"));
        r.Ok.Should().Be(c.GetProperty("ok").GetBoolean());
        if (r.Ok) r.Stored.Should().Be(Num("stored"));
        else r.Error.Should().Be(c.GetProperty("error").GetString());
    }

    [Fact]
    public void RestrictedStems_MatchFile() =>
        RestrictedItemFilter.Stems.Should().Equal(Root.GetProperty("restrictedItems").GetProperty("stems").EnumerateArray().Select(e => e.GetString()!));

    [Theory, MemberData(nameof(RestrictedIds))]
    public void RestrictedItems_MatchVector(string id)
    {
        var c = Case("restrictedItems", "cases", id);
        RestrictedItemFilter.Match(c.GetProperty("text").GetString()).Should().Equal(c.GetProperty("markers").EnumerateArray().Select(e => e.GetString()!));
    }

    [Theory, MemberData(nameof(OwnerTextIds))]
    public void OwnerText_MatchesVector(string id)
    {
        var c = Case("ownerText", "cases", id);
        var r = OwnerTextChecks.Check(c.GetProperty("text").GetString());
        r.Errors.Should().Equal(c.GetProperty("errors").EnumerateArray().Select(e => e.GetString()!));
        r.Warnings.Should().Equal(c.GetProperty("warnings").EnumerateArray().Select(e => e.GetString()!));
    }

    [Fact]
    public void OwnerText_TextsMatchFile()
    {
        var o = Root.GetProperty("ownerText");
        OwnerTextChecks.ErrorText.Should().Be(o.GetProperty("errorText").GetString());
        OwnerTextChecks.WarningTexts.Should().BeEquivalentTo(o.GetProperty("warningTexts").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!));
    }

    [Theory, MemberData(nameof(GateIds))]
    public void Gate_MatchesVector(string id)
    {
        var c = Case("gate", "cases", id);
        int? Max() => c.GetProperty("max").ValueKind == JsonValueKind.Null ? null : c.GetProperty("max").GetInt32();
        var provider = c.GetProperty("providerComplete").GetBoolean()
            ? new StayProviderFacts(ServiceBooking.Core.Enums.StayProviderStatus.SelfEmployed, "Иванов И. И.", "123456789012", null, "г. Москва")
            : new StayProviderFacts(null, null, null, null, null);
        var unit = Enum.Parse<GateUnit>(c.GetProperty("unit").GetString()!);
        var r = StaysBookingGate.Evaluate(c.GetProperty("companyActive").GetBoolean(), c.GetProperty("hasActivePlan").GetBoolean(),
            c.GetProperty("published").GetInt32(), Max(), c.GetProperty("prepayPercent").GetInt32(),
            c.GetProperty("hasPaymentDetails").GetBoolean() ? "карта" : null, provider, unit);
        r.Accepting.Should().Be(c.GetProperty("accepting").GetBoolean());
        r.ReasonCode?.ToString().Should().Be(c.GetProperty("reasonCode").GetString());
        if (c.TryGetProperty("reasonText", out var text)) r.ReasonText.Should().Be(text.GetString());
    }
}
