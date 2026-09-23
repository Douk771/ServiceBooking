using FluentAssertions;
using ServiceBooking.LegalKit;

namespace ServiceBooking.UnitTests.LegalKit;

/// <summary>`legal.values.json` validation (ARCHITECTURE_CYCLE11.md §103.3, risk A3/A12) — pure parsing
/// against a temp file, no server, no database.</summary>
public class PlaceholderValuesTests
{
    private static string WriteTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "legal-values-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Load_AllThirteenKeysPresentAndNonEmpty_Succeeds()
    {
        var path = WriteTempFile(LegalKitFixture.ValidValuesJson());
        try
        {
            var values = PlaceholderValues.Load(path);
            values.Values.Should().HaveCount(13);
            values.Values["НАИМЕНОВАНИЕ_ОПЕРАТОРА"].Should().Be("ООО Тест");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MissingKey_ThrowsListingWhichKeyIsMissing()
    {
        var path = WriteTempFile("""{ "values": {} }""");
        try
        {
            var act = () => PlaceholderValues.Load(path);
            var ex = act.Should().Throw<PlaceholderValuesException>().Which;
            ex.Problems.Should().Contain(p => p.Contains("НАИМЕНОВАНИЕ_ОПЕРАТОРА") && p.Contains("отсутствует"));
            ex.Problems.Should().HaveCount(PlaceholderValues.RequiredKeys.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_EmptyStringValue_IsRejected()
    {
        var path = WriteTempFile("""{ "values": { "НАИМЕНОВАНИЕ_ОПЕРАТОРА": "" } }""");
        try
        {
            var act = () => PlaceholderValues.Load(path);
            act.Should().Throw<PlaceholderValuesException>()
                .Which.Problems.Should().Contain(p => p.Contains("НАИМЕНОВАНИЕ_ОПЕРАТОРА") && p.Contains("пустое"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_UnknownExtraKey_IsRejected()
    {
        var path = WriteTempFile("""{ "values": { "НЕИЗВЕСТНЫЙ_КЛЮЧ": "x" } }""");
        try
        {
            var act = () => PlaceholderValues.Load(path);
            act.Should().Throw<PlaceholderValuesException>()
                .Which.Problems.Should().Contain(p => p.Contains("НЕИЗВЕСТНЫЙ_КЛЮЧ") && p.Contains("неизвестный ключ"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_FileMissing_Throws()
    {
        var act = () => PlaceholderValues.Load(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".json"));
        act.Should().Throw<PlaceholderValuesException>();
    }

    [Fact]
    public void Load_InvalidJson_Throws()
    {
        var path = WriteTempFile("not json");
        try
        {
            var act = () => PlaceholderValues.Load(path);
            act.Should().Throw<PlaceholderValuesException>();
        }
        finally { File.Delete(path); }
    }

    // contracts/cycle11/legal-values.schema.json imposes shape checks beyond "present and non-empty"
    // (ARCHITECTURE_CYCLE11.md §111: "прав контракт"). The scenario the review called out by name:
    // an ИНН that's obviously a placeholder-for-a-placeholder, not a real one.
    [Fact]
    public void Load_InnIsNotDigits_IsRejected()
    {
        var json = LegalKitFixture.ValidValuesJson().Replace("\"1234567890\"", "\"уточнить у бухгалтера\"");
        var path = WriteTempFile(json);
        try
        {
            var act = () => PlaceholderValues.Load(path);
            act.Should().Throw<PlaceholderValuesException>()
                .Which.Problems.Should().Contain(p => p.Contains("ИНН_ОПЕРАТОРА") && p.Contains("формату"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("123456789")]   // 9 digits — neither 10 nor 12
    [InlineData("12345678901")] // 11 digits
    public void Load_InnWrongLength_IsRejected(string badInn)
    {
        var json = LegalKitFixture.ValidValuesJson().Replace("\"1234567890\"", $"\"{badInn}\"");
        var path = WriteTempFile(json);
        try
        {
            var act = () => PlaceholderValues.Load(path);
            act.Should().Throw<PlaceholderValuesException>()
                .Which.Problems.Should().Contain(p => p.Contains("ИНН_ОПЕРАТОРА"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_OgrnWrongLength_IsRejected()
    {
        var json = LegalKitFixture.ValidValuesJson().Replace("\"1234567890123\"", "\"12345\"");
        var path = WriteTempFile(json);
        try
        {
            var act = () => PlaceholderValues.Load(path);
            act.Should().Throw<PlaceholderValuesException>()
                .Which.Problems.Should().Contain(p => p.Contains("ОГРН_ОПЕРАТОРА"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_RknDateWrongFormat_IsRejected()
    {
        var json = LegalKitFixture.ValidValuesJson().Replace("\"01.01.2026\"", "\"2026-01-01\"");
        var path = WriteTempFile(json);
        try
        {
            var act = () => PlaceholderValues.Load(path);
            act.Should().Throw<PlaceholderValuesException>()
                .Which.Problems.Should().Contain(p => p.Contains("ДАТА_УВЕДОМЛЕНИЯ_РКН"));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("test@example.com", true)]
    [InlineData("не похоже на почту", false)]
    [InlineData("тест@", false)]
    public void Load_ContactEmail_ValidatesFormat(string email, bool shouldPass)
    {
        var json = LegalKitFixture.ValidValuesJson().Replace("\"test@example.com\"", $"\"{email}\"");
        var path = WriteTempFile(json);
        try
        {
            var act = () => PlaceholderValues.Load(path);
            if (shouldPass)
                act.Should().NotThrow();
            else
                act.Should().Throw<PlaceholderValuesException>()
                    .Which.Problems.Should().Contain(p => p.Contains("ПОЧТА_ДЛЯ_ОБРАЩЕНИЙ"));
        }
        finally { File.Delete(path); }
    }
}
