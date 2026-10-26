using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.DTOs.Organization;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Application.Campaigns;

public class CampaignService(
    INetworkRepository networks,
    ICampaignRepository campaigns,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ICampaignService
{
    public async Task<CampaignResponse> CreateAsync(CreateCampaignRequest request, CancellationToken cancellationToken)
    {
        var networkId = InputValues.RequiredId(request.NetworkId, nameof(request.NetworkId), CampaignContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, networkId);

        var network = await networks.FindByIdAsync(networkId, cancellationToken);

        if (network is null || !network.IsActive)
        {
            throw new BusinessRuleException(CampaignErrors.NetworkUnavailableCode, CampaignErrors.NetworkUnavailableMessage);
        }

        // RN02: as datas digitadas valem no fuso da rede (US-02.6).
        var timeZone = NetworkTime.Resolve(network.TimeZone);

        var campaign = Campaign.CreateDraft(
            network.Id,
            request.Name,
            request.Description,
            NetworkTime.ToUtc(request.StartsAt!.Value, timeZone, nameof(request.StartsAt)),
            NetworkTime.ToUtc(request.EndsAt!.Value, timeZone, nameof(request.EndsAt)),
            timeProvider.GetUtcNow().UtcDateTime);

        await campaigns.AddAsync(campaign, cancellationToken);

        return Map(campaign, network);
    }

    public async Task<IReadOnlyList<CampaignResponse>> GetAllAsync(Guid? networkId, string? status, CancellationToken cancellationToken)
    {
        var scopedNetworkId = InputValues.RequiredId(networkId, "NetworkId", CampaignContract.NetworkRequired);
        NetworkScope.Ensure(currentUser, scopedNetworkId);

        var statusFilter = ParseStatus(status);

        // Rede inativa continua consultável: é só leitura.
        var network = await networks.FindByIdAsync(scopedNetworkId, cancellationToken)
            ?? throw new ResourceNotFoundException(OrganizationContract.NetworkNotFound);

        var items = await campaigns.GetByNetworkAsync(scopedNetworkId, statusFilter, cancellationToken);
        return items.Select(campaign => Map(campaign, network)).ToList();
    }

    public async Task<CampaignResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await campaigns.FindByIdAsync(id, cancellationToken)
            ?? throw new ResourceNotFoundException(CampaignContract.CampaignNotFound);

        // A rede só é conhecida depois de carregar a campanha (US-02.5, cenário 3).
        NetworkScope.Ensure(currentUser, campaign.NetworkId);

        return Map(campaign, campaign.Network!);
    }

    /// <summary>Aceita só os nomes do enum, sem diferenciar maiúsculas; números são recusados.</summary>
    private static CampaignStatus? ParseStatus(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();

        if (!Enum.GetNames<CampaignStatus>().Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            throw new RequestValidationException("Status", CampaignContract.StatusInvalid);
        }

        return Enum.Parse<CampaignStatus>(trimmed, ignoreCase: true);
    }

    // Status gravado e IssuedVouchers = 0 até existirem o status efetivo e a emissão (spec US-01.9, PA-2 a PA-4).
    private static CampaignResponse Map(Campaign campaign, Network network)
    {
        var timeZone = NetworkTime.Resolve(network.TimeZone);

        return new CampaignResponse(
            campaign.Id,
            campaign.NetworkId,
            campaign.Name,
            campaign.Description,
            NetworkTime.ToLocal(campaign.StartsAt, timeZone),
            NetworkTime.ToLocal(campaign.EndsAt, timeZone),
            network.TimeZone,
            campaign.Status.ToString(),
            campaign.TotalVoucherLimit,
            IssuedVouchers: 0,
            // O banco devolve o DateTime sem Kind; sem isso o GET sairia sem "Z", diferente do POST.
            DateTime.SpecifyKind(campaign.CreatedAt, DateTimeKind.Utc));
    }
}
