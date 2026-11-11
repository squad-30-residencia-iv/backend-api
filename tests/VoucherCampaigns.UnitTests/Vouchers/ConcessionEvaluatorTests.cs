using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;
using static VoucherCampaigns.UnitTests.Vouchers.MotorFixtures;

namespace VoucherCampaigns.UnitTests.Vouchers;

/// <summary>Passos 4 a 7 do fluxo da concessão (spec GH-045, 1.2): condições, escopo, bloqueios, alvo e desempate D1.</summary>
public class ConcessionEvaluatorTests
{
    private static readonly IReadOnlyDictionary<Guid, int> NoVouchers = new Dictionary<Guid, int>();

    // Esquema do cenário da US-04.1: valor total, volume de itens e categoria.
    [Theory]
    [InlineData(ConditionType.PurchaseTotal, 180, true)]
    [InlineData(ConditionType.PurchaseTotal, 149.99, false)]
    [InlineData(ConditionType.ItemQuantity, 12, true)]
    [InlineData(ConditionType.ItemQuantity, 9, false)]
    public void Condition_IsEvaluatedAgainstThePurchase(ConditionType type, decimal value, bool expected)
    {
        var campaign = ActiveCampaign();
        campaign.WithConditions(type == ConditionType.PurchaseTotal
            ? Condition(ConditionType.PurchaseTotal, minAmount: 150)
            : Condition(ConditionType.ItemQuantity, minQuantity: 10));

        var purchase = type == ConditionType.PurchaseTotal
            ? Purchase(totalAmount: value)
            : Purchase(items: [new PurchaseItem("CAN-001", (int)value, 1m, null, null)]);

        Assert.Equal(expected, campaign.IsSatisfiedBy(purchase));
    }

    [Fact]
    public void Conditions_AreCumulative()
    {
        var campaign = ActiveCampaign();
        campaign.WithConditions(Condition(ConditionType.PurchaseTotal, minAmount: 150), Condition(ConditionType.Category, categoryId: PapelariaId));

        Assert.True(campaign.IsSatisfiedBy(Purchase()));
        Assert.False(campaign.IsSatisfiedBy(Purchase(items: [new PurchaseItem("CAN-001", 5, 40m, CanetaId, null)])));
        Assert.False(campaign.IsSatisfiedBy(Purchase(totalAmount: 149.99m)));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(2, false)]
    [InlineData(null, false)]
    public void CustomerPurchaseCount_UsesTheValueSentBySatellite(int? purchases, bool expected)
    {
        var campaign = ActiveCampaign();
        campaign.WithConditions(Condition(ConditionType.CustomerPurchaseCount, minQuantity: 3));

        Assert.Equal(expected, campaign.IsSatisfiedBy(Purchase(customerPurchaseCount: purchases)));
    }

    [Fact]
    public void Product_MatchesByCatalogIdOnly()
    {
        var campaign = ActiveCampaign();
        campaign.WithConditions(Condition(ConditionType.Product, productId: MochilaId));

        Assert.True(campaign.IsSatisfiedBy(Purchase()));
        Assert.False(campaign.IsSatisfiedBy(Purchase(items: [new PurchaseItem("MOCH-G", 1, 120m, null, null)])));
    }

    [Fact]
    public void Scope_MatchesNetworkRegionOrStoreWithTheChannel()
    {
        var store = Store();
        var network = ActiveCampaign();
        network.Status = CampaignStatus.Draft;
        network.ReplaceScopes([Scope(ChannelType.Physical)], Created);
        var region = ActiveCampaign();
        region.Status = CampaignStatus.Draft;
        region.ReplaceScopes([Scope(ChannelType.Virtual, regionId: RegionId)], Created);
        var otherStore = ActiveCampaign();
        otherStore.Status = CampaignStatus.Draft;
        otherStore.ReplaceScopes([Scope(ChannelType.Physical, establishmentId: OtherStoreId)], Created);

        Assert.True(network.IsInScope(store, ChannelType.Physical));
        Assert.False(network.IsInScope(store, ChannelType.Virtual));
        Assert.True(region.IsInScope(store, ChannelType.Virtual));
        Assert.False(region.IsInScope(store, ChannelType.Physical));
        Assert.False(otherStore.IsInScope(store, ChannelType.Physical));
        Assert.True(otherStore.IsInScope(Store(OtherStoreId), ChannelType.Physical));
    }

    [Fact]
    public void Evaluate_WithoutSatisfiedCampaign_ReturnsNoCampaignMatched()
    {
        var campaign = ActiveCampaign();
        campaign.WithConditions(Condition(ConditionType.PurchaseTotal, minAmount: 500));

        var result = ConcessionEvaluator.Evaluate([campaign], Purchase(), NoVouchers, DuringPeriod);

        Assert.Empty(result.Eligible);
        Assert.Equal(ConcessionRefusal.NoCampaignMatched, result.Refusal);
        Assert.Null(result.RefusedCampaignId);
    }

