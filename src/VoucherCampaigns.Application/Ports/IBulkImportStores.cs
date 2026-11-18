using VoucherCampaigns.Domain.Catalog;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Ports;

/// <summary>Importação de lojas em lote (spec GH-066, D-2): tudo ou nada, numa gravação só.</summary>
public interface IEstablishmentImportStore
{
    /// <summary>Dos códigos informados, os que já existem na região (mesma comparação do cadastro um a um).</summary>
    Task<IReadOnlyList<string>> GetExistingCodesAsync(Guid regionId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken);

    Task AddAllAsync(IReadOnlyList<Establishment> establishments, CancellationToken cancellationToken);
}

/// <summary>Importação de produtos em lote (spec GH-066, D-3): tudo ou nada, numa gravação só.</summary>
public interface IProductImportStore
{
    /// <summary>Dos códigos informados, os que já existem no catálogo (mesma comparação do cadastro um a um).</summary>
    Task<IReadOnlyList<string>> GetExistingCodesAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken);

    Task AddAllAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken);
}
