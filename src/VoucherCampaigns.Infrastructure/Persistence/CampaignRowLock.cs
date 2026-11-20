using Microsoft.EntityFrameworkCore;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Infrastructure.Persistence;

/// <summary>
/// Trava a linha da campanha na transação corrente e devolve o status gravado (spec GH-061, 7.1). Toda transação que
/// mexe no voucher e na campanha trava a campanha primeiro, na mesma ordem do cancelamento, da reserva e das
/// transições de status: sem ordem inversa não há deadlock, e o status lido depois da trava é o que já foi gravado.
/// </summary>
internal static class CampaignRowLock
{
    public static async Task<CampaignStatus?> LockAsync(AppDbContext context, Guid campaignId, CancellationToken cancellationToken)
    {
        // Um UPDATE sem efeito trava a linha no PostgreSQL e pega o escritor único no SQLite; os dois provedores aceitam.
        var rows = await context.Campaigns
            .Where(campaign => campaign.Id == campaignId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(campaign => campaign.ReservedVouchers, campaign => campaign.ReservedVouchers), cancellationToken);

        if (rows == 0)
        {
            return null;
        }

        return await context.Campaigns.AsNoTracking()
            .Where(campaign => campaign.Id == campaignId)
            .Select(campaign => (CampaignStatus?)campaign.Status)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
