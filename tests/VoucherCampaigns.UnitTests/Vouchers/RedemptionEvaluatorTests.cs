using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;
using static VoucherCampaigns.UnitTests.Vouchers.MotorFixtures;

namespace VoucherCampaigns.UnitTests.Vouchers;

/// <summary>Passos 4 e 5 do fluxo da verificação e do resgate (spec GH-049, 1.2): motivos, desconto e recomendação.</summary>
public class RedemptionEvaluatorTests
{
    private const string Transaction = "TRX-RESGATE";

    [Fact]
    public void AvailableVoucherWithinValidity_IsApplicable_WithThePercentageOfTheTotal()
    {
        var campaign = ActiveCampaign();
        var voucher = VoucherOf(campaign, VoucherStatus.Available);

        var refusal = Refusal(voucher, campaign, Purchase());

        Assert.Null(refusal);
        Assert.Equal(20.00m, RedemptionEvaluator.Discount(Target(campaign), Purchase()));
    }

    [Fact]
    public void Discount_OnACategoryTarget_UsesOnlyTheMatchingItems_AndCapsTheFixedAmount()
    {
        var percentage = ActiveCampaign(items: Item(TargetType.Category, BenefitType.Percentage, 10));
        var fixedSmall = ActiveCampaign(items: Item(TargetType.Category, BenefitType.FixedAmount, 50));
        var fixedLarge = ActiveCampaign(items: Item(TargetType.Category, BenefitType.FixedAmount, 500));

        // A mochila (Papelaria) custa 120; as canetas não contam.
        Assert.Equal(12.00m, RedemptionEvaluator.Discount(Target(percentage), Purchase()));
        Assert.Equal(50.00m, RedemptionEvaluator.Discount(Target(fixedSmall), Purchase()));
        Assert.Equal(120.00m, RedemptionEvaluator.Discount(Target(fixedLarge), Purchase()));
    }

    [Fact]
    public void ProvisionalVoucher_OnlyAppliesToItsOwnPurchase_WhenTheCampaignAllowsIt()
    {
        var allowed = ActiveCampaign();
        allowed.Status = CampaignStatus.Draft;
        allowed.ReplaceConditions([], allowUseInSamePurchase: true, Created);
        allowed.Status = CampaignStatus.Active;
        var denied = ActiveCampaign();

        var ownAllowed = VoucherOf(allowed, VoucherStatus.Provisional, reservedUntil: DuringPeriod.AddMinutes(15));
        var ownDenied = VoucherOf(denied, VoucherStatus.Provisional, reservedUntil: DuringPeriod.AddMinutes(15));
        var other = VoucherOf(allowed, VoucherStatus.Provisional, reservedUntil: DuringPeriod.AddMinutes(15), transactionId: "TRX-OUTRA");

        Assert.Null(Refusal(ownAllowed, allowed, Purchase()));
        Assert.Equal(RedemptionRefusal.SamePurchaseNotAllowed, Refusal(ownDenied, denied, Purchase()));
        Assert.Equal(RedemptionRefusal.NotConfirmed, Refusal(other, allowed, Purchase()));
    }

    [Theory]
    [InlineData(VoucherStatus.Expired, RedemptionRefusal.Expired)]
    [InlineData(VoucherStatus.Redeemed, RedemptionRefusal.NoUsesLeft)]
    [InlineData(VoucherStatus.Cancelled, RedemptionRefusal.Cancelled)]
    public void StoredStatus_ThatCannotBeRedeemed_IsRefused(VoucherStatus status, RedemptionRefusal expected)
    {
        var campaign = ActiveCampaign();

        Assert.Equal(expected, Refusal(VoucherOf(campaign, status), campaign, Purchase()));
    }

    [Fact]
    public void AvailableVoucher_AfterItsOwnValidity_IsExpired_EvenIfTheCampaignIsStillActive()
    {
        var campaign = ActiveCampaign();
        var voucher = VoucherOf(campaign, VoucherStatus.Available, validUntil: Utc(2026, 10, 9, 23, 59, 59));

        Assert.Equal(RedemptionRefusal.Expired, Refusal(voucher, campaign, Purchase()));
        Assert.Null(Refusal(voucher, campaign, Purchase(), utcNow: Utc(2026, 10, 9, 23, 59, 59)));
    }

