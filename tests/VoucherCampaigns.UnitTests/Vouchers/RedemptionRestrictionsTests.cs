using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;
using static VoucherCampaigns.UnitTests.Vouchers.MotorFixtures;

namespace VoucherCampaigns.UnitTests.Vouchers;

/// <summary>Alvos e janelas de resgate (spec GH-070, D-2 e D-3) no domínio, sem fuso: a hora já chega local.</summary>
public class RedemptionRestrictionsTests
{
    [Theory]
    [InlineData(2026, 10, 16, 8, 0, true)]    // sexta, início
    [InlineData(2026, 10, 16, 11, 59, true)]  // sexta, o minuto do fim conta inteiro
    [InlineData(2026, 10, 16, 12, 0, false)]
    [InlineData(2026, 10, 16, 7, 59, false)]
    [InlineData(2026, 10, 17, 9, 0, false)]   // sábado
    public void Window_OnWeekdayMornings(int year, int month, int day, int hour, int minute, bool expected)
    {
        var window = Window(8 * 60, 11 * 60 + 59, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday);

        Assert.Equal(expected, window.Contains(new DateTime(year, month, day, hour, minute, 59)));
    }

    [Theory]
    [InlineData(2026, 10, 16, 22, 0, true)]   // sexta 22:00
    [InlineData(2026, 10, 16, 23, 59, true)]
    [InlineData(2026, 10, 17, 0, 0, true)]    // sábado 00:00, ainda a janela de sexta
    [InlineData(2026, 10, 17, 1, 59, true)]
    [InlineData(2026, 10, 17, 2, 0, false)]
    [InlineData(2026, 10, 16, 1, 30, false)]  // sexta de madrugada pertence à janela de quinta, que não existe
    [InlineData(2026, 10, 17, 22, 30, false)] // sábado à noite: a janela só começa na sexta
    public void Window_ThatCrossesMidnight_BelongsToTheStartDay(int year, int month, int day, int hour, int minute, bool expected)
    {
        var window = Window(22 * 60, 1 * 60 + 59, DayOfWeek.Friday);

        Assert.Equal(expected, window.Contains(new DateTime(year, month, day, hour, minute, 0)));
    }

    [Fact]
    public void Campaign_WithoutWindows_IsAlwaysRedeemable_AndWithWindows_NeedsOneOfThem()
    {
        var campaign = ActiveCampaign();
        Assert.True(campaign.IsRedeemableAt(new DateTime(2026, 10, 17, 3, 0, 0)));

        campaign.RedemptionWindows.Add(Window(8 * 60, 9 * 60, DayOfWeek.Saturday));
        campaign.RedemptionWindows.Add(Window(20 * 60, 21 * 60, DayOfWeek.Saturday));

        Assert.False(campaign.IsRedeemableAt(new DateTime(2026, 10, 17, 3, 0, 0)));
        Assert.True(campaign.IsRedeemableAt(new DateTime(2026, 10, 17, 20, 30, 0)));
    }

    [Fact]
    public void Evaluator_RefusesOutsideTheWindow()
    {
        var campaign = ActiveCampaign();
        campaign.RedemptionWindows.Add(Window(8 * 60, 9 * 60, DayOfWeek.Monday));
        var voucher = AvailableVoucher(campaign);

        Assert.Equal(RedemptionRefusal.OutsideRedemptionWindow, Refusal(voucher, campaign, Purchase(), Store(), DuringPeriod));
        Assert.Null(Refusal(voucher, campaign, Purchase(), Store(), new DateTime(2026, 10, 12, 8, 30, 0)));
    }

    [Fact]
    public void Evaluator_WithRedemptionTargets_IgnoresTheConcessionTarget_AndUsesThemAsTheDiscountBase()
    {
        // Concessão em tudo (10%); resgate só nas canetas, R$ 80 (2 x R$ 40) na compra padrão.
        var campaign = ActiveCampaign();
        campaign.RedemptionTargets.Add(new CampaignRedemptionTarget { Id = NextId(), TargetType = TargetType.Product, ProductId = CanetaId });
        var voucher = AvailableVoucher(campaign);
        var target = campaign.Items.Single();

        Assert.Null(Refusal(voucher, campaign, Purchase(), Store(), DuringPeriod));
        Assert.Equal(8.00m, RedemptionEvaluator.Discount(target, Purchase(), campaign.RedemptionTargets.ToList()));

        var withoutPen = Purchase(120m, null, new PurchaseItem("MOCH-G", 1, 120m, MochilaId, PapelariaId));
        Assert.Equal(RedemptionRefusal.TargetNotInPurchase, Refusal(voucher, campaign, withoutPen, Store(), DuringPeriod));
    }

