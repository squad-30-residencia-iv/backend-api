using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

public class OrganizationRepository(AppDbContext context) : IOrganizationRepository
{
    public async Task<IReadOnlyList<Network>> GetNetworksAsync(bool? activeOnly, CancellationToken cancellationToken)
    {
        var query = Active(context.Networks.AsNoTracking(), activeOnly);
        return await query.OrderBy(network => network.Name).ToListAsync(cancellationToken);
    }

    public Task<Network?> GetNetworkByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Networks.AsNoTracking().FirstOrDefaultAsync(network => network.Id == id, cancellationToken);

    public async Task<Network> CreateNetworkAsync(Network network, CancellationToken cancellationToken)
    {
        context.Networks.Add(network);
        await ConstraintSave.SaveAsync(context, OrganizationErrors.NetworkNameTakenCode, OrganizationErrors.NetworkNameTakenMessage, cancellationToken);
        return network;
    }

    public async Task<Network> UpdateNetworkAsync(Network network, CancellationToken cancellationToken)
    {
        IQueryable<Network> query = context.Networks;

        if (!network.IsActive)
        {
            query = query
                .Include(stored => stored.Regions)
                .ThenInclude(region => region.Establishments);
        }

        var stored = await query.FirstAsync(stored => stored.Id == network.Id, cancellationToken);
        stored.Name = network.Name;
        stored.TimeZone = network.TimeZone;
        stored.IsActive = network.IsActive;

        // Rede, regiões e lojas na mesma gravação (US-02.2). Os canais são atributo da loja e não mudam.
        if (!network.IsActive)
        {
            foreach (var region in stored.Regions)
            {
                region.IsActive = false;

                foreach (var establishment in region.Establishments)
                {
                    establishment.IsActive = false;
                }
            }
        }

        await ConstraintSave.SaveAsync(context, OrganizationErrors.NetworkNameTakenCode, OrganizationErrors.NetworkNameTakenMessage, cancellationToken);
        return stored;
    }

    public Task<bool> ExistsNetworkByNameAsync(string name, Guid? excludeId, CancellationToken cancellationToken) =>
        context.Networks.AnyAsync(network => network.Name == name && (excludeId == null || network.Id != excludeId), cancellationToken);

    public async Task<IReadOnlyList<Region>> GetRegionsAsync(Guid networkId, bool? activeOnly, CancellationToken cancellationToken)
    {
        var query = Active(context.Regions.AsNoTracking().Where(region => region.NetworkId == networkId), activeOnly);
        return await query.OrderBy(region => region.Name).ToListAsync(cancellationToken);
    }

    public Task<Region?> GetRegionByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Regions.AsNoTracking().FirstOrDefaultAsync(region => region.Id == id, cancellationToken);

    public async Task<Region> CreateRegionAsync(Region region, CancellationToken cancellationToken)
    {
        context.Regions.Add(region);
        await ConstraintSave.SaveAsync(context, OrganizationErrors.RegionNameTakenCode, OrganizationErrors.RegionNameTakenMessage, cancellationToken);
        return region;
    }

    public async Task<Region> UpdateRegionAsync(Region region, CancellationToken cancellationToken)
    {
        IQueryable<Region> query = context.Regions;

        if (!region.IsActive)
        {
            query = query.Include(stored => stored.Establishments);
        }

        var stored = await query.FirstAsync(stored => stored.Id == region.Id, cancellationToken);
        stored.Name = region.Name;
        stored.IsActive = region.IsActive;

        if (!region.IsActive)
        {
            foreach (var establishment in stored.Establishments)
            {
                establishment.IsActive = false;
            }
        }

        await ConstraintSave.SaveAsync(context, OrganizationErrors.RegionNameTakenCode, OrganizationErrors.RegionNameTakenMessage, cancellationToken);
        return stored;
    }

    public Task<bool> ExistsRegionByNameInNetworkAsync(Guid networkId, string name, Guid? excludeId, CancellationToken cancellationToken) =>
        context.Regions.AnyAsync(
            region => region.NetworkId == networkId && region.Name == name && (excludeId == null || region.Id != excludeId),
            cancellationToken);

    public async Task<IReadOnlyList<Establishment>> GetEstablishmentsAsync(Guid regionId, bool? activeOnly, CancellationToken cancellationToken)
    {
        var query = Active(
            context.Establishments.AsNoTracking()
                .Include(establishment => establishment.Channels)
                .Where(establishment => establishment.RegionId == regionId),
            activeOnly);
        return await query.OrderBy(establishment => establishment.Name).ToListAsync(cancellationToken);
    }

    public Task<Establishment?> GetEstablishmentByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Establishments.AsNoTracking()
            .Include(establishment => establishment.Channels)
            .FirstOrDefaultAsync(establishment => establishment.Id == id, cancellationToken);

    public async Task<Establishment> CreateEstablishmentAsync(Establishment establishment, CancellationToken cancellationToken)
    {
        context.Establishments.Add(establishment);
        await ConstraintSave.SaveAsync(
            context, OrganizationErrors.EstablishmentCodeTakenCode, OrganizationErrors.EstablishmentCodeTakenMessage, cancellationToken);
        return establishment;
    }

    public async Task<Establishment> UpdateEstablishmentAsync(Establishment establishment, CancellationToken cancellationToken)
    {
        var stored = await context.Establishments
            .Include(stored => stored.Channels)
            .FirstAsync(stored => stored.Id == establishment.Id, cancellationToken);
        stored.Code = establishment.Code;
        stored.Name = establishment.Name;
        stored.IsActive = establishment.IsActive;

        // Loja e canais na mesma gravação: o canal que já existe é atualizado, o novo é inserido.
        foreach (var channel in establishment.Channels)
        {
            var storedChannel = stored.Channels.FirstOrDefault(candidate => candidate.Id == channel.Id);

            if (storedChannel is null)
            {
                context.Channels.Add(channel);
            }
            else
            {
                storedChannel.IsActive = channel.IsActive;
            }
        }

        await ConstraintSave.SaveAsync(
            context, OrganizationErrors.EstablishmentCodeTakenCode, OrganizationErrors.EstablishmentCodeTakenMessage, cancellationToken);
        return stored;
    }

    public Task<bool> ExistsEstablishmentByCodeInRegionAsync(Guid regionId, string code, Guid? excludeId, CancellationToken cancellationToken) =>
        context.Establishments.AnyAsync(
            establishment => establishment.RegionId == regionId && establishment.Code == code && (excludeId == null || establishment.Id != excludeId),
            cancellationToken);

    private static IQueryable<T> Active<T>(IQueryable<T> query, bool? activeOnly) where T : class =>
        activeOnly switch
        {
            true => query.Where(entity => EF.Property<bool>(entity, "IsActive")),
            false => query.Where(entity => !EF.Property<bool>(entity, "IsActive")),
            _ => query
        };
}
