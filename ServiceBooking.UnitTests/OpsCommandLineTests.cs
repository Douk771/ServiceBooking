using FluentAssertions;
using ServiceBooking.API.Services.Ops;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE28.md §575.1, API_CONTRACT_CYCLE28.md §602 — every form of the operator command line.</summary>
public class OpsCommandLineTests
{
    [Theory]
    [InlineData]
    [InlineData("--urls=http://+:8080")]
    [InlineData("tariffs", "apply")]
    public void Parse_ReturnsNull_WhenFirstArgumentIsNotOps(params string[] args) =>
        OpsCommandLine.Parse(args).Should().BeNull();

    [Theory]
    [InlineData(OpsAction.TariffsPlan, "tariffs", "plan")]
    [InlineData(OpsAction.TariffsApply, "tariffs", "apply")]
    [InlineData(OpsAction.ShowcasePlan, "showcase", "plan")]
    [InlineData(OpsAction.ShowcaseCreate, "showcase", "create")]
    [InlineData(OpsAction.ShowcaseRecreate, "showcase", "recreate")]
    [InlineData(OpsAction.ShowcaseDelete, "showcase", "delete")]
    [InlineData(OpsAction.DemoReset, "demo", "reset")]
    public void Parse_RecognisesEveryCommand(OpsAction expected, params string[] words)
    {
        var cli = OpsCommandLine.Parse(["ops", .. words]);

        cli.Should().NotBeNull();
        cli!.Error.Should().BeNull();
        cli.Action.Should().Be(expected);
        cli.Confirmed.Should().BeFalse("nothing changes without --yes");
    }

    [Theory]
    [InlineData("create", ShowcasePlanKind.Create)]
    [InlineData("recreate", ShowcasePlanKind.Recreate)]
    [InlineData("delete", ShowcasePlanKind.Delete)]
    public void Parse_ShowcasePlan_CarriesWhatToPlan(string word, ShowcasePlanKind expected)
    {
        var cli = OpsCommandLine.Parse(["ops", "showcase", "plan", word]);

        cli!.Action.Should().Be(OpsAction.ShowcasePlan);
        cli.PlanOf.Should().Be(expected);
    }

    [Fact]
    public void Parse_ShowcasePlanWithoutKind_LeavesItToTheRunner() =>
        OpsCommandLine.Parse(["ops", "showcase", "plan"])!.PlanOf.Should().BeNull();

    [Theory]
    [InlineData("--yes")]
    [InlineData("--YES")]
    public void Parse_Yes_ConfirmsInAnyPosition(string yes)
    {
        OpsCommandLine.Parse(["ops", "showcase", "recreate", yes])!.Confirmed.Should().BeTrue();
        OpsCommandLine.Parse(["ops", yes, "showcase", "recreate"])!.Confirmed.Should().BeTrue();
    }

    [Fact]
    public void Parse_IsCaseInsensitive_ForOpsAndWords()
    {
        var cli = OpsCommandLine.Parse(["OPS", "Showcase", "DELETE"]);

        cli!.Action.Should().Be(OpsAction.ShowcaseDelete);
    }

    [Fact]
    public void Parse_KeepsConfigurationTokensForTheHost_AndOnlyThose()
    {
        var cli = OpsCommandLine.Parse(["ops", "tariffs", "plan", "--Logging:LogLevel:Default=Warning", "--yes"]);

        cli!.HostArgs.Should().Equal("--Logging:LogLevel:Default=Warning");
        cli.Action.Should().Be(OpsAction.TariffsPlan);
    }

    [Theory]
    [InlineData("ops")]
    [InlineData("ops", "tariffs")]
    [InlineData("ops", "showcase", "explode")]
    [InlineData("ops", "showcase", "plan", "everything")]
    [InlineData("ops", "demo")]
    [InlineData("ops", "tariffs", "plan", "extra")]
    public void Parse_UnknownOrIncompleteCommand_IsAnErrorWithExitCode64(params string[] args)
    {
        var cli = OpsCommandLine.Parse(args);

        cli.Should().NotBeNull();
        cli!.Action.Should().BeNull();
        cli.Error.Should().NotBeNullOrWhiteSpace();
        OpsCommandLine.ExitUnknownCommand.Should().Be(64);
    }

    [Fact]
    public void Parse_UnknownOption_IsAnError()
    {
        var cli = OpsCommandLine.Parse(["ops", "showcase", "delete", "--force"]);

        cli!.Action.Should().BeNull();
        cli.Error.Should().Contain("--force");
    }

    [Fact]
    public void Help_NamesEveryCommand()
    {
        foreach (var word in new[] { "tariffs plan", "tariffs apply", "showcase plan", "showcase create", "showcase recreate", "showcase delete", "demo reset" })
            OpsCommandLine.Help.Should().Contain(word);
    }

    // ── ARCHITECTURE_CYCLE35.md §35.9.6: --profile ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("--profile", "demo")]
    [InlineData("--PROFILE", "DEMO")]
    [InlineData("--profile=demo")]
    public void Parse_ProfileDemo_IsRecognisedInAnyForm_AndIsNotAHostArgument(params string[] profileTokens)
    {
        var cli = OpsCommandLine.Parse(["ops", "showcase", "plan", .. profileTokens]);

        cli!.Error.Should().BeNull();
        cli.Action.Should().Be(OpsAction.ShowcasePlan);
        cli.Profile.Should().Be(OpsCommandLine.DemoProfile);
        cli.HostArgs.Should().BeEmpty("--profile=demo must not reach the host configuration");
    }

    [Fact]
    public void Parse_WithoutProfile_ThereIsNoProfile_SoTheProductionOutputOfCycle28StaysUntouched() =>
        OpsCommandLine.Parse(["ops", "showcase", "plan"])!.Profile.Should().BeNull();

    [Fact]
    public void Parse_ProfileProd_IsAccepted() =>
        OpsCommandLine.Parse(["ops", "showcase", "create", "--profile", "prod"])!.Profile.Should().Be(OpsCommandLine.ProdProfile);

    [Theory]
    [InlineData("showcase", "create")]
    [InlineData("showcase", "recreate")]
    [InlineData("showcase", "delete")]
    public void Parse_ProfileDemo_OnAChangingCommand_StillParses_TheRunnerRefusesItWithExitCode2(params string[] words)
    {
        var cli = OpsCommandLine.Parse(["ops", .. words, "--profile", "demo", "--yes"]);

        cli!.Error.Should().BeNull();
        cli.Profile.Should().Be("demo");
        OpsCommandLine.DemoProfileRefusal.Should().Be("Профиль demo создаётся только сбросом демо: ops demo reset");
    }

    [Theory]
    [InlineData("--profile")]
    [InlineData("--profile", "staging")]
    [InlineData("--profile=")]
    public void Parse_ProfileWithoutOrWithAnUnknownValue_IsAnError(params string[] profileTokens) =>
        OpsCommandLine.Parse(["ops", "showcase", "plan", .. profileTokens])!.Error.Should().NotBeNullOrEmpty();

    [Fact]
    public void Parse_ProfileOnANonShowcaseCommand_IsAnError() =>
        OpsCommandLine.Parse(["ops", "demo", "reset", "--profile", "demo"])!.Error.Should().Contain("showcase");
}
