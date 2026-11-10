namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Estado do pedido de concessão (US-04.9, US-07.2). Gravado como texto.</summary>
public enum ConcessionStatus
{
    /// <summary>Nenhuma campanha concedeu; o motivo fica em <see cref="ConcessionRefusal"/>.</summary>
    NotGranted,

    /// <summary>Voucher provisório emitido e saldo reservado até a confirmação da venda.</summary>
    Provisional,

    /// <summary>Venda confirmada: o voucher está disponível e o saldo foi consumido.</summary>
    Confirmed,

    /// <summary>Venda desfeita pela aplicação satélite: voucher descartado e saldo devolvido.</summary>
    Cancelled,

    /// <summary>Prazo de reserva vencido sem confirmação (PR-17): voucher descartado e saldo devolvido.</summary>
    Expired
}
