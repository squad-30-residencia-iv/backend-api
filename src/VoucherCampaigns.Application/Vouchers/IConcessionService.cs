namespace VoucherCampaigns.Application.Vouchers;

/// <summary>Motor de concessão (EP-04) exposto às aplicações satélites (US-07.2, US-07.4).</summary>
public interface IConcessionService
{
    /// <summary>Avalia a compra e concede, no máximo, um voucher provisório (fluxo 1.2 da spec GH-045). Idempotente pela transação.</summary>
    Task<ConcessionResponse> GrantAsync(ConcessionRequest request, CancellationToken cancellationToken);

    /// <summary>Confirma a venda: o voucher vira disponível e o saldo é consumido (US-04.9, cenário 1).</summary>
    Task<ConcessionResponse> ConfirmAsync(Guid concessionId, CancellationToken cancellationToken);

    /// <summary>Desfaz a venda: o voucher é descartado e o saldo devolvido (US-04.9, cenário 2).</summary>
    Task<ConcessionResponse> CancelAsync(Guid concessionId, CancellationToken cancellationToken);

    Task<ConcessionResponse> GetAsync(Guid concessionId, CancellationToken cancellationToken);

    /// <summary>Vouchers do cliente na rede da aplicação, com o status efetivo (US-04.8, US-07.4).</summary>
    Task<IReadOnlyList<VoucherResponse>> GetCustomerVouchersAsync(string document, string? status, CancellationToken cancellationToken);
}
