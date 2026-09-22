using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.Infrastructure.Migrations;

namespace ServiceBooking.UnitTests;

/// <summary>
/// US-113 (ARCHITECTURE_CYCLE9.md §103.3). Reads <see cref="ExpandCityDirectory.Rows"/> — the exact
/// array the migration inserts — directly, no database required: this is what "зелёный без базы" means
/// for this migration. Guards against the two ways a hand-maintained geography list actually breaks in
/// practice: a time zone id that doesn't resolve on the runtime image (§103.3's whole reason for naming
/// tzdata/zone.tab as the source, not "whatever looked right"), and a duplicate (Name, Region) pair that
/// would otherwise only surface as a migration-time unique-index violation.
///
/// Each check loops over every row inside one [Fact] and reports every offending row in the failure
/// message, instead of exploding into one [Theory] case per row — ~200 rows is data, not 200 separate
/// behaviors, and TEST_CATALOG.md's per-cycle test counts would otherwise balloon on nothing but this
/// list growing.
/// </summary>
public class CityDirectoryDataTests
{
    // §103.3: "310 ± 10 строк итого (сейчас 91)" — 91 pre-existing (AddNotificationChannels/SeedCities)
    // + this migration's own rows. Asserting the exact row count here, not just "some new rows", makes an
    // accidental partial edit (someone deletes a chunk while resolving a merge conflict, say) fail loudly
    // instead of silently shipping a thinner directory than the migration's own header comment claims.
    [Fact]
    public void Rows_TotalWithExistingDirectoryIsWithinTargetRange()
    {
        const int preExistingRowCount = 91;
        var total = preExistingRowCount + ExpandCityDirectory.Rows.Length;

        total.Should().BeInRange(300, 320, "§103.3 targets 310 ± 10 rows total across both migrations");
    }

    [Fact]
    public void Rows_IsNotEmpty() => ExpandCityDirectory.Rows.Should().NotBeEmpty();

    [Fact]
    public void Rows_EveryTimeZoneIdResolves()
    {
        // Same call DeploymentSafetyChecks.ValidateTimeZoneDatabase makes for "Asia/Barnaul" — a city
        // whose zone doesn't resolve here would give a company in that city the wrong visit/reminder
        // times the moment someone creates it, without any migration-time signal that anything was wrong.
        var unresolvable = ExpandCityDirectory.Rows
            .Where(row =>
            {
                try
                {
                    TimeZoneInfo.FindSystemTimeZoneById(row.TimeZoneId);
                    return false;
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    return true;
                }
            })
            .Select(row => $"{row.Name}, {row.Region} -> {row.TimeZoneId}")
            .ToList();

        unresolvable.Should().BeEmpty();
    }

    [Fact]
    public void Rows_SearchNameRuleMatchesCitySearchNormalizeForEveryRow()
    {
        // The migration computes SearchName via ExpandCityDirectory.NormalizeForSearch (Infrastructure
        // can't reference CitySearch in the API project) — this is the check that keeps the two rules in
        // lockstep, the same role CitySearchTests plays for the ORIGINAL 91-row seed's hand-typed values.
        var mismatches = ExpandCityDirectory.Rows
            .Where(row => ExpandCityDirectory.NormalizeForSearch(row.Name) != CitySearch.Normalize(row.Name))
            .Select(row => row.Name)
            .ToList();

        mismatches.Should().BeEmpty();
    }

    [Fact]
    public void Rows_HasNoDuplicateNameRegionPairs()
    {
        var duplicates = ExpandCityDirectory.Rows
            .GroupBy(r => (r.Name, r.Region))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Name}, {g.Key.Region}")
            .ToList();

        duplicates.Should().BeEmpty("IX_Cities_Name_Region is unique — a duplicate here would fail the migration itself");
    }

    [Fact]
    public void Rows_EveryRegionAndNameIsNonBlank()
    {
        var blankRegion = ExpandCityDirectory.Rows.Where(r => string.IsNullOrWhiteSpace(r.Region)).Select(r => r.Name).ToList();
        var blankName = ExpandCityDirectory.Rows.Where(r => string.IsNullOrWhiteSpace(r.Name)).Select(r => r.Region).ToList();

        blankRegion.Should().BeEmpty("every row must have a Region — {0}", string.Join(", ", blankRegion));
        blankName.Should().BeEmpty("every row must have a Name — {0}", string.Join(", ", blankName));
    }
}
