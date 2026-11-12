using System.Globalization;
using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Application.Vouchers;

/// <summary>Verificação de aplicáveis, resgate e estorno (spec GH-049, fluxo 1.2). A rede é a da aplicação autenticada.</summary>
public class RedemptionService(
    IRedemptionRepository redemptions,
    IConcessionRepository concessions,
    ICatalogRepository catalog,
    ICurrentSatelliteApp app,
    IAuditTrail audit,
    IRedemptionReverser reverser,
    TimeProvider timeProvider) : IRedemptionService
{
    private static readonly EstablishmentErrors Errors = new(
        RedemptionErrors.EstablishmentNotFoundCode, RedemptionErrors.EstablishmentNotFoundMessage,
        RedemptionErrors.EstablishmentInactiveCode, RedemptionErrors.EstablishmentInactiveMessage,
        RedemptionErrors.ChannelNotEnabledCode, RedemptionErrors.ChannelNotEnabledMessage);

    private static readonly CultureInfo Brazil = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<RedemptionCheckResponse> CheckAsync(RedemptionCheckRequest request, CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(request, cancellationToken);
        var vouchers = await redemptions.GetCustomerVouchersAsync(app.NetworkId, context.Input.Document, cancellationToken);

        var candidates = vouchers
            .Where(voucher => voucher.EffectiveStatus(context.UtcNow) != VoucherStatus.Discarded)
            .Select(voucher => Evaluate(voucher, context))
            .ToList();
        var ranked = RedemptionEvaluator.Rank(candidates);

        return new RedemptionCheckResponse(
            context.Input.TransactionId,
            OnlyOnePerPurchase: true,
            context.Existing?.Id,
            ranked
                .Select((candidate, index) => new ApplicableVoucherResponse(
                    VoucherMapper.Map(candidate.Voucher, context.UtcNow), candidate.Discount, Recommended: index == 0))
                .ToList(),
            candidates
                .Where(candidate => candidate.Refusal is not null)
                .Select(candidate => new InapplicableVoucherResponse(
                    VoucherMapper.Map(candidate.Voucher, context.UtcNow),
                    candidate.Refusal.ToString()!,
                    Detail(candidate.Refusal!.Value, candidate.Voucher)))
                .ToList());
    }

    public async Task<RedemptionResponse> RedeemAsync(RedemptionRequest request, CancellationToken cancellationToken)
    {
        var voucherId = InputValues.RequiredId(request.VoucherId, nameof(request.VoucherId), RedemptionContract.VoucherIdRequired);
        var context = await LoadContextAsync(request, cancellationToken);

        // Idempotência (US-07.3, cenário 5) e um voucher por compra (cenário 4), antes de qualquer gravação.
        if (context.Existing is not null)
        {
            return context.Existing.VoucherId == voucherId
                ? Map(context.Existing, context.UtcNow)
                : throw Refused(RedemptionRefusal.TransactionAlreadyRedeemed, null);
        }

        var voucher = await RequireVoucherAsync(voucherId, context.Input.Document, cancellationToken);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var candidate = Evaluate(voucher, context);

            if (candidate.Refusal is { } refusal)
            {
                // O voucher pode ter sido relido depois de um pedido igual gravar o resgate desta transação
                // (ex.: já aparece Redeemed): antes de recusar, vale a idempotência.
                var settled = await redemptions.FindByTransactionAsync(app.NetworkId, context.Input.TransactionId, cancellationToken);

                if (settled is not null)
                {
                    return settled.VoucherId == voucherId
                        ? Map(settled, context.UtcNow)
                        : throw Refused(RedemptionRefusal.TransactionAlreadyRedeemed, null);
                }

                await RegisterRefusalAsync(voucher, refusal, context, cancellationToken);
                throw Refused(refusal, voucher);
            }

            await using (var transaction = await redemptions.BeginTransactionAsync(cancellationToken))
            {
                // Primeiro comando da transação (D-9): quem perde a corrida pela última utilização relê o voucher.
                if (await redemptions.TryConsumeUseAsync(voucher.Id, context.UtcNow, cancellationToken))
                {
                    var redemption = new Redemption
                    {
                        Id = Guid.CreateVersion7(new DateTimeOffset(context.UtcNow)),
                        NetworkId = app.NetworkId,
                        SatelliteAppId = app.ApplicationId,
                        VoucherId = voucher.Id,
                        CampaignId = voucher.CampaignId,
                        TransactionId = context.Input.TransactionId,
                        CustomerDocument = context.Input.Document,
                        EstablishmentId = context.Store.Id,
                        Channel = context.Input.Channel,
                        TotalAmount = context.Input.TotalAmount,
                        DiscountAmount = candidate.Discount,
                        ItemCount = context.Purchase.ItemCount,
                        RedeemedAt = context.UtcNow,
                        Status = RedemptionStatus.Effective
                    };

                    redemptions.Add(redemption);
                    redemptions.Add(NewEvent(voucher.Id, VoucherEventTypes.Redeemed, context.UtcNow, new RedeemedPayload(
                        redemption.Id, redemption.TransactionId, redemption.EstablishmentId, redemption.Channel.ToString(),
                        redemption.TotalAmount, redemption.DiscountAmount, voucher.UsesCount + 1)));
                    AppendAudit(AuditEventTypes.VoucherRedeemed, voucher.CampaignId, nameof(Voucher), voucher.Id, context.UtcNow,
                        new RedeemedPayload(
                            redemption.Id, redemption.TransactionId, redemption.EstablishmentId, redemption.Channel.ToString(),
                            redemption.TotalAmount, redemption.DiscountAmount, voucher.UsesCount + 1));

                    if (await redemptions.TrySaveChangesAsync(cancellationToken))
                    {
                        await transaction.CommitAsync(cancellationToken);

                        return Map(await RequireRedemptionAsync(redemption.Id, cancellationToken), context.UtcNow);
                    }

                    // Dois resgates simultâneos na mesma transação: a utilização desta é desfeita e vale o que gravou primeiro.
                    break;
                }
            }

            // Perdeu a corrida: se foi um pedido igual desta mesma transação, vale o dele (idempotência); senão reavalia o voucher.
            var raced = await redemptions.FindByTransactionAsync(app.NetworkId, context.Input.TransactionId, cancellationToken);

            if (raced is not null)
            {
                return raced.VoucherId == voucherId
                    ? Map(raced, context.UtcNow)
                    : throw Refused(RedemptionRefusal.TransactionAlreadyRedeemed, null);
            }

            voucher = await RequireVoucherAsync(voucherId, context.Input.Document, cancellationToken);
        }

        var replayed = await redemptions.FindByTransactionAsync(app.NetworkId, context.Input.TransactionId, cancellationToken)
            ?? throw new InvalidOperationException("O resgate não convergiu depois de duas tentativas.");

        return replayed.VoucherId == voucherId
            ? Map(replayed, context.UtcNow)
            : throw Refused(RedemptionRefusal.TransactionAlreadyRedeemed, null);
    }

    public async Task<RedemptionResponse> ReverseAsync(Guid redemptionId, CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var redemption = await RequireRedemptionAsync(redemptionId, cancellationToken);

        if (redemption.Status == RedemptionStatus.Reversed)
        {
            return Map(redemption, utcNow);
        }

        await using (var transaction = await redemptions.BeginTransactionAsync(cancellationToken))
        {
            if (await reverser.ReverseAsync(redemption.Id, "Application", app.ApplicationId, utcNow, cancellationToken))
            {
                await redemptions.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }

        return Map(await RequireRedemptionAsync(redemptionId, cancellationToken), utcNow);
    }

    public async Task<RedemptionResponse> GetAsync(Guid redemptionId, CancellationToken cancellationToken) =>
        Map(await RequireRedemptionAsync(redemptionId, cancellationToken), timeProvider.GetUtcNow().UtcDateTime);

    private async Task<PurchaseContext> LoadContextAsync(IPurchaseRequest request, CancellationToken cancellationToken)
    {
        var input = PurchaseParsing.Parse(request);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var store = PurchaseParsing.RequireEstablishment(
            await concessions.FindEstablishmentAsync(input.EstablishmentId, app.NetworkId, cancellationToken), input.Channel, Errors);
        var purchase = await PurchaseParsing.ResolveAsync(catalog, input, null, cancellationToken);
        var existing = await redemptions.FindByTransactionAsync(app.NetworkId, input.TransactionId, cancellationToken);

        return new PurchaseContext(input, purchase, store, existing, utcNow);
    }

    private static RedemptionCandidate Evaluate(Voucher voucher, PurchaseContext context)
    {
        var campaign = voucher.Campaign ?? throw VoucherMapper.MissingNavigation(nameof(Voucher.Campaign));
        var target = voucher.Item ?? throw VoucherMapper.MissingNavigation(nameof(Voucher.Item));
        var concession = voucher.Concession ?? throw VoucherMapper.MissingNavigation(nameof(Voucher.Concession));

        var refusal = RedemptionEvaluator.Refusal(
            voucher, campaign, target, concession, context.Purchase, context.Store, context.Input.Channel,
            context.Input.TransactionId, context.Existing, context.UtcNow);

        return new RedemptionCandidate(voucher, refusal, refusal is null ? RedemptionEvaluator.Discount(target, context.Purchase) : 0m);
    }

    /// <summary>Vencido na tentativa de resgate persiste o status (US-05.3, cenário 2); toda recusa é auditada com o motivo (D-15).</summary>
    private async Task RegisterRefusalAsync(Voucher voucher, RedemptionRefusal refusal, PurchaseContext context, CancellationToken cancellationToken)
    {
        if (refusal == RedemptionRefusal.Expired && await redemptions.TryMarkExpiredAsync(voucher.Id, context.UtcNow, cancellationToken))
        {
            redemptions.Add(NewEvent(voucher.Id, VoucherEventTypes.Expired, context.UtcNow, null));
        }

        AppendAudit(AuditEventTypes.VoucherRedemptionRefused, voucher.CampaignId, nameof(Voucher), voucher.Id, context.UtcNow,
            new RefusedPayload(context.Input.TransactionId, context.Store.Id, context.Input.Channel.ToString(), refusal.ToString()));
        await redemptions.SaveChangesAsync(cancellationToken);
    }

    private async Task<Voucher> RequireVoucherAsync(Guid voucherId, string document, CancellationToken cancellationToken) =>
        await redemptions.FindVoucherAsync(voucherId, app.NetworkId, document, cancellationToken)
            ?? throw new ResourceNotFoundException(RedemptionErrors.VoucherNotFoundMessage);

    private async Task<Redemption> RequireRedemptionAsync(Guid redemptionId, CancellationToken cancellationToken) =>
        await redemptions.FindByIdAsync(redemptionId, app.NetworkId, cancellationToken)
            ?? throw new ResourceNotFoundException(RedemptionErrors.RedemptionNotFoundMessage);

    private static BusinessRuleException Refused(RedemptionRefusal refusal, Voucher? voucher) =>
        new(RedemptionErrors.CodeOf(refusal), Detail(refusal, voucher), new Dictionary<string, object?> { ["reason"] = refusal.ToString() });

    private static string Detail(RedemptionRefusal refusal, Voucher? voucher)
    {
        var template = RedemptionErrors.MessageOf(refusal);
        var campaign = voucher?.Campaign;

        return refusal switch
        {
            RedemptionRefusal.Expired when voucher is not null && campaign is not null =>
                string.Format(template, NetworkTime.ToLocal(voucher.ValidUntil, VoucherMapper.TimeZoneOf(campaign)).ToString("dd/MM/yyyy HH:mm:ss")),
            RedemptionRefusal.AmountBelowMinimum when campaign?.MinPurchaseAmount is { } minimum => string.Format(template, minimum.ToString("N2", Brazil)),
            RedemptionRefusal.AmountAboveMaximum when campaign?.MaxPurchaseAmount is { } maximum => string.Format(template, maximum.ToString("N2", Brazil)),
            _ => template.Replace(" {0}", string.Empty).Replace("R$ ", string.Empty)
        };
    }

    private RedemptionResponse Map(Redemption redemption, DateTime utcNow)
    {
        var voucher = redemption.Voucher ?? throw VoucherMapper.MissingNavigation(nameof(Redemption.Voucher));
        var timeZone = VoucherMapper.TimeZoneOf(voucher.Campaign ?? throw VoucherMapper.MissingNavigation(nameof(Voucher.Campaign)));

        return new RedemptionResponse(
            redemption.Id,
            redemption.TransactionId,
            redemption.Status.ToString(),
            VoucherMapper.Map(voucher, utcNow),
            redemption.DiscountAmount,
            NetworkTime.ToLocal(redemption.RedeemedAt, timeZone),
            redemption.ReversedAt is { } reversedAt ? NetworkTime.ToLocal(reversedAt, timeZone) : null);
    }

    private VoucherEvent NewEvent(Guid voucherId, string eventType, DateTime utcNow, object? payload) => new()
    {
        Id = Guid.CreateVersion7(new DateTimeOffset(utcNow)),
        VoucherId = voucherId,
        EventType = eventType,
        OccurredAt = utcNow,
        ActorType = AuditActorType.Application,
        ActorId = app.ApplicationId,
        Payload = payload is null ? null : AuditJson.Serialize(payload)
    };

    private void AppendAudit(string eventType, Guid? campaignId, string entityType, Guid entityId, DateTime utcNow, object payload) =>
        audit.Append(new AuditEntry
        {
            Id = Guid.CreateVersion7(new DateTimeOffset(utcNow)),
            EventType = eventType,
            ActorType = AuditActorType.Application,
            ActorId = app.ApplicationId,
            NetworkId = app.NetworkId,
            CampaignId = campaignId,
            EntityType = entityType,
            EntityId = entityId,
            OccurredAt = utcNow,
            Payload = AuditJson.Serialize(payload)
        });

    private sealed record PurchaseContext(ParsedPurchase Input, Purchase Purchase, Establishment Store, Redemption? Existing, DateTime UtcNow);

    private sealed record RedeemedPayload(
        Guid RedemptionId, string TransactionId, Guid EstablishmentId, string Channel, decimal TotalAmount, decimal DiscountAmount, int UsesCount);

    private sealed record RefusedPayload(string TransactionId, Guid EstablishmentId, string Channel, string Reason);
}