    [Fact]
    public void ProvisionalVoucher_RedeemedInItsOwnPurchaseUpToTheLimit_HasNoUsesLeft()
    {
        var campaign = ActiveCampaign();
        campaign.Status = CampaignStatus.Draft;
        campaign.ReplaceConditions([], allowUseInSamePurchase: true, Created);
        campaign.Status = CampaignStatus.Active;
        var voucher = VoucherOf(campaign, VoucherStatus.Provisional, usesCount: 1, reservedUntil: DuringPeriod.AddMinutes(15));

        Assert.Equal(RedemptionRefusal.NoUsesLeft, Refusal(voucher, campaign, Purchase()));
    }

    [Fact]
    public void PausedEndedOrSoldOutCampaign_DoesNotBlock_ButCancelledDoes()
    {
        foreach (var status in new[] { CampaignStatus.Paused, CampaignStatus.Ended, CampaignStatus.SoldOut })
        {
            var campaign = ActiveCampaign();
            campaign.Status = status;
            Assert.Null(Refusal(VoucherOf(campaign, VoucherStatus.Available), campaign, Purchase()));
        }

        var cancelled = ActiveCampaign();
        cancelled.Status = CampaignStatus.Cancelled;
        Assert.Equal(RedemptionRefusal.CampaignCancelled, Refusal(VoucherOf(cancelled, VoucherStatus.Available), cancelled, Purchase()));
    }

    [Fact]
    public void StoreOrChannelOutsideTheCampaignScope_IsOutOfScope()
    {
        var campaign = ActiveCampaign();
        campaign.Status = CampaignStatus.Draft;
        campaign.ReplaceScopes([Scope(ChannelType.Physical, establishmentId: StoreId)], Created);
        campaign.Status = CampaignStatus.Active;
        var voucher = VoucherOf(campaign, VoucherStatus.Available);

        Assert.Null(Refusal(voucher, campaign, Purchase()));
        Assert.Equal(RedemptionRefusal.OutOfScope, Refusal(voucher, campaign, Purchase(), channel: ChannelType.Virtual));
        Assert.Equal(RedemptionRefusal.OutOfScope, Refusal(voucher, campaign, Purchase(), store: Store(OtherStoreId)));
    }

    [Fact]
    public void PurchaseOutsideTheAmountRange_IsRefused_OnTheGrossTotal()
    {
        var campaign = ActiveCampaign();
        campaign.Status = CampaignStatus.Draft;
        campaign.SetUsageRestrictions(150m, 300m, 1, Created);
        campaign.Status = CampaignStatus.Active;
        var voucher = VoucherOf(campaign, VoucherStatus.Available);

        Assert.Equal(RedemptionRefusal.AmountBelowMinimum, Refusal(voucher, campaign, Purchase(totalAmount: 149.99m)));
        Assert.Null(Refusal(voucher, campaign, Purchase(totalAmount: 150m)));
        Assert.Null(Refusal(voucher, campaign, Purchase(totalAmount: 300m)));
        Assert.Equal(RedemptionRefusal.AmountAboveMaximum, Refusal(voucher, campaign, Purchase(totalAmount: 300.01m)));
    }

    [Fact]
    public void TargetAbsentFromThePurchase_IsRefused_ExceptForTheAllTarget()
    {
        var category = ActiveCampaign(items: Item(TargetType.Category, BenefitType.Percentage, 10));
        var all = ActiveCampaign();
        var withoutPapelaria = Purchase(items: new PurchaseItem("CAN-001", 2, 100m, CanetaId, null));

        Assert.Equal(RedemptionRefusal.TargetNotInPurchase, Refusal(VoucherOf(category, VoucherStatus.Available), category, withoutPapelaria));
        Assert.Null(Refusal(VoucherOf(all, VoucherStatus.Available), all, withoutPapelaria));
    }

