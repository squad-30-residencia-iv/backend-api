using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoucherCampaigns.Api.Authentication;
using VoucherCampaigns.Application.Identity;

namespace VoucherCampaigns.Api.Controllers;

/// <summary>
/// Gerenciamento de aplicações satélite pelo administrador da plataforma (GH-028).
/// </summary>
[ApiController]
[Authorize(Policy = AuthorizationPolicies.ManageSatelliteApps)]
[Route("api/v1/satellite/apps")]
public class SatelliteAppsController(ISatelliteAppManagementService service) : ControllerBase
{
    /// <summary>Cadastra uma nova aplicação satélite e devolve o segredo gerado uma única vez.</summary>
    [HttpPost]
    [ProducesResponseType<CreateSatelliteAppResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreateSatelliteAppResponse>> Create(
        [FromBody] CreateSatelliteAppRequest request,
        CancellationToken cancellationToken)
    {
        var response = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.App.Id }, response);
    }

    /// <summary>Lista aplicações satélite filtradas pela rede obrigatória.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SatelliteAppItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<SatelliteAppItemResponse>>> GetByNetwork(
        [FromQuery] Guid networkId,
        CancellationToken cancellationToken)
    {
        var apps = await service.GetByNetworkAsync(networkId, cancellationToken);
        return Ok(apps);
    }

    /// <summary>Consulta dados cadastrais de uma aplicação satélite.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<SatelliteAppItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SatelliteAppItemResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var app = await service.GetByIdAsync(id, cancellationToken);
        return Ok(app);
    }

    /// <summary>Atualiza nome e escopos da aplicação satélite.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<SatelliteAppItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SatelliteAppItemResponse>> Update(
        Guid id,
        [FromBody] UpdateSatelliteAppRequest request,
        CancellationToken cancellationToken)
    {
        var app = await service.UpdateAsync(id, request, cancellationToken);
        return Ok(app);
    }

    /// <summary>Inativa a aplicação satélite.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType<SatelliteAppItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SatelliteAppItemResponse>> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var app = await service.DeactivateAsync(id, cancellationToken);
        return Ok(app);
    }

    /// <summary>Inativa a aplicação satélite (rota alternativa PATCH de paridade com o portal).</summary>
    [HttpPatch("{id:guid}/deactivate")]
    [ProducesResponseType<SatelliteAppItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SatelliteAppItemResponse>> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var app = await service.DeactivateAsync(id, cancellationToken);
        return Ok(app);
    }

    /// <summary>Rotaciona o segredo da aplicação e devolve o novo segredo gerado uma única vez.</summary>
    [HttpPost("{id:guid}/rotate-secret")]
    [ProducesResponseType<RotateSecretResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RotateSecretResponse>> RotateSecret(
        Guid id,
        CancellationToken cancellationToken)
    {
        var response = await service.RotateSecretAsync(id, cancellationToken);
        return Ok(response);
    }
}
