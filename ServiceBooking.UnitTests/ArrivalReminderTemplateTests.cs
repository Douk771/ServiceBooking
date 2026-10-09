using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.11, LEGAL_REVIEW_CYCLE39.md (Т39-11…13) — the reminder template: the vectors of service-vectors.json → reminderTemplate, the guarantee that a
/// NULL template keeps the text of cycle 37 byte for byte, the hard filter of the push text, the lengths and the checks at saving.
/// </summary>
public class ArrivalReminderTemplateTests
{
    private static JsonElement Root => ContractFiles.Load("cycle39", "service-vectors.json").RootElement.Clone().GetProperty("reminderTemplate");

    public static IEnumerable<object[]> Ids() => Root.GetProperty("cases").EnumerateArray().Select(c => new object[] { c.GetProperty("id").GetString()! });

    private static ReminderFacts Facts(JsonElement? overrideNode = null)
    {
        var f = Root.GetProperty("facts");
        string? Str(string name)
        {
            if (overrideNode is { } o && o.TryGetProperty(name, out var ov)) return ov.ValueKind == JsonValueKind.Null ? null : ov.GetString();
            return f.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.GetString() : null;
        }
        var due = overrideNode is { } o2 && o2.TryGetProperty("dueAtCheckInRub", out var d) ? d.GetInt32() : f.GetProperty("dueAtCheckInRub").GetInt32();
        var sessionsNode = overrideNode is { } o3 && o3.TryGetProperty("sessions", out var s) ? s : f.GetProperty("sessions");
        var sessions = sessionsNode.EnumerateArray().Select(x => new ReminderSessionFact(x.GetProperty("serviceName").GetString()!,
            DateOnly.Parse(x.GetProperty("businessDate").GetString()!, CultureInfo.InvariantCulture), x.GetProperty("startMinute").GetInt32(), x.GetProperty("hours").GetInt32())).ToList();
        return new ReminderFacts(Str("companyName")!, Str("houseName")!, DateOnly.Parse(Str("checkInDate")!, CultureInfo.InvariantCulture),
            DateOnly.Parse(Str("checkOutDate")!, CultureInfo.InvariantCulture), f.GetProperty("nights").GetInt32(), Str("checkInTime")!, Str("checkOutTime")!,
            Str("guestName"), Str("address"), Str("companyPhone"), due, Str("bookingUrl")!, Str("unsubscribeUrl"), sessions);
    }

