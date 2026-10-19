namespace VoucherCampaigns.Application.Authentication;

/// <summary>Implementado pelos requests cuja operação pertence a uma rede (ex.: cadastro de campanha).</summary>
public interface INetworkScopedRequest
{
    Guid NetworkId { get; }
}
