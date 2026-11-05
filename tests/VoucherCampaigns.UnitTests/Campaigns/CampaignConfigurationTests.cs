using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.UnitTests.Campaigns;

/// <summary>Alvos, benefícios e quantidades da campanha em rascunho (US-01.2, US-01.3).</summary>
public class CampaignConfigurationTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Papelaria = Guid.CreateVersion7();
    private static readonly Guid Mochila = Guid.CreateVersion7();

    [Fact]
    public void ReplaceItems_Scenario1_SingleAllTarget_IsStored()
    {
        var campaign = Draft();

        campaign.ReplaceItems([All(10)], Now);

        var item = Assert.Single(campaign.Items);
        Assert.Equal(TargetType.All, item.TargetType);
        Assert.Equal(BenefitType.Percentage, item.BenefitType);
        Assert.Equal(10, item.BenefitValue);
        Assert.Equal(campaign.Id, item.CampaignId);
        Assert.Equal(Now, campaign.UpdatedAt);
    }

    [Fact]
    public void ReplaceItems_ReplacesPreviousItems()
    {
        var campaign = Draft();
        campaign.ReplaceItems([All(10)], Now);

        campaign.ReplaceItems([Category(Papelaria, 15), Product(Mochila, 20, BenefitType.FixedAmount)], Now);

        Assert.Equal([TargetType.Category, TargetType.Product], campaign.Items.Select(item => item.TargetType));
    }

    // Esquema do cenário "Validação do valor do benefício" (RN05).
    [Theory]
    [InlineData(BenefitType.Percentage, "0", "CAMPAIGN_BENEFIT_NOT_POSITIVE")]
    [InlineData(BenefitType.Percentage, "101", "CAMPAIGN_BENEFIT_PERCENTAGE_ABOVE_100")]
    [InlineData(BenefitType.Percentage, "100", null)]
    [InlineData(BenefitType.FixedAmount, "0", "CAMPAIGN_BENEFIT_NOT_POSITIVE")]
    [InlineData(BenefitType.FixedAmount, "-5.00", "CAMPAIGN_BENEFIT_NOT_POSITIVE")]
    [InlineData(BenefitType.FixedAmount, "20.00", null)]
    [InlineData(BenefitType.FixedAmount, "150", null)]
    public void ReplaceItems_BenefitValue_FollowsRn05(BenefitType type, string value, string? expectedCode)
    {
        var campaign = Draft();
        var item = All(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), type);

        if (expectedCode is null)
        {
            campaign.ReplaceItems([item], Now);
            Assert.Single(campaign.Items);
            return;
        }

        var exception = Assert.Throws<BusinessRuleException>(() => campaign.ReplaceItems([item], Now));
        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public void ReplaceItems_OverlappingTargets_AreAllowed()
    {
        var campaign = Draft();

        campaign.ReplaceItems([All(5), Category(Papelaria, 15), Product(Mochila, 20)], Now);

        Assert.Equal(3, campaign.Items.Count);
    }

    [Theory]
    [MemberData(nameof(DuplicatedTargets))]
    public void ReplaceItems_SameTargetTwice_Throws(CampaignItem first, CampaignItem second)
    {
        var exception = Assert.Throws<BusinessRuleException>(() => Draft().ReplaceItems([first, second], Now));

        Assert.Equal(CampaignErrors.DuplicatedTargetCode, exception.Code);
    }

    public static TheoryData<CampaignItem, CampaignItem> DuplicatedTargets() => new()
    {
        { All(10), All(20) },
        { Category(Papelaria, 10), Category(Papelaria, 15) },
        { Product(Mochila, 10), Product(Mochila, 20) }
    };

    [Fact]
    public void ReplaceItems_WhenRuleFails_KeepsCurrentItems()
    {
        var campaign = Draft();
        campaign.ReplaceItems([All(10)], Now);

        Assert.Throws<BusinessRuleException>(() => campaign.ReplaceItems([Category(Papelaria, 15), All(0)], Now));

        var item = Assert.Single(campaign.Items);
        Assert.Equal(10, item.BenefitValue);
    }

    [Theory]
    [InlineData(CampaignStatus.PendingApproval)]
    [InlineData(CampaignStatus.Active)]
    [InlineData(CampaignStatus.Cancelled)]
    public void Configuration_OutsideDraft_Throws(CampaignStatus status)
    {
        var campaign = Draft();
        campaign.Status = status;

        Assert.Equal(CampaignErrors.NotDraftCode, Assert.Throws<BusinessRuleException>(() => campaign.ReplaceItems([All(10)], Now)).Code);
        Assert.Equal(CampaignErrors.NotDraftCode, Assert.Throws<BusinessRuleException>(() => campaign.SetQuantities(10, null, Now)).Code);
    }

    [Fact]
    public void SetQuantities_Scenario1_StoresTotal()
    {
        var campaign = Draft();
        campaign.ReplaceItems([All(10)], Now);

        campaign.SetQuantities(5000, null, Now);

        Assert.Equal(5000, campaign.TotalVoucherLimit);
        Assert.Equal(5000, campaign.UndistributedVouchers);
    }

    [Fact]
    public void Quotas_Scenario2_WithinTotal_ShowUndistributedBalance()
    {
        var campaign = Draft();
        campaign.SetQuantities(5000, null, Now);

        campaign.ReplaceItems([Category(Papelaria, 15, quota: 3000), Product(Mochila, 20, quota: 1500)], Now);

        Assert.Equal(500, campaign.UndistributedVouchers);
    }

    [Fact]
    public void Quotas_Scenario3_AboveTotal_ThrowsWithBacklogMessage()
    {
        var campaign = Draft();
        campaign.SetQuantities(5000, null, Now);

        var exception = Assert.Throws<BusinessRuleException>(() =>
            campaign.ReplaceItems([Category(Papelaria, 15, quota: 3000), Product(Mochila, 20, quota: 2500)], Now));

        Assert.Equal(CampaignErrors.QuotasExceedTotalCode, exception.Code);
        Assert.Equal("A soma das quantidades por alvo (5.500) ultrapassa a quantidade total da campanha (5.000)", exception.Message);
        Assert.Empty(campaign.Items);
    }

    [Fact]
    public void SetQuantities_BelowCurrentQuotas_Throws()
    {
        var campaign = Draft();
        campaign.ReplaceItems([Category(Papelaria, 15, quota: 3000), Product(Mochila, 20, quota: 1500)], Now);

        var exception = Assert.Throws<BusinessRuleException>(() => campaign.SetQuantities(4000, null, Now));

        Assert.Equal("A soma das quantidades por alvo (4.500) ultrapassa a quantidade total da campanha (4.000)", exception.Message);
        Assert.Null(campaign.TotalVoucherLimit);
    }

    [Fact]
    public void Quotas_WithoutTotal_AreAccepted()
    {
        var campaign = Draft();

        campaign.ReplaceItems([Category(Papelaria, 15, quota: 3000), Product(Mochila, 20, quota: 1500)], Now);

        Assert.Null(campaign.UndistributedVouchers);
    }

    [Fact]
    public void Quota_WithSingleTarget_Throws()
    {
        var exception = Assert.Throws<BusinessRuleException>(() => Draft().ReplaceItems([All(10, quota: 100)], Now));

        Assert.Equal(CampaignErrors.QuotaSingleTargetCode, exception.Code);
    }

    [Fact]
    public void SetQuantities_Scenario4_PerCustomerLimitIsIndependentOfTotal()
    {
        var campaign = Draft();

        campaign.SetQuantities(10, 2, Now);
        Assert.Equal(2, campaign.PerCustomerVoucherLimit);

        campaign.SetQuantities(10, 50, Now);
        Assert.Equal(50, campaign.PerCustomerVoucherLimit);
    }

    private static Campaign Draft() =>
        Campaign.CreateDraft(
            Guid.CreateVersion7(),
            "Volta às Aulas",
            null,
            new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 1, 2, 59, 0, DateTimeKind.Utc),
            Now);

    private static CampaignItem All(decimal value, BenefitType type = BenefitType.Percentage, int? quota = null) =>
        new() { TargetType = TargetType.All, BenefitType = type, BenefitValue = value, VoucherLimit = quota };

    private static CampaignItem Category(Guid id, decimal value, int? quota = null) =>
        new() { TargetType = TargetType.Category, CategoryId = id, BenefitType = BenefitType.Percentage, BenefitValue = value, VoucherLimit = quota };

    private static CampaignItem Product(Guid id, decimal value, BenefitType type = BenefitType.FixedAmount, int? quota = null) =>
        new() { TargetType = TargetType.Product, ProductId = id, BenefitType = type, BenefitValue = value, VoucherLimit = quota };
}
