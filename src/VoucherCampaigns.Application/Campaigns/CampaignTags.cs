using System.ComponentModel.DataAnnotations;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Mensagens das tags de campanha (spec GH-077).</summary>
public static class CampaignTagContract
{
    public const string NameRequired = "Informe o nome da tag.";
    public const string NameLength = "O nome da tag deve ter no máximo 40 caracteres.";
    public const string NetworkRequired = "Informe a rede da tag.";
    public const string ActiveRequired = "Informe se a tag está ativa.";
    public const string NotFound = "Tag não encontrada.";
    public const string TagIdsRequired = "Informe a lista de tags (vazia para tirar todas).";
    public const string TagIdsTooMany = "A campanha pode ter no máximo 5 tags.";
    public const string TagIdInvalid = "Informe o id de cada tag.";
}

public record CreateCampaignTagRequest(
    [Required(ErrorMessage = CampaignTagContract.NetworkRequired)] Guid? NetworkId,
    [Required(ErrorMessage = CampaignTagContract.NameRequired)]
    [StringLength(CampaignTag.NameMaxLength, MinimumLength = 1, ErrorMessage = CampaignTagContract.NameLength)]
    string? Name) : INetworkScopedRequest
{
    Guid INetworkScopedRequest.NetworkId => NetworkId ?? Guid.Empty;
}

public record UpdateCampaignTagRequest(
    [Required(ErrorMessage = CampaignTagContract.NameRequired)]
    [StringLength(CampaignTag.NameMaxLength, MinimumLength = 1, ErrorMessage = CampaignTagContract.NameLength)]
    string? Name,
    [Required(ErrorMessage = CampaignTagContract.ActiveRequired)] bool? IsActive);

/// <param name="CampaignCount">Quantas campanhas da rede usam a tag.</param>
public record CampaignTagResponse(Guid Id, Guid NetworkId, string Name, bool IsActive, int CampaignCount, DateTime CreatedAt);

/// <summary>Lista completa das tags da campanha; vazia tira todas (spec GH-077, D-2).</summary>
public record ReplaceCampaignTagsRequest(
    [Required(ErrorMessage = CampaignTagContract.TagIdsRequired)]
    IReadOnlyList<Guid?>? TagIds);

/// <summary>Tag na resposta da campanha; a inativada continua aparecendo, com <c>IsActive = false</c>.</summary>
public record CampaignTagRefResponse(Guid Id, string Name, bool IsActive);

public interface ICampaignTagRepository
{
    Task<IReadOnlyList<CampaignTag>> ListAsync(Guid networkId, bool? activeOnly, CancellationToken cancellationToken);

    /// <summary>Rastreada, para alteração.</summary>
    Task<CampaignTag?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CampaignTag>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<bool> NameExistsAsync(Guid networkId, string normalizedName, Guid? exceptId, CancellationToken cancellationToken);

    Task AddAsync(CampaignTag tag, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> CountCampaignsAsync(IReadOnlyCollection<Guid> tagIds, CancellationToken cancellationToken);
}

public interface ICampaignTagService
{
    Task<IReadOnlyList<CampaignTagResponse>> ListAsync(Guid? networkId, bool? activeOnly, CancellationToken cancellationToken);

    Task<CampaignTagResponse> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<CampaignTagResponse> CreateAsync(CreateCampaignTagRequest request, CancellationToken cancellationToken);

    Task<CampaignTagResponse> UpdateAsync(Guid id, UpdateCampaignTagRequest request, CancellationToken cancellationToken);

