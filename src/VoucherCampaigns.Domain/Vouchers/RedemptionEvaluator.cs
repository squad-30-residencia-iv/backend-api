using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Organization;

namespace VoucherCampaigns.Domain.Vouchers;

/// <summary>Voucher do cliente avaliado nesta compra: o motivo da recusa ou, quando serve, o desconto previsto (D-7).</summary>
public sealed record RedemptionCandidate(Voucher Voucher, RedemptionRefusal? Refusal, decimal Discount);

/// <summary>Passos 4 e 5 do fluxo da verificação e do resgate (spec GH-049, 1.2), sem I/O.</summary>
public static class RedemptionEvaluator
{
    /// <summary>
    /// Primeiro motivo que se aplica, na ordem do fluxo: já resgatado nesta compra, status efetivo (provisório só na própria
    /// compra com a permissão da campanha), campanha cancelada, escopo, faixa de valor, alvo presente e compra já com outro resgate.
    /// </summary>
    /// <param name="existingInTransaction">Resgate já gravado para esta transação, se houver.</param>
    public static RedemptionRefusal? Refusal(
        Voucher voucher,
        Campaign campaign,
        CampaignItem target,
        Concession concession,
        Purchase purchase,
        Establishment store,
        ChannelType channel,
        string transactionId,
        Redemption? existingInTransaction,
        DateTime utcNow)
    {
        // O voucher que esta compra já resgatou explica mais do que o status dele (que pode ter virado Redeemed por isso).
        if (existingInTransaction is not null && existingInTransaction.VoucherId == voucher.Id)
        {
            return RedemptionRefusal.AlreadyRedeemedInThisPurchase;
        }

        switch (voucher.EffectiveStatus(utcNow))
        {
            case VoucherStatus.Provisional when !string.Equals(concession.TransactionId, transactionId, StringComparison.Ordinal):
                return RedemptionRefusal.NotConfirmed;
            case VoucherStatus.Provisional when !campaign.AllowUseInSamePurchase:
                return RedemptionRefusal.SamePurchaseNotAllowed;
            case VoucherStatus.Expired:
                return RedemptionRefusal.Expired;
            case VoucherStatus.Redeemed:
                return RedemptionRefusal.NoUsesLeft;
            case VoucherStatus.Cancelled:
                return RedemptionRefusal.Cancelled;
            case VoucherStatus.Discarded:
                // Descartado nunca valeu; quem lista os vouchers já o deixa de fora.
                return RedemptionRefusal.NotConfirmed;
        }

        // Provisório resgatado na própria compra e ainda não confirmado continua Provisional, mas sem utilização.
        if (voucher.RemainingUses == 0)
        {
            return RedemptionRefusal.NoUsesLeft;
        }

        if (campaign.Status == CampaignStatus.Cancelled)
        {
            return RedemptionRefusal.CampaignCancelled;
        }

        if (!campaign.IsInScope(store, channel))
        {
            return RedemptionRefusal.OutOfScope;
        }

        if (campaign.MinPurchaseAmount is { } minimum && purchase.TotalAmount < minimum)
        {
            return RedemptionRefusal.AmountBelowMinimum;
        }

        if (campaign.MaxPurchaseAmount is { } maximum && purchase.TotalAmount > maximum)
        {
            return RedemptionRefusal.AmountAboveMaximum;
        }

        if (target.TargetType != TargetType.All && !purchase.Items.Any(target.Matches))
        {
            return RedemptionRefusal.TargetNotInPurchase;
        }

        if (existingInTransaction is not null)
        {
            return RedemptionRefusal.TransactionAlreadyRedeemed;
        }

        return null;
    }

    /// <summary>
    /// Desconto do voucher nesta compra (D-7): percentual sobre os itens do alvo (alvo "todos" = valor total) ou o valor
    /// fixo limitado a essa base, para o desconto nunca passar do que o alvo vale na compra.
    /// </summary>
    public static decimal Discount(CampaignItem target, Purchase purchase)
    {
        var baseAmount = target.TargetType == TargetType.All
            ? purchase.TotalAmount
            : purchase.Items.Where(target.Matches).Sum(item => item.Amount);

        return target.BenefitType == BenefitType.FixedAmount
            ? Math.Min(target.BenefitValue, baseAmount)
            : Math.Round(baseAmount * target.BenefitValue / 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Aplicáveis na ordem de recomendação (D-8): maior desconto → vence primeiro → menor id. O primeiro é o recomendado.</summary>
    public static IReadOnlyList<RedemptionCandidate> Rank(IEnumerable<RedemptionCandidate> applicable) =>
        applicable
            .Where(candidate => candidate.Refusal is null)
            .OrderByDescending(candidate => candidate.Discount)
            .ThenBy(candidate => candidate.Voucher.ValidUntil)
            .ThenBy(candidate => candidate.Voucher.Id)
            .ToList();
}
