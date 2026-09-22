using FluentAssertions;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

public class NotificationTemplateRendererTests
{
    private static readonly TemplateContext Ctx = new(
        ClientName: "Иван", ServiceName: "Стрижка", MasterName: "Пётр",
        Date: "01.07.2026", Time: "12:00", CompanyName: "Салон Красоты",
        Address: "ул. Ленина, 1", CompanyPhone: "+79991234567",
        CancellationReason: "по просьбе клиента", NewDate: "02.07.2026", NewTime: "13:00");

    [Fact]
    public void Render_SubstitutesAllKnownPlaceholders()
    {
        var body = "{КлиентИмя} {Услуга} {Мастер} {Дата} {Время} {Салон} {Адрес} {ТелефонСалона} " +
                   "{ПричинаОтмены} {НоваяДата} {НовоеВремя}";

        var rendered = NotificationTemplateRenderer.Render(body, Ctx);

        rendered.Should().Be(
            "Иван Стрижка Пётр 01.07.2026 12:00 Салон Красоты ул. Ленина, 1 +79991234567 " +
            "по просьбе клиента 02.07.2026 13:00");
    }

    [Fact]
    public void Render_MissingOptionalContextFields_SubstitutesEmptyString()
    {
        var minimalCtx = new TemplateContext("Иван", "Стрижка", "Пётр", "01.07.2026", "12:00", "Салон");
        var rendered = NotificationTemplateRenderer.Render("{ПричинаОтмены}|end", minimalCtx);
        rendered.Should().Be("|end");
    }

    [Fact]
    public void AppendUnsubscribeLine_AddsBlankLineSeparator()
    {
        var result = NotificationTemplateRenderer.AppendUnsubscribeLine("Текст", "Отписаться: https://ezbook.ru/u/x");
        result.Should().Be("Текст\n\nОтписаться: https://ezbook.ru/u/x");
    }
}

public class NotificationTemplateValidatorTests
{
    [Fact]
    public void Validate_ValidReminderTemplate_Ok()
    {
        var result = NotificationTemplateValidator.Validate(
            "Здравствуйте, {КлиентИмя}! Ждём вас {Дата} в {Время}.", NotificationType.Reminder);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_EmptyBody_Fails(string? body)
    {
        var result = NotificationTemplateValidator.Validate(body, NotificationType.BookingConfirmed);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("1 до 1000");
    }

    [Fact]
    public void Validate_TooLongBody_Fails()
    {
        var body = new string('a', 1001);
        var result = NotificationTemplateValidator.Validate(body, NotificationType.BookingConfirmed);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("1 до 1000");
    }

    [Fact]
    public void Validate_UnknownPlaceholder_Fails()
    {
        var result = NotificationTemplateValidator.Validate("Скидка {Скидка}!", NotificationType.BookingConfirmed);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("{Скидка}");
    }

    [Theory]
    [InlineData("Заходите на http://evil.example.com для скидки")]
    [InlineData("Заходите на www.evil.example.com для скидки")]
    public void Validate_ExternalLink_Fails(string body)
    {
        var result = NotificationTemplateValidator.Validate(body, NotificationType.BookingConfirmed);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("ссылки");
    }

    [Theory]
    [InlineData("Подробнее: https://ezbook.ru/info")]
    [InlineData("Подробнее: https://sub.ezbook.ru/info")]
    public void Validate_OwnDomainLink_Passes(string body)
    {
        var result = NotificationTemplateValidator.Validate(body, NotificationType.BookingConfirmed);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_OnlyPlaceholdersAndWhitespace_Fails()
    {
        var result = NotificationTemplateValidator.Validate("{КлиентИмя}  {Дата} {Время} ", NotificationType.BookingConfirmed);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("одних подстановок");
    }

    [Theory]
    [InlineData("Здравствуйте, {КлиентИмя}! Ждём {Дата}.")]     // missing {Время}
    [InlineData("Здравствуйте, {КлиентИмя}! Ждём в {Время}.")]  // missing {Дата}
    public void Validate_ReminderMissingRequiredPlaceholder_Fails(string body)
    {
        var result = NotificationTemplateValidator.Validate(body, NotificationType.Reminder);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("{Дата}").And.Contain("{Время}");
    }

    [Fact]
    public void Validate_NonReminderType_DoesNotRequireDateTimePlaceholders()
    {
        var result = NotificationTemplateValidator.Validate("Спасибо, {КлиентИмя}!", NotificationType.BookingConfirmed);
        result.IsValid.Should().BeTrue();
    }

    // ── T5-B12 hard bans (ARCHITECTURE_CYCLE5.md §51.2, US-69 п. 3) — never bypassable ──────────────

    [Theory]
    [InlineData("Звоните нам: +7 999 123-45-67")]
    [InlineData("Звоните нам: 89991234567")]
    [InlineData("Наш номер (999) 123-45-67, ждём")]
    public void Validate_PhoneNumberOtherThanCompanyPlaceholder_Fails(string body)
    {
        var result = NotificationTemplateValidator.Validate(body, NotificationType.BookingConfirmed);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("телефон");
    }

    [Fact]
    public void Validate_CompanyPhonePlaceholder_DoesNotTripThePhoneBan()
    {
        var result = NotificationTemplateValidator.Validate("Звоните нам: {ТелефонСалона}", NotificationType.BookingConfirmed);
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Пишите нам в Telegram!")]
    [InlineData("Мы в Инстаграм: @salon")]
    [InlineData("Подписывайтесь во Вконтакте")]
    public void Validate_ThirdPartyMessengerOrSocialMention_Fails(string body)
    {
        var result = NotificationTemplateValidator.Validate(body, NotificationType.BookingConfirmed);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("мессенджер");
    }
}

public class DefaultTemplatesTests
{
    [Theory]
    [InlineData(NotificationType.BookingConfirmed)]
    [InlineData(NotificationType.Reminder)]
    [InlineData(NotificationType.BookingCancelled)]
    [InlineData(NotificationType.BookingRescheduled)]
    [InlineData(NotificationType.StaffBookingCreated)]
    [InlineData(NotificationType.StaffBookingCancelled)]
    public void For_EveryType_ReturnsNonEmptyValidTemplate(NotificationType type)
    {
        var body = DefaultTemplates.For(type);
        body.Should().NotBeNullOrWhiteSpace();

        // The platform's own default text must itself pass validation for its type — otherwise
        // "reset to platform text" (PUT with body: "") would hand the owner back invalid text.
        NotificationTemplateValidator.Validate(body, type).IsValid.Should().BeTrue();
    }

    [Fact]
    public void CustomizableTypes_IsExactlyTheFourClientFacingTypes()
    {
        DefaultTemplates.CustomizableTypes.Should().BeEquivalentTo(
        [
            NotificationType.BookingConfirmed,
            NotificationType.Reminder,
            NotificationType.BookingCancelled,
            NotificationType.BookingRescheduled,
        ]);
    }
}
