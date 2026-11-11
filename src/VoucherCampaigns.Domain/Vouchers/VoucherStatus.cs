namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Status do voucher (RF14c, RF21). Gravado como texto; a leitura usa o efetivo (<see cref="Voucher.EffectiveStatus"/>).</summary>
public enum VoucherStatus
{
    /// <summary>Emitido com a venda ainda não confirmada; o saldo está reservado.</summary>
    Provisional,

    /// <summary>Venda confirmada; pode ser resgatado até a validade.</summary>
    Available,

    /// <summary>Todas as utilizações consumidas (EP-05).</summary>
    Redeemed,

    /// <summary>Validade própria vencida (US-05.3).</summary>
    Expired,

    /// <summary>Invalidado pelo cancelamento da campanha (RN10, EP-05).</summary>
    Cancelled,

    /// <summary>Provisório descartado: venda desfeita ou reserva vencida. Nunca valeu.</summary>
    Discarded
}