    [Fact]
    public void PurchaseThatAlreadyRedeemed_RefusesOtherVouchers_AndFlagsTheRedeemedOne()
    {
        var campaign = ActiveCampaign();
        var redeemed = VoucherOf(campaign, VoucherStatus.Available);
        var other = VoucherOf(campaign, VoucherStatus.Available);
        var existing = new Redemption { Id = Guid.CreateVersion7(), VoucherId = redeemed.Id, TransactionId = Transaction };

        Assert.Equal(RedemptionRefusal.AlreadyRedeemedInThisPurchase, Refusal(redeemed, campaign, Purchase(), existing: existing));
        Assert.Equal(RedemptionRefusal.TransactionAlreadyRedeemed, Refusal(other, campaign, Purchase(), existing: existing));
    }

    [Fact]
    public void Rank_PutsTheLargestDiscountFirst_ThenTheOneThatExpiresFirst()
    {
        var campaign = ActiveCampaign();
        var small = new RedemptionCandidate(VoucherOf(campaign, VoucherStatus.Available), null, 15m);
        var large = new RedemptionCandidate(VoucherOf(campaign, VoucherStatus.Available), null, 27m);
        var largeLater = new RedemptionCandidate(VoucherOf(campaign, VoucherStatus.Available, validUntil: Utc(2026, 12, 1, 0, 0)), null, 27m);
        var refused = new RedemptionCandidate(VoucherOf(campaign, VoucherStatus.Available), RedemptionRefusal.Expired, 0m);

        var ranked = RedemptionEvaluator.Rank([small, refused, largeLater, large]);

        Assert.Equal([large, largeLater, small], ranked);
    }

    [Theory]
    [InlineData(RedemptionRefusal.NoUsesLeft, "REDEMPTION_NO_USES_LEFT")]
    [InlineData(RedemptionRefusal.AmountBelowMinimum, "REDEMPTION_AMOUNT_BELOW_MINIMUM")]
    [InlineData(RedemptionRefusal.TransactionAlreadyRedeemed, "REDEMPTION_TRANSACTION_ALREADY_REDEEMED")]
    public void ErrorCode_IsThePrefixedSnakeCaseOfTheReason(RedemptionRefusal refusal, string expected) =>
        Assert.Equal(expected, RedemptionErrors.CodeOf(refusal));

    private static RedemptionRefusal? Refusal(
        Voucher voucher,
        Campaign campaign,
        Purchase purchase,
        ChannelType channel = ChannelType.Physical,
        Domain.Organization.Establishment? store = null,
        Redemption? existing = null,
        DateTime? utcNow = null) =>
        RedemptionEvaluator.Refusal(
            voucher, campaign, Target(campaign), voucher.Concession!, purchase, store ?? Store(), channel, Transaction, existing, utcNow ?? DuringPeriod);

    private static CampaignItem Target(Campaign campaign) => campaign.Items.OrderBy(item => item.Id).First();

    private static Voucher VoucherOf(
        Campaign campaign,
        VoucherStatus status,
        int maxUses = 1,
        int usesCount = 0,
        DateTime? validUntil = null,
        DateTime? reservedUntil = null,
        string transactionId = Transaction)
    {
        var concession = new Concession { Id = NextId(), NetworkId = NetworkId, TransactionId = transactionId, CustomerDocument = "12345678901" };

        return new Voucher
        {
            Id = NextId(),
            Code = VoucherCode.Generate(),
            NetworkId = NetworkId,
            CampaignId = campaign.Id,
            Campaign = campaign,
            CampaignItemId = Target(campaign).Id,
            Item = Target(campaign),
            ConcessionId = concession.Id,
            Concession = concession,
            CustomerDocument = "12345678901",
            Status = status,
            IssuedAt = Utc(2026, 10, 5, 12, 0),
            ValidUntil = validUntil ?? Utc(2026, 11, 4, 12, 0),
            ReservedUntil = reservedUntil,
            MaxUses = maxUses,
            UsesCount = usesCount,
            EstablishmentId = StoreId,
            Channel = ChannelType.Physical
        };
    }
}
