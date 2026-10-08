using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE37.md §37.6 — one vectors file (contracts/cycle37/stay-vectors.json) shared with the dom frontend.</summary>
public class StayVectorsTests
{
    private static JsonElement Root => ContractFiles.Load("cycle37", "stay-vectors.json").RootElement.Clone();

    public static IEnumerable<object[]> Ids(string section) =>
        Root.GetProperty(section).GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("id").GetString()! });

    public static IEnumerable<object[]> MoneyIds() => Ids("money");
    public static IEnumerable<object[]> PriceIds() => Ids("nightPrice");
    public static IEnumerable<object[]> RefundIds() => Ids("refund");
    public static IEnumerable<object[]> StayIds() => Ids("stay");

    private static JsonElement Case(string section, string id) =>
        Root.GetProperty(section).GetProperty("cases").EnumerateArray().First(c => c.GetProperty("id").GetString() == id);

    [Theory, MemberData(nameof(MoneyIds))]
    public void Money(string id)
    {
        var c = Case("money", id);
        var i = c.GetProperty("input");
        var eb = i.GetProperty("extraBeds");
        var result = StayMoney.Quote(new StayMoneyInput(
            i.GetProperty("nightPrices").EnumerateArray().Select(x => x.GetInt32()).ToList(),
            i.GetProperty("capacity").GetInt32(), eb.GetProperty("enabled").GetBoolean(), eb.GetProperty("max").GetInt32(), eb.GetProperty("priceRub").GetInt32(),
            i.GetProperty("dogsForbidden").GetBoolean(), i.GetProperty("hasCot").GetBoolean(), i.GetProperty("dogFeeRub").GetInt32(), i.GetProperty("cotFeeRub").GetInt32(),
            i.GetProperty("prepayPercent").GetInt32(), i.GetProperty("adults").GetInt32(), i.GetProperty("children").GetInt32(), i.GetProperty("dogs").GetInt32(), i.GetProperty("needCot").GetBoolean()));
        var e = c.GetProperty("expected");
        if (e.TryGetProperty("error", out var err))
        {
            result.Error.ToString().Should().Be(err.GetString());
            return;
        }
        result.Error.Should().BeNull();
        result.ExtraBeds.Should().Be(e.GetProperty("extraBeds").GetInt32());
        result.TotalRub.Should().Be(e.GetProperty("totalRub").GetInt32());
        result.PrepayRub.Should().Be(e.GetProperty("prepayRub").GetInt32());
        result.DueAtCheckInRub.Should().Be(e.GetProperty("dueAtCheckInRub").GetInt32());
        result.AverageNightRub.Should().Be(e.GetProperty("averageNightRub").GetInt32());
        result.FirstNightRub.Should().Be(e.GetProperty("firstNightRub").GetInt32());
        var lines = e.GetProperty("lines").EnumerateArray().ToList();
        result.Lines.Should().HaveCount(lines.Count);
        for (var k = 0; k < lines.Count; k++)
        {
            result.Lines[k].Kind.ToString().Should().Be(lines[k].GetProperty("kind").GetString());
            result.Lines[k].AmountRub.Should().Be(lines[k].GetProperty("amountRub").GetInt32());
            result.Lines[k].PrepayEligible.Should().Be(lines[k].GetProperty("prepayEligible").GetBoolean());
            if (lines[k].TryGetProperty("quantity", out var q)) result.Lines[k].Quantity.Should().Be(q.GetInt32());
            if (lines[k].TryGetProperty("unitPriceRub", out var u)) result.Lines[k].UnitPriceRub.Should().Be(u.GetInt32());
            if (lines[k].TryGetProperty("nights", out var n)) result.Lines[k].Nights.Should().Be(n.GetInt32());
        }
    }

    [Theory, MemberData(nameof(PriceIds))]
    public void NightPrice(string id)
    {
        var c = Case("nightPrice", id);
        var h = Root.GetProperty("nightPrice").GetProperty("houses").GetProperty(c.GetProperty("house").GetString()!);
        var mode = Enum.Parse<HousePriceMode>(h.GetProperty("mode").GetString()!);
        var periods = h.GetProperty("periods").EnumerateArray().Select(p => new PricePeriodValue(
            DateOnly.Parse(p.GetProperty("startDate").GetString()!), DateOnly.Parse(p.GetProperty("endDate").GetString()!), p.GetProperty("priceRub").GetInt32())).ToList();
        var price = HousePricing.PriceFor(mode, h.GetProperty("constantPriceRub").GetInt32(), periods, DateOnly.Parse(c.GetProperty("date").GetString()!));
        var expected = c.GetProperty("expectedPriceRub");
        price.Should().Be(expected.ValueKind == JsonValueKind.Null ? null : expected.GetInt32());
    }

    [Theory, MemberData(nameof(RefundIds))]
    public void Refund(string id)
    {
        var c = Case("refund", id);
        var common = Root.GetProperty("refund").GetProperty("common");
        var view = StayRefund.Compute(
            Enum.Parse<StayBookingStatus>(c.GetProperty("status").GetString()!), Enum.Parse<StayCancellationPolicy>(c.GetProperty("policy").GetString()!),
            c.GetProperty("prepayRub").GetInt32(), c.GetProperty("firstNightRub").GetInt32(),
            DateOnly.Parse(common.GetProperty("checkInDate").GetString()!), TimeOnly.Parse(common.GetProperty("checkInTime").GetString()!), common.GetProperty("timeZoneId").GetString()!,
            DateTime.Parse(c.GetProperty("atUtc").GetString()!, null, System.Globalization.DateTimeStyles.AdjustToUniversal), c.GetProperty("cancelledBy").GetString() == "Owner");
        var e = c.GetProperty("expected");
        view.Kind.ToString().Should().Be(e.GetProperty("kind").GetString());
        view.RefundAtLeastRub.Should().Be(e.GetProperty("refundAtLeastRub").GetInt32());
        view.MaxDeductionRub.Should().Be(e.GetProperty("maxDeductionRub").GetInt32());
        view.Text.Should().NotBeNullOrWhiteSpace();
    }

    [Theory, MemberData(nameof(StayIds))]
    public void Stay(string id)
    {
        var section = Root.GetProperty("stay");
        var baseObj = section.GetProperty("base");
        var c = Case("stay", id);
        var s = baseObj.GetProperty("settings");
        bool Flag(string n, bool d) => c.TryGetProperty("settings", out var o) && o.TryGetProperty(n, out var v) ? v.GetBoolean() : s.GetProperty(n).GetBoolean();
        var settings = new StayRulesSettings(s.GetProperty("minNights").GetInt32(), s.GetProperty("maxNights").GetInt32(), s.GetProperty("horizonDays").GetInt32(),
            Flag("allowGapFill", false), Flag("allowSameDayCheckIn", false));
        var occ = (c.TryGetProperty("occupancies", out var o2) ? o2 : baseObj.GetProperty("occupancies")).EnumerateArray().Select(p => new OccupiedPeriod(
            DateOnly.Parse(p.GetProperty("startDate").GetString()!), DateOnly.Parse(p.GetProperty("endDate").GetString()!),
            p.TryGetProperty("holdExpiresAtUtc", out var h) ? DateTime.Parse(h.GetString()!, null, System.Globalization.DateTimeStyles.AdjustToUniversal) : null)).ToList();
        var manual = c.TryGetProperty("manual", out var m) && m.GetBoolean();
        var result = StayRules.CheckStay(DateOnly.Parse(c.GetProperty("checkIn").GetString()!), DateOnly.Parse(c.GetProperty("checkOut").GetString()!),
            DateOnly.Parse(baseObj.GetProperty("today").GetString()!), DateTime.Parse(baseObj.GetProperty("nowUtc").GetString()!, null, System.Globalization.DateTimeStyles.AdjustToUniversal),
            settings, occ, manual);
        (result?.ToString() ?? "Ok").Should().Be(c.GetProperty("expected").GetString());
    }
}
