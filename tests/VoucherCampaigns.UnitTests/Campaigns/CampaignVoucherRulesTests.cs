using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.UnitTests.Campaigns;

/// <summary>Validade e restrições do voucher (US-01.12) e condições de geração (US-01.4).</summary>
public class CampaignVoucherRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = new(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 11, 1, 2, 59, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateDraft_HasNoVoucherRulesNorConditions()
    {
        var campaign = Draft();

        Assert.Null(campaign.VoucherValidityType);
        Assert.Null(campaign.MaxUsesPerVoucher);
        Assert.Null(campaign.MinPurchaseAmount);
        Assert.Empty(campaign.Conditions);
        Assert.False(campaign.AllowUseInSamePurchase);
    }

    [Fact]
    public void SetRelativeValidity_WithCapBeforeEnd_IsStored()
    {
        var campaign = Draft();
        var cap = End.AddDays(-1);

        campaign.SetRelativeValidity(60, VoucherValidityUnit.Days, cap, Now);

        Assert.Equal(VoucherValidityType.Relative, campaign.VoucherValidityType);
        Assert.Equal(60, campaign.VoucherValidityAmount);
        Assert.Equal(VoucherValidityUnit.Days, campaign.VoucherValidityUnit);
        Assert.Equal(cap, campaign.VoucherValidityCap);
        Assert.Null(campaign.VoucherValidUntil);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void SetRelativeValidity_CapNotAfterStart_Throws(int minutesAfterStart)
    {
        var campaign = Draft();

        var exception = Assert.Throws<BusinessRuleException>(
            () => campaign.SetRelativeValidity(30, VoucherValidityUnit.Days, Start.AddMinutes(minutesAfterStart), Now));

        Assert.Equal(CampaignErrors.VoucherValidityCapBeforeStartCode, exception.Code);
        Assert.Null(campaign.VoucherValidityType);
    }

    [Fact]
    public void SetFixedValidity_OnEnd_IsStoredAndClearsRelativeFields()
    {
        var campaign = Draft();
        campaign.SetRelativeValidity(48, VoucherValidityUnit.Hours, End, Now);

        campaign.SetFixedValidity(End, Now);

        Assert.Equal(VoucherValidityType.FixedDate, campaign.VoucherValidityType);
        Assert.Equal(End, campaign.VoucherValidUntil);
        Assert.Null(campaign.VoucherValidityAmount);
        Assert.Null(campaign.VoucherValidityUnit);
        Assert.Null(campaign.VoucherValidityCap);
    }

    [Fact]
    public void SetFixedValidity_BeforeEnd_Throws()
    {
        var campaign = Draft();

        var exception = Assert.Throws<BusinessRuleException>(() => campaign.SetFixedValidity(End.AddMinutes(-1), Now));

        Assert.Equal(CampaignErrors.VoucherValidUntilBeforeEndCode, exception.Code);
    }

    [Fact]
    public void UpdateDetails_EndAfterFixedValidity_ThrowsAndKeepsPeriod()
    {
        var campaign = Draft();
        campaign.SetFixedValidity(End, Now);

        var exception = Assert.Throws<BusinessRuleException>(
            () => campaign.UpdateDetails("X", null, Start, End.AddDays(1), Now));

        Assert.Equal(CampaignErrors.VoucherValidUntilBeforeEndCode, exception.Code);
        Assert.Equal(End, campaign.EndsAt);
    }

    [Fact]
    public void UpdateDetails_StartAfterCap_ThrowsAndKeepsPeriod()
    {
        var campaign = Draft();
        campaign.SetRelativeValidity(30, VoucherValidityUnit.Days, Start.AddDays(2), Now);

        var exception = Assert.Throws<BusinessRuleException>(
            () => campaign.UpdateDetails("X", null, Start.AddDays(2), End, Now));

        Assert.Equal(CampaignErrors.VoucherValidityCapBeforeStartCode, exception.Code);
        Assert.Equal(Start, campaign.StartsAt);
    }

    [Fact]
    public void SetUsageRestrictions_IsStored()
    {
        var campaign = Draft();

        campaign.SetUsageRestrictions(50m, 500m, 1, Now);

        Assert.Equal(50m, campaign.MinPurchaseAmount);
        Assert.Equal(500m, campaign.MaxPurchaseAmount);
        Assert.Equal(1, campaign.MaxUsesPerVoucher);
    }

    [Fact]
    public void SetUsageRestrictions_MinAboveMax_Throws()
    {
        var campaign = Draft();

        var exception = Assert.Throws<BusinessRuleException>(() => campaign.SetUsageRestrictions(500.01m, 500m, 1, Now));

        Assert.Equal(CampaignErrors.PurchaseRangeInvalidCode, exception.Code);
        Assert.Null(campaign.MaxUsesPerVoucher);
    }

    [Fact]
    public void ReplaceConditions_Scenario2_KeepsBothAndFlag()
    {
        var campaign = Draft();
        var categoryId = Guid.CreateVersion7();

        campaign.ReplaceConditions(
            [PurchaseTotal(150m), new CampaignCondition { Type = ConditionType.Category, CategoryId = categoryId }],
            allowUseInSamePurchase: true,
            Now);

        Assert.Equal(2, campaign.Conditions.Count);
        Assert.All(campaign.Conditions, condition => Assert.Equal(campaign.Id, condition.CampaignId));
        Assert.True(campaign.AllowUseInSamePurchase);
    }

    [Fact]
    public void ReplaceConditions_Scenario3_ZeroQuantity_ThrowsWithBacklogMessageAndKeepsList()
    {
        var campaign = Draft();
        campaign.ReplaceConditions([PurchaseTotal(150m)], false, Now);

        var exception = Assert.Throws<BusinessRuleException>(() => campaign.ReplaceConditions(
            [new CampaignCondition { Type = ConditionType.ItemQuantity, MinQuantity = 0 }], true, Now));

        Assert.Equal(CampaignErrors.ConditionMinQuantityNotPositiveCode, exception.Code);
        Assert.Equal("A quantidade mínima de itens deve ser maior que zero", exception.Message);
        Assert.Single(campaign.Conditions);
        Assert.False(campaign.AllowUseInSamePurchase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void ReplaceConditions_MinAmountNotPositive_Throws(int amount)
    {
        var exception = Assert.Throws<BusinessRuleException>(
            () => Draft().ReplaceConditions([PurchaseTotal(amount)], false, Now));

        Assert.Equal(CampaignErrors.ConditionMinAmountNotPositiveCode, exception.Code);
    }

    [Theory]
    [InlineData(ConditionType.CustomerPurchaseCount)]
    [InlineData(ConditionType.ProductCombination)]
    [InlineData(ConditionType.PurchasePeriod)]
    public void ReplaceConditions_UnavailableType_Throws(ConditionType type)
    {
        var exception = Assert.Throws<BusinessRuleException>(
            () => Draft().ReplaceConditions([new CampaignCondition { Type = type }], false, Now));

        Assert.Equal(CampaignErrors.ConditionTypeUnavailableCode, exception.Code);
    }

    [Fact]
    public void ReplaceConditions_Repeated_ThrowsButDifferentCategoriesAreAccepted()
    {
        var campaign = Draft();
        var category = Guid.CreateVersion7();

        Assert.Equal(
            CampaignErrors.ConditionDuplicatedCode,
            Assert.Throws<BusinessRuleException>(
                () => campaign.ReplaceConditions([PurchaseTotal(100m), PurchaseTotal(200m)], false, Now)).Code);
        Assert.Equal(
            CampaignErrors.ConditionDuplicatedCode,
            Assert.Throws<BusinessRuleException>(() => campaign.ReplaceConditions(
                [Category(category), Category(category)], false, Now)).Code);

        campaign.ReplaceConditions([Category(category), Category(Guid.CreateVersion7())], false, Now);

        Assert.Equal(2, campaign.Conditions.Count);
    }

    [Fact]
    public void ReplaceConditions_EmptyList_ClearsConditions()
    {
        var campaign = Draft();
        campaign.ReplaceConditions([PurchaseTotal(150m)], false, Now);

        campaign.ReplaceConditions([], true, Now);

        Assert.Empty(campaign.Conditions);
        Assert.True(campaign.AllowUseInSamePurchase);
    }

    [Theory]
    [InlineData(CampaignStatus.PendingApproval)]
    [InlineData(CampaignStatus.Active)]
    public void OutsideDraft_VoucherRulesAndConditionsAreRejected(CampaignStatus status)
    {
        var campaign = Draft();
        campaign.Status = status;

        Assert.Equal(
            CampaignErrors.NotDraftCode,
            Assert.Throws<BusinessRuleException>(() => campaign.SetRelativeValidity(30, VoucherValidityUnit.Days, null, Now)).Code);
        Assert.Equal(
            CampaignErrors.NotDraftCode,
            Assert.Throws<BusinessRuleException>(() => campaign.SetFixedValidity(End, Now)).Code);
        Assert.Equal(
            CampaignErrors.NotDraftCode,
            Assert.Throws<BusinessRuleException>(() => campaign.SetUsageRestrictions(null, null, 1, Now)).Code);
        Assert.Equal(
            CampaignErrors.NotDraftCode,
            Assert.Throws<BusinessRuleException>(() => campaign.ReplaceConditions([], false, Now)).Code);
    }

    private static CampaignCondition PurchaseTotal(decimal minAmount) =>
        new() { Type = ConditionType.PurchaseTotal, MinAmount = minAmount };

    private static CampaignCondition Category(Guid categoryId) =>
        new() { Type = ConditionType.Category, CategoryId = categoryId };

    private static Campaign Draft() =>
        Campaign.CreateDraft(Guid.CreateVersion7(), "Volta às Aulas", null, Start, End, Now);
}
