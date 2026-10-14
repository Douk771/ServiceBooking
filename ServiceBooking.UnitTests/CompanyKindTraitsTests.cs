using FluentAssertions;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE42.md §42.3.2–§42.3.3 — every CompanyKind has a row of traits; slot verticals are exactly Stays and Baths.</summary>
public class CompanyKindTraitsTests
{
    public static IEnumerable<object[]> Kinds() => Enum.GetValues<CompanyKind>().Select(k => new object[] { k });

    [Theory, MemberData(nameof(Kinds))]
    public void EveryKind_HasTraits(CompanyKind kind)
    {
        var t = CompanyKindTraits.For(kind);
        t.TariffLine.Should().Be(kind);
        t.KindLabel.Should().NotBeNullOrWhiteSpace();
        t.PhotoOwnerLabel.Should().NotBeNullOrWhiteSpace();
        (t.SalonRefusalText is null).Should().Be(t.IsSalon, "only a salon has no refusal text");
        CompanyKindTraits.CompanyPagePath(kind, "abc").Should().Contain("abc");
    }

    [Theory, MemberData(nameof(Kinds))]
    public void SlotVerticals_FindIsDefinedForEveryKind(CompanyKind kind)
    {
        var v = SlotVerticals.Find(kind);
        SlotVerticals.IsSlotKind(kind).Should().Be(kind is CompanyKind.Stays or CompanyKind.Baths);
        if (v is not null) v.Kind.Should().Be(kind);
        else FluentActions.Invoking(() => SlotVerticals.Get(kind)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BathsTraits_FollowTheArchitectureTable()
    {
        var t = CompanyKindTraits.For(CompanyKind.Baths);
        t.VisibleOnSalonPublicPage.Should().BeFalse();
        t.HasCompanyGallery.Should().BeTrue();
        t.UsesStaffPositions.Should().BeTrue();
        t.HasSalonSeatLimit.Should().BeFalse();
        t.SalonRefusalText.Should().Be("Это компания «Бани»: записи, услуги и расписание для неё недоступны.");
    }

    [Fact]
    public void Verticals_DifferByData()
    {
        SlotVerticals.Stays.Unit.Should().Be(ServiceBooking.API.Services.Stays.GateUnit.House);
        SlotVerticals.Baths.Unit.Should().Be(ServiceBooking.API.Services.Stays.GateUnit.Resource);
        SlotVerticals.Stays.ResourcePagePath("c", "s").Should().Be("/c/uslugi/s");
        SlotVerticals.Baths.ResourcePagePath("c", "s").Should().Be("/c/s");
        SlotVerticals.Baths.RequiresCapacityToPublish.Should().BeTrue();
        SlotVerticals.Stays.HasHouses.Should().BeTrue();
        SlotVerticals.Baths.ApiPrefix.Should().Be("api/baths");
    }
}