    [Fact]
    public void Discount_CountsAnItemMatchingTwoRedemptionTargetsOnce()
    {
        var target = Item(TargetType.All, BenefitType.FixedAmount, 500);
        var redemptionTargets = new List<CampaignRedemptionTarget>
        {
            new() { TargetType = TargetType.Category, CategoryId = PapelariaId },
            new() { TargetType = TargetType.Product, ProductId = MochilaId }
        };

        // A mochila (R$ 120) casa com a categoria e com o produto; o valor fixo fica limitado a ela.
        Assert.Equal(120.00m, RedemptionEvaluator.Discount(target, Purchase(), redemptionTargets));
    }

    [Fact]
    public void ReplaceRedemptionTargets_RefusesRepeatedTargets_AndOutsideTheDraft()
    {
        var draft = Campaign.CreateDraft(NetworkId, "Rascunho", null, Utc(2026, 10, 1, 11, 0), Utc(2026, 10, 31, 23, 0), Created);
        var shoes = new CampaignRedemptionTarget { TargetType = TargetType.Category, CategoryId = PapelariaId };
        var again = new CampaignRedemptionTarget { TargetType = TargetType.Category, CategoryId = PapelariaId };

        var repeated = Assert.Throws<BusinessRuleException>(() => draft.ReplaceRedemptionTargets([shoes, again], Created));
        Assert.Equal(CampaignErrors.RedemptionTargetDuplicatedCode, repeated.Code);
        Assert.Empty(draft.RedemptionTargets);

        var active = ActiveCampaign();
        var notDraft = Assert.Throws<BusinessRuleException>(() => active.ReplaceRedemptionWindows([Window(0, 60, DayOfWeek.Monday)], Created));
        Assert.Equal(CampaignErrors.NotDraftCode, notDraft.Code);
    }

    [Fact]
    public void Duplicate_CopiesTheRedemptionTargetsAndWindows()
    {
        var source = ActiveCampaign();
        source.RedemptionTargets.Add(new CampaignRedemptionTarget { Id = NextId(), TargetType = TargetType.Product, ProductId = CanetaId });
        source.RedemptionWindows.Add(Window(22 * 60, 2 * 60, DayOfWeek.Friday, DayOfWeek.Saturday));

        var copy = source.Duplicate("Cópia", Utc(2026, 11, 1, 11, 0), Utc(2026, 11, 30, 23, 0), Created);

        var target = Assert.Single(copy.RedemptionTargets);
        Assert.Equal((TargetType.Product, CanetaId, copy.Id), (target.TargetType, target.ProductId!.Value, target.CampaignId));
        var window = Assert.Single(copy.RedemptionWindows);
        Assert.Equal([DayOfWeek.Friday, DayOfWeek.Saturday], window.Days);
        Assert.Equal((22 * 60, 2 * 60), (window.StartMinute, window.EndMinute));
    }

    private static CampaignRedemptionWindow Window(int start, int end, params DayOfWeek[] days) => new()
    {
        Id = NextId(),
        DaysOfWeek = CampaignRedemptionWindow.MaskOf(days),
        StartMinute = start,
        EndMinute = end
    };

    private static RedemptionRefusal? Refusal(Voucher voucher, Campaign campaign, Purchase purchase, Establishment store, DateTime networkLocalNow) =>
        RedemptionEvaluator.Refusal(
            voucher, campaign, campaign.Items.Single(), voucher.Concession!, purchase, store, ChannelType.Physical, "TRX-JANELA", null,
            DuringPeriod, networkLocalNow);

    private static Voucher AvailableVoucher(Campaign campaign)
    {
        var concession = new Concession { Id = NextId(), NetworkId = NetworkId, TransactionId = "TRX-ORIGEM", CustomerDocument = "12345678901" };
        var target = campaign.Items.Single();

        return new Voucher
        {
            Id = NextId(),
            Code = VoucherCode.Generate(),
            NetworkId = NetworkId,
            CampaignId = campaign.Id,
            Campaign = campaign,
            CampaignItemId = target.Id,
            Item = target,
            ConcessionId = concession.Id,
            Concession = concession,
            CustomerDocument = "12345678901",
            Status = VoucherStatus.Available,
            IssuedAt = Utc(2026, 10, 5, 12, 0),
            ValidUntil = Utc(2026, 11, 4, 12, 0),
            MaxUses = 1,
            EstablishmentId = StoreId,
            Channel = ChannelType.Physical
        };
    }
}
