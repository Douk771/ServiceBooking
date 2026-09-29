using FluentAssertions;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE19.md §407/§408, §414 — OwnerSubscriptionService.BuildPendingRequestDto's own
/// cycle-19 logic (retired-line marking, non-retired-only price estimate, notice text). Constructed
/// with `null!` dependencies: the method under test never touches the db/resolver/usage-reader fields
/// it was given at construction — it only reads its own parameters — so this is a pure unit test, no
/// host, no database.
/// </summary>
public class OwnerSubscriptionServiceBuildPendingRequestDtoTests
{
    private static readonly OwnerSubscriptionService Sut = new(null!, null!, null!, null!, null!, null!);

    private static SubscriptionOption Option(Guid id, string name, string? capabilityKey, decimal? price = 100m) =>
        new() { Id = id, Code = name, Name = name, CapabilityKey = capabilityKey, PricePerMonth = price };

    private static BillingAccount Account(string optionsJson, Guid? requestedPlanId = null) => new()
    {
        Id = Guid.NewGuid(),
        OwnerUserId = "owner-1",
        RequestedAtUtc = DateTime.UtcNow,
        RequestedOptionsJson = optionsJson,
        RequestedPlanId = requestedPlanId,
        RequestedComment = null,
    };

    [Fact]
    public void NoPendingRequest_ReturnsNull()
    {
        var account = new BillingAccount { Id = Guid.NewGuid(), OwnerUserId = "owner-1", RequestedAtUtc = null };

        Sut.BuildPendingRequestDto(account, [], 0m).Should().BeNull();
    }

    [Fact]
    public void RetiredLimitOption_IsMarkedRetired_AndExcludedFromEstimate()
    {
        var retiredOptionId = Guid.NewGuid();
        var retiredOption = Option(retiredOptionId, "Дополнительные сотрудники", CapabilityKeys.Employees, 300m);
        var account = Account(
            OwnerSubscriptionService.SerializeOptionLines([new RequestedOptionLine(retiredOptionId, 2)]));

        var dto = Sut.BuildPendingRequestDto(account, [retiredOption], currentPlanPrice: 1000m);

        dto.Should().NotBeNull();
        dto!.Items.Should().ContainSingle();
        dto.Items[0].Retired.Should().BeTrue();
        dto.Items[0].Name.Should().Be("Дополнительные сотрудники");
        dto.EstimatedMonthlyPrice.Should().Be(1000m);
        dto.RetiredOptionsNotice.Should().Be(BillingTexts.RetiredOptionsInRequestNotice(["Дополнительные сотрудники"]));
    }

    [Fact]
    public void NonRetiredOption_IsNotMarkedRetired_AndCountsTowardsEstimate()
    {
        var optionId = Guid.NewGuid();
        var option = Option(optionId, "WhatsApp-канал", CapabilityKeys.NotificationsWhatsApp, 250m);
        var account = Account(
            OwnerSubscriptionService.SerializeOptionLines([new RequestedOptionLine(optionId, 3)]));

        var dto = Sut.BuildPendingRequestDto(account, [option], currentPlanPrice: 1000m);

        dto!.Items[0].Retired.Should().BeFalse();
        dto.EstimatedMonthlyPrice.Should().Be(1000m + 250m * 3);
        dto.RetiredOptionsNotice.Should().BeNull();
    }

    [Fact]
    public void UnknownOption_ReadsAsDash_AndIsNotRetired()
    {
        var unknownOptionId = Guid.NewGuid();
        var account = Account(
            OwnerSubscriptionService.SerializeOptionLines([new RequestedOptionLine(unknownOptionId, 1)]));

        var dto = Sut.BuildPendingRequestDto(account, [], currentPlanPrice: 1000m);

        dto!.Items[0].Name.Should().Be("—");
        dto.Items[0].Retired.Should().BeFalse();
        dto.RetiredOptionsNotice.Should().BeNull();
    }

    [Fact]
    public void MixOfRetiredAndNonRetired_NoticeListsOnlyDistinctRetiredNames()
    {
        var retiredId1 = Guid.NewGuid();
        var retiredId2 = Guid.NewGuid();
        var nonRetiredId = Guid.NewGuid();
        var options = new List<SubscriptionOption>
        {
            Option(retiredId1, "Дополнительные сотрудники", CapabilityKeys.Employees, 300m),
            Option(retiredId2, "Дополнительные компании", CapabilityKeys.Companies, 400m),
            Option(nonRetiredId, "WhatsApp-канал", CapabilityKeys.NotificationsWhatsApp, 250m),
        };
        var account = Account(OwnerSubscriptionService.SerializeOptionLines(
        [
            new RequestedOptionLine(retiredId1, 1),
            new RequestedOptionLine(retiredId2, 1),
            new RequestedOptionLine(nonRetiredId, 1),
        ]));

        var dto = Sut.BuildPendingRequestDto(account, options, currentPlanPrice: 1000m);

        dto!.EstimatedMonthlyPrice.Should().Be(1000m + 250m);
        dto.RetiredOptionsNotice.Should().Be(
            BillingTexts.RetiredOptionsInRequestNotice(["Дополнительные сотрудники", "Дополнительные компании"]));
    }

    [Fact]
    public void PlanChangeOnWithdrawnPlan_SetsIrreversibilityNotice()
    {
        var currentPlanId = Guid.NewGuid();
        var newPlanId = Guid.NewGuid();
        var account = Account(OwnerSubscriptionService.SerializeOptionLines([]), requestedPlanId: newPlanId);

        var dto = Sut.BuildPendingRequestDto(
            account, [], currentPlanPrice: 1000m, currentPlanWithdrawn: true, currentPlanId: currentPlanId);

        dto!.IrreversibilityNotice.Should().NotBeNull();
    }

    [Fact]
    public void OptionsOnlyRequest_OnWithdrawnPlan_DoesNotSetIrreversibilityNotice()
    {
        var currentPlanId = Guid.NewGuid();
        var account = Account(OwnerSubscriptionService.SerializeOptionLines([]), requestedPlanId: null);

        var dto = Sut.BuildPendingRequestDto(
            account, [], currentPlanPrice: 1000m, currentPlanWithdrawn: true, currentPlanId: currentPlanId);

        dto!.IrreversibilityNotice.Should().BeNull();
    }
}
