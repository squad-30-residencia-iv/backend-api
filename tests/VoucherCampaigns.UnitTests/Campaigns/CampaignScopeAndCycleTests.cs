using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.UnitTests.Campaigns;

/// <summary>Escopo (US-02.3), duplicação (US-01.10), pausa e encerramento (US-01.8).</summary>
public class CampaignScopeAndCycleTests
{
    private static readonly Guid NetworkId = Guid.CreateVersion7();
    private static readonly Guid ActorId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();

    // 28/09/2026 12:00 em Brasília; vigência de 01/10 11:00 a 01/11 02:59 (UTC).
    private static readonly DateTime Now = Utc(2026, 9, 28, 15, 0);
    private static readonly DateTime DuringPeriod = Utc(2026, 10, 10, 12, 0);

    [Fact]
    public void Submit_WithoutScope_Throws()
    {
        var campaign = ConfiguredDraft();

        var exception = Assert.Throws<BusinessRuleException>(() => campaign.Submit(ActorId, Now));

        Assert.Equal(CampaignErrors.SubmitScopeRequiredCode, exception.Code);
        Assert.Equal(CampaignStatus.Draft, campaign.Status);
    }

    [Fact]
    public void ReplaceScopes_ReplacesTheListAndRejectsRepeatedLocationAndChannel()
    {
        var campaign = ConfiguredDraft();
        campaign.ReplaceScopes([Scope(null, ChannelType.Physical)], Now);

        campaign.ReplaceScopes([Scope(StoreId, ChannelType.Physical), Scope(StoreId, ChannelType.Virtual)], Now);
        var exception = Assert.Throws<BusinessRuleException>(() =>
            campaign.ReplaceScopes([Scope(StoreId, ChannelType.Physical), Scope(StoreId, ChannelType.Physical)], Now));

        Assert.Equal(CampaignErrors.ScopeDuplicatedCode, exception.Code);
        Assert.Equal(2, campaign.Scopes.Count);
        Assert.All(campaign.Scopes, scope => Assert.Equal(campaign.Id, scope.CampaignId));
    }

    [Fact]
    public void ReplaceScopes_OutsideDraft_Throws()
    {
        var campaign = ReadyCampaign();
        campaign.Status = CampaignStatus.Scheduled;

        var exception = Assert.Throws<BusinessRuleException>(() => campaign.ReplaceScopes([Scope(null, ChannelType.Virtual)], Now));

        Assert.Equal(CampaignErrors.NotDraftCode, exception.Code);
    }

    [Fact]
    public void Pause_ScheduledAlreadyStarted_RecordsTheEffectiveStatus()
    {
        var campaign = ReadyCampaign();
        campaign.Status = CampaignStatus.Scheduled;

        var statusEvent = campaign.Pause(ActorId, DuringPeriod);

        Assert.Equal(CampaignStatus.Paused, campaign.Status);
        Assert.Equal(CampaignStatus.Active, statusEvent.FromStatus);
        Assert.Equal(CampaignStatus.Paused, statusEvent.ToStatus);
        Assert.Equal(ActorId, statusEvent.ActorUserId);
        Assert.False(campaign.CanIssueVouchers(DuringPeriod));
    }

    [Theory]
    [InlineData(CampaignStatus.Scheduled, "2026-09-30T12:00:00Z")]
    [InlineData(CampaignStatus.Draft, "2026-10-10T12:00:00Z")]
    [InlineData(CampaignStatus.Paused, "2026-10-10T12:00:00Z")]
    [InlineData(CampaignStatus.Active, "2026-11-05T12:00:00Z")]
    public void Pause_WhenNotActive_Throws(CampaignStatus stored, string instant)
    {
        var campaign = ReadyCampaign();
        campaign.Status = stored;

        var exception = Assert.Throws<BusinessRuleException>(() => campaign.Pause(ActorId, Parse(instant)));

        Assert.Equal(CampaignErrors.PauseNotActiveCode, exception.Code);
        Assert.Equal(stored, campaign.Status);
    }

    [Theory]
    [InlineData(CampaignStatus.Active)]
    [InlineData(CampaignStatus.Paused)]
    public void End_ActiveOrPaused_BecomesEnded(CampaignStatus stored)
    {
        var campaign = ReadyCampaign();
        campaign.Status = stored;

        var statusEvent = campaign.End(ActorId, DuringPeriod);

        Assert.Equal(CampaignStatus.Ended, campaign.Status);
        Assert.Equal(stored, statusEvent.FromStatus);
        Assert.Equal(CampaignStatus.Ended, campaign.EffectiveStatus(DuringPeriod));
    }