/// <summary>Estorno compartilhado entre o resgate, o desfazimento e a expiração da concessão (GH-049, D-10 e D-19).</summary>
public class RedemptionReverser(IRedemptionRepository redemptions, IAuditTrail audit) : IRedemptionReverser
{
    public async Task<bool> ReverseAsync(Guid redemptionId, string cause, Guid? actorApplicationId, DateTime utcNow, CancellationToken cancellationToken)
    {
        // Comando condicional por id: só quem marcar este resgate como estornado devolve a utilização dele.
        if (!await redemptions.TryReverseAsync(redemptionId, utcNow, cancellationToken))
        {
            return false;
        }

        var redemption = await redemptions.FindAsync(redemptionId, cancellationToken)
            ?? throw new InvalidOperationException("O resgate estornado não foi encontrado.");
        await redemptions.TryReturnUseAsync(redemption.VoucherId, cancellationToken);

        var payload = new ReversedPayload(redemption.Id, redemption.TransactionId, redemption.DiscountAmount, cause);
        redemptions.Add(new VoucherEvent
        {
            Id = Guid.CreateVersion7(new DateTimeOffset(utcNow)),
            VoucherId = redemption.VoucherId,
            EventType = VoucherEventTypes.RedemptionReversed,
            OccurredAt = utcNow,
            ActorType = actorApplicationId is null ? null : AuditActorType.Application,
            ActorId = actorApplicationId,
            Payload = AuditJson.Serialize(payload)
        });

        if (actorApplicationId is { } actorId)
        {
            audit.Append(new AuditEntry
            {
                Id = Guid.CreateVersion7(new DateTimeOffset(utcNow)),
                EventType = AuditEventTypes.VoucherRedemptionReversed,
                ActorType = AuditActorType.Application,
                ActorId = actorId,
                NetworkId = redemption.NetworkId,
                CampaignId = redemption.CampaignId,
                EntityType = nameof(Voucher),
                EntityId = redemption.VoucherId,
                OccurredAt = utcNow,
                Payload = AuditJson.Serialize(payload)
            });
        }

        return true;
    }

    public async Task<int> ReverseAllOfVoucherAsync(Guid voucherId, string cause, Guid? actorApplicationId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var reversed = 0;

        foreach (var redemptionId in await redemptions.GetEffectiveIdsByVoucherAsync(voucherId, cancellationToken))
        {
            if (await ReverseAsync(redemptionId, cause, actorApplicationId, utcNow, cancellationToken))
            {
                reversed++;
            }
        }

        return reversed;
    }

    private sealed record ReversedPayload(Guid RedemptionId, string TransactionId, decimal DiscountAmount, string Cause);
}
