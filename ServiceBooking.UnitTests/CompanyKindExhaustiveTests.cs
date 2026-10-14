using FluentAssertions;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.3.4 — every public helper that takes a <see cref="CompanyKind"/> must answer for ALL values of the enum without throwing
/// (a new kind with a missing switch arm fails here, not in production). Helpers that refuse a kind by design are listed with the reason.
/// </summary>
public class CompanyKindExhaustiveTests
{
    public static IEnumerable<object[]> Kinds() => Enum.GetValues<CompanyKind>().Select(k => new object[] { k });

    private static readonly PublicSiteLinks Links = new(Options.Create(new PublicSitesOptions()));

    [Theory, MemberData(nameof(Kinds))]
    public void CompanyKindTraits_answers_for_every_kind(CompanyKind kind)
    {
        CompanyKindTraits.For(kind).Should().NotBeNull();
        CompanyKindTraits.CompanyPagePath(kind, "slug").Should().Contain("slug");
    }

    [Theory, MemberData(nameof(Kinds))]
    public void CompanyKindGuard_answers_for_every_kind(CompanyKind kind)
    {
        var text = CompanyKindGuard.RefusalTextFor(kind);
        (text is null).Should().Be(kind == CompanyKind.Services);
        CompanyKindGuard.RejectNonSalon(kind)?.Value.Should().Be(text);
        text.Should().Be(CompanyKindTraits.For(kind).SalonRefusalText, "the guard and the traits table must say the same");
    }

    [Theory, MemberData(nameof(Kinds))]
    public void CompanyPhotoTexts_answer_for_every_kind(CompanyKind kind)
    {
        CompanyPhotoTexts.LimitReached(kind).Should().Contain(CompanyKindTraits.For(kind).PhotoOwnerLabel);
        CompanyPhotoTexts.ReorderMismatch(kind).Should().Contain(CompanyKindTraits.For(kind).PhotoOwnerLabel);
    }

    [Theory, MemberData(nameof(Kinds))]
    public void PublicSiteLinks_answer_for_every_kind(CompanyKind kind)
    {
        var baseUrl = Links.SiteBaseUrl(kind);
        baseUrl.Should().StartWith("https://").And.NotEndWith("/");
        Links.CompanyPageUrl(kind, "slug").Should().StartWith(baseUrl).And.EndWith("slug");
    }

    [Fact]
    public void PublicSiteLinks_give_every_kind_its_own_site()
    {
        Enum.GetValues<CompanyKind>().Select(Links.SiteBaseUrl).Distinct().Should().HaveCount(Enum.GetValues<CompanyKind>().Length);
        Links.SiteBaseUrl(CompanyKind.Baths).Should().Be("https://bani.ezbook.ru");
    }

    [Fact]
    public void PublicSiteLinks_BlankBathsBaseUrl_meansTheDefault()
    {
        var links = new PublicSiteLinks(Options.Create(new PublicSitesOptions { BathsBaseUrl = "  " }));
        links.SiteBaseUrl(CompanyKind.Baths).Should().Be("https://bani.ezbook.ru");
    }

    [Theory, MemberData(nameof(Kinds))]
    public void PublicSiteLinks_slot_methods_follow_the_vertical_and_refuse_other_kinds(CompanyKind kind)
    {
        var token = "tok";
        if (!SlotVerticals.IsSlotKind(kind))
        {
            FluentActions.Invoking(() => Links.ServiceOrderPageUrl(kind, token)).Should().Throw<ArgumentOutOfRangeException>();
            FluentActions.Invoking(() => Links.SlotSubscriptionUrl(kind)).Should().Throw<ArgumentOutOfRangeException>();
            return;
        }
        var site = Links.SiteBaseUrl(kind);
        Links.ServiceOrderPageUrl(kind, token).Should().Be($"{site}/s/tok");
        Links.SlotSubscriptionUrl(kind).Should().Be($"{site}/cabinet/subscription");
        Links.CabinetServiceSessionUrl(kind, Guid.Empty, Guid.Empty).Should().StartWith($"{site}/cabinet/");
        Links.ResourcePageUrl(kind, "c", "s").Should().Be(site + SlotVerticals.Get(kind).ResourcePagePath("c", "s"));
    }

    [Theory, MemberData(nameof(Kinds))]
    public void BillingTexts_pending_line_names_the_traits_label(CompanyKind kind) =>
        BillingTexts.RequestOfOtherLine(kind).Should().Contain($"«{CompanyKindTraits.For(kind).KindLabel}»");

    [Theory, MemberData(nameof(Kinds))]
    public void StaffPositionTexts_exist_exactly_for_kinds_with_positions(CompanyKind kind)
    {
        if (CompanyKindTraits.For(kind).UsesStaffPositions)
            StaffPositionTexts.For(kind).Position.Should().NotBeNullOrWhiteSpace();
        else
            FluentActions.Invoking(() => StaffPositionTexts.For(kind)).Should().Throw<ArgumentOutOfRangeException>("callers ask only when UsesStaffPositions is true");
    }

    [Theory, MemberData(nameof(Kinds))]
    public void SlotVerticals_Find_does_not_throw(CompanyKind kind) =>
        SlotVerticals.IsSlotKind(kind).Should().Be(kind is CompanyKind.Stays or CompanyKind.Baths);

    [Fact]
    public void Gallery_and_position_rules_follow_the_architecture_table()
    {
        var galleryKinds = Enum.GetValues<CompanyKind>().Where(k => CompanyKindTraits.For(k).HasCompanyGallery).ToArray();
        galleryKinds.Should().BeEquivalentTo([CompanyKind.Services, CompanyKind.Orders, CompanyKind.Baths]);
        Enum.GetValues<CompanyKind>().Where(k => CompanyKindTraits.For(k).UsesStaffPositions)
            .Should().BeEquivalentTo([CompanyKind.Stays, CompanyKind.Baths]);
    }
}
