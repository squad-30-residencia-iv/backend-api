using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.UnitTests.Campaigns;

public class CampaignLifecycleTests
{
    private static readonly Guid NetworkId = Guid.CreateVersion7();
    private static readonly Guid ActorId = Guid.CreateVersion7();

    // 28/09/2026 12:00 em Brasília.
    private static readonly DateTime Now = Utc(2026, 9, 28, 15, 0);

    [Fact]
    public void Submit_CompleteDraft_BecomesPendingAndRecordsTheActor()
    {
        var campaign = ReadyCampaign();

        var statusEvent = campaign.Submit(ActorId, Now);

        Assert.Equal(CampaignStatus.PendingApproval, campaign.Status);
        Assert.Equal(ActorId, campaign.SubmittedByUserId);
        Assert.Equal(Now, campaign.SubmittedAt);
        Assert.Equal(CampaignStatus.Draft, statusEvent.FromStatus);
        Assert.Equal(CampaignStatus.PendingApproval, statusEvent.ToStatus);
        Assert.Equal(ActorId, statusEvent.ActorUserId);
        Assert.Null(statusEvent.Reason);
    }

    [Fact]
    public void Submit_WithoutAudience_Throws()
    {
        var campaign = Campaign.CreateDraft(NetworkId, "Volta às Aulas", null, Utc(2026, 10, 1, 11, 0), Utc(2026, 11, 1, 2, 59), Now);

        var exception = Assert.Throws<BusinessRuleException>(() => campaign.Submit(ActorId, Now));

        Assert.Equal(CampaignErrors.SubmitAudienceRequiredCode, exception.Code);
        Assert.Equal(CampaignStatus.Draft, campaign.Status);
    }

    [Fact]
    public void Approve_BeforeStart_BecomesScheduled()
    {
        var campaign = ReadyCampaign();
        campaign.Submit(ActorId, Now);

        var statusEvent = campaign.Approve(ActorId, Now);

        Assert.Equal(CampaignStatus.Scheduled, campaign.Status);
        Assert.Equal(ActorId, campaign.DecidedByUserId);
        Assert.Equal(CampaignStatus.Scheduled, statusEvent.ToStatus);
        Assert.Null(campaign.RejectionReason);
    }

    [Fact]
    public void Approve_DuringPeriod_BecomesActive()
    {
        var campaign = ReadyCampaign();
        campaign.Submit(ActorId, Now);

        campaign.Approve(ActorId, Utc(2026, 10, 15, 15, 0));

        Assert.Equal(CampaignStatus.Active, campaign.Status);
    }

    [Fact]
    public void Approve_AfterEnd_ThrowsAndKeepsPending()
    {
        var campaign = ReadyCampaign();
        campaign.Submit(ActorId, Now);

        var exception = Assert.Throws<BusinessRuleException>(() => campaign.Approve(ActorId, Utc(2026, 11, 2, 15, 0)));

        Assert.Equal(CampaignErrors.ApprovePeriodEndedCode, exception.Code);
        Assert.Equal(CampaignStatus.PendingApproval, campaign.Status);
    }

    [Fact]
    public void Reject_StoresTrimmedReason()
    {
        var campaign = ReadyCampaign();
        campaign.Submit(ActorId, Now);

        var statusEvent = campaign.Reject(ActorId, "  Fora do calendário  ", Now);

        Assert.Equal(CampaignStatus.Rejected, campaign.Status);
        Assert.Equal("Fora do calendário", campaign.RejectionReason);
        Assert.Equal("Fora do calendário", statusEvent.Reason);
    }

    [Fact]
    public void Reject_BlankReason_StoresNull()
    {
        var campaign = ReadyCampaign();
        campaign.Submit(ActorId, Now);

        campaign.Reject(ActorId, "   ", Now);

        Assert.Null(campaign.RejectionReason);
    }

    private static Campaign ReadyCampaign()
    {
        var campaign = Campaign.CreateDraft(NetworkId, "Volta às Aulas", null, Utc(2026, 10, 1, 11, 0), Utc(2026, 11, 1, 2, 59), Now);
        campaign.SetAudience(CampaignAudienceType.AllCustomers, Now);
        campaign.ReplaceItems(
            [new CampaignItem { TargetType = TargetType.All, BenefitType = BenefitType.Percentage, BenefitValue = 10 }],
            Now);
        campaign.SetQuantities(100, null, Now);
        campaign.SetRelativeValidity(30, VoucherValidityUnit.Days, null, Now);
        campaign.SetUsageRestrictions(null, null, 1, Now);
        return campaign;
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
