using ServiceBooking.API.Services.Baths;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using Xunit;

namespace ServiceBooking.UnitTests;

/// <summary>BE-42-3: pure rules of resources — publish problems with capacity, tariff check of publishing, names of positions.</summary>
public class Cycle42ResourceRulesTests
{
    [Fact]
    public void Problems_RequireCapacity_OnlyWhenTheVerticalAsks()
    {
        Assert.Equal([StaysServiceConflictCode.ServiceNoCapacity], ServicePublishRules.Problems(false, true, true, requiresCapacity: true, hasCapacity: false));
        Assert.Empty(ServicePublishRules.Problems(false, true, true, requiresCapacity: true, hasCapacity: true));
        Assert.Empty(ServicePublishRules.Problems(false, true, true, requiresCapacity: false, hasCapacity: false));
    }

    [Fact]
    public void Problems_CapacityGoesAfterWindows()
    {
        var list = ServicePublishRules.Problems(true, false, false, requiresCapacity: true, hasCapacity: false);
        Assert.Equal([StaysServiceConflictCode.ServiceArchived, StaysServiceConflictCode.ServiceNoPrice, StaysServiceConflictCode.ServiceNoWindows, StaysServiceConflictCode.ServiceNoCapacity], list);
    }

    [Fact]
    public void PublishGate_NoPlan_Denied() => Assert.Equal(BathsPublishGate.NoPlanText, BathsPublishGate.Check(false, null, null, 0));

    [Theory]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, true)]
    [InlineData(3, 2, false)]
    [InlineData(3, 3, true)]
    public void PublishGate_Limit(int max, int published, bool denied) =>
        Assert.Equal(denied, BathsPublishGate.Check(true, "План", max, published) is not null);

    [Fact]
    public void PublishGate_Unlimited_Allowed() => Assert.Null(BathsPublishGate.Check(true, "Без лимита", null, 500));

    [Theory]
    [InlineData(1, "Тариф «П» позволяет опубликовать 1 ресурс")]
    [InlineData(3, "Тариф «П» позволяет опубликовать 3 ресурса")]
    [InlineData(5, "Тариф «П» позволяет опубликовать 5 ресурсов")]
    public void PublishGate_LimitText(int max, string expected) => Assert.Equal(expected, BathsPublishGate.LimitText("П", max));

    [Theory]
    [InlineData("  Пиво   Светлое ", "пиво светлое")]
    [InlineData("ЁЛКА", "елка")]
    public void NormalizeName(string name, string expected) => Assert.Equal(expected, ServiceItemWriter.NormalizeName(name));

    [Theory]
    [InlineData("", 10, 1, "Название позиции — от 1 до 100 символов")]
    [InlineData("Веник", -1, 1, "Цена — от 0 до 100 000 ₽")]
    [InlineData("Веник", 10, 0, "Максимум на сеанс — от 1 до 50")]
    [InlineData("Веник", 10, 1, null)]
    public void FormError(string name, int price, int max, string? expected) =>
        Assert.Equal(expected, ServiceItemWriter.FormError(new ServiceBooking.API.DTOs.Stays.ServiceItemInput(name, price, max, true), out _));
}