    // D-9: 10% de R$ 200 (R$ 20) vence R$ 15 fixos; o empate vai para a que termina primeiro, depois a aprovada primeiro.
    [Fact]
    public void Evaluate_OrdersByBenefitThenEndThenApproval()
    {
        var percent = ActiveCampaign("Dez por cento", items: Item(TargetType.All, BenefitType.Percentage, 10));
        var fixedAmount = ActiveCampaign("Quinze reais", items: Item(TargetType.All, BenefitType.FixedAmount, 15));
        var sameBenefitEndsFirst = ActiveCampaign("Termina antes", endsAt: Utc(2026, 10, 20, 2, 59), items: Item(TargetType.All, BenefitType.FixedAmount, 20));
        var sameBenefitApprovedFirst = ActiveCampaign("Aprovada antes", decidedAt: Decided.AddDays(-1), items: Item(TargetType.All, BenefitType.FixedAmount, 20));

        var result = ConcessionEvaluator.Evaluate(
            [fixedAmount, sameBenefitApprovedFirst, percent, sameBenefitEndsFirst], Purchase(), NoVouchers, DuringPeriod);

        Assert.Equal(
            ["Termina antes", "Aprovada antes", "Dez por cento", "Quinze reais"],
            result.Eligible.Select(entry => entry.Campaign.Name));
        Assert.Equal(20m, result.Eligible[2].EstimatedBenefit);
        Assert.Null(result.Refusal);
    }

    [Fact]
    public void Evaluate_IsDeterministic_WhenEverythingTies()
    {
        var first = ActiveCampaign("A");
        var second = ActiveCampaign("B");

        var once = ConcessionEvaluator.Evaluate([second, first], Purchase(), NoVouchers, DuringPeriod);
        var again = ConcessionEvaluator.Evaluate([first, second], Purchase(), NoVouchers, DuringPeriod);

        Assert.Equal(once.Eligible[0].Campaign.Id, again.Eligible[0].Campaign.Id);
        Assert.Equal(first.Id, once.Eligible[0].Campaign.Id);
    }

    // D-10: produto antes de categoria antes de "todos"; sem alvo na compra, o primeiro configurado.
    [Fact]
    public void ChooseTarget_PrefersTheMostSpecificTargetInThePurchase()
    {
        var all = Item(TargetType.All, BenefitType.Percentage, 5);
        var category = Item(TargetType.Category, BenefitType.Percentage, 10);
        var product = Item(TargetType.Product, BenefitType.FixedAmount, 30);
        var campaign = ActiveCampaign(items: [all, category, product]);

        Assert.Equal(product.Id, campaign.ChooseTarget(Purchase()).Id);
        Assert.Equal(category.Id, campaign.ChooseTarget(Purchase(items: [new PurchaseItem("CAD-1", 1, 10m, CanetaId, PapelariaId)])).Id);
        Assert.Equal(all.Id, campaign.ChooseTarget(Purchase(items: [new PurchaseItem("X", 1, 10m, null, null)])).Id);

        var onlyCategory = ActiveCampaign(items: [Item(TargetType.Category, BenefitType.Percentage, 10), Item(TargetType.Product, BenefitType.FixedAmount, 30)]);
        Assert.Equal(TargetType.Category, onlyCategory.ChooseTarget(Purchase(items: [new PurchaseItem("X", 1, 10m, null, null)])).TargetType);
    }

    [Fact]
    public void EstimatedBenefit_PercentageUsesTheTargetItemsOfThePurchase()
    {
        var category = Item(TargetType.Category, BenefitType.Percentage, 10);
        var all = Item(TargetType.All, BenefitType.Percentage, 10);
        var absent = Item(TargetType.Product, BenefitType.Percentage, 50);
        absent.ProductId = Guid.CreateVersion7();

        Assert.Equal(12m, ConcessionEvaluator.EstimatedBenefit(category, Purchase()));
        Assert.Equal(20m, ConcessionEvaluator.EstimatedBenefit(all, Purchase()));
        Assert.Equal(0m, ConcessionEvaluator.EstimatedBenefit(absent, Purchase()));
    }