    Task InactivateAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>Cadastro das tags da rede pelo administrador (spec GH-077, D-1); leitura para analista e gestor.</summary>
public class CampaignTagService(
    ICampaignTagRepository tags,
    INetworkRepository networks,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICampaignTagService
{
    public async Task<IReadOnlyList<CampaignTagResponse>> ListAsync(Guid? networkId, bool? activeOnly, CancellationToken cancellationToken)
    {
        var scoped = InputValues.RequiredId(networkId, nameof(networkId), CampaignTagContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, scoped);
        _ = await networks.FindByIdAsync(scoped, cancellationToken) ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);

        var items = await tags.ListAsync(scoped, activeOnly, cancellationToken);
        var counts = await tags.CountCampaignsAsync(items.Select(item => item.Id).ToList(), cancellationToken);

        return items.Select(item => Map(item, counts.GetValueOrDefault(item.Id))).ToList();
    }

    public async Task<CampaignTagResponse> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await MapAsync(await RequireAsync(id, cancellationToken), cancellationToken);

    public async Task<CampaignTagResponse> CreateAsync(CreateCampaignTagRequest request, CancellationToken cancellationToken)
    {
        var networkId = InputValues.RequiredId(request.NetworkId, nameof(request.NetworkId), CampaignTagContract.NetworkRequired);
        var name = Name(request.Name);
        NetworkScope.Ensure(currentUser, networkId);
        await RequireActiveNetworkAsync(networkId, cancellationToken);
        await EnsureNameFreeAsync(networkId, name, null, cancellationToken);

        var tag = new CampaignTag
        {
            Id = Guid.CreateVersion7(),
            NetworkId = networkId,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };
        tag.Rename(name);
        await tags.AddAsync(tag, cancellationToken);

        return Map(tag, 0);
    }

    public async Task<CampaignTagResponse> UpdateAsync(Guid id, UpdateCampaignTagRequest request, CancellationToken cancellationToken)
    {
        var tag = await RequireAsync(id, cancellationToken);
        var name = Name(request.Name);
        var active = InputValues.RequiredFlag(request.IsActive, nameof(request.IsActive), CampaignTagContract.ActiveRequired);

        if (active)
        {
            await RequireActiveNetworkAsync(tag.NetworkId, cancellationToken);
        }

        await EnsureNameFreeAsync(tag.NetworkId, name, tag.Id, cancellationToken);
        tag.Rename(name);
        tag.IsActive = active;
        await tags.SaveChangesAsync(cancellationToken);

        return await MapAsync(tag, cancellationToken);
    }

    public async Task InactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var tag = await RequireAsync(id, cancellationToken);
        tag.IsActive = false;
        await tags.SaveChangesAsync(cancellationToken);
    }

    private async Task<CampaignTag> RequireAsync(Guid id, CancellationToken cancellationToken)
    {
        var tag = await tags.FindAsync(id, cancellationToken) ?? throw new ResourceNotFoundException(CampaignTagContract.NotFound);
        NetworkScope.Ensure(currentUser, tag.NetworkId);

        return tag;
    }

    private async Task RequireActiveNetworkAsync(Guid networkId, CancellationToken cancellationToken)
    {
        var network = await networks.FindByIdAsync(networkId, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);

        if (!network.IsActive)
        {
            throw new BusinessRuleException(CampaignErrors.NetworkUnavailableCode, CampaignErrors.NetworkUnavailableMessage);
        }
    }

    private async Task EnsureNameFreeAsync(Guid networkId, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (await tags.NameExistsAsync(networkId, CampaignTag.Normalize(name), exceptId, cancellationToken))
        {
            throw new BusinessRuleException(CampaignErrors.TagNameTakenCode, CampaignErrors.TagNameTakenMessage);
        }
    }

    private static string Name(string? value)
    {
        var name = value?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            throw new RequestValidationException("Name", CampaignTagContract.NameRequired);
        }

        return name.Length <= CampaignTag.NameMaxLength ? name : throw new RequestValidationException("Name", CampaignTagContract.NameLength);
    }

    private async Task<CampaignTagResponse> MapAsync(CampaignTag tag, CancellationToken cancellationToken) =>
        Map(tag, (await tags.CountCampaignsAsync([tag.Id], cancellationToken)).GetValueOrDefault(tag.Id));

    private static CampaignTagResponse Map(CampaignTag tag, int campaignCount) =>
        new(tag.Id, tag.NetworkId, tag.Name, tag.IsActive, campaignCount, DateTime.SpecifyKind(tag.CreatedAt, DateTimeKind.Utc));
}
