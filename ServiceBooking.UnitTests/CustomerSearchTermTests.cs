using FluentAssertions;
using ServiceBooking.API.Services.Orders.Reports;

namespace ServiceBooking.UnitTests;

/// <summary>API_CONTRACT_CYCLE25.md §526 — the "покупатель" filter: phone fragment or name.</summary>
public class CustomerSearchTermTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_IsNoFilter(string? input) => CustomerSearchTerm.Parse(input).Kind.Should().Be(CustomerSearchKind.None);

    [Theory]
    [InlineData("1234", "1234")]
    [InlineData("+7 (999) 123-45", "799912345")]
    [InlineData(" 12 34 ", "1234")]
    public void FourOrMoreDigits_IsAPhone(string input, string digits)
    {
        var t = CustomerSearchTerm.Parse(input);
        t.Kind.Should().Be(CustomerSearchKind.Phone);
        t.Value.Should().Be(digits);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("+7 9")]
    [InlineData("а")]
    public void TooShort(string input) => CustomerSearchTerm.Parse(input).Kind.Should().Be(CustomerSearchKind.TooShort);

    [Theory]
    [InlineData("Анна", "Анна")]
    [InlineData("  Ан ", "Ан")]
    [InlineData("Анна2", "Анна2")]
    public void Name(string input, string value)
    {
        var t = CustomerSearchTerm.Parse(input);
        t.Kind.Should().Be(CustomerSearchKind.Name);
        t.Value.Should().Be(value);
    }
}
