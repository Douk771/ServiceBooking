using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Cycle 22, package P5 (ARCHITECTURE_CYCLE22.md §382, SPEC.md R22-2, US-22-08): the five large
/// controllers and Program.cs are split mechanically — and "mechanically" is checked here, not trusted.
/// Every endpoint the live host exposes (controller actions AND minimal/health endpoints) is flattened to
/// one canonical line WITHOUT controller/action names:
/// <c>METHOD template | auth: … | ratelimit: … | filters: … | params: … | meta: …</c>,
/// where auth/ratelimit/filters list every attribute (with its public properties) split by level
/// (<c>C:</c> controller, <c>A:</c> action), params the action's parameters with their binding source, and
/// meta the non-attribute endpoint metadata type names (conventions). The sorted table must equal the golden
/// file captured from the PRE-split code byte for byte — the golden file is never regenerated to make a
/// split pass: a difference means the split changed a route, a policy, a limiter or a binding.
/// </summary>
public class Cycle22RouteTableTests(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    private const string GoldenFileName = "Cycle22RouteTable.golden.txt";

    [Fact, TestCase("CY22-12")]
    public void RouteTable_EqualsPreSplitGolden()
    {
        var live = BuildTable(Factory.Services);
        var goldenPath = Path.Combine(ThisDirectory(), GoldenFileName);

        // One-off capture switch (used once, on the pre-split code): CY22_WRITE_ROUTE_GOLDEN=1.
        if (Environment.GetEnvironmentVariable("CY22_WRITE_ROUTE_GOLDEN") == "1")
            File.WriteAllText(goldenPath, live);

        File.Exists(goldenPath).Should().BeTrue($"the golden route table {goldenPath} is committed");
        var golden = File.ReadAllText(goldenPath).Replace("\r\n", "\n");

        if (golden != live)
        {
            var g = golden.Split('\n').ToHashSet();
            var l = live.Split('\n').ToHashSet();
            var diff = new StringBuilder();
            foreach (var line in g.Except(l)) diff.Append("- ").AppendLine(line);
            foreach (var line in l.Except(g)) diff.Append("+ ").AppendLine(line);
            live.Should().Be(golden, "the route/attribute table must not change across the P5 split; diff:\n" + diff);
        }
    }

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    private static string BuildTable(IServiceProvider services)
    {
        var source = services.GetRequiredService<EndpointDataSource>();
        var lines = new List<string>();
        foreach (var endpoint in source.Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods is { Count: > 0 } m
                ? string.Join(",", m.OrderBy(x => x, StringComparer.Ordinal))
                : "ANY";
            var template = endpoint.RoutePattern.RawText ?? "";
            var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            // Route name/order (the only route-attribute facts not already in METHOD + resolved template).
            var routeInfo = action?.AttributeRouteInfo is { } ri ? $" name={ri.Name ?? "-"},order={ri.Order}" : "";

            var auth = new List<string>();
            var rate = new List<string>();
            var filters = new List<string>();
            var parameters = new List<string>();

            if (action != null)
            {
                Classify("C", action.ControllerTypeInfo.GetCustomAttributes(inherit: true), auth, rate, filters);
                Classify("A", action.MethodInfo.GetCustomAttributes(inherit: true), auth, rate, filters);
                foreach (var p in action.Parameters)
                {
                    var pAttrs = p is ControllerParameterDescriptor cp
                        ? cp.ParameterInfo.GetCustomAttributes(inherit: true).Where(IsRelevant).Select(Describe)
                            .OrderBy(x => x, StringComparer.Ordinal)
                        : Enumerable.Empty<string>();
                    parameters.Add($"{p.Name}:{TypeName(p.ParameterType)}:{p.BindingInfo?.BindingSource?.Id ?? "-"}"
                                   + $"[{string.Join(";", pAttrs)}]");
                }
            }

            var meta = endpoint.Metadata
                .Where(x => x is not Attribute && x is not ControllerActionDescriptor)
                .Select(x => TypeName(x.GetType()))
                .Where(n => !n.Contains('<'))
                .Distinct()
                .OrderBy(x => x, StringComparer.Ordinal);
            if (action == null)
            {
                // Minimal/health endpoints: attribute metadata is folded into "filters" (no controller level).
                Classify("E", endpoint.Metadata.OfType<Attribute>(), auth, rate, filters);
            }

            lines.Add($"{methods} {template}{routeInfo} | auth: {Join(auth)} | ratelimit: {Join(rate)} | filters: {Join(filters)}"
                      + $" | params: {string.Join(", ", parameters)} | meta: {string.Join(",", meta)}");
        }

        lines.Sort(StringComparer.Ordinal);
        return string.Join("\n", lines) + "\n";
    }

    private static string Join(List<string> items) =>
        items.Count == 0 ? "-" : string.Join("; ", items.OrderBy(x => x, StringComparer.Ordinal));

    private static void Classify(string level, IEnumerable<object> attributes,
        List<string> auth, List<string> rate, List<string> filters)
    {
        foreach (var a in attributes.Where(IsRelevant))
        {
            var name = a.GetType().Name;
            var line = $"{level}:{Describe(a)}";
            if (name.Contains("Authorize") || name.Contains("AllowAnonymous")) auth.Add(line);
            else if (name.Contains("RateLimiting")) rate.Add(line);
            else filters.Add(line);
        }
    }

    /// <summary>Compiler-emitted attributes (nullable context, async state machine, debugger hints) depend on
    /// where a method happens to live, not on what the endpoint does — excluded. So are the route-template
    /// attributes themselves ([Route], [HttpGet(...)]): their effect is the line's METHOD + RESOLVED template
    /// (+ name/order), which is what must not move — a split controller legitimately spells
    /// <c>[Route("api/[controller]")]</c> as the literal it resolved to.</summary>
    private static bool IsRelevant(object attribute)
    {
        if (attribute is Microsoft.AspNetCore.Mvc.Routing.IRouteTemplateProvider) return false;
        var ns = attribute.GetType().Namespace ?? "";
        return ns != "System.Runtime.CompilerServices" && ns != "System.Diagnostics"
               && !ns.StartsWith("System.Diagnostics.", StringComparison.Ordinal);
    }

    private static string Describe(object attribute)
    {
        var type = attribute.GetType();
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "TypeId" && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={Format(SafeGet(p, attribute))}");
        return $"{TypeName(type)}({string.Join(",", props)})";
    }

    private static object? SafeGet(PropertyInfo p, object target)
    {
        try { return p.GetValue(target); }
        catch (Exception ex) { return "!" + ex.GetType().Name; }
    }

    private static string Format(object? value) => value switch
    {
        null => "null",
        string s => s,
        Type t => TypeName(t),
        IEnumerable e => "[" + string.Join(",", e.Cast<object?>().Select(Format)) + "]",
        _ => value.ToString() ?? "",
    };

    private static string TypeName(Type t)
    {
        if (!t.IsGenericType) return t.FullName ?? t.Name;
        var name = t.GetGenericTypeDefinition().FullName ?? t.Name;
        name = name[..name.IndexOf('`')];
        return $"{name}<{string.Join(",", t.GetGenericArguments().Select(TypeName))}>";
    }
}