    [Theory]
    [InlineData(CampaignStatus.Scheduled, "2026-09-30T12:00:00Z")]
    [InlineData(CampaignStatus.Rejected, "2026-10-10T12:00:00Z")]
    [InlineData(CampaignStatus.PendingApproval, "2026-10-10T12:00:00Z")]
    [InlineData(CampaignStatus.Ended, "2026-10-10T12:00:00Z")]
    public void End_WhenNotActiveNorPaused_Throws(CampaignStatus stored, string instant)
    {
        var campaign = ReadyCampaign();
        campaign.Status = stored;

        var exception = Assert.Throws<BusinessRuleException>(() => campaign.End(ActorId, Parse(instant)));

        Assert.Equal(CampaignErrors.EndNotAllowedCode, exception.Code);
    }

    [Fact]
    public void Duplicate_CopiesTheConfigurationIntoANewDraft()
    {
        var source = ReadyCampaign();
        source.SetRelativeValidity(30, VoucherValidityUnit.Days, Utc(2026, 12, 31, 3, 0), Now);
        source.ReplaceConditions([new CampaignCondition { Type = ConditionType.PurchaseTotal, MinAmount = 50 }], true, Now);
        source.Submit(ActorId, Now);
        source.Approve(ActorId, Now);

        var copy = source.Duplicate("Volta às Aulas 2", source.StartsAt, source.EndsAt, Now);

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal(CampaignStatus.Draft, copy.Status);
        Assert.Null(copy.SubmittedByUserId);
        Assert.Null(copy.DecidedByUserId);
        Assert.Equal("Volta às Aulas 2", copy.Name);
        Assert.Equal(source.NetworkId, copy.NetworkId);
        Assert.Equal(source.TotalVoucherLimit, copy.TotalVoucherLimit);
        Assert.Equal(source.AudienceType, copy.AudienceType);
        Assert.Equal(source.MaxUsesPerVoucher, copy.MaxUsesPerVoucher);
        Assert.True(copy.AllowUseInSamePurchase);
        Assert.Equal(source.VoucherValidityCap, copy.VoucherValidityCap);
        Assert.Single(copy.Items);
        Assert.Single(copy.Conditions);
        Assert.Single(copy.Scopes);
        Assert.All(copy.Items, item => Assert.Equal(copy.Id, item.CampaignId));
        Assert.DoesNotContain(copy.Items, item => source.Items.Any(original => original.Id == item.Id));
        Assert.Equal(source.Scopes.Single().ChannelType, copy.Scopes.Single().ChannelType);
    }

    [Fact]
    public void Duplicate_DropsValidityDatesThatNoLongerFitTheNewPeriod()
    {
        var withCap = ReadyCampaign();
        withCap.SetRelativeValidity(30, VoucherValidityUnit.Days, Utc(2026, 10, 20, 3, 0), Now);
        var withFixedDate = ReadyCampaign();
        withFixedDate.SetFixedValidity(Utc(2026, 11, 30, 3, 0), Now);

        var cappedCopy = withCap.Duplicate("Nova", Utc(2026, 11, 1, 11, 0), Utc(2026, 12, 1, 2, 59), Now);
        var fixedCopy = withFixedDate.Duplicate("Nova", Utc(2026, 12, 1, 11, 0), Utc(2027, 1, 1, 2, 59), Now);

        Assert.Equal(VoucherValidityType.Relative, cappedCopy.VoucherValidityType);
        Assert.Equal(30, cappedCopy.VoucherValidityAmount);
        Assert.Null(cappedCopy.VoucherValidityCap);
        Assert.Null(fixedCopy.VoucherValidityType);
        Assert.Null(fixedCopy.VoucherValidUntil);
    }

    [Fact]
    public void Duplicate_WithStartInThePast_Throws()
    {
        var source = ReadyCampaign();

        var exception = Assert.Throws<BusinessRuleException>(() =>
            source.Duplicate("Nova", source.StartsAt, source.EndsAt, Utc(2026, 11, 5, 12, 0)));

        Assert.Equal(CampaignErrors.StartInPastCode, exception.Code);
    }

    private static Campaign ConfiguredDraft()
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

    private static Campaign ReadyCampaign()
    {
        var campaign = ConfiguredDraft();
        campaign.ReplaceScopes([Scope(StoreId, ChannelType.Physical)], Now);
        return campaign;
    }

    private static CampaignScope Scope(Guid? establishmentId, ChannelType channel) =>
        new() { Id = Guid.CreateVersion7(), EstablishmentId = establishmentId, ChannelType = channel };

    private static DateTime Parse(string instant) =>
        DateTime.Parse(instant, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

    private static DateTime Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
