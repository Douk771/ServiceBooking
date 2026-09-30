using FluentAssertions;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Showcase;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE28.md §575.2, §575.4, §574.4 — the generator's own random source, stable ids and the phone block.</summary>
public class ShowcasePrimitivesTests
{
    [Fact]
    public void Random_SameKey_GivesTheSameSequence_DifferentKey_GivesAnother()
    {
        var a = new ShowcaseRandom("prod:x");
        var b = new ShowcaseRandom("prod:x");
        var c = new ShowcaseRandom("prod:y");

        var first = Enumerable.Range(0, 20).Select(_ => a.NextUInt64()).ToList();

        first.Should().Equal(Enumerable.Range(0, 20).Select(_ => b.NextUInt64()));
        first.Should().NotEqual(Enumerable.Range(0, 20).Select(_ => c.NextUInt64()));
    }

    [Fact]
    public void Random_Algorithm_IsPinned()
    {
        // xorshift64* with an FNV-1a/SplitMix64 seed. If this fails the algorithm changed and EVERY re-seed would produce a different showcase: do not edit the
        // numbers, add a new class instead (ARCHITECTURE_CYCLE28.md §575.2).
        var random = new ShowcaseRandom("pin");

        Enumerable.Range(0, 4).Select(_ => random.NextUInt64()).Should().Equal(
            587080358670913181UL, 69869100464313909UL, 14493240508606947591UL, 7991180051423591715UL);
    }

    [Fact]
    public void Random_Ranges_AreRespected()
    {
        var random = new ShowcaseRandom("ranges");
        for (var i = 0; i < 5000; i++)
        {
            random.Next(7).Should().BeInRange(0, 6);
            random.Next(3, 9).Should().BeInRange(3, 8);
            random.NextDouble().Should().BeInRange(0, 0.999999999999);
        }
    }

    [Fact]
    public void Random_Next_RefusesAnEmptyRange()
    {
        var random = new ShowcaseRandom("k");
        FluentActions.Invoking(() => random.Next(0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => random.Next(5, 5)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Random_Shuffle_IsAPermutation_AndDeterministic()
    {
        var items = Enumerable.Range(0, 50).ToList();
        var a = items.ToList();
        var b = items.ToList();

        new ShowcaseRandom("s").Shuffle(a);
        new ShowcaseRandom("s").Shuffle(b);

        a.Should().Equal(b);
        a.Should().BeEquivalentTo(items);
        a.Should().NotEqual(items);
    }

    [Fact]
    public void Random_Chance_RoughlyFollowsTheProbability()
    {
        var random = new ShowcaseRandom("chance");

        var hits = Enumerable.Range(0, 10000).Count(_ => random.Chance(0.3));

        hits.Should().BeInRange(2700, 3300);
    }

    [Fact]
    public void Ids_V5_MatchesTheRfcTestVector() =>
        ShowcaseIds.V5(Guid.Parse("6ba7b810-9dad-11d1-80b4-00c04fd430c8"), "www.example.com")
            .Should().Be(Guid.Parse("2ed6657d-e927-568b-95e1-2665a8aea6a2"));

    [Fact]
    public void Ids_AreStable_AndDependOnProfileKindAndKey()
    {
        ShowcaseIds.For("prod", "company", "lavanda").Should().Be(ShowcaseIds.For("prod", "company", "lavanda"));
        ShowcaseIds.For("prod", "company", "lavanda").Should().Be(Guid.Parse("91539507-fd12-5ad8-8d6c-8ebe696dcfbc"), "ids must never change between releases");
        new[]
        {
            ShowcaseIds.For("prod", "company", "lavanda"), ShowcaseIds.For("demo", "company", "lavanda"),
            ShowcaseIds.For("prod", "user", "lavanda"), ShowcaseIds.For("prod", "company", "borodach"),
        }.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Phones_AreSequentialInsideTheBlock_Unique_AndPassTheProductsOwnValidation()
    {
        var phones = new ShowcasePhones();
        var issued = Enumerable.Range(0, 600).Select(_ => phones.Next()).ToList();

        issued.Should().OnlyHaveUniqueItems();
        issued.First().Should().Be("72005550000");
        issued.Should().OnlyContain(p => ShowcasePhones.IsShowcasePhone(p));
        foreach (var phone in issued)
        {
            PhoneNormalizer.TryNormalizeRussian(phone, out var canonical).Should().BeTrue();
            canonical.Should().Be(phone);
        }
        phones.Issued.Should().Be(600);
    }

    [Fact]
    public void Phones_TheBlockIsExhaustedLoudly()
    {
        var phones = new ShowcasePhones();
        for (var i = 0; i < 10000; i++) phones.Next();

        FluentActions.Invoking(() => phones.Next()).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("72005550000", true)]
    [InlineData("72005559999", true)]
    [InlineData("72005560000", false)]
    [InlineData("71995559999", false)]
    [InlineData("79001234567", false)]
    [InlineData("7200555000", false)]
    [InlineData("+72005550000", false)]
    [InlineData(null, false)]
    public void Phones_IsShowcasePhone_IsExactlyTheBlock(string? phone, bool expected) =>
        ShowcasePhones.IsShowcasePhone(phone).Should().Be(expected);
}
