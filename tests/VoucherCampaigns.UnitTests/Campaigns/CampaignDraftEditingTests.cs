using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.UnitTests.Campaigns;

/// <summary>Público elegível (US-01.5) e edição dos dados básicos em rascunho (US-01.7).</summary>
public class CampaignDraftEditingTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Start = new(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 11, 1, 2, 59, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateDraft_HasNoAudience()
    {
        Assert.Null(Draft().AudienceType);
    }

    [Fact]
    public void SetAudience_AllCustomers_IsStored()
    {
        var campaign = Draft();
        var later = Now.AddHours(1);

        campaign.SetAudience(CampaignAudienceType.AllCustomers, later);

        Assert.Equal(CampaignAudienceType.AllCustomers, campaign.AudienceType);
        Assert.Equal(later, campaign.UpdatedAt);
    }

    [Fact]
    public void SetAudience_Restricted_ThrowsAndKeepsPreviousAudience()
    {
        var campaign = Draft();
        campaign.SetAudience(CampaignAudienceType.AllCustomers, Now);

        var exception = Assert.Throws<BusinessRuleException>(() => campaign.SetAudience(CampaignAudienceType.Restricted, Now));

        Assert.Equal(CampaignErrors.AudienceRestrictedUnavailableCode, exception.Code);
        Assert.Equal(CampaignAudienceType.AllCustomers, campaign.AudienceType);
    }

    [Fact]
    public void UpdateDetails_ReplacesNameDescriptionAndPeriod()
    {
        var campaign = Draft();
        var later = Now.AddHours(1);

        campaign.UpdateDetails("  Volta às Aulas 2026  ", "  Material escolar  ", Start.AddDays(4), End.AddDays(14), later);

        Assert.Equal("Volta às Aulas 2026", campaign.Name);
        Assert.Equal("Material escolar", campaign.Description);
        Assert.Equal(Start.AddDays(4), campaign.StartsAt);
        Assert.Equal(End.AddDays(14), campaign.EndsAt);
        Assert.Equal(later, campaign.UpdatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void UpdateDetails_BlankDescription_ClearsIt(string? description)
    {
        var campaign = Draft("Descrição antiga");

        campaign.UpdateDetails("Volta às Aulas", description, Start, End, Now);

        Assert.Null(campaign.Description);
    }

    [Fact]
    public void UpdateDetails_StartAfterOrEqualToEnd_Throws()
    {
        var campaign = Draft();

        Assert.Equal(
            CampaignErrors.InvalidPeriodCode,
            Assert.Throws<BusinessRuleException>(() => campaign.UpdateDetails("X", null, End, Start, Now)).Code);
        Assert.Equal(
            CampaignErrors.InvalidPeriodCode,
            Assert.Throws<BusinessRuleException>(() => campaign.UpdateDetails("X", null, Start, Start, Now)).Code);
    }

    [Fact]
    public void UpdateDetails_NewStartInPast_Throws()
    {
        var campaign = Draft();

        var exception = Assert.Throws<BusinessRuleException>(
            () => campaign.UpdateDetails("X", null, Now.AddMinutes(-1), End, Now));

        Assert.Equal(CampaignErrors.StartInPastCode, exception.Code);
        Assert.Equal(Start, campaign.StartsAt);
    }

    [Fact]
    public void UpdateDetails_StartInCurrentMinute_IsAccepted()
    {
        var campaign = Draft();

        campaign.UpdateDetails("X", null, Now, End, Now.AddSeconds(30));

        Assert.Equal(Now, campaign.StartsAt);
    }

    [Fact]
    public void UpdateDetails_UnchangedStartAlreadyPassed_IsAccepted()
    {
        var campaign = Draft();
        var afterStart = Start.AddDays(1);

        campaign.UpdateDetails("Nome corrigido", null, Start, End, afterStart);

        Assert.Equal("Nome corrigido", campaign.Name);
    }

    [Fact]
    public void UpdateDetails_ChangedEndInPast_Throws()
    {
        var campaign = Draft();
        var afterStart = Start.AddDays(1);

        var exception = Assert.Throws<BusinessRuleException>(
            () => campaign.UpdateDetails("X", null, Start, afterStart.AddHours(-1), afterStart));

        Assert.Equal(CampaignErrors.EndInPastCode, exception.Code);
        Assert.Equal(End, campaign.EndsAt);
    }

    [Fact]
    public void UpdateDetails_KeepsConfiguration()
    {
        var campaign = Draft();
        campaign.ReplaceItems(
            [new CampaignItem { TargetType = TargetType.All, BenefitType = BenefitType.Percentage, BenefitValue = 10 }], Now);
        campaign.SetQuantities(100, 2, Now);
        campaign.SetAudience(CampaignAudienceType.AllCustomers, Now);

        campaign.UpdateDetails("X", null, Start, End, Now);

        Assert.Single(campaign.Items);
        Assert.Equal(100, campaign.TotalVoucherLimit);
        Assert.Equal(2, campaign.PerCustomerVoucherLimit);
        Assert.Equal(CampaignAudienceType.AllCustomers, campaign.AudienceType);
    }

    [Theory]
    [InlineData(CampaignStatus.PendingApproval)]
    [InlineData(CampaignStatus.Active)]
    public void OutsideDraft_AudienceAndDetailsAreRejected(CampaignStatus status)
    {
        var campaign = Draft();
        campaign.Status = status;

        Assert.Equal(
            CampaignErrors.NotDraftCode,
            Assert.Throws<BusinessRuleException>(() => campaign.SetAudience(CampaignAudienceType.AllCustomers, Now)).Code);
        Assert.Equal(
            CampaignErrors.NotDraftCode,
            Assert.Throws<BusinessRuleException>(() => campaign.UpdateDetails("X", null, Start, End, Now)).Code);
    }

    private static Campaign Draft(string? description = null) =>
        Campaign.CreateDraft(Guid.CreateVersion7(), "Volta às Aulas", description, Start, End, Now);
}
