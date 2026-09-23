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
    // §103.3: "310 ± 10 строк итого (сейчас 91)" — 91 pre-existing (AddNotificationChannels.SeedRows)
    // + this migration's own rows. Reads AddNotificationChannels.SeedRows.Length directly instead of a
    // hand-typed "91" constant: a hand-typed count can't see a merge-conflict-driven shrink of the OLD
    // seed array, only of ExpandCityDirectory.Rows. Both arrays are public static and require no
    // database, so this stays a pure in-memory comparison.
    [Fact]
    public void Rows_TotalWithExistingDirectoryIsWithinTargetRange()
    {
        var total = AddNotificationChannels.SeedRows.Length + ExpandCityDirectory.Rows.Length;

        total.Should().BeInRange(300, 320, "§103.3 targets 310 ± 10 rows total across both migrations");
    }

    // §103.3 point 3: the Up() INSERT is a correlated "WHERE NOT EXISTS" per (Name, Region) against
    // whatever is already in the table — including the 91 rows AddNotificationChannels seeds. A
    // (Name, Region) pair present in BOTH arrays would not raise the row count by one the way every other
    // entry in Rows does: NOT EXISTS silently skips it, so §103.3's "91 + 210 = 301" claim would be false
    // for that row without any migration-time signal. This is the one check in this class that reasons
    // about the STITCH between the two migrations rather than either array in isolation.
    [Fact]
    public void Rows_HasNoOverlapWithPreExistingSeedDirectory()
    {
        var preExisting = AddNotificationChannels.SeedRows
            .Select(r => (r.Name, r.Region))
            .ToHashSet();

        var overlapping = ExpandCityDirectory.Rows
            .Where(r => preExisting.Contains((r.Name, r.Region)))
            .Select(r => $"{r.Name}, {r.Region}")
            .ToList();

        overlapping.Should().BeEmpty(
            "any (Name, Region) pair already present in AddNotificationChannels.SeedRows would be " +
            "silently skipped by ExpandCityDirectory's \"WHERE NOT EXISTS\" INSERT, not actually added");
    }

    // ExpandCityDirectory's own class doc comment lists exactly three regions where a NEW row is
    // deliberately given a DIFFERENT (and more correct) TimeZoneId than the PRE-EXISTING row for that
    // same region — Волгоград/Саратов/Ульяновск were seeded (cycle 4) as Europe/Moscow before corrected
    // tzdata offsets were used, and this migration intentionally does not retroactively fix those three
    // pre-existing rows. Outside that documented, closed list, a region-zone mismatch between the two
    // seed arrays is not a deliberate choice — it is the exact "wrong, more obvious choice is one hour
    // off" mistake the migration's own doc comment warns about (as already happened once for Барнаул vs
    // Asia/Novosibirsk), and nothing short of this comparison would catch it before it reaches a company's
    // reminder/visit-time calculation.
    private static readonly HashSet<string> RegionsWithDocumentedPreExistingZoneMismatch =
        new() { "Волгоградская область", "Саратовская область", "Ульяновская область" };

    [Fact]
    public void Rows_TimeZoneIdIsConsistentPerRegionAcrossBothSeedArrays_ExceptDocumentedMismatches()
    {
        var zoneByRegion = AddNotificationChannels.SeedRows
            .Select(r => (r.Name, r.Region, r.TimeZoneId))
            .Concat(ExpandCityDirectory.Rows)
            .Where(r => !RegionsWithDocumentedPreExistingZoneMismatch.Contains(r.Region))
            .GroupBy(r => r.Region)
            .Where(g => g.Select(r => r.TimeZoneId).Distinct().Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(r => $"{r.Name}={r.TimeZoneId}").Distinct())}")
            .ToList();

        zoneByRegion.Should().BeEmpty(
            "every city in the same region must resolve to the same IANA time zone, outside the three " +
            "regions ExpandCityDirectory's own doc comment documents as a deliberate, un-fixed pre-existing mismatch");
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
