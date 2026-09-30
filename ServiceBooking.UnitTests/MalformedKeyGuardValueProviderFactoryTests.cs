using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using ServiceBooking.API.Services;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE29.md §29.4 — a malformed form/query key is a 400 path, not an unhandled ArgumentException.</summary>
public class MalformedKeyGuardValueProviderFactoryTests
{
    private sealed class ThrowingFactory(Exception ex) : IValueProviderFactory
    {
        public Task CreateValueProviderAsync(ValueProviderFactoryContext context) => throw ex;
    }

    private static ValueProviderFactoryContext QueryContext(string queryString)
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString(queryString);
        return new ValueProviderFactoryContext(new ActionContext(http, new RouteData(), new ActionDescriptor()));
    }

    private static ValueProviderFactoryContext FormContext(string body)
    {
        var http = new DefaultHttpContext();
        http.Request.ContentType = "application/x-www-form-urlencoded";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        return new ValueProviderFactoryContext(new ActionContext(http, new RouteData(), new ActionDescriptor()));
    }

    [Fact]
    public async Task Query_MalformedKey_BecomesValueProviderExceptionWithQueryText()
    {
        var guard = new MalformedKeyGuardValueProviderFactory(new JQueryQueryStringValueProviderFactory(), MalformedKeySource.Query);

        var act = () => guard.CreateValueProviderAsync(QueryContext("?a[=1"));

        (await act.Should().ThrowAsync<ValueProviderException>()).Which.Message
            .Should().Be(RequestTexts.MalformedQueryParameterName);
    }

    [Fact]
    public async Task Form_MalformedKey_BecomesValueProviderExceptionWithFormText()
    {
        var guard = new MalformedKeyGuardValueProviderFactory(new JQueryFormValueProviderFactory(), MalformedKeySource.Form);

        var act = () => guard.CreateValueProviderAsync(FormContext("file[=1"));

        (await act.Should().ThrowAsync<ValueProviderException>()).Which.Message
            .Should().Be(RequestTexts.MalformedFormFieldName);
    }

    [Theory]
    [InlineData("?a[b]=1")]
    [InlineData("?ids[]=1&ids[]=2")]
    [InlineData("?plain=1")]
    public async Task Query_ValidKeys_AddProvider(string query)
    {
        var guard = new MalformedKeyGuardValueProviderFactory(new JQueryQueryStringValueProviderFactory(), MalformedKeySource.Query);
        var context = QueryContext(query);

        await guard.CreateValueProviderAsync(context);

        context.ValueProviders.Should().HaveCount(1);
    }

    [Fact]
    public async Task Form_ValidBracketKey_AddsProvider()
    {
        var guard = new MalformedKeyGuardValueProviderFactory(new JQueryFormValueProviderFactory(), MalformedKeySource.Form);
        var context = FormContext("meta[x]=1");

        await guard.CreateValueProviderAsync(context);

        context.ValueProviders.Should().HaveCount(1);
    }

    [Fact]
    public async Task OtherExceptions_PassThroughUnchanged()
    {
        var original = new InvalidOperationException("boom");
        var guard = new MalformedKeyGuardValueProviderFactory(new ThrowingFactory(original), MalformedKeySource.Form);

        var act = () => guard.CreateValueProviderAsync(QueryContext(""));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(original);
    }

    [Fact]
    public async Task ExistingValueProviderException_PassesThroughUnchanged()
    {
        var original = new ValueProviderException("framework text");
        var guard = new MalformedKeyGuardValueProviderFactory(new ThrowingFactory(original), MalformedKeySource.Form);

        var act = () => guard.CreateValueProviderAsync(QueryContext(""));

        (await act.Should().ThrowAsync<ValueProviderException>()).Which.Should().BeSameAs(original);
    }

    [Fact]
    public void Install_ReplacesBothFactoriesInPlace()
    {
        var other = new ThrowingFactory(new Exception());
        var form = new JQueryFormValueProviderFactory();
        var query = new JQueryQueryStringValueProviderFactory();
        var list = new List<IValueProviderFactory> { other, form, query };

        MalformedKeyGuardValueProviderFactory.Install(list);

        list[0].Should().BeSameAs(other);
        list[1].Should().BeOfType<MalformedKeyGuardValueProviderFactory>().Which.Inner.Should().BeSameAs(form);
        list[2].Should().BeOfType<MalformedKeyGuardValueProviderFactory>().Which.Inner.Should().BeSameAs(query);
    }

    [Fact]
    public void Install_MissingFactory_Throws()
    {
        var list = new List<IValueProviderFactory> { new JQueryFormValueProviderFactory() };

        var act = () => MalformedKeyGuardValueProviderFactory.Install(list);

        act.Should().Throw<InvalidOperationException>();
    }
}
