using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.UnitTests.Vouchers;

/// <summary>Campanhas e compras prontas para os testes do motor. Vigência de 01/10 11:00 a 01/11 02:59 (UTC), aprovada em 28/09.</summary>
internal static class MotorFixtures
{
    public static readonly Guid NetworkId = Guid.CreateVersion7();
    public static readonly Guid RegionId = Guid.CreateVersion7();
    public static readonly Guid StoreId = Guid.CreateVersion7();
    public static readonly Guid OtherStoreId = Guid.CreateVersion7();
    public static readonly Guid PapelariaId = Guid.CreateVersion7();
    public static readonly Guid MochilaId = Guid.CreateVersion7();
    public static readonly Guid CanetaId = Guid.CreateVersion7();

    public static readonly DateTime Created = Utc(2026, 9, 28, 15, 0);
    public static readonly DateTime Decided = Utc(2026, 9, 29, 15, 0);
    public static readonly DateTime DuringPeriod = Utc(2026, 10, 10, 12, 0);

    private static int _sequence;

    public static Establishment Store(Guid? id = null) => new()
    {
        Id = id ?? StoreId,
        RegionId = RegionId,
        Code = "001",
        Name = "Loja Centro",
        Channels = [new Channel { Type = ChannelType.Physical }]
    };

    /// <summary>Campanha ativa, rede inteira nos dois canais, alvo "todos" de 10%, total 100, validade de 30 dias, 1 uso.</summary>
    public static Campaign ActiveCampaign(
        string name = "Volta às Aulas",
        DateTime? endsAt = null,
        DateTime? decidedAt = null,
        int? totalVoucherLimit = 100,
        params CampaignItem[] items)
    {
        var campaign = Campaign.CreateDraft(NetworkId, name, null, Utc(2026, 10, 1, 11, 0), endsAt ?? Utc(2026, 11, 1, 2, 59), Created);
        campaign.Id = NextId();
        campaign.ReplaceItems(items.Length == 0 ? [Item(TargetType.All, BenefitType.Percentage, 10)] : items, Created);

        if (totalVoucherLimit is { } total)
        {
            campaign.SetQuantities(total, null, Created);
        }

        campaign.SetAudience(CampaignAudienceType.AllCustomers, Created);
        campaign.SetRelativeValidity(30, VoucherValidityUnit.Days, null, Created);
        campaign.SetUsageRestrictions(null, null, 1, Created);
        campaign.ReplaceScopes([Scope(ChannelType.Physical), Scope(ChannelType.Virtual)], Created);
        campaign.Status = CampaignStatus.Active;
        campaign.DecidedAt = decidedAt ?? Decided;

        return campaign;
    }

    public static CampaignItem Item(TargetType targetType, BenefitType benefitType, decimal value, int? voucherLimit = null) => new()
    {
        Id = NextId(),
        TargetType = targetType,
        CategoryId = targetType == TargetType.Category ? PapelariaId : null,
        ProductId = targetType == TargetType.Product ? MochilaId : null,
        BenefitType = benefitType,
        BenefitValue = value,
        VoucherLimit = voucherLimit
    };

    public static CampaignCondition Condition(ConditionType type, decimal? minAmount = null, int? minQuantity = null, Guid? categoryId = null, Guid? productId = null) => new()
    {
        Id = NextId(),
        Type = type,
        MinAmount = minAmount,
        MinQuantity = minQuantity,
        CategoryId = categoryId,
        ProductId = productId
    };

    public static CampaignScope Scope(ChannelType channel, Guid? regionId = null, Guid? establishmentId = null) => new()
    {
        Id = NextId(),
        RegionId = regionId,
        EstablishmentId = establishmentId,
        ChannelType = channel
    };

    /// <summary>Compra de R$ 200 com uma mochila (Papelaria) de R$ 120 e duas canetas (sem categoria conhecida) de R$ 40.</summary>
    public static Purchase Purchase(decimal totalAmount = 200m, int? customerPurchaseCount = null, params PurchaseItem[] items) =>
        new(totalAmount, items.Length == 0 ? DefaultItems : items, customerPurchaseCount);

    public static readonly PurchaseItem[] DefaultItems =
    [
        new("MOCH-G", 1, 120m, MochilaId, PapelariaId),
        new("CAN-001", 2, 40m, CanetaId, null)
    ];

    /// <summary>Substitui as condições numa campanha ativa (a configuração só muda em rascunho).</summary>
    public static Campaign WithConditions(this Campaign campaign, params CampaignCondition[] conditions)
    {
        var status = campaign.Status;
        campaign.Status = CampaignStatus.Draft;
        campaign.ReplaceConditions(conditions, campaign.AllowUseInSamePurchase, Created);
        campaign.Status = status;

        return campaign;
    }

    /// <summary>Ids v7 crescentes: a ordem de criação é a ordem de configuração.</summary>
    public static Guid NextId() => Guid.CreateVersion7(new DateTimeOffset(Created).AddMilliseconds(Interlocked.Increment(ref _sequence)));

    public static DateTime Utc(int year, int month, int day, int hour, int minute, int second = 0) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);
}