    [Theory, MemberData(nameof(Ids))]
    public void Vector(string id)
    {
        var c = Root.GetProperty("cases").EnumerateArray().First(x => x.GetProperty("id").GetString() == id);
        var facts = Facts(c.TryGetProperty("factsOverride", out var fo) ? fo : null);
        var template = c.TryGetProperty("template", out var t) ? (t.ValueKind == JsonValueKind.Null ? null : t.GetString())
            : c.TryGetProperty("templateLength", out var len) ? new string('а', len.GetInt32()) : null;
        var pushOn = c.GetProperty("pushTextEnabled").GetBoolean();
        var e = c.GetProperty("expected");

        if (e.TryGetProperty("messenger", out var messenger))
            ArrivalReminderTemplate.Render(template, facts, ReminderMode.Messenger).Text.Should().Be(messenger.GetString());
        if (e.TryGetProperty("page", out var page))
            ArrivalReminderTemplate.Render(template, facts, ReminderMode.Page).Text.Should().Be(page.GetString());
        if (e.TryGetProperty("push", out var push))
        {
            var rendered = ArrivalReminderTemplate.Render(template, facts, ReminderMode.Push, pushOn);
            rendered.Text.Should().Be(push.GetString());
            if (e.TryGetProperty("pushDropped", out var dropped))
                rendered.Dropped.Select(d => (d.Line, d.Reason.ToString())).Should().Equal(dropped.EnumerateArray().Select(d => (d.GetProperty("line").GetInt32(), d.GetProperty("reason").GetString()!)));
        }
        if (e.TryGetProperty("validation", out var validation))
        {
            var v = ArrivalReminderTemplate.Validate(template);
            v.Errors.Select(x => x.Code.ToString()).Should().Equal(validation.GetProperty("errors").EnumerateArray().Select(x => x.GetString()!));
            v.ConfirmationRequired.Should().Be(validation.GetProperty("confirmationRequired").GetBoolean());
            v.Warnings.Select(x => x.ToString()).Should().Equal(validation.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()!));
        }
    }

    [Fact]
    public void Default_template_in_the_messenger_mode_is_the_text_of_cycle_37_when_lines_are_joined_by_a_space()
    {
        // The editor shows Default; the code keeps the legacy sentence for NULL. The two representations must never drift apart.
        foreach (var overrideJson in new[] { "{}", "{\"address\":null}", "{\"dueAtCheckInRub\":0}", "{\"address\":null,\"dueAtCheckInRub\":0}" })
        {
            using var doc = JsonDocument.Parse(overrideJson);
            var facts = Facts(doc.RootElement);
            var legacy = ArrivalReminderTemplate.Render(null, facts, ReminderMode.Messenger).Text;
            var rendered = ArrivalReminderTemplate.Render(ArrivalReminderTemplate.Default, facts, ReminderMode.Messenger).Text;
            rendered.Replace("\n\nОтписаться", "\u0001").Replace("\n", " ").Replace("\u0001", "\n\nОтписаться").Should().Be(legacy, overrideJson);
        }
    }

    [Fact]
    public void Null_template_gives_the_old_texts_byte_for_byte()
    {
        var facts = Facts();
        ArrivalReminderTemplate.Render(null, facts, ReminderMode.Messenger).Text.Should().Be(ArrivalReminderTemplate.LegacyMessenger(facts));
        ArrivalReminderTemplate.Render(null, facts, ReminderMode.Push, pushTextEnabled: true).Text.Should().Be("Завтра заезд — откройте бронь");
        StayNotificationTexts.GuestPush(NotificationType.StayGuestArrivalReminder, Guid.NewGuid(), "t").Body.Should().Be(ArrivalReminderTemplate.FixedPush);
        Root.GetProperty("fixedPush").GetString().Should().Be(ArrivalReminderTemplate.FixedPush);
        Root.GetProperty("defaultTemplate").GetString().Should().Be(ArrivalReminderTemplate.Default);
    }

    [Fact]
    public void A_template_equal_to_the_default_is_stored_as_null()
    {
        ArrivalReminderTemplate.IsDefault(ArrivalReminderTemplate.Default).Should().BeTrue();
        ArrivalReminderTemplate.IsDefault(ArrivalReminderTemplate.Default.Replace("\n", "\r\n")).Should().BeTrue();
        ArrivalReminderTemplate.IsDefault(ArrivalReminderTemplate.Default + " ").Should().BeTrue();
        ArrivalReminderTemplate.IsDefault(ArrivalReminderTemplate.Default + "x").Should().BeFalse();
    }

    [Fact]
    public void Push_never_carries_the_name_address_phone_sum_or_link_of_a_booking()
    {
        // Т39-13: whatever the owner writes, the five values never reach a push.
        var template = string.Join("\n", ArrivalReminderTemplate.Placeholders.Select(p => $"Строка {p.Token}"));
        var facts = Facts();
        var push = ArrivalReminderTemplate.Render(template, facts, ReminderMode.Push, pushTextEnabled: true);
        foreach (var secret in new[] { facts.GuestName!, facts.Address!, facts.CompanyPhone!, "12 500", facts.BookingUrl })
            push.Text.Should().NotContain(secret);
        push.Dropped.Count(d => d.Reason == ReminderDropReason.ForbiddenPlaceholder).Should().Be(5);
    }

    [Theory]
    [InlineData("Код 1234", ReminderDropReason.Digits)]
    [InlineData("Номер 12 34", ReminderDropReason.Digits)]
    [InlineData("Звоните 8 900", ReminderDropReason.Digits)]
    [InlineData("Смотрите www.site", ReminderDropReason.Link)]
    [InlineData("Смотрите https://x.y", ReminderDropReason.Link)]
    [InlineData("Сайт отеля.рф", ReminderDropReason.Link)]
    [InlineData("Пишите a.b@c.d", ReminderDropReason.Email)]
    [InlineData("Позвоните +7 9", ReminderDropReason.Phone)]
    [InlineData("Пароль на листке", ReminderDropReason.CodeWord)]
    [InlineData("Ключница у входа", ReminderDropReason.CodeWord)]
    [InlineData("Сейф в шкафу", ReminderDropReason.CodeWord)]
    [InlineData("Домофон не работает", ReminderDropReason.CodeWord)]
    [InlineData("Вайфай бесплатный", ReminderDropReason.CodeWord)]
    [InlineData("Код калитки", ReminderDropReason.CodeWord)]
    public void Push_filter_names_the_reason(string line, ReminderDropReason reason) =>
        ArrivalReminderTemplate.PushFilter(line).Should().Be(reason);

    [Theory]
    [InlineData("Ждём вас в «{Дом}» {ДатаЗаезда} с {ВремяЗаезда}")]
    [InlineData("Не забудьте тёплые вещи")]
    [InlineData("До встречи!")]
    [InlineData("Заезд в 14:00")]
    public void Push_filter_lets_ordinary_lines_through(string line) =>
        ArrivalReminderTemplate.PushFilter(line).Should().BeNull();

    [Fact]
    public void The_values_of_placeholders_are_not_filtered()
    {
        // the company is called «Дом 1234»: it is a value, not the owner's text — the line stays.
        var facts = Facts() with { CompanyName = "Дом 1234" };
        ArrivalReminderTemplate.Render("Добро пожаловать в {Компания}", facts, ReminderMode.Push, pushTextEnabled: true).Text.Should().Be("Добро пожаловать в Дом 1234");
    }

    [Fact]
    public void Push_is_cut_to_180_characters_and_never_inside_a_surrogate_pair()
    {
        var facts = Facts();
        var long180 = string.Join(" ", Enumerable.Repeat("слово", 60));
        var push = ArrivalReminderTemplate.Render(long180, facts, ReminderMode.Push, pushTextEnabled: true).Text;
        push.Length.Should().Be(ArrivalReminderTemplate.PushMaxLength);
        push.Should().EndWith("…");
        Root.GetProperty("pushLength").GetProperty("maxLength").GetInt32().Should().Be(ArrivalReminderTemplate.PushMaxLength);

        var emoji = string.Concat(Enumerable.Repeat("😀", 100));
        var cut = ArrivalReminderTemplate.CutTo(emoji, 180);
        cut.Length.Should().BeLessOrEqualTo(180);
        char.IsHighSurrogate(cut[^2]).Should().BeFalse("the character before the ellipsis is a complete pair");
        for (var i = 0; i < cut.Length - 1; i++) if (char.IsHighSurrogate(cut[i])) char.IsLowSurrogate(cut[i + 1]).Should().BeTrue();
    }

    [Fact]
    public void Messenger_text_fits_1000_characters_and_keeps_the_link_and_the_unsubscribe_line()
    {
        var facts = Facts() with { Address = new string('а', 300), GuestName = new string('б', 100) };
        var template = string.Join("\n", Enumerable.Repeat("{Адрес} {ИмяГостя} {Компания} {Дом} {Услуги} очень длинная строка текста владельца для проверки лимита", 8));
        var text = ArrivalReminderTemplate.Render(template, facts, ReminderMode.Messenger).Text;
        text.Length.Should().BeLessOrEqualTo(ArrivalReminderTemplate.MessengerMaxLength);
        text.Should().Contain(facts.BookingUrl).And.EndWith($"Отписаться от сообщений: {facts.UnsubscribeUrl}");
    }

    [Fact]
    public void Page_text_has_no_link_line_and_fits_1000()
    {
        var facts = Facts();
        var page = ArrivalReminderTemplate.Render("Привет\nБронь: {СсылкаНаБронь}", facts, ReminderMode.Page).Text;
        page.Should().Be("Привет").And.NotContain("http");
        var template = string.Join("\n", Enumerable.Repeat(new string('я', 90), 7));
        ArrivalReminderTemplate.Render(template, facts, ReminderMode.Page).Text.Length.Should().BeLessOrEqualTo(ArrivalReminderTemplate.PageMaxLength);
    }

    [Fact]
    public void Validation_collects_markers_and_soft_warnings()
    {
        var v = ArrivalReminderTemplate.Validate("Код от калитки 4512, паспорт, штраф, карта 4276 1234 5678 9012");
        v.ConfirmationRequired.Should().BeTrue();
        v.Markers.Should().Contain("код").And.Contain("4512");
        v.Warnings.Should().BeEquivalentTo([ReminderWarning.CancellationTermsInText, ReminderWarning.Passport, ReminderWarning.CardNumber]);
        ArrivalReminderTemplate.Validate("Паспорт 4510 123456").Warnings.Should().Contain(ReminderWarning.PassportNumber);
        ArrivalReminderTemplate.Validate("Ждём вас {ДатаЗаезда} с {ВремяЗаезда}").ConfirmationRequired.Should().BeFalse("time and date are placeholders, not the owner's digits");
        ArrivalReminderTemplate.Validate("Депозит 100 ₽").Errors.Should().ContainSingle(e => e.Code == ReminderErrorCode.ForbiddenWords);
        ArrivalReminderTemplate.Validate("{Нет} и {Дом}").Errors.Single().Message.Should().StartWith("Неизвестная подстановка {Нет}. Можно: {Компания}, {Дом}, {ДатаЗаезда}");
    }

    [Fact]
    public void Placeholder_table_matches_the_contract()
    {
        ArrivalReminderTemplate.Placeholders.Select(p => p.Token).Should().Equal(
            "{Компания}", "{Дом}", "{ДатаЗаезда}", "{ДатаВыезда}", "{Ночей}", "{ВремяЗаезда}", "{ВремяВыезда}", "{Услуги}", "{ИмяГостя}", "{Адрес}",
            "{ТелефонКомпании}", "{КОплатеПриЗаселении}", "{СсылкаНаБронь}");
        ArrivalReminderTemplate.Placeholders.Where(p => !p.InPush).Select(p => p.Token).Should().Equal("{ИмяГостя}", "{Адрес}", "{ТелефонКомпании}", "{КОплатеПриЗаселении}", "{СсылкаНаБронь}");
        ArrivalReminderTemplate.Placeholders.Single(p => !p.OnPage && p.InMessenger).Token.Should().Be("{СсылкаНаБронь}");
    }
}
