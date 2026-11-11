using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Vouchers;
using static VoucherCampaigns.UnitTests.Vouchers.MotorFixtures;

namespace VoucherCampaigns.UnitTests.Vouchers;

/// <summary>Validade calculada na emissão (US-04.3) com o minuto do fim inteiro (D9), e o status efetivo do voucher.</summary>
public class VoucherValidityTests
{
    private static Campaign Draft()
    {
        var campaign = ActiveCampaign();
        campaign.Status = CampaignStatus.Draft;
        return campaign;
    }

    [Fact]
    public void Relative_AddsDaysToTheIssueInstant()
    {
        var campaign = Draft();
        campaign.SetRelativeValidity(30, VoucherValidityUnit.Days, null, Created);

        Assert.Equal(Utc(2026, 10, 31, 17, 30), VoucherValidity.Compute(campaign, Utc(2026, 10, 1, 17, 30)));
    }

    [Fact]
    public void Relative_InHours()
    {
        var campaign = Draft();
        campaign.SetRelativeValidity(48, VoucherValidityUnit.Hours, null, Created);

        Assert.Equal(Utc(2026, 10, 3, 17, 30), VoucherValidity.Compute(campaign, Utc(2026, 10, 1, 17, 30)));
    }

    [Fact]
    public void FixedDate_EndsAtTheLastSecondOfTheMinute()
    {
        var campaign = Draft();
        campaign.SetFixedValidity(Utc(2026, 11, 16, 2, 59), Created);

        Assert.Equal(Utc(2026, 11, 16, 2, 59, 59), VoucherValidity.Compute(campaign, Utc(2026, 10, 1, 17, 30)));
    }

    [Fact]
    public void Relative_WithCap_TheCapPrevails()
    {
        var campaign = Draft();
        campaign.SetRelativeValidity(60, VoucherValidityUnit.Days, Utc(2026, 11, 1, 2, 59), Created);

        Assert.Equal(Utc(2026, 11, 1, 2, 59, 59), VoucherValidity.Compute(campaign, Utc(2026, 10, 25, 12, 0)));
        Assert.Equal(Utc(2026, 10, 11, 12, 0), VoucherValidity.Compute(campaign.WithValidity(10), Utc(2026, 10, 1, 12, 0)));
    }

    [Fact]
    public void Voucher_IsValidUntilTheLastSecondInclusive()
    {
        var voucher = new Voucher { Status = VoucherStatus.Available, ValidUntil = Utc(2026, 11, 1, 2, 59, 59) };

        Assert.Equal(VoucherStatus.Available, voucher.EffectiveStatus(Utc(2026, 11, 1, 2, 59, 59).AddMilliseconds(900)));
        Assert.Equal(VoucherStatus.Expired, voucher.EffectiveStatus(Utc(2026, 11, 1, 3, 0)));
    }

    [Fact]
    public void Voucher_ProvisionalWithExpiredReservation_IsDiscarded()
    {
        var voucher = new Voucher { Status = VoucherStatus.Provisional, ValidUntil = Utc(2026, 12, 1, 0, 0), ReservedUntil = Utc(2026, 10, 1, 12, 15) };

        Assert.Equal(VoucherStatus.Provisional, voucher.EffectiveStatus(Utc(2026, 10, 1, 12, 14, 59)));
        Assert.Equal(VoucherStatus.Discarded, voucher.EffectiveStatus(Utc(2026, 10, 1, 12, 15)));
    }

    [Fact]
    public void Concession_ProvisionalWithExpiredReservation_IsExpired()
    {
        var concession = new Concession { Status = ConcessionStatus.Provisional, ReservedUntil = Utc(2026, 10, 1, 12, 15) };

        Assert.Equal(ConcessionStatus.Provisional, concession.EffectiveStatus(Utc(2026, 10, 1, 12, 14)));
        Assert.Equal(ConcessionStatus.Expired, concession.EffectiveStatus(Utc(2026, 10, 1, 12, 15)));
    }

    [Fact]
    public void BusinessTime_TheWholeMinuteCounts()
    {
        var end = Utc(2026, 11, 1, 2, 59);

        Assert.False(BusinessTime.HasPassed(end, Utc(2026, 11, 1, 2, 59, 59)));
        Assert.True(BusinessTime.HasPassed(end, Utc(2026, 11, 1, 3, 0)));
        Assert.Equal(Utc(2026, 11, 1, 2, 59, 59), BusinessTime.LastSecondOfMinute(end.AddSeconds(30)));
    }

    [Fact]
    public void Approve_InTheLastSecondOfThePeriod_StillApproves()
    {
        var campaign = ActiveCampaign();
        campaign.Status = CampaignStatus.PendingApproval;

        campaign.Approve(Guid.CreateVersion7(), Utc(2026, 11, 1, 2, 59, 59));

        Assert.Equal(CampaignStatus.Active, campaign.Status);
    }

    [Fact]
    public void VoucherCode_Has12CrockfordCharactersAndDoesNotRepeat()
    {
        var codes = Enumerable.Range(0, 1000).Select(_ => VoucherCode.Generate()).ToList();

        Assert.All(codes, code => Assert.Matches("^[0-9ABCDEFGHJKMNPQRSTVWXYZ]{12}$", code));
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }
}

file static class CampaignExtensions
{
    public static Campaign WithValidity(this Campaign campaign, int days)
    {
        campaign.Status = CampaignStatus.Draft;
        campaign.SetRelativeValidity(days, VoucherValidityUnit.Days, campaign.VoucherValidityCap, Created);
        return campaign;
    }
}
