using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Lista completa de locais onde a campanha vale (US-02.3); substitui a atual.</summary>
public record ReplaceCampaignScopesRequest(
    [Required(ErrorMessage = CampaignContract.ScopesRequired)]
    [MinLength(1, ErrorMessage = CampaignContract.ScopesRequired)]
    [MaxLength(CampaignContract.MaxScopes, ErrorMessage = CampaignContract.ScopesTooMany)]
    IReadOnlyList<CampaignScopeRequest?>? Scopes);

/// <summary>Região ou loja, com os canais; sem nenhuma das duas vale a rede inteira.</summary>
public record CampaignScopeRequest(
    Guid? RegionId,
    Guid? EstablishmentId,
    [Required(ErrorMessage = CampaignContract.ScopeChannelsRequired)]
    IReadOnlyList<string>? Channels);
