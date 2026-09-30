using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ServiceBooking.API.Services;

public enum MalformedKeySource { Form, Query }

/// <summary>
/// Wraps the two JQuery value-provider factories: they normalise every key eagerly and throw
/// ArgumentException on a key such as "file[", which MVC does not catch (500). Here that exception, and only
/// that one, thrown by the wrapped factory, becomes a ValueProviderException, which MVC turns into a
/// ModelState error and thus a 400 (ARCHITECTURE_CYCLE29.md §29.4).
/// </summary>
public sealed class MalformedKeyGuardValueProviderFactory(IValueProviderFactory inner, MalformedKeySource source)
    : IValueProviderFactory
{
    public IValueProviderFactory Inner => inner;
    public MalformedKeySource Source => source;

    public async Task CreateValueProviderAsync(ValueProviderFactoryContext context)
    {
        try
        {
            await inner.CreateValueProviderAsync(context);
        }
        catch (ArgumentException ex)
        {
            var http = context.ActionContext.HttpContext;
            http.RequestServices?.GetService<ILogger<MalformedKeyGuardValueProviderFactory>>()?.LogInformation(
                "Rejected request with a malformed {Source} key on {Method} {Path}",
                source == MalformedKeySource.Form ? "form" : "query", http.Request.Method, http.Request.Path.Value);
            throw new ValueProviderException(
                source == MalformedKeySource.Form ? RequestTexts.MalformedFormFieldName : RequestTexts.MalformedQueryParameterName, ex);
        }
    }

    /// <summary>
    /// Wraps whichever of the two JQuery factories are registered. JQueryQueryStringValueProviderFactory is not in the
    /// default list on net8 (the plain query factory does not normalise keys and cannot throw), so a missing one is skipped;
    /// only a missing form factory, which is always default, is an error.
    /// </summary>
    public static void Install(IList<IValueProviderFactory> factories)
    {
        if (!Replace<JQueryFormValueProviderFactory>(factories, MalformedKeySource.Form))
            throw new InvalidOperationException($"{nameof(JQueryFormValueProviderFactory)} is not registered; the malformed-key guard cannot be installed.");
        Replace<JQueryQueryStringValueProviderFactory>(factories, MalformedKeySource.Query);
    }

    private static bool Replace<T>(IList<IValueProviderFactory> factories, MalformedKeySource source) where T : IValueProviderFactory
    {
        for (var i = 0; i < factories.Count; i++)
        {
            if (factories[i] is T)
            {
                factories[i] = new MalformedKeyGuardValueProviderFactory(factories[i], source);
                return true;
            }
        }
        return false;
    }
}
