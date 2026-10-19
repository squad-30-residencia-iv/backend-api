using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.UnitTests.Campaigns;

public class CampaignTests
{
    private static readonly Guid NetworkId = Guid.CreateVersion7();

    // 28/09/2026 12:00 em Brasília.
    private static readonly DateTime Now = Utc(2026, 9, 28, 15, 0);

    [Fact]
    public void CreateDraft_WithValidData_CreatesDraftWithIdAndUtcPeriod()
    {
        var campaign = Campaign.CreateDraft(NetworkId, "  Volta às Aulas ", " ", Utc(2026, 10, 1, 11, 0), Utc(2026, 11, 1, 2, 59), Now);

        Assert.Equal(CampaignStatus.Draft, campaign.Status);
        Assert.NotEqual(Guid.Empty, campaign.Id);
        Assert.Equal(NetworkId, campaign.NetworkId);
        Assert.Equal("Volta às Aulas", campaign.Name);
        Assert.Null(campaign.Description);
        Assert.Equal(Utc(2026, 10, 1, 11, 0), campaign.StartsAt);
        Assert.Equal(Now, campaign.CreatedAt);
    }

    [Fact]
    public void CreateDraft_Scenario2_InvertedPeriod_Throws()
    {
        var exception = Assert.Throws<BusinessRuleException>(() =>
            Campaign.CreateDraft(NetworkId, "Volta às Aulas", null, Utc(2026, 10, 31, 3, 0), Utc(2026, 10, 1, 3, 0), Now));

        Assert.Equal("CAMPAIGN_INVALID_PERIOD", exception.Code);
        Assert.Equal("A data de início não pode ser posterior à data de fim", exception.Message);
    }

    [Fact]
    public void CreateDraft_StartEqualToEnd_Throws()
    {
        var instant = Utc(2026, 10, 1, 11, 0);

        var exception = Assert.Throws<BusinessRuleException>(() =>
            Campaign.CreateDraft(NetworkId, "Volta às Aulas", null, instant, instant, Now));

        Assert.Equal("CAMPAIGN_INVALID_PERIOD", exception.Code);
    }

    [Fact]
    public void CreateDraft_Scenario4_StartBeforeToday_Throws()
    {
        var exception = Assert.Throws<BusinessRuleException>(() =>
            Campaign.CreateDraft(NetworkId, "Volta às Aulas", null, Utc(2026, 9, 20, 3, 0), Utc(2026, 10, 31, 3, 0), Now));

        Assert.Equal("CAMPAIGN_START_IN_PAST", exception.Code);
        Assert.Equal("A data de início não pode ser anterior à data atual", exception.Message);
    }

    [Fact]
    public void CreateDraft_StartInCurrentMinute_IsAccepted()
    {
        var now = Now.AddSeconds(42);

        var campaign = Campaign.CreateDraft(NetworkId, "Agora", null, Now, Now.AddDays(1), now);

        Assert.Equal(Now, campaign.StartsAt);
    }

    [Fact]
    public void CreateDraft_StartOneMinuteAgo_Throws()
    {
        var exception = Assert.Throws<BusinessRuleException>(() =>
            Campaign.CreateDraft(NetworkId, "Agora", null, Now.AddMinutes(-1), Now.AddDays(1), Now));

        Assert.Equal("CAMPAIGN_START_IN_PAST", exception.Code);
    }

    [Fact]
    public void CreateDraft_InvertedAndInPast_ReportsInvalidPeriodFirst()
    {
        var exception = Assert.Throws<BusinessRuleException>(() =>
            Campaign.CreateDraft(NetworkId, "Volta às Aulas", null, Utc(2026, 9, 20, 3, 0), Utc(2026, 9, 10, 3, 0), Now));

        Assert.Equal("CAMPAIGN_INVALID_PERIOD", exception.Code);
    }

    [Fact]
    public void CreateDraft_WithNonUtcDate_ThrowsArgumentException()
    {
        var local = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Unspecified);

        Assert.Throws<ArgumentException>(() => Campaign.CreateDraft(NetworkId, "X", null, local, Utc(2026, 11, 1, 0, 0), Now));
    }

    [Fact]
    public void CanIssueVouchers_Scenario3_DraftWithStartedPeriod_IsFalse()
    {
        var campaign = StartedCampaign(CampaignStatus.Draft);

        Assert.False(campaign.CanIssueVouchers(Utc(2026, 10, 10, 12, 0)));
    }

    [Theory]
    [InlineData(CampaignStatus.PendingApproval)]
    [InlineData(CampaignStatus.Scheduled)]
    [InlineData(CampaignStatus.Paused)]
    [InlineData(CampaignStatus.Ended)]
    [InlineData(CampaignStatus.Cancelled)]
    [InlineData(CampaignStatus.SoldOut)]
    public void CanIssueVouchers_NonActiveStatus_IsFalse(CampaignStatus status)
    {
        Assert.False(StartedCampaign(status).CanIssueVouchers(Utc(2026, 10, 10, 12, 0)));
    }

    [Fact]
    public void CanIssueVouchers_Active_OnlyWithinPeriod()
    {
        var campaign = StartedCampaign(CampaignStatus.Active);

        Assert.False(campaign.CanIssueVouchers(campaign.StartsAt.AddTicks(-1)));
        Assert.True(campaign.CanIssueVouchers(campaign.StartsAt));
        Assert.True(campaign.CanIssueVouchers(Utc(2026, 10, 10, 12, 0)));
        Assert.False(campaign.CanIssueVouchers(campaign.EndsAt));
    }

    private static Campaign StartedCampaign(CampaignStatus status)
    {
        var campaign = Campaign.CreateDraft(NetworkId, "Volta às Aulas", null, Utc(2026, 10, 1, 11, 0), Utc(2026, 11, 1, 2, 59), Now);
        campaign.Status = status;

        return campaign;
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
