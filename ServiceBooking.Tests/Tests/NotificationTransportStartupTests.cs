using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// QA cycle 9 (ARCHITECTURE_CYCLE9.md §104.2, task B13's "нераспознанный транспорт в конфиге роняет
/// старт") — <c>Program.cs</c> (§104.2/§367-410) throws <c>InvalidOperationException</c> for an
/// unrecognized <c>Notifications:Provider</c> value BEFORE the host finishes building (fail-loud, not a
/// silent fallback to the logging stub that would let a Production deployment believe notifications are
/// going out when nothing is). This is the one functional (host-boot-level) test that can actually prove
/// that — a unit test can exercise the registry's own <c>MissingTransportImplementationException</c> in
/// isolation, but only booting the real <c>Program</c> proves the WHOLE process refuses to start.
///
/// Deliberately its own tiny factory/class (not reusing <see cref="TestDatabaseFixture"/>/
/// <see cref="ApiTestBase"/>): the host under test must NEVER reach a healthy state, so there is nothing
/// to clean up or share — one throwaway database-less connection string is enough (Program.cs's
/// Notifications:Provider switch runs before migrations ever touch the database).
/// </summary>
public class NotificationTransportStartupTests
{
    [Fact, TestCase("MAX-005")]
    public void UnrecognizedProvider_ThrowsAtStartup_BeforeHostBecomesHealthy()
    {
        // A syntactically valid but never-created connection string — sufficient because the offending
        // code (Program.cs's `default: throw new InvalidOperationException(...)`) runs as a top-level
        // statement, before `builder.Build()`/migrations are reached, so this factory must never actually
        // open the database to prove the point.
        const string unusedConnectionString =
            "Host=127.0.0.1;Port=1;Database=sb_startup_qa9_unused;Username=postgres;Password=postgres";

        using var factory = new BadProviderFactory(unusedConnectionString);

        // WebApplicationFactory surfaces a startup-time exception thrown by top-level Program.cs code
        // when its Services property is first touched (it builds the host lazily on first access).
        var act = () => _ = factory.Services;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown Notifications:Provider*",
                "an unrecognized transport/provider value must fail LOUD at startup, never silently fall back");
    }

    private sealed class BadProviderFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            TestHostSettings.Apply(builder, "startup-qa9", connectionString);
            builder.UseSetting("Notifications:Provider", "definitely-not-a-real-provider");
        }
    }
}
