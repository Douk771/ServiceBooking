using FluentAssertions;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.LegalKit;
using ServiceBooking.LegalKit.Commands;

namespace ServiceBooking.UnitTests.LegalKit;

/// <summary>CY48-B7 (ARCHITECTURE_CYCLE48_STOREFRONT.md §48.13.1, API_CONTRACT_CYCLE48_STOREFRONT.md §48.29): the site requisites must equal legal.values.json.</summary>
public class RequisitesDriftTests
{
    private static readonly string[] Keys = ["ИНН_ОПЕРАТОРА", "ПОЧТОВЫЙ_АДРЕС"];

    private static Dictionary<string, string> Site() => new() { ["ИНН_ОПЕРАТОРА"] = "222410420811", ["ПОЧТОВЫЙ_АДРЕС"] = "г. Барнаул, ул. Ленина, 1", ["СРОК_СЧЁТА"] = "на сайте" };

    [Fact]
    public void Equal_GivesNoDifferences() =>
        RequisitesDrift.Compare(new Dictionary<string, string>(Site()) { ["лишнее"] = "x" }, Site(), Keys).Should().BeEmpty();

    [Fact]
    public void OneDifferentKey_IsReportedWithBothValues()
    {
        var legal = Site();
        legal["ИНН_ОПЕРАТОРА"] = "222410420812";

        var differences = RequisitesDrift.Compare(legal, Site(), Keys);

        differences.Should().ContainSingle().Which.Should().Be(new RequisitesDrift.Difference("ИНН_ОПЕРАТОРА", "222410420812", "222410420811"));
    }

    [Fact]
    public void MissingKeyInLegalValues_IsADifference_WithNullLegalValue()
    {
        var legal = Site();
        legal.Remove("ПОЧТОВЫЙ_АДРЕС");

        RequisitesDrift.Compare(legal, Site(), Keys).Should().ContainSingle()
            .Which.Should().Be(new RequisitesDrift.Difference("ПОЧТОВЫЙ_АДРЕС", null, "г. Барнаул, ул. Ленина, 1"));
    }

    [Theory]
    [InlineData(" г. Барнаул, ул. Ленина, 1")]
    [InlineData("г. Барнаул, ул. Ленина, 1 ")]
    [InlineData("г. Барнаул,  ул. Ленина, 1")]
    [InlineData("Г. Барнаул, ул. Ленина, 1")]
    [InlineData("г. Барнаул, ул. Ленина, 1 ")]
    public void AWhitespaceOrCaseDifference_Counts(string legalValue)
    {
        var legal = Site();
        legal["ПОЧТОВЫЙ_АДРЕС"] = legalValue;

        RequisitesDrift.Compare(legal, Site(), Keys).Should().ContainSingle().Which.Key.Should().Be("ПОЧТОВЫЙ_АДРЕС");
    }

    [Fact]
    public void OnlySharedKeys_AreCompared()
    {
        var legal = Site();
        legal["СРОК_СЧЁТА"] = "в файле значений другое";

        RequisitesDrift.Compare(legal, Site(), Keys).Should().BeEmpty();
    }

    [Fact]
    public void SharedKeyAbsentFromTheSiteFile_Throws()
    {
        var act = () => RequisitesDrift.Compare(Site(), Site(), ["НЕТ_НА_САЙТЕ"]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*НЕТ_НА_САЙТЕ*");
    }

    [Fact]
    public void RefusalText_NamesTheKeyAndBothValues()
    {
        var text = RequisitesDrift.RefusalText(new RequisitesDrift.Difference("ИНН_ОПЕРАТОРА", "1", "2"));
        var missing = RequisitesDrift.RefusalText(new RequisitesDrift.Difference("ИНН_ОПЕРАТОРА", null, "2"));

        text.Should().Contain("ИНН_ОПЕРАТОРА").And.Contain("на сайте «2»").And.Contain("в файле значений «1»").And.Contain("ТЛ48-06");
        missing.Should().Contain("«— нет —»");
    }

    [Fact]
    public void StatusLines_MarkEachSharedKey()
    {
        var legal = Site();
        legal["ПОЧТОВЫЙ_АДРЕС"] = "другой";

        var lines = RequisitesDrift.StatusLines(legal, Site(), Keys);

        lines[0].Should().Be("Реквизиты сайта (contracts/legal/operator-public.json) против legal.values.json:");
        lines[1].Should().Be("  ИНН_ОПЕРАТОРА: совпадает");
        lines[2].Should().Be("  ПОЧТОВЫЙ_АДРЕС: РАСХОДИТСЯ — на сайте «г. Барнаул, ул. Ленина, 1», в файле «другой»");
    }

    [Fact]
    public void Publish_WithDriftedRequisite_ExitsWith5_AndWritesNothing()
    {
        var source = LegalKitFixture.CreateSourceDir();
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        var drifted = LegalKitFixture.PublishableValuesJson().Replace(OperatorPublicRequisites.Get("ИНН_ОПЕРАТОРА"), "111111111111");
        var valuesPath = Path.Combine(Path.GetTempPath(), "legal-values-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(valuesPath, drifted);
        try
        {
            LegalSourceSet.Build(source, built);

            var exitCode = PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-10-05", "--effective-from", "2026-10-05"]));

            exitCode.Should().Be(5);
            Directory.Exists(outDir).Should().BeFalse("a refusal writes nothing");
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(built);
            LegalKitFixture.Delete(outDir);
            File.Delete(valuesPath);
        }
    }

    [Fact]
    public void Publish_WithValuesMissingASharedKey_IsRefusedNotCrashed()
    {
        // ПОЧТОВЫЙ_АДРЕС removed: the values loader already rejects it, drift must not turn that into an exception.
        var source = LegalKitFixture.CreateSourceDir();
        var built = Path.Combine(Path.GetTempPath(), "legalkit-built-" + Guid.NewGuid().ToString("N"));
        var outDir = Path.Combine(Path.GetTempPath(), "legalkit-operator-" + Guid.NewGuid().ToString("N"));
        var json = LegalKitFixture.PublishableValuesJson();
        var valuesPath = Path.Combine(Path.GetTempPath(), "legal-values-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(valuesPath, json.Replace("\"ПОЧТОВЫЙ_АДРЕС\"", "\"ПОЧТОВЫЙ_АДРЕС_2\""));
        try
        {
            LegalSourceSet.Build(source, built);

            PublishCommand.Run(CliArgs.Parse(
                ["--source", built, "--out", outDir, "--values", valuesPath, "--version", "2026-10-05", "--effective-from", "2026-10-05"]))
                .Should().Be(5);
        }
        finally
        {
            LegalKitFixture.Delete(source);
            LegalKitFixture.Delete(built);
            LegalKitFixture.Delete(outDir);
            File.Delete(valuesPath);
        }
    }
}
