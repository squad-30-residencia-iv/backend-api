using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Application.Vouchers;

/// <summary>Filtros da listagem do portal (GH-050), já resolvidos para UTC e para os dígitos do CPF.</summary>
/// <param name="SearchDigits">CPF inteiro (11 dígitos) para a busca exata; outro tamanho não busca por CPF.</param>
public sealed record PortalVoucherFilter(
    Guid NetworkId,
    Guid? CampaignId,
    VoucherStatus? Status,
    DateTime? IssuedFromUtc,
    DateTime? IssuedToUtcExclusive,
    string? SearchText,
    string? SearchDigits,
    DateTime UtcNow);

public interface IPortalVoucherReadRepository
{
    /// <summary>
    /// Vouchers da rede com os filtros do portal no banco (status efetivo, período de emissão e busca) e a página
    /// opcional (spec GH-071, D-5), dos mais novos para os mais antigos. O total é o da consulta filtrada inteira.
    /// </summary>
    Task<(int Total, IReadOnlyList<Voucher> Items)> ListPageAsync(PortalVoucherFilter filter, int? skip, int? take, CancellationToken cancellationToken);

    Task<Voucher?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Establishment?> FindEstablishmentAsync(Guid establishmentId, CancellationToken cancellationToken);
}
