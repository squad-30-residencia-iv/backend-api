using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Infrastructure.Persistence.Repositories;

public class OrganizationRepository(AppDbContext context) : IOrganizationRepository
{
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
                .ThenInclude(region => region.Establishments)
                .ThenInclude(establishment => establishment.Channels);
        }

        var stored = await query.FirstAsync(stored => stored.Id == network.Id, cancellationToken);
        stored.Name = network.Name;
        stored.TimeZone = network.TimeZone;
        stored.IsActive = network.IsActive;

        // Rede, regiões, lojas e canais na mesma gravação.
        if (!network.IsActive)
        {
            foreach (var region in stored.Regions)
            {
                region.IsActive = false;

                foreach (var establishment in region.Establishments)
                {
                    establishment.IsActive = false;

                    foreach (var channel in establishment.Channels)
                    {
                        channel.IsActive = false;
                    }
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
            query = query
                .Include(stored => stored.Establishments)
                .ThenInclude(establishment => establishment.Channels);
        }

        var stored = await query.FirstAsync(stored => stored.Id == region.Id, cancellationToken);
        stored.Name = region.Name;
        stored.IsActive = region.IsActive;

        if (!region.IsActive)
        {
            foreach (var establishment in stored.Establishments)
            {
                establishment.IsActive = false;

                foreach (var channel in establishment.Channels)
                {
                    channel.IsActive = false;
                }
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
        var query = Active(context.Establishments.AsNoTracking().Where(establishment => establishment.RegionId == regionId), activeOnly);
        return await query.OrderBy(establishment => establishment.Name).ToListAsync(cancellationToken);
    }

    public Task<Establishment?> GetEstablishmentByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Establishments.AsNoTracking().FirstOrDefaultAsync(establishment => establishment.Id == id, cancellationToken);

    public async Task<Establishment> CreateEstablishmentAsync(Establishment establishment, CancellationToken cancellationToken)
    {
        context.Establishments.Add(establishment);
        await ConstraintSave.SaveAsync(
            context, OrganizationErrors.EstablishmentCodeTakenCode, OrganizationErrors.EstablishmentCodeTakenMessage, cancellationToken);
        return establishment;
    }

    public async Task<Establishment> UpdateEstablishmentAsync(Establishment establishment, CancellationToken cancellationToken)
    {
        IQueryable<Establishment> query = context.Establishments;

        if (!establishment.IsActive)
        {
            query = query.Include(stored => stored.Channels);
        }

        var stored = await query.FirstAsync(stored => stored.Id == establishment.Id, cancellationToken);
        stored.Code = establishment.Code;
        stored.Name = establishment.Name;
        stored.IsActive = establishment.IsActive;

        if (!establishment.IsActive)
        {
            foreach (var channel in stored.Channels)
            {
                channel.IsActive = false;
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

    public async Task<IReadOnlyList<Channel>> GetChannelsAsync(Guid establishmentId, bool? activeOnly, CancellationToken cancellationToken)
    {
        var query = Active(context.Channels.AsNoTracking().Where(channel => channel.EstablishmentId == establishmentId), activeOnly);
        return await query.OrderBy(channel => channel.Type).ToListAsync(cancellationToken);
    }

    public Task<Channel?> GetChannelByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Channels.AsNoTracking().FirstOrDefaultAsync(channel => channel.Id == id, cancellationToken);

    public async Task<Channel> CreateChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        context.Channels.Add(channel);
        await ConstraintSave.SaveAsync(context, OrganizationErrors.ChannelTypeTakenCode, OrganizationErrors.ChannelTypeTakenMessage, cancellationToken);
        return channel;
    }

    public async Task<Channel> UpdateChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        var stored = await context.Channels.FirstAsync(stored => stored.Id == channel.Id, cancellationToken);
        stored.IsActive = channel.IsActive;
        await context.SaveChangesAsync(cancellationToken);
        return stored;
    }

    public Task<bool> ExistsChannelByTypeInEstablishmentAsync(
        Guid establishmentId, ChannelType type, Guid? excludeId, CancellationToken cancellationToken) =>
        context.Channels.AnyAsync(
            channel => channel.EstablishmentId == establishmentId && channel.Type == type && (excludeId == null || channel.Id != excludeId),
            cancellationToken);

    private static IQueryable<T> Active<T>(IQueryable<T> query, bool? activeOnly) where T : class =>
        activeOnly switch
        {
            true => query.Where(entity => EF.Property<bool>(entity, "IsActive")),
            false => query.Where(entity => !EF.Property<bool>(entity, "IsActive")),
            _ => query
        };
}
