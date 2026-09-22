using FluentAssertions;
using ServiceBooking.TestKit;

namespace ServiceBooking.UnitTests;

/// <summary>
/// T9 review: <see cref="TestData.Phone"/>'s "always exactly 12 characters, +79... shape" guarantee was
/// held up only by mental arithmetic ("classSlot + 6-digit counter, last 9 digits kept") — never asserted.
/// Covers the two slot widths the review specifically called out (a 3-char "c01" slot and a 4-char "c100"
/// one) since the digit-trimming/padding logic in <see cref="TestData.Phone"/> depends on how many digits
/// the slot itself contributes.
/// </summary>
public class TestDataTests
{
    [Theory]
    [InlineData("c01")]
    [InlineData("c100")]
    public void Phone_is_always_exactly_12_characters_shaped_plus79(string classSlot)
    {
        var data = new TestData(classSlot);

        for (var i = 0; i < 20; i++)
        {
            var phone = data.Phone();

            phone.Should().HaveLength(12);
            phone.Should().MatchRegex(@"^\+79\d{9}$");
        }
    }

    [Fact]
    public void Phone_never_repeats_within_one_class()
    {
        var data = new TestData("c01");
        var seen = new HashSet<string>();

        for (var i = 0; i < 200; i++)
            seen.Add(data.Phone()).Should().BeTrue();
    }

    [Fact]
    public void Different_classes_never_collide_even_at_the_same_call_count()
    {
        var a = new TestData("c01");
        var b = new TestData("c02");

        for (var i = 0; i < 50; i++)
            a.Phone().Should().NotBe(b.Phone());
    }

    [Fact]
    public void Email_folds_in_the_class_slot_for_diagnosability()
    {
        var data = new TestData("c07");

        data.Email().Should().Contain("c07");
    }
}
