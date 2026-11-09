using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.Authentication;

// Atributos no parâmetro: o MVC valida records posicionais pelo construtor.
public record SatelliteTokenRequest(
    [Required(ErrorMessage = "Informe o clientId da aplicação.")] string ClientId,
    [Required(ErrorMessage = "Informe o clientSecret da aplicação.")] string ClientSecret);

/// <param name="TokenType">Sempre <c>Bearer</c>.</param>
/// <param name="ExpiresIn">Validade do token, em segundos.</param>
public record SatelliteTokenResponse(string AccessToken, string TokenType, int ExpiresIn, IReadOnlyList<string> Scopes);

public record SatelliteAppResponse(Guid ApplicationId, string ClientId, Guid NetworkId, IReadOnlyList<string> Scopes);
