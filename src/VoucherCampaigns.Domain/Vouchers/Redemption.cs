using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Status do resgate (spec GH-049, D-2): efetivo ou estornado (D-19).</summary>
public enum RedemptionStatus
{
    Effective,
    Reversed
}

/// <summary>
/// Resgate de um voucher numa compra (US-05.4, US-07.3). Um por transação em cada rede; o mesmo voucher
/// pode ter vários, um por utilização. O estorno devolve a utilização e marca <see cref="Status"/>.
/// </summary>
public class Redemption
{
    public Guid Id { get; set; }

    public Guid NetworkId { get; set; }

    /// <summary>Aplicação satélite que resgatou (sem FK, como em <see cref="Concession"/>).</summary>
    public Guid SatelliteAppId { get; set; }

    public Guid VoucherId { get; set; }

    public Voucher? Voucher { get; set; }

    public Guid CampaignId { get; set; }

    /// <summary>Identificador da compra no satélite: um voucher por compra (US-07.3, cenário 4) e idempotência (cenário 5).</summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>CPF, só dígitos.</summary>
    public string CustomerDocument { get; set; } = string.Empty;

    public Guid EstablishmentId { get; set; }

    public ChannelType Channel { get; set; }

    /// <summary>Valor bruto da compra (PR-16).</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Desconto aplicado, calculado pelo alvo do voucher nesta compra (D-7).</summary>
    public decimal DiscountAmount { get; set; }

    public int ItemCount { get; set; }

    public DateTime RedeemedAt { get; set; }

    public RedemptionStatus Status { get; set; }

    public DateTime? ReversedAt { get; set; }
}
