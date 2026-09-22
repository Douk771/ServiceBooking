using System.Collections.Concurrent;
using FluentAssertions;
using ServiceBooking.TestKit;

namespace ServiceBooking.UnitTests;

/// <summary>
/// T9 review: no coverage existed for <see cref="TestSlot.NextForClass"/> — the review's own words,
/// "the regexp already accepts c01..c999, nothing needed to change", was an assertion that held the
/// entire safety story of DROP DATABASE in phase 2 together without a single test backing it. Covers
/// format, monotonicity/uniqueness under concurrency, and the L5 overflow guard.
/// </summary>
public class TestSlotTests
{
    [Fact]
    public void NextForClass_produces_the_documented_c_prefixed_format()
    {
        var slot = TestSlot.NextForClass();

        slot.Should().MatchRegex("^c[0-9]{2,}$");
    }

    // NextForClass's counter is a real process-wide singleton (deliberately never reset — §91.3), shared
    // with every other test in this same process, including other tests in THIS file running in
    // parallel. Kept small (10, not hundreds) so this suite doesn't run the shared counter into L5's
    // c999 ceiling out from under a sibling test — the ceiling itself is covered directly against the
    // pure FormatOrThrow seam below, without touching the shared counter at all.
    [Fact]
    public void NextForClass_never_repeats_a_value_across_sequential_calls()
    {
        var seen = new HashSet<string>();

        for (var i = 0; i < 10; i++)
            seen.Add(TestSlot.NextForClass()).Should().BeTrue("slot #{0} must be unique", i);
    }

    [Fact]
    public void NextForClass_never_repeats_a_value_under_concurrent_callers()
    {
        // §91.4/§92.2: many test classes' fixtures can call this at once (P classes in flight). The
        // per-process counter behind it must stay collision-free under real concurrency, not just when
        // called from a single thread.
        var seen = new ConcurrentDictionary<string, byte>();
        var duplicates = 0;

        Parallel.For(0, 10, _ =>
        {
            var slot = TestSlot.NextForClass();
            if (!seen.TryAdd(slot, 0))
                Interlocked.Increment(ref duplicates);
        });

        duplicates.Should().Be(0);
    }

    [Fact]
    public void Reserved_phase1_slot_names_are_never_handed_out()
    {
        for (var i = 0; i < 10; i++)
        {
            var slot = TestSlot.NextForClass();
            new[] { TestSlot.Api, TestSlot.Legal, TestSlot.Dispatch }.Should().NotContain(slot);
        }
    }

    // T9 review (L5): TestSlot.FormatOrClass is the pure seam that lets the c999->c1000 boundary be
    // tested without racing the real, process-wide, never-reset counter up to 1000 real increments.
    [Theory]
    [InlineData(1, "c01")]
    [InlineData(9, "c09")]
    [InlineData(99, "c99")]
    [InlineData(999, "c999")]
    public void FormatOrThrow_formats_within_the_schema_pattern(int index, string expected)
    {
        var slot = TestSlot.FormatOrThrow(index);

        slot.Should().Be(expected);
        // contracts/cycle8/testkit-status.schema.json's own slot pattern for class slots: c[0-9]{2,3}.
        slot.Should().MatchRegex("^c[0-9]{2,3}$");
    }

    [Fact]
    public void FormatOrThrow_refuses_past_the_schema_pattern_ceiling_instead_of_silently_overflowing()
    {
        var act = () => TestSlot.FormatOrThrow(1000);

        act.Should().Throw<TestSafetyException>()
            .WithMessage("*Отказ*");
    }
}
