using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.Identity;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Identity;
using VoucherCampaigns.Infrastructure.Authentication;
using VoucherCampaigns.Infrastructure.Persistence;

namespace VoucherCampaigns.Infrastructure.Identity;

public class SatelliteAppManagementService(
    AppDbContext db,
    TimeProvider timeProvider) : ISatelliteAppManagementService
{
    public const string ClientIdTakenCode = "SATELLITE_CLIENT_ID_TAKEN";
    public const string ClientIdTakenMessage = "Já existe uma aplicação cadastrada com este identificador.";
    public const string AppNotFoundMessage = "Aplicação satélite não encontrada.";

    public async Task<CreateSatelliteAppResponse> CreateAsync(CreateSatelliteAppRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new RequestValidationException("name", "Nome é obrigatório.");
        }

        if (string.IsNullOrWhiteSpace(request.ClientId))
        {
            throw new RequestValidationException("clientId", "ClientId é obrigatório.");
        }

        if (!request.NetworkId.HasValue || request.NetworkId.Value == Guid.Empty)
        {
            throw new RequestValidationException("networkId", "A rede é obrigatória.");
        }

        if (request.Scopes is null || !SatelliteScopes.TryNormalizeAll(request.Scopes, out var normalizedScopes))
        {
            throw new RequestValidationException("scopes", "Escopo informado inválido.");
        }

        var normalizedClientId = request.ClientId.Trim().ToLowerInvariant();

        var exists = await db.SatelliteApps.AnyAsync(a => a.ClientId == normalizedClientId, cancellationToken);
        if (exists)
        {
            throw new BusinessRuleException(ClientIdTakenCode, ClientIdTakenMessage);
        }

        var secret = GenerateSecret();
        var secretHash = PasswordHasher.Hash(secret);

        var app = new SatelliteApp
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name.Trim(),
            ClientId = normalizedClientId,
            NetworkId = request.NetworkId.Value,
            Scopes = string.Join(' ', normalizedScopes),
            SecretHash = secretHash,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        db.SatelliteApps.Add(app);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateSatelliteAppResponse(ToResponse(app), secret);
    }

    public async Task<IReadOnlyList<SatelliteAppItemResponse>> GetByNetworkAsync(Guid networkId, CancellationToken cancellationToken)
    {
        if (networkId == Guid.Empty)
        {
            throw new RequestValidationException("networkId", "A rede é obrigatória.");
        }

        var apps = await db.SatelliteApps.AsNoTracking()
            .Where(a => a.NetworkId == networkId)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);

        return apps.Select(ToResponse).ToList();
    }

    public async Task<SatelliteAppItemResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var app = await db.SatelliteApps.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (app is null)
        {
            throw new ResourceNotFoundException(AppNotFoundMessage);
        }

        return ToResponse(app);
    }

    public async Task<SatelliteAppItemResponse> UpdateAsync(Guid id, UpdateSatelliteAppRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new RequestValidationException("name", "Nome é obrigatório.");
        }

        if (request.Scopes is null || !SatelliteScopes.TryNormalizeAll(request.Scopes, out var normalizedScopes))
        {
            throw new RequestValidationException("scopes", "Escopo informado inválido.");
        }

        var app = await db.SatelliteApps.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (app is null)
        {
            throw new ResourceNotFoundException(AppNotFoundMessage);
        }

        app.Name = request.Name.Trim();
        app.Scopes = string.Join(' ', normalizedScopes);

        await db.SaveChangesAsync(cancellationToken);

        return ToResponse(app);
    }

    public async Task<SatelliteAppItemResponse> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var app = await db.SatelliteApps.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (app is null)
        {
            throw new ResourceNotFoundException(AppNotFoundMessage);
        }

        app.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);

        return ToResponse(app);
    }

    public async Task<RotateSecretResponse> RotateSecretAsync(Guid id, CancellationToken cancellationToken)
    {
        var app = await db.SatelliteApps.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (app is null)
        {
            throw new ResourceNotFoundException(AppNotFoundMessage);
        }

        var newSecret = GenerateSecret();
        app.SecretHash = PasswordHasher.Hash(newSecret);

        await db.SaveChangesAsync(cancellationToken);

        return new RotateSecretResponse(newSecret);
    }

    private static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return "sec_" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static SatelliteAppItemResponse ToResponse(SatelliteApp app)
    {
        var scopes = app.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new SatelliteAppItemResponse(
            app.Id,
            app.Name,
            app.ClientId,
            app.NetworkId,
            scopes,
            app.IsActive,
            app.CreatedAt);
    }
}
