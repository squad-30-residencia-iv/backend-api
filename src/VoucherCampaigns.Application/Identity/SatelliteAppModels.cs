using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.Identity;

/// <summary>Criação de aplicação satélite pelo administrador.</summary>
public record CreateSatelliteAppRequest(
    [Required(ErrorMessage = "Nome é obrigatório.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Nome deve ter entre 1 e 150 caracteres.")]
    string Name,

    [Required(ErrorMessage = "ClientId é obrigatório.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "ClientId deve ter entre 1 e 100 caracteres.")]
    string ClientId,

    [Required(ErrorMessage = "Rede é obrigatória.")]
    Guid? NetworkId,

    [Required(ErrorMessage = "Escopos são obrigatórios.")]
    IReadOnlyList<string> Scopes);

/// <summary>Atualização dos dados da aplicação satélite.</summary>
public record UpdateSatelliteAppRequest(
    [Required(ErrorMessage = "Nome é obrigatório.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "Nome deve ter entre 1 e 150 caracteres.")]
    string Name,

    [Required(ErrorMessage = "Escopos são obrigatórios.")]
    IReadOnlyList<string> Scopes);

/// <summary>Dados cadastrais da aplicação satélite (sem segredo).</summary>
public record SatelliteAppItemResponse(
    Guid Id,
    string Name,
    string ClientId,
    Guid NetworkId,
    IReadOnlyList<string> Scopes,
    bool IsActive,
    DateTime CreatedAt)
{
    public bool Active => IsActive;
}

/// <summary>Resposta da criação com o segredo retornado uma única vez.</summary>
public record CreateSatelliteAppResponse(
    SatelliteAppItemResponse App,
    string ClientSecret);

/// <summary>Resposta da rotação de segredo com o novo segredo retornado uma única vez.</summary>
public record RotateSecretResponse(
    string ClientSecret)
{
    public string NewSecret => ClientSecret;
}
