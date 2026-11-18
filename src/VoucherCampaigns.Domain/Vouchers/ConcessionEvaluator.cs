using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Eligibility;

namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Campanha apta a conceder, com o alvo escolhido (D-10) e o benefício estimado nesta compra (D-9).</summary>
public sealed record EligibleConcession(Campaign Campaign, CampaignItem Target, decimal EstimatedBenefit);

/// <summary>
/// Resultado da avaliação: as aptas na ordem de desempate D1 e, quando nenhuma é apta, o motivo da
/// satisfeita melhor colocada, preferindo o bloqueio de uma campanha ativa ao de uma inativa
/// (ou <see cref="ConcessionRefusal.NoCampaignMatched"/>).
/// </summary>
public sealed record EvaluationResult(
    IReadOnlyList<EligibleConcession> Eligible,
    ConcessionRefusal? Refusal,
    Guid? RefusedCampaignId)
{
    public static readonly EvaluationResult NoMatch = new([], ConcessionRefusal.NoCampaignMatched, null);
}

/// <summary>Passos 4 a 7 do fluxo da concessão (spec GH-045, 1.2), sem I/O.</summary>
public static class ConcessionEvaluator
{
    /// <param name="candidates">Campanhas aprovadas da rede cujo escopo contempla a loja e o canal.</param>
    /// <param name="customerVouchers">Vouchers não descartados do cliente, por campanha (RF06c).</param>
    /// <param name="eligibility">Resultado do público restrito por campanha (spec GH-047); ausente = todos os clientes.</param>
    public static EvaluationResult Evaluate(
        IReadOnlyList<Campaign> candidates,
        Purchase purchase,
        IReadOnlyDictionary<Guid, int> customerVouchers,
        DateTime utcNow,
        IReadOnlyDictionary<Guid, EligibilityResult>? eligibility = null)
    {
        // D1: maior benefício nesta compra → termina primeiro → aprovada primeiro → id, para a repetição escolher igual.
        var satisfied = candidates
            .Where(campaign => campaign.IsSatisfiedBy(purchase))
            .Select(campaign =>
            {
                var target = campaign.ChooseTarget(purchase);
                return (Campaign: campaign, Target: target, Benefit: EstimatedBenefit(target, purchase));
            })
            .OrderByDescending(entry => entry.Benefit)
            .ThenBy(entry => entry.Campaign.EndsAt)
            .ThenBy(entry => entry.Campaign.DecidedAt ?? DateTime.MaxValue)
            .ThenBy(entry => entry.Campaign.Id)
            .ToList();

        if (satisfied.Count == 0)
        {
            return EvaluationResult.NoMatch;
        }

        var eligible = new List<EligibleConcession>();
        ConcessionRefusal? refusal = null;
        Guid? refusedCampaignId = null;

        foreach (var (campaign, target, benefit) in satisfied)
        {
            var block = Block(campaign, target, customerVouchers, eligibility, utcNow);

            if (block is null)
            {
                eligible.Add(new EligibleConcession(campaign, target, benefit));
            }
            else if (refusal is null || (refusal == ConcessionRefusal.CampaignNotActive && block != ConcessionRefusal.CampaignNotActive))
            {
                // O motivo é o da satisfeita melhor colocada, mas uma campanha efetivamente ativa que esgotou (ou o
                // cliente no limite) explica mais do que uma pausada ou encerrada que só continua candidata pelo histórico.
                refusal = block;
                refusedCampaignId = campaign.Id;
            }
        }

        return eligible.Count > 0
            ? new EvaluationResult(eligible, null, null)
            : new EvaluationResult(eligible, refusal, refusedCampaignId);
    }

    /// <summary>
    /// Benefício que o voucher daria aplicado a esta compra (D-9): valor fixo, ou percentual sobre os itens do alvo
    /// (o total, no alvo <see cref="TargetType.All"/>). Alvo ausente na compra vale zero, só para a comparação.
    /// </summary>
    public static decimal EstimatedBenefit(CampaignItem target, Purchase purchase)
    {
        if (target.BenefitType == BenefitType.FixedAmount)
        {
            return target.BenefitValue;
        }

        var baseAmount = target.TargetType == TargetType.All
            ? purchase.TotalAmount
            : purchase.Items.Where(target.Matches).Sum(item => item.Amount);

        return Math.Round(baseAmount * target.BenefitValue / 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static ConcessionRefusal? Block(
        Campaign campaign,
        CampaignItem target,
        IReadOnlyDictionary<Guid, int> customerVouchers,
        IReadOnlyDictionary<Guid, EligibilityResult>? eligibility,
        DateTime utcNow)
    {
        var status = campaign.EffectiveStatus(utcNow);

        if (status == CampaignStatus.SoldOut)
        {
            return ConcessionRefusal.CampaignSoldOut;
        }

        if (status != CampaignStatus.Active)
        {
            return ConcessionRefusal.CampaignNotActive;
        }

        // Elegibilidade depois das condições e do status, antes do limite e da cota (spec GH-047, 1.3 e D-14).
        if (eligibility is not null && eligibility.TryGetValue(campaign.Id, out var result) && !result.Eligible)
        {
            return ConcessionRefusal.CustomerNotEligible;
        }

        if (campaign.PerCustomerVoucherLimit is { } limit && customerVouchers.GetValueOrDefault(campaign.Id) >= limit)
        {
            return ConcessionRefusal.CustomerLimitReached;
        }

        return target.HasQuota ? null : ConcessionRefusal.TargetSoldOut;
    }
}