    // Esquema da US-04.4: só a efetivamente ativa concede.
    [Theory]
    [InlineData(CampaignStatus.Scheduled, "2026-09-30T12:00:00Z", ConcessionRefusal.CampaignNotActive)]
    [InlineData(CampaignStatus.Paused, "2026-10-10T12:00:00Z", ConcessionRefusal.CampaignNotActive)]
    [InlineData(CampaignStatus.Ended, "2026-10-10T12:00:00Z", ConcessionRefusal.CampaignNotActive)]
    [InlineData(CampaignStatus.Cancelled, "2026-10-10T12:00:00Z", ConcessionRefusal.CampaignNotActive)]
    [InlineData(CampaignStatus.Active, "2026-11-01T03:00:00Z", ConcessionRefusal.CampaignNotActive)]
    [InlineData(CampaignStatus.Scheduled, "2026-10-10T12:00:00Z", null)]
    public void Evaluate_BlocksCampaignsThatAreNotEffectivelyActive(CampaignStatus stored, string instant, ConcessionRefusal? expected)
    {
        var campaign = ActiveCampaign();
        campaign.Status = stored;
        var utcNow = DateTime.Parse(instant, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

        var result = ConcessionEvaluator.Evaluate([campaign], Purchase(), NoVouchers, utcNow);

        Assert.Equal(expected, result.Refusal);
        Assert.Equal(expected is null ? campaign.Id : null, result.Eligible.FirstOrDefault()?.Campaign.Id);
        Assert.Equal(expected is null ? null : campaign.Id, result.RefusedCampaignId);
    }

    [Fact]
    public void Evaluate_SoldOutCampaign_CountsReservations()
    {
        var campaign = ActiveCampaign(totalVoucherLimit: 5);
        campaign.IssuedVouchers = 3;
        campaign.ReservedVouchers = 2;

        var result = ConcessionEvaluator.Evaluate([campaign], Purchase(), NoVouchers, DuringPeriod);

        Assert.Equal(CampaignStatus.SoldOut, campaign.EffectiveStatus(DuringPeriod));
        Assert.Equal(ConcessionRefusal.CampaignSoldOut, result.Refusal);

        campaign.ReservedVouchers = 1;
        Assert.Equal(CampaignStatus.Active, campaign.EffectiveStatus(DuringPeriod));
        Assert.Single(ConcessionEvaluator.Evaluate([campaign], Purchase(), NoVouchers, DuringPeriod).Eligible);
    }

    [Fact]
    public void Evaluate_WithoutTotal_NeverSellsOut()
    {
        var campaign = ActiveCampaign(totalVoucherLimit: null);
        campaign.IssuedVouchers = 1_000_000;

        Assert.Equal(CampaignStatus.Active, campaign.EffectiveStatus(DuringPeriod));
    }

    [Fact]
    public void Evaluate_TargetWithoutQuota_RefusesWithoutFallingBackToAnotherTarget()
    {
        var mochila = Item(TargetType.Product, BenefitType.FixedAmount, 30, voucherLimit: 10);
        var papelaria = Item(TargetType.Category, BenefitType.Percentage, 10, voucherLimit: 50);
        var campaign = ActiveCampaign(items: [mochila, papelaria]);
        mochila.IssuedVouchers = 10;

        var result = ConcessionEvaluator.Evaluate([campaign], Purchase(), NoVouchers, DuringPeriod);

        Assert.Empty(result.Eligible);
        Assert.Equal(ConcessionRefusal.TargetSoldOut, result.Refusal);
        Assert.True(papelaria.HasQuota);
    }

    [Fact]
    public void Evaluate_CustomerAtTheLimit_IsRefused()
    {
        var campaign = ActiveCampaign();
        campaign.PerCustomerVoucherLimit = 2;
        var vouchers = new Dictionary<Guid, int> { [campaign.Id] = 2 };

        var result = ConcessionEvaluator.Evaluate([campaign], Purchase(), vouchers, DuringPeriod);

        Assert.Equal(ConcessionRefusal.CustomerLimitReached, result.Refusal);
        Assert.Single(ConcessionEvaluator.Evaluate([campaign], Purchase(), new Dictionary<Guid, int> { [campaign.Id] = 1 }, DuringPeriod).Eligible);
    }

    [Fact]
    public void Evaluate_PrefersTheRefusalOfAnActiveCampaignOverAnInactiveOne()
    {
        var paused = ActiveCampaign("Pausada", items: Item(TargetType.All, BenefitType.FixedAmount, 50));
        paused.Status = CampaignStatus.Paused;
        var soldOut = ActiveCampaign("Esgotada", totalVoucherLimit: 1, items: Item(TargetType.All, BenefitType.FixedAmount, 5));
        soldOut.IssuedVouchers = 1;

        var result = ConcessionEvaluator.Evaluate([paused, soldOut], Purchase(), NoVouchers, DuringPeriod);

        Assert.Empty(result.Eligible);
        Assert.Equal(ConcessionRefusal.CampaignSoldOut, result.Refusal);
        Assert.Equal(soldOut.Id, result.RefusedCampaignId);
    }

    [Fact]
    public void Evaluate_ReportsTheBestRankedBlockedCampaign_OnlyWhenNoneIsEligible()
    {
        var blocked = ActiveCampaign("Pausada", items: Item(TargetType.All, BenefitType.FixedAmount, 50));
        blocked.Status = CampaignStatus.Paused;
        var eligible = ActiveCampaign("Ativa", items: Item(TargetType.All, BenefitType.FixedAmount, 5));

        var result = ConcessionEvaluator.Evaluate([blocked, eligible], Purchase(), NoVouchers, DuringPeriod);

        Assert.Equal(eligible.Id, Assert.Single(result.Eligible).Campaign.Id);
        Assert.Null(result.Refusal);
    }
}
