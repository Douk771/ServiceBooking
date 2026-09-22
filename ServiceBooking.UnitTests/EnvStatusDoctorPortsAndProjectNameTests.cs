using FluentAssertions;
using ServiceBooking.TestKit;

namespace ServiceBooking.UnitTests;

/// <summary>
/// T8 doctor findings (2 and 3 in the session's task list):
///
/// (2) `doctor`'s `ports-free` check used to fail the whole run (exit code 2) whenever the DEV-STACK
/// ports (SB_DB_PORT/SB_API_PORT/SB_WEB_PORT — what `docker compose up` publishes) were occupied by a
/// neighbour, even though a test run never binds those ports itself (container mode uses a dynamic
/// Testcontainers port; server mode uses SERVICEBOOKING_TEST_CONNECTION). <see cref="EnvStatus.BuildPortsFreeCheck"/>
/// now always reports Ok=true (informational/warning only) while still surfacing genuine neighbour
/// conflicts in Detail — covered below without any socket I/O.
///
/// (3) `status`/`doctor` used to compare compose ownership against the now-defunct SB_PROJECT_NAME
/// variable (commit 8b288a9 removed docker-compose.yml's own use of it — compose derives the project
/// name from the checkout directory, overridable only via its own COMPOSE_PROJECT_NAME). <see cref="EnvStatus.DeriveComposeProjectName"/>
/// covers the pure directory-name-to-project-name normalization that now backs the default.
/// </summary>
public class EnvStatusDoctorPortsAndProjectNameTests
{
    // ── BuildPortsFreeCheck — always non-blocking (Ok=true), regardless of conflicts ──

    [Fact]
    public void BuildPortsFreeCheck_is_ok_and_unremarkable_when_no_port_is_in_use()
    {
        PortInfo[] ports =
        [
            new("SB_DB_PORT", 5432, "default", InUse: false, OwnedByThisCopy: false),
            new("SB_API_PORT", 5000, "default", InUse: false, OwnedByThisCopy: false),
        ];

        var check = EnvStatus.BuildPortsFreeCheck(ports);

        check.Name.Should().Be("ports-free");
        check.Ok.Should().BeTrue();
        check.Detail.Should().Contain("свободны или заняты этой же рабочей копией");
    }

    [Fact]
    public void BuildPortsFreeCheck_is_ok_when_ports_are_owned_by_this_working_copy()
    {
        PortInfo[] ports =
        [
            new("SB_DB_PORT", 5432, "default", InUse: true, OwnedByThisCopy: true),
        ];

        var check = EnvStatus.BuildPortsFreeCheck(ports);

        check.Ok.Should().BeTrue();
        check.Detail.Should().Contain("свободны или заняты этой же рабочей копией");
    }

    [Fact]
    public void BuildPortsFreeCheck_stays_ok_but_warns_when_a_neighbour_holds_a_dev_stack_port()
    {
        // This is finding 2's exact reproduction: a neighbour (local Postgres.app / a running API)
        // occupies the dev-stack ports. It must not push doctor's overall exit code to 2.
        PortInfo[] ports =
        [
            new("SB_DB_PORT", 5432, "default", InUse: true, OwnedByThisCopy: false),
            new("SB_API_PORT", 5000, "default", InUse: true, OwnedByThisCopy: false),
            new("SB_WEB_PORT", 5173, "default", InUse: false, OwnedByThisCopy: false),
        ];

        var check = EnvStatus.BuildPortsFreeCheck(ports);

        check.Ok.Should().BeTrue("a busy dev-stack port must not fail a test run's readiness check");
        check.Detail.Should().Contain("Предупреждение (не блокирует doctor)");
        check.Detail.Should().Contain("SB_DB_PORT=5432");
        check.Detail.Should().Contain("SB_API_PORT=5000");
        check.Detail.Should().NotContain("SB_WEB_PORT=5173", "only the actually-conflicting ports should be listed");
    }

    // ── DeriveComposeProjectName — mirrors compose-go's directory-derived default ──

    [Fact]
    public void DeriveComposeProjectName_lowercases_the_directory_name()
    {
        EnvStatus.DeriveComposeProjectName("/Users/dev/ServiceBooking").Should().Be("servicebooking");
    }

    [Fact]
    public void DeriveComposeProjectName_strips_characters_compose_would_reject()
    {
        EnvStatus.DeriveComposeProjectName("/Users/dev/ServiceBooking (worktree 2)").Should().Be("servicebookingworktree2");
    }

    [Fact]
    public void DeriveComposeProjectName_ignores_a_trailing_path_separator()
    {
        EnvStatus.DeriveComposeProjectName("/Users/dev/ServiceBooking/").Should().Be("servicebooking");
    }

    [Fact]
    public void DeriveComposeProjectName_strips_leading_non_alphanumeric_characters()
    {
        EnvStatus.DeriveComposeProjectName("/Users/dev/--ServiceBooking").Should().Be("servicebooking");
    }

    [Fact]
    public void DeriveComposeProjectName_falls_back_when_the_sanitized_name_is_empty()
    {
        EnvStatus.DeriveComposeProjectName("/Users/dev/---").Should().Be("servicebooking");
    }

    [Fact]
    public void DeriveComposeProjectName_distinguishes_two_differently_named_working_copies()
    {
        // The exact scenario finding 3 describes: a second checkout (e.g. "ServiceBooking-wt2") must
        // get its OWN derived name, not silently fall back to the first copy's "servicebooking".
        EnvStatus.DeriveComposeProjectName("/Users/dev/ServiceBooking-wt2").Should().Be("servicebooking-wt2");
        EnvStatus.DeriveComposeProjectName("/Users/dev/ServiceBooking-wt2").Should().NotBe(EnvStatus.DeriveComposeProjectName("/Users/dev/ServiceBooking"));
    }
}
