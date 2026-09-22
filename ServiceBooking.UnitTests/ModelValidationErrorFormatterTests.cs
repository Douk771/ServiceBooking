using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

public class ModelValidationErrorFormatterTests
{
    private static ActionContext BuildContext(ModelStateDictionary modelState) =>
        new(new DefaultHttpContext(), new RouteData(), new ActionDescriptor(), modelState);

    [Fact]
    public void BuildResponse_ReturnsBadRequestWithPlainTextRussianBody()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Phone", "The Phone field is required.");

        var result = ModelValidationErrorFormatter.BuildResponse(BuildContext(modelState)) as ContentResult;

        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        result.ContentType.Should().StartWith("text/plain");
        result.Content.Should().NotBeNullOrWhiteSpace();
        // Cyrillic-only sanity check: nothing here should be a raw C# identifier like "Phone".
        result.Content.Should().Contain("номер телефона");
        result.Content.Should().NotContain("Phone");
    }

    [Fact]
    public void BuildResponse_KnownFieldMissing_MentionsRequiredReason()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("masterId", "The masterId field is required.");

        var result = (ContentResult)ModelValidationErrorFormatter.BuildResponse(BuildContext(modelState));

        result.Content.Should().Contain("мастер").And.Contain("не заполнено");
    }

    [Fact]
    public void BuildResponse_KnownFieldWithBadFormat_MentionsFormatReason()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("companyId", "The value '' is not valid.");

        var result = (ContentResult)ModelValidationErrorFormatter.BuildResponse(BuildContext(modelState));

        result.Content.Should().Contain("компания").And.Contain("неверном формате");
    }

    [Fact]
    public void BuildResponse_MultipleFields_CombinesThemIntoOneMessage()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Phone", "The Phone field is required.");
        modelState.AddModelError("Password", "The Password field is required.");

        var result = (ContentResult)ModelValidationErrorFormatter.BuildResponse(BuildContext(modelState));

        result.Content.Should().Contain("номер телефона").And.Contain("пароль");
    }

    [Fact]
    public void BuildResponse_NestedBodyFieldKey_StripsPrefixAndStillMapsLabel()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("dto.Phone", "The Phone field is required.");

        var result = (ContentResult)ModelValidationErrorFormatter.BuildResponse(BuildContext(modelState));

        result.Content.Should().Contain("номер телефона");
    }

    [Fact]
    public void BuildResponse_UnmappedFieldOnly_StillReturnsNonEmptyRussianMessage()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("SomeUnmappedInternalField", "The SomeUnmappedInternalField field is required.");

        var result = (ContentResult)ModelValidationErrorFormatter.BuildResponse(BuildContext(modelState));

        result.Content.Should().NotBeNullOrWhiteSpace();
        result.Content.Should().NotContain("SomeUnmappedInternalField");
    }

    [Fact]
    public void BuildResponse_MixOfMappedAndUnmappedFields_MentionsThereAreMore()
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError("Phone", "The Phone field is required.");
        modelState.AddModelError("SomeUnmappedInternalField", "The field is required.");

        var result = (ContentResult)ModelValidationErrorFormatter.BuildResponse(BuildContext(modelState));

        result.Content.Should().Contain("номер телефона").And.Contain("другие");
    }
}
