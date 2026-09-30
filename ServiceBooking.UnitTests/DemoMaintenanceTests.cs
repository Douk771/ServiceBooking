using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Demo;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §579–§580 — the demo's request-pipeline pieces, called directly (<see cref="DefaultHttpContext"/>, a temp folder for the flag file):
/// the maintenance flag and middleware, the noindex header, <c>[DemoOnly]</c> and the <c>[DemoForbidden]</c> filter. No server, no database.
/// </summary>
public class DemoMaintenanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sb-demo-flag-" + Guid.NewGuid().ToString("N"));

    public DemoMaintenanceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static readonly DateTime Now = new(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);

    private sealed class FakeEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = root;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private DemoMaintenanceFlag NewFlag(string path = "state/demo-resetting") =>
        new(Options.Create(new DemoModeOptions { Enabled = true, MaintenanceFlagPath = path }), new FakeEnvironment(_root), NullLogger<DemoMaintenanceFlag>.Instance);

    // ── the flag ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Flag_RelativePath_IsResolvedAgainstTheContentRoot_AbsoluteIsKept()
    {
        DemoMaintenanceFlag.ResolvePath("App_Data/state/demo-resetting", "/app").Should().Be(Path.GetFullPath("/app/App_Data/state/demo-resetting"));
        DemoMaintenanceFlag.ResolvePath("/var/run/flag", "/app").Should().Be("/var/run/flag");
    }

    [Fact]
    public void Flag_Begin_RaisesIt_End_LowersIt()
    {
        var flag = NewFlag();

        flag.IsResetting(Now).Should().BeFalse("no file, no reset");
        flag.Begin(Now);
        File.Exists(flag.FullPath).Should().BeTrue("the folder is created on demand");
        flag.IsResetting(Now).Should().BeTrue();
        flag.End();
        flag.IsResetting(Now).Should().BeFalse();
        ((Action)flag.End).Should().NotThrow("lowering a flag that is not there is fine");
    }

    [Fact]
    public void Flag_AnswerIsCachedForASecond_ThenRead_Again()
    {
        var flag = NewFlag();
        flag.Begin(Now);
        flag.IsResetting(Now).Should().BeTrue();

        // Another process (the `ops` command) lowers it: this process still says "resetting" within the second, and the truth after it.
        File.Delete(flag.FullPath);
        flag.IsResetting(Now.AddMilliseconds(400)).Should().BeTrue();
        flag.IsResetting(Now.AddMilliseconds(1100)).Should().BeFalse();
    }

    [Fact]
    public void Flag_FromAnotherProcess_IsSeenWithoutBegin()
    {
        var flag = NewFlag();
        Directory.CreateDirectory(Path.GetDirectoryName(flag.FullPath)!);
        File.WriteAllText(flag.FullPath, Now.ToString("o"));

        flag.IsResetting(Now.AddSeconds(30)).Should().BeTrue();
    }

    [Fact]
    public void Flag_OlderThanTenMinutes_IsHung_AndIgnored()
    {
        var flag = NewFlag();
        Directory.CreateDirectory(Path.GetDirectoryName(flag.FullPath)!);
        File.WriteAllText(flag.FullPath, Now.ToString("o"));

        flag.IsResetting(Now.AddMinutes(9)).Should().BeTrue();
        flag.IsResetting(Now.AddMinutes(11)).Should().BeFalse("a dead reset must not hold the demo in 503 forever (R28-15)");
    }

    [Theory]
    [InlineData(false, null, 0, false, false)]                 // no file
    [InlineData(true, "2026-10-01T01:00:00.0000000Z", 5, true, false)]
    [InlineData(true, "2026-10-01T01:00:00.0000000Z", 11, false, true)]
    [InlineData(true, "garbage", 5, true, false)]              // unreadable content → the file's own write time
    [InlineData(true, "garbage", 20, false, true)]
    public void Flag_Evaluate_IsPure(bool exists, string? content, int minutesAfter, bool resetting, bool stale)
    {
        var state = DemoMaintenanceFlag.Evaluate(exists, content, lastWriteUtc: Now, Now.AddMinutes(minutesAfter));

        state.Resetting.Should().Be(resetting);
        state.Stale.Should().Be(stale);
    }

    // ── the maintenance middleware ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/companies", "GET", true)]
    [InlineData("/api/bookings", "POST", true)]
    [InlineData("/api/demo/login", "POST", true)]
    [InlineData("/api/demo/status", "POST", true)]
    [InlineData("/api/demo/status", "GET", false)]
    [InlineData("/API/Demo/Status", "GET", false)]
    [InlineData("/api/health/ready", "GET", false)]
    [InlineData("/api/health/live", "GET", false)]
    [InlineData("/", "GET", false)]
    [InlineData("/company/primer-lavanda", "GET", false)]
    [InlineData("/uploads/showcase/x.jpg", "GET", false)]
    public void Middleware_DecidesByPathAndMethod(string path, string method, bool blocked) =>
        DemoMaintenanceMiddleware.IsBlockedPath(new PathString(path), method).Should().Be(blocked);

    private async Task<(DefaultHttpContext Context, bool NextCalled)> RunMaintenanceAsync(bool demo, bool resetting, string path = "/api/companies", string method = "GET")
    {
        var flag = NewFlag();
        if (resetting) flag.Begin(DateTime.UtcNow);
        var called = false;
        var middleware = new DemoMaintenanceMiddleware(_ => { called = true; return Task.CompletedTask; },
            Options.Create(new DemoModeOptions { Enabled = demo }), flag);
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);
        return (context, called);
    }

    [Fact]
    public async Task Middleware_WhileResetting_Answers503_WithTheTextAndBothHeaders_AndDoesNotCallTheApp()
    {
        var (context, nextCalled) = await RunMaintenanceAsync(demo: true, resetting: true);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(503);
        context.Response.Headers["Retry-After"].ToString().Should().Be("60");
        context.Response.Headers["X-Demo-Resetting"].ToString().Should().Be("1");
        context.Response.ContentType.Should().StartWith("text/plain");
        context.Response.Body.Position = 0;
        new StreamReader(context.Response.Body).ReadToEnd().Should().Be("Демо обновляется, зайдите через минуту");
    }

    [Theory]
    [InlineData("/api/health/ready", "GET")]
    [InlineData("/api/demo/status", "GET")]
    [InlineData("/", "GET")]
    public async Task Middleware_WhileResetting_LetsHealthStatusAndStaticFilesThrough(string path, string method)
    {
        var (context, nextCalled) = await RunMaintenanceAsync(demo: true, resetting: true, path, method);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Middleware_NotResetting_PassesThrough()
    {
        var (_, nextCalled) = await RunMaintenanceAsync(demo: true, resetting: false);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Middleware_OutsideDemoMode_IsInert_EvenWithAFlagFileLyingAround()
    {
        var (context, nextCalled) = await RunMaintenanceAsync(demo: false, resetting: true);

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    // ── noindex header ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, "noindex, nofollow")]
    [InlineData(false, "")]
    public async Task ResponseHeaders_AreSetOnlyInDemoMode(bool demo, string expected)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        var middleware = new DemoResponseHeadersMiddleware(ctx => ctx.Response.WriteAsync("x"), Options.Create(new DemoModeOptions { Enabled = demo }));
        context.Response.Body = new MemoryStream();

        // DefaultHttpContext has no real OnStarting pipeline: run the callbacks the way the server would, right before the body.
        var feature = new TestResponseFeature();
        context.Features.Set<IHttpResponseFeature>(feature);
        await middleware.InvokeAsync(context);
        await feature.FireOnStartingAsync();

        context.Response.Headers["X-Robots-Tag"].ToString().Should().Be(expected);
    }

    private sealed class TestResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _starting = [];
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => false;
        public void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));
        public void OnCompleted(Func<object, Task> callback, object state) { }
        public async Task FireOnStartingAsync()
        {
            foreach (var (callback, state) in _starting.AsEnumerable().Reverse()) await callback(state);
        }
    }

    // ── [DemoOnly] ──────────────────────────────────────────────────────────────────────────────────

    private static ResourceExecutingContext ResourceContext(bool demo, IEnumerable<object> endpointMetadata, ClaimsPrincipal? user = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new DemoModeOptions { Enabled = demo }))
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        if (user is not null) http.User = user;
        var descriptor = new ActionDescriptor { EndpointMetadata = endpointMetadata.ToList() };
        return new ResourceExecutingContext(new ActionContext(http, new RouteData(), descriptor), [], []);
    }

    [Fact]
    public void DemoOnly_OutsideDemoMode_Answers404WithAnEmptyBody_BeforeAnyAction()
    {
        var context = ResourceContext(demo: false, []);

        new DemoOnlyAttribute().OnResourceExecuting(context);

        context.Result.Should().BeOfType<NotFoundResult>("an empty 404, not a NotFound(\"...\") with a body");
    }

    [Fact]
    public void DemoOnly_InDemoMode_LetsTheRequestThrough()
    {
        var context = ResourceContext(demo: true, []);

        new DemoOnlyAttribute().OnResourceExecuting(context);

        context.Result.Should().BeNull();
    }

    // ── [DemoForbidden] ─────────────────────────────────────────────────────────────────────────────

    private static ClaimsPrincipal User(bool demoClaim, bool authenticated = true)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "u1") };
        if (demoClaim) claims.Add(new Claim("sb_demo", "1"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "test" : null));
    }

    private static async Task<(ResourceExecutingContext Context, bool NextCalled)> RunForbiddenAsync(bool demo, bool marked, ClaimsPrincipal user)
    {
        object[] metadata = marked ? [new DemoForbiddenAttribute()] : [];
        var context = ResourceContext(demo, metadata, user);
        var called = false;
        var filter = new DemoForbiddenFilter(Options.Create(new DemoModeOptions { Enabled = demo }));

        await filter.OnResourceExecutionAsync(context, () =>
        {
            called = true;
            return Task.FromResult(new ResourceExecutedContext(context, []));
        });
        return (context, called);
    }

    [Fact]
    public async Task DemoForbidden_DemoToken_MarkedAction_Is403WithATextBody_AndTheHeader()
    {
        var (context, nextCalled) = await RunForbiddenAsync(demo: true, marked: true, User(demoClaim: true));

        nextCalled.Should().BeFalse("the action must not run");
        var result = context.Result.Should().BeOfType<ContentResult>().Subject;
        result.StatusCode.Should().Be(403);
        result.ContentType.Should().StartWith("text/plain");
        result.Content.Should().Be("В демо-версии это действие недоступно.");
        context.HttpContext.Response.Headers["X-Demo-Restricted"].ToString().Should().Be("1");
    }

    [Theory]
    [InlineData(true, false, true, "an unmarked action is not restricted")]
    [InlineData(true, true, false, "a visitor who registered himself has no sb_demo and is not restricted")]
    [InlineData(false, true, true, "outside demo mode nothing changes")]
    public async Task DemoForbidden_OtherCombinations_PassThrough(bool demo, bool marked, bool demoClaim, string why)
    {
        var (context, nextCalled) = await RunForbiddenAsync(demo, marked, User(demoClaim));

        nextCalled.Should().BeTrue(why);
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task DemoForbidden_AnonymousCaller_IsNotTouched_AuthorizationAnswersFirst()
    {
        var (context, nextCalled) = await RunForbiddenAsync(demo: true, marked: true, User(demoClaim: true, authenticated: false));

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public void DemoForbidden_IsDemoToken_ReadsTheClaimValue()
    {
        DemoForbiddenFilter.IsDemoToken(User(demoClaim: true)).Should().BeTrue();
        DemoForbiddenFilter.IsDemoToken(User(demoClaim: false)).Should().BeFalse();
        DemoForbiddenFilter.IsDemoToken(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sb_demo", "0")], "test"))).Should().BeFalse();
    }
}
