using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>
/// Pedido de concessão feito pela aplicação satélite e a decisão tomada (US-04.1, US-04.6, US-07.2).
/// A rede e o identificador da transação formam a chave de idempotência (PR-07).
/// </summary>
public class Concession
{
    public Guid Id { get; set; }

    public Guid NetworkId { get; set; }

    public Guid SatelliteAppId { get; set; }

    /// <summary>Identificador único da transação, informado pelo satélite (PR-07).</summary>
    public string TransactionId { get; set; } = string.Empty;

    /// <summary>CPF do cliente, só os 11 dígitos (decisão D4).</summary>
    public string CustomerDocument { get; set; } = string.Empty;

    public Guid EstablishmentId { get; set; }

    public ChannelType Channel { get; set; }

    /// <summary>Valor bruto da compra (PR-16).</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Volume de itens da compra.</summary>
    public int ItemCount { get; set; }

    /// <summary>Quantidade de compras do cliente, enviada pelo satélite (decisão D2); null quando não informada.</summary>
    public int? CustomerPurchaseCount { get; set; }

    /// <summary>Instante UTC do pedido.</summary>
    public DateTime RequestedAt { get; set; }

    public ConcessionStatus Status { get; set; }

    /// <summary>Motivo da não concessão; só quando <see cref="Status"/> é <see cref="ConcessionStatus.NotGranted"/>.</summary>
    public ConcessionRefusal? Refusal { get; set; }

    /// <summary>Campanha satisfeita que foi bloqueada (status, saldo ou limite); null sem campanha satisfeita.</summary>
    public Guid? RefusedCampaignId { get; set; }

    /// <summary>Fim do prazo de reserva (PR-17); só enquanto provisória.</summary>
    public DateTime? ReservedUntil { get; set; }

    /// <summary>Instante UTC da confirmação, do desfazimento ou da expiração.</summary>
    public DateTime? DecidedAt { get; set; }

    /// <summary>Voucher emitido por esta concessão; null quando não houve concessão.</summary>
    public Voucher? Voucher { get; set; }

    /// <summary>Estado na leitura: a provisória com reserva vencida já conta como expirada, mesmo antes da liberação.</summary>
    public ConcessionStatus EffectiveStatus(DateTime utcNow) =>
        Status == ConcessionStatus.Provisional && ReservedUntil is { } reservedUntil && utcNow >= reservedUntil
            ? ConcessionStatus.Expired
            : Status;
}
