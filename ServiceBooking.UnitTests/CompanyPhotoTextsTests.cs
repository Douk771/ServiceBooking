using FluentAssertions;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

public class CompanyPhotoTextsTests
{
    [Fact]
    public void Salon_texts_are_unchanged_byte_for_byte()
    {
        CompanyPhotoTexts.LimitReached(CompanyKind.Services).Should().Be("В галерее салона может быть не больше 10 фотографий");
        CompanyPhotoTexts.ReorderMismatch(CompanyKind.Services).Should().Be("Список должен содержать все фотографии салона ровно по одному разу");
    }

    [Fact]
    public void Shop_texts_use_the_shop_noun()
    {
        CompanyPhotoTexts.LimitReached(CompanyKind.Orders).Should().Be("В галерее магазина может быть не больше 10 фотографий");
        CompanyPhotoTexts.ReorderMismatch(CompanyKind.Orders).Should().Be("Список должен содержать все фотографии магазина ровно по одному разу");
    }
}
