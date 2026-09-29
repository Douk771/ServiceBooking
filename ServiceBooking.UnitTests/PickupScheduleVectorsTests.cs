using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §449–§450 — the reference vectors <c>contracts/cycle24/pickup-schedule-vectors.json</c> against the pure schedule and the
/// pure acceptance rule: for every case the snapshot, the pickup settings and the gate input are built from the file (defaults + the case's own
/// fields) and EVERY field of <c>expect</c> is compared. A change of the rule is a change of the file, in the same commit.
/// </summary>
public class PickupScheduleVectorsTests
{
    public static IEnumerable<object[]> CaseIds()
    {
        using var doc = ContractFiles.Load("cycle24", "pickup-schedule-vectors.json");
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
            yield return [c.GetProperty("id").GetString()!];
    }

    [Fact]
    public void FileHasTheEighteenAgreedCases()
    {
        using var doc = ContractFiles.Load("cycle24", "pickup-schedule-vectors.json");
        doc.RootElement.GetProperty("cases").GetArrayLength().Should().Be(18);
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void Case_MatchesEveryExpectedField(string id)
    {
        using var doc = ContractFiles.Load("cycle24", "pickup-schedule-vectors.json");
        var defaults = doc.RootElement.GetProperty("defaults");
        var c = doc.RootElement.GetProperty("cases").EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
        JsonElement Field(string name) => c.TryGetProperty(name, out var v) ? v : defaults.GetProperty(name);

        var now = Utc(c.GetProperty("nowUtc").GetString()!);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(defaults.GetProperty("timeZoneId").GetString()!);
        var settingsJson = Field("settings");
        var settings = new ShopSettings
        {
            AsapEnabled = settingsJson.GetProperty("asapEnabled").GetBoolean(),
            ScheduledEnabled = settingsJson.GetProperty("scheduledEnabled").GetBoolean(),
            SlotStepMinutes = settingsJson.GetProperty("slotStepMinutes").GetInt32(),
            PreorderDays = settingsJson.GetProperty("preorderDays").GetInt32(),
            MinPrepMinutes = settingsJson.GetProperty("minPrepMinutes").GetInt32(),
        };
        var acceptance = Field("acceptance");
        settings.OrdersStopped = acceptance.GetProperty("stopped").GetBoolean();
        settings.PausedUntilUtc = acceptance.GetProperty("pausedUntilUtc") is { ValueKind: JsonValueKind.String } p ? Utc(p.GetString()!) : null;

        var hours = Field("workingHours") is { ValueKind: JsonValueKind.Object } wh ? WeeklyOf(wh) : null;
        var special = new Dictionary<DateOnly, SpecialDayHours>();
        foreach (var d in Field("specialDays").EnumerateObject())
        {
            var closed = d.Value.TryGetProperty("isClosed", out var ic) && ic.GetBoolean();
            special[DateOnly.ParseExact(d.Name, "yyyy-MM-dd", CultureInfo.InvariantCulture)] = closed
                ? SpecialDayHours.Closed
                : new SpecialDayHours(false, IntervalsOf(d.Value.GetProperty("intervals")));
        }
        var schedule = new ShopScheduleSnapshot(zone, hours, special);
        var planJson = Field("plan");
        var plan = OrdersPlan.FallbackFree with
        {
            AllowOrders = planJson.GetProperty("allowOrders").GetBoolean(),
            MaxOrdersPerMonth = planJson.GetProperty("maxOrdersPerMonth") is { ValueKind: JsonValueKind.Number } m ? m.GetInt32() : null,
        };
        var company = new Company { Kind = CompanyKind.Orders, IsActive = Field("companyIsActive").GetBoolean() };

        var gate = ShopOrderingGate.Evaluate(new ShopGateInput(company, settings, schedule, plan, Field("ordersThisMonth").GetInt32(), now));
        var expect = c.GetProperty("expect");

        gate.Accepting.Should().Be(expect.GetProperty("accepting").GetBoolean(), id);
        if (expect.TryGetProperty("notAcceptingCode", out var code))
            (gate.Code?.ToString()).Should().Be(code.ValueKind == JsonValueKind.Null ? null : code.GetString(), id + " code");
        if (expect.TryGetProperty("customerText", out var customerText))
            gate.ReasonText.Should().Be(customerText.ValueKind == JsonValueKind.Null ? null : customerText.GetString(), id + " customerText");
        if (expect.TryGetProperty("ownerText", out var ownerText)) gate.OwnerText.Should().Be(ownerText.GetString(), id + " ownerText");
        if (expect.TryGetProperty("acceptanceMode", out var mode)) gate.Acceptance.Mode.ToString().Should().Be(mode.GetString(), id + " mode");

        if (expect.TryGetProperty("openState", out var open))
        {
            gate.OpenState.IsOpen.Should().Be(open.GetProperty("isOpen").GetBoolean(), id + " isOpen");
            gate.OpenState.Text.Should().Be(open.GetProperty("text").GetString(), id + " openState.text");
        }

        if (expect.TryGetProperty("asap", out var asap))
        {
            gate.Asap.Available.Should().Be(asap.GetProperty("available").GetBoolean(), id + " asap.available");
            if (asap.TryGetProperty("readyAtUtc", out var ready)) gate.Asap.ReadyAtUtc.Should().Be(Utc(ready.GetString()!), id + " readyAt");
            if (asap.TryGetProperty("pickupDate", out var pickupDate)) gate.Asap.PickupDate.Should().Be(Date(pickupDate.GetString()!), id + " pickupDate");
            gate.Asap.Text.Should().Be(asap.GetProperty("text").GetString(), id + " asap.text");
        }

        if (expect.TryGetProperty("dates", out var dates))
            gate.Dates.Select(d => d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Should().Equal(dates.EnumerateArray().Select(x => x.GetString()!), id + " dates");
        if (expect.TryGetProperty("datesWithoutSlots", out var without))
            foreach (var w in without.EnumerateObject())
            {
                var date = gate.Dates.Single(d => d.Date == Date(w.Name));
                date.HasSlots.Should().BeFalse(id + " " + w.Name);
                date.ReasonText.Should().Be(w.Value.GetString(), id + " reason " + w.Name);
            }

        if (expect.TryGetProperty("slots", out var slots))
            foreach (var s in slots.EnumerateObject())
            {
                var actual = gate.Schedule.SlotsForDate(Date(s.Name), now, forStaff: false);
                actual.Count.Should().Be(s.Value.GetProperty("count").GetInt32(), id + " slots " + s.Name);
                if (s.Value.TryGetProperty("first", out var first))
                    (actual[0].StartUtc, actual[0].EndUtc).Should().Be((Utc(first[0].GetString()!), Utc(first[1].GetString()!)), id + " first " + s.Name);
                if (s.Value.TryGetProperty("last", out var last))
                    (actual[^1].StartUtc, actual[^1].EndUtc).Should().Be((Utc(last[0].GetString()!), Utc(last[1].GetString()!)), id + " last " + s.Name);
            }

        if (expect.TryGetProperty("pauseEndOfDayUtc", out var pauseEnd))
            gate.Schedule.PauseEndOfDay(now).Should().Be(Utc(pauseEnd.GetString()!), id + " pauseEndOfDay");
    }

    // ── file → model ────────────────────────────────────────────────────────────────────────────────

    private static DateTime Utc(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static DateOnly Date(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static WeeklyHours WeeklyOf(JsonElement obj) => new(obj.EnumerateObject().ToDictionary(
        d => Enum.Parse<DayOfWeek>(d.Name), d => IntervalsOf(d.Value)));

    /// <summary>[["09:00","21:00"], …] with the API's rule: end ≤ start goes through midnight.</summary>
    private static IReadOnlyList<TimeInterval> IntervalsOf(JsonElement array)
    {
        var parsed = ShopScheduleRules.ParseDay(array.EnumerateArray()
            .Select(i => new TimeIntervalText(i[0].GetString(), i[1].GetString())).ToList());
        parsed.Ok.Should().BeTrue(parsed.Error);
        return parsed.Intervals!;
    }
}
