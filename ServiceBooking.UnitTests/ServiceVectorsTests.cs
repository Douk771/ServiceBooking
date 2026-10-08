using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.3–§39.8, API_CONTRACT_CYCLE39.md §39.38 — ONE vectors file (contracts/cycle39/service-vectors.json) shared with the dom frontend.
/// Every section of it is exercised here by the pure classes of the services vertical; a rule changes together with the vectors in the same commit.
/// </summary>
public class ServiceVectorsTests
{
    private const string Tz = "Asia/Novokuznetsk";

    private static JsonElement Root => ContractFiles.Load("cycle39", "service-vectors.json").RootElement.Clone();

    private static IEnumerable<object[]> Ids(string section, string array = "cases") =>
        Root.GetProperty(section).GetProperty(array).EnumerateArray().Select(c => new object[] { c.GetProperty("id").GetString()! });

    public static IEnumerable<object[]> BusinessDayIds() => Ids("businessDay");
    public static IEnumerable<object[]> ToUtcIds() => Ids("businessDay", "toUtc");
    public static IEnumerable<object[]> WindowIds() => Ids("windows");
    public static IEnumerable<object[]> RuleIds() => Ids("priceRules");
    public static IEnumerable<object[]> RuleLabelIds() => Ids("priceRules", "labels");
    public static IEnumerable<object[]> PriceIds() => Ids("price");
    public static IEnumerable<object[]> MoneyIds() => Ids("money");
    public static IEnumerable<object[]> StartIds() => Ids("starts");
    public static IEnumerable<object[]> OverlapIds() => Ids("overlap");
    public static IEnumerable<object[]> RefundIds() => Ids("refund");
    public static IEnumerable<object[]> RefundConfigIds() =>
        Root.GetProperty("refund").GetProperty("config").GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("id").GetString()! });
    public static IEnumerable<object[]> FormatIds() => Ids("format");
    public static IEnumerable<object[]> DateLabelIds() => Ids("format", "businessDateLabel");

    private static JsonElement Case(JsonElement array, string id) => array.EnumerateArray().First(c => c.GetProperty("id").GetString() == id);

    private static JsonElement Cases(string section, string array = "cases") => Root.GetProperty(section).GetProperty(array);

    private static DateTime Utc(string iso) => DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static DateOnly Date(string iso) => DateOnly.Parse(iso, CultureInfo.InvariantCulture);

    // ── businessDay ──

    [Fact]
    public void Common_section_matches_the_code_defaults()
    {
        Root.GetProperty("common").GetProperty("timeZoneId").GetString().Should().Be(Tz);
        Root.GetProperty("common").GetProperty("businessDayStartMinute").GetInt32().Should().Be(BusinessClock.DefaultBusinessDayStartMinute);
    }

    [Theory, MemberData(nameof(BusinessDayIds))]
    public void BusinessDay(string id)
    {
        var c = Case(Cases("businessDay"), id);
        var (date, minute) = BusinessClock.BusinessDateOf(Tz, Utc(c.GetProperty("utc").GetString()!));
        date.Should().Be(Date(c.GetProperty("expected").GetProperty("businessDate").GetString()!));
        minute.Should().Be(c.GetProperty("expected").GetProperty("minute").GetInt32());
    }

    [Theory, MemberData(nameof(ToUtcIds))]
    public void BusinessDay_ToUtc(string id)
    {
        var c = Case(Cases("businessDay", "toUtc"), id);
        BusinessClock.ToUtc(Tz, Date(c.GetProperty("businessDate").GetString()!), c.GetProperty("minute").GetInt32())
            .Should().Be(Utc(c.GetProperty("expectedUtc").GetString()!));
    }

    [Fact]
    public void BusinessDay_is_the_inverse_of_ToUtc_for_every_minute_of_a_week()
    {
        var start = new DateOnly(2026, 12, 28);
        for (var d = 0; d < 7; d++)
            for (var minute = 360; minute < 1800; minute += 30)
            {
                var date = start.AddDays(d);
                var (back, backMinute) = BusinessClock.BusinessDateOf(Tz, BusinessClock.ToUtc(Tz, date, minute));
                (back, backMinute).Should().Be((date, minute));
            }
    }

    [Fact]
    public void Today_of_a_service_is_still_friday_at_01_00_on_saturday()
    {
        BusinessClock.TodayBusinessDate(Tz, Utc("2027-01-15T18:00:00Z")).Should().Be(new DateOnly(2027, 1, 15));
        BusinessClock.TodayBusinessDate(Tz, Utc("2027-01-15T23:00:00Z")).Should().Be(new DateOnly(2027, 1, 16));
        BusinessClock.DayOfWeekIso(new DateOnly(2027, 1, 15)).Should().Be(5);
        BusinessClock.DayOfWeekIso(new DateOnly(2027, 1, 17)).Should().Be(7);
    }

    // ── windows ──

    [Theory, MemberData(nameof(WindowIds))]
    public void Windows(string id)
    {
        var c = Case(Cases("windows"), id);
        var windows = c.GetProperty("windows").EnumerateArray().Select(w => new WindowSpec(w.GetProperty("startMinute").GetInt32(), w.GetProperty("endMinute").GetInt32())).ToList();
        var check = ServiceScheduleRules.Validate(windows);
        var expected = c.GetProperty("expected");
        check.Ok.Should().Be(expected.GetProperty("ok").GetBoolean());
        if (!check.Ok) check.Error.ToString().Should().Be(expected.GetProperty("error").GetString());
        else ServiceScheduleRules.Sorted(windows).Select(w => ServiceTimeFormat.Window(w.StartMinute, w.EndMinute))
            .Should().Equal(expected.GetProperty("labels").EnumerateArray().Select(l => l.GetString()));
    }

    [Fact]
    public void Window_errors_have_the_texts_of_the_contract()
    {
        ServiceScheduleRules.Message(ServiceScheduleRules.Validate([new(725, 960)]), "пт").Should().Be("Время — с шагом 30 минут");
        ServiceScheduleRules.Message(ServiceScheduleRules.Validate([new(960, 960)]), "пт").Should().Be("Начало окна должно быть раньше конца");
        ServiceScheduleRules.Message(ServiceScheduleRules.Validate([new(300, 600)]), "пт").Should().Be("Окно должно уложиться с 06:00 до 06:00 следующего дня");
        ServiceScheduleRules.Message(ServiceScheduleRules.Validate([new(360, 480), new(540, 600), new(720, 780), new(900, 960)]), "пт").Should().Be("Не больше трёх окон в день");
        ServiceScheduleRules.Message(ServiceScheduleRules.Validate([new(720, 960), new(900, 1200)]), "пт").Should().Be("Окна 12:00 – 16:00 и 15:00 – 20:00 (пт) пересекаются");
    }

    // ── price rules ──

    [Theory, MemberData(nameof(RuleIds))]
    public void PriceRules(string id)
    {
        var root = Root.GetProperty("priceRules");
        var existing = root.GetProperty("existing").EnumerateArray()
            .Select(r => (Id: Guid.Parse("00000000-0000-0000-0000-000000000001"), Name: r.GetProperty("id").GetString()!, Rule: ToRule(r))).ToList();
        var c = Case(root.GetProperty("cases"), id);
        var check = ServicePriceRules.Validate(ToRule(c.GetProperty("rule")), existing.Select(e => (e.Id, e.Rule)));
        var expected = c.GetProperty("expected");
        check.Ok.Should().Be(expected.GetProperty("ok").GetBoolean());
        if (check.Ok) return;
        check.Error.ToString().Should().Be(expected.GetProperty("error").GetString());
        if (expected.TryGetProperty("conflictingRuleId", out var conflict)) check.ConflictingRuleId.Should().Be(existing.Single(e => e.Name == conflict.GetString()).Id);
    }

    [Fact]
    public void A_rule_can_be_updated_without_conflicting_with_itself()
    {
        var id = Guid.NewGuid();
        var rule = new PriceRuleSpec(16, 18, 26, 2000);
        ServicePriceRules.Validate(rule with { PriceRub = 2500 }, [(id, rule)], ignoreId: id).Ok.Should().BeTrue();
        ServicePriceRules.Validate(rule with { PriceRub = 2500 }, [(id, rule)]).Error.Should().Be(PriceRuleError.PriceRuleOverlap);
    }

    [Fact]
    public void A_price_outside_1_to_100000_is_refused()
    {
        ServicePriceRules.Validate(new(16, 10, 12, 0), []).Error.Should().Be(PriceRuleError.PriceOutOfRange);
        ServicePriceRules.Validate(new(16, 10, 12, 100_001), []).Error.Should().Be(PriceRuleError.PriceOutOfRange);
        ServicePriceRules.Validate(new(16, 10, 12, 100_000), []).Ok.Should().BeTrue();
    }

    [Theory, MemberData(nameof(RuleLabelIds))]
    public void PriceRule_labels(string id)
    {
        var c = Case(Cases("priceRules", "labels"), id);
        var r = c.GetProperty("rule");
        var mask = r.GetProperty("daysMask").GetInt32();
        ServiceTimeFormat.RuleGuest(mask, r.GetProperty("fromHour").GetInt32(), r.GetProperty("toHour").GetInt32()).Should().Be(c.GetProperty("expectedGuest").GetString());
        ServiceTimeFormat.RuleStaff(mask, r.GetProperty("fromHour").GetInt32(), r.GetProperty("toHour").GetInt32()).Should().Be(c.GetProperty("expectedStaff").GetString());
    }

    private static PriceRuleSpec ToRule(JsonElement r) =>
        new(r.GetProperty("daysMask").GetInt32(), r.GetProperty("fromHour").GetInt32(), r.GetProperty("toHour").GetInt32(), r.GetProperty("priceRub").GetInt32());

    private static List<PriceRuleSpec> RuleSet(string name) =>
        Root.GetProperty("price").GetProperty("ruleSets").GetProperty(name).EnumerateArray().Select(ToRule).ToList();

    // ── price ──

    [Theory, MemberData(nameof(PriceIds))]
    public void HourPrices(string id)
    {
        var c = Case(Cases("price"), id);
        var prices = ServicePricing.HourPrices(RuleSet(c.GetProperty("ruleSet").GetString()!), Date(c.GetProperty("businessDate").GetString()!),
            c.GetProperty("startMinute").GetInt32(), c.GetProperty("hours").GetInt32());
        prices.Should().Equal(c.GetProperty("expected").EnumerateArray().Select(p => p.ValueKind == JsonValueKind.Null ? (int?)null : p.GetInt32()));
    }

    // ── money ──

    [Theory, MemberData(nameof(MoneyIds))]
    public void Money(string id)
    {
        var c = Case(Cases("money"), id);
        var input = c.GetProperty("input");
        var prepay = input.GetProperty("prepayPercent");
        var result = ServiceMoney.Quote(
            input.GetProperty("hourPrices").EnumerateArray().Select(p => p.GetInt32()).ToList(),
            input.GetProperty("items").EnumerateArray().Select(i => new ServiceItemQuantity(i.GetProperty("unitPriceRub").GetInt32(), i.GetProperty("quantity").GetInt32())).ToList(),
            prepay.ValueKind == JsonValueKind.Null ? null : prepay.GetInt32());
        var e = c.GetProperty("expected");
        result.ServiceAmountRub.Should().Be(e.GetProperty("serviceAmountRub").GetInt32());
        result.ItemsAmountRub.Should().Be(e.GetProperty("itemsAmountRub").GetInt32());
        result.TotalRub.Should().Be(e.GetProperty("totalRub").GetInt32());
        result.PrepayRub.Should().Be(e.GetProperty("prepayRub").GetInt32());
        result.DueOnSiteRub.Should().Be(e.GetProperty("dueOnSiteRub").GetInt32());
        result.FirstHourRub.Should().Be(e.GetProperty("firstHourRub").GetInt32());
    }

    // ── starts ──

    private static ServiceSlotInput StartInput(JsonElement root, JsonElement c)
    {
        var service = c.TryGetProperty("service", out var s) ? s : root.GetProperty("baseService");
        var rules = c.TryGetProperty("ruleSet", out var ruleSet)
            ? RuleSet(ruleSet.GetString()!)
            : c.GetProperty("rules").EnumerateArray().Select(ToRule).ToList();
        var windowsNode = c.GetProperty("windows");
        if (windowsNode.ValueKind == JsonValueKind.String) windowsNode = root.GetProperty(windowsNode.GetString()!);
        var windows = windowsNode.EnumerateArray().Select(w => new WindowSpec(w.GetProperty("startMinute").GetInt32(), w.GetProperty("endMinute").GetInt32())).ToList();
        var occupied = c.GetProperty("occupied").EnumerateArray()
            .Select(o => new OccupiedSpec(Utc(o.GetProperty("startUtc").GetString()!), Utc(o.GetProperty("occupiedUntilUtc").GetString()!))).ToList();
        StayRangeSpec? stay = c.TryGetProperty("stayRange", out var sr)
            ? new StayRangeSpec(Utc(sr.GetProperty("fromUtc").GetString()!), Utc(sr.GetProperty("toUtc").GetString()!)) : null;
        return new ServiceSlotInput(
            Date(c.GetProperty("businessDate").GetString()!), Tz, Utc(c.GetProperty("nowUtc").GetString()!), c.GetProperty("horizonDays").GetInt32(),
            new ServiceSlotRules(service.GetProperty("minHours").GetInt32(), service.GetProperty("maxHours").GetInt32(), service.GetProperty("stepMinutes").GetInt32(),
                service.GetProperty("bufferMinutes").GetInt32(), service.GetProperty("minLeadMinutes").GetInt32()),
            windows, rules, occupied, stay);
    }

    [Theory, MemberData(nameof(StartIds))]
    public void Starts(string id)
    {
        var root = Root.GetProperty("starts");
        var c = Case(root.GetProperty("cases"), id);
        var input = StartInput(root, c);

        if (c.TryGetProperty("checks", out var checks))
        {
            foreach (var check in checks.EnumerateArray())
            {
                var start = check.GetProperty("startMinute").GetInt32();
                var hours = check.GetProperty("hours").GetInt32();
                var allowed = check.GetProperty("allowed").GetBoolean();
                ServiceSlotCalculator.IsStartAllowed(input, start, hours).Should().Be(allowed, $"{id}: {start} × {hours} ч");
                (ServiceSlotCalculator.Diagnose(input, start, hours) is null).Should().Be(allowed);
            }
            return;
        }

        var result = ServiceSlotCalculator.Calculate(input);
        var expected = c.GetProperty("expected");
        var reason = expected.GetProperty("reason");
        (result.Reason?.ToString()).Should().Be(reason.ValueKind == JsonValueKind.Null ? null : reason.GetString());
        result.Starts.Select(s => (s.StartMinute, s.MaxHours)).Should().Equal(
            expected.GetProperty("starts").EnumerateArray().Select(s => (s.GetProperty("startMinute").GetInt32(), s.GetProperty("maxHours").GetInt32())));
        foreach (var s in result.Starts) s.StartUtc.Should().Be(BusinessClock.ToUtc(Tz, input.BusinessDate, s.StartMinute));
    }

    [Theory, MemberData(nameof(StartIds))]
    public void Whatever_is_shown_is_accepted_and_nothing_else_is(string id)
    {
        // «показали ⇒ сервер примет»: IsStartAllowed agrees with the list for EVERY start and duration of the date, shown or not.
        var root = Root.GetProperty("starts");
        var input = StartInput(root, Case(root.GetProperty("cases"), id));
        var list = ServiceSlotCalculator.Calculate(input).Starts;
        for (var minute = 300; minute <= 1800; minute += 30)
            for (var hours = 1; hours <= 12; hours++)
            {
                var shown = hours >= input.Service.MinHours && list.Any(s => s.StartMinute == minute && s.MaxHours >= hours);
                ServiceSlotCalculator.IsStartAllowed(input, minute, hours).Should().Be(shown);
                (ServiceSlotCalculator.Diagnose(input, minute, hours) is null).Should().Be(shown);
            }
    }

    [Fact]
    public void Diagnose_names_the_first_failure_in_the_order_of_the_contract()
    {
        var root = Root.GetProperty("starts");
        var baseCase = Case(root.GetProperty("cases"), "ST01");
        var input = StartInput(root, baseCase);
        ServiceSlotCalculator.Diagnose(input, 720, 5).Should().Be(ServiceRefusalCode.StartUnavailable, "720 + 5 ч выходит за конец окна");
        ServiceSlotCalculator.Diagnose(input, 720, 1).Should().Be(ServiceRefusalCode.HoursOutOfRange);
        ServiceSlotCalculator.Diagnose(input, 721, 2).Should().Be(ServiceRefusalCode.StartUnavailable);
        ServiceSlotCalculator.Diagnose(input, 1500, 4).Should().Be(ServiceRefusalCode.StartUnavailable);

        var past = StartInput(root, Case(root.GetProperty("cases"), "ST09"));
        ServiceSlotCalculator.Diagnose(past, 720, 2).Should().Be(ServiceRefusalCode.DateInPast);
        var far = StartInput(root, Case(root.GetProperty("cases"), "ST08"));
        ServiceSlotCalculator.Diagnose(far, 720, 2).Should().Be(ServiceRefusalCode.BeyondHorizon);

        var lead = StartInput(root, Case(root.GetProperty("cases"), "ST05"));
        ServiceSlotCalculator.Diagnose(lead, 720, 2).Should().Be(ServiceRefusalCode.TooEarly);

        var stay = StartInput(root, Case(root.GetProperty("cases"), "ST06"));
        ServiceSlotCalculator.Diagnose(stay, 720, 2).Should().Be(ServiceRefusalCode.OutsideStay);

        var noPrice = StartInput(root, Case(root.GetProperty("cases"), "ST07"));
        ServiceSlotCalculator.Diagnose(noPrice, 720, 2).Should().Be(ServiceRefusalCode.NoPriceForHours);

        var taken = StartInput(root, Case(root.GetProperty("cases"), "ST02"));
        ServiceSlotCalculator.Diagnose(taken, 1380, 2).Should().Be(ServiceRefusalCode.SlotTaken);
    }

    [Fact]
    public void A_closed_date_has_no_starts()
    {
        var root = Root.GetProperty("starts");
        var input = StartInput(root, Case(root.GetProperty("cases"), "ST01")) with { Windows = [] };
        ServiceSlotCalculator.Calculate(input).Reason.Should().Be(StartsReason.Closed);
    }

    // ── overlap (the semantics of the database constraint) ──

    [Theory, MemberData(nameof(OverlapIds))]
    public void Overlap(string id)
    {
        var c = Case(Cases("overlap"), id);
        var a = c.GetProperty("a");
        var b = c.GetProperty("b");
        ServiceSlotCalculator.Conflicts(
            Utc(a.GetProperty("startUtc").GetString()!), Utc(a.GetProperty("endUtc").GetString()!), a.GetProperty("bufferMinutes").GetInt32(),
            Utc(b.GetProperty("startUtc").GetString()!), Utc(b.GetProperty("endUtc").GetString()!), b.GetProperty("bufferMinutes").GetInt32())
            .Should().Be(c.GetProperty("conflict").GetBoolean());
    }

    // ── refund ──

    [Theory, MemberData(nameof(RefundIds))]
    public void Refund(string id)
    {
        var root = Root.GetProperty("refund");
        var c = Case(root.GetProperty("cases"), id);
        var result = ServiceRefund.Compute(
            Enum.Parse<StayBookingStatus>(c.GetProperty("status").GetString()!), Enum.Parse<StayServiceCancellationPolicy>(c.GetProperty("policy").GetString()!),
            c.GetProperty("boundaryHours").GetInt32(), c.GetProperty("prepayRub").GetInt32(), c.GetProperty("firstHourRub").GetInt32(),
            Utc(root.GetProperty("common").GetProperty("startUtc").GetString()!), Utc(c.GetProperty("atUtc").GetString()!), c.GetProperty("cancelledBy").GetString() == "Owner");
        var e = c.GetProperty("expected");
        result.Kind.ToString().Should().Be(e.GetProperty("kind").GetString());
        var atLeast = e.GetProperty("refundAtLeastRub");
        result.RefundAtLeastRub.Should().Be(atLeast.ValueKind == JsonValueKind.Null ? null : atLeast.GetInt32());
        result.MaxDeductionRub.Should().Be(e.GetProperty("maxDeductionRub").GetInt32());
    }

    [Fact]
    public void A_sum_of_not_less_than_zero_is_never_promised()
    {
        // Т39-02: with a zero rest the result is CostsOnlyUpTo with a null sum and the text says «только фактические расходы», never «не меньше 0 ₽».
        var start = Utc("2027-01-15T15:00:00Z");
        foreach (var prepay in new[] { 0, 500, 1200, 2000, 3000 })
            foreach (var status in Enum.GetValues<StayBookingStatus>())
                for (var hoursBefore = 0; hoursBefore < 30; hoursBefore += 3)
                    foreach (var policy in Enum.GetValues<StayServiceCancellationPolicy>())
                    {
                        var r = ServiceRefund.Compute(status, policy, 12, prepay, 2000, start, start.AddHours(-hoursBefore), cancelledByOwner: false);
                        r.Text.Should().NotContain("не меньше 0 ₽").And.NotContain("не меньше 0 ").And.NotMatchRegex(@"не меньше\s+0\s");
                        if (r.Kind == ServiceRefundKind.CostsOnlyUpTo) r.RefundAtLeastRub.Should().BeNull();
                        else r.RefundAtLeastRub.Should().NotBeNull();
                    }
    }

    [Theory, MemberData(nameof(RefundConfigIds))]
    public void Refund_configuration_validator(string id)
    {
        var c = Case(Root.GetProperty("refund").GetProperty("config").GetProperty("cases"), id);
        var errors = c.TryGetProperty("policyName", out var name)
            ? ServiceRefund.ValidateConfiguration(1, 3, 24, 12, [name.GetString()!])
            : ServiceRefund.ValidateConfiguration(c.GetProperty("maxDeductionHours").GetInt32(), c.GetProperty("boundary").GetProperty("min").GetInt32(),
                c.GetProperty("boundary").GetProperty("max").GetInt32(), c.GetProperty("boundary").GetProperty("default").GetInt32());
        (errors.Count == 0).Should().Be(c.GetProperty("valid").GetBoolean(), string.Join("; ", errors));
    }

    // ── format ──

    [Theory, MemberData(nameof(FormatIds))]
    public void Format(string id)
    {
        var c = Case(Cases("format"), id);
        var date = Date(c.GetProperty("businessDate").GetString()!);
        var start = c.GetProperty("startMinute").GetInt32();
        var hours = c.GetProperty("hours").GetInt32();
        ServiceTimeFormat.Guest(date, start, hours).Should().Be(c.GetProperty("expectedGuest").GetString());
        ServiceTimeFormat.Staff(date, start, hours).Should().Be(c.GetProperty("expectedStaff").GetString());
        ServiceTimeFormat.StartLabel(date, start).Should().Be(c.GetProperty("expectedStartLabel").GetString());
    }

    [Theory, MemberData(nameof(DateLabelIds))]
    public void Business_date_label(string id)
    {
        var c = Case(Cases("format", "businessDateLabel"), id);
        ServiceTimeFormat.BusinessDateLabel(Date(c.GetProperty("businessDate").GetString()!)).Should().Be(c.GetProperty("expected").GetString());
    }

    [Fact]
    public void A_guest_never_reads_the_words_of_the_internal_model()
    {
        // ЮР39-8 / Т39-04: no «бизнес-день», no hours 6…30 anywhere in a guest label.
        var date = new DateOnly(2027, 1, 15);
        foreach (var start in new[] { 360, 720, 1380, 1470, 1740 })
            foreach (var hours in new[] { 1, 2, 3 })
                foreach (var text in new[] { ServiceTimeFormat.Guest(date, start, hours), ServiceTimeFormat.GuestMoment(date, start), ServiceTimeFormat.GuestEnd(date, start, hours), ServiceTimeFormat.StartLabel(date, start) })
                    text.ToLowerInvariant().Should().NotContain("бизнес").And.NotContain("след. дня").And.NotMatchRegex(@"\b(2[5-9]|30):\d\d");
    }
}
