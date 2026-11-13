using VoucherCampaigns.Application.Audit;
using VoucherCampaigns.Application.Authentication;
using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.Eligibility;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Audit;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Eligibility;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Application.Vouchers;

/// <summary>Motor de concessão (spec GH-045). A rede é sempre a da aplicação satélite autenticada.</summary>
public class ConcessionService(
    IConcessionRepository concessions,
    ICatalogRepository catalog,
    ICurrentSatelliteApp app,
    IAuditTrail audit,
    IReservationReleaser releaser,
    IRedemptionReverser redemptionReverser,
    VoucherOptions options,
    TimeProvider timeProvider) : IConcessionService
{
    private static readonly EstablishmentErrors Errors = new(
        ConcessionErrors.EstablishmentNotFoundCode, ConcessionErrors.EstablishmentNotFoundMessage,
        ConcessionErrors.EstablishmentInactiveCode, ConcessionErrors.EstablishmentInactiveMessage,
        ConcessionErrors.ChannelNotEnabledCode, ConcessionErrors.ChannelNotEnabledMessage);

    public async Task<ConcessionResponse> GrantAsync(ConcessionRequest request, CancellationToken cancellationToken)
    {
        var input = PurchaseParsing.Parse(request);
        var networkId = app.NetworkId;
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var profile = EligibilityParsing.ParseProfile(request.Customer!, input.Document, utcNow, nameof(request.Customer));

        var establishment = await RequireEstablishmentAsync(input, networkId, cancellationToken);

        // Idempotência (PR-07): a transação já avaliada devolve a decisão original, no estado atual.
        var existing = await concessions.FindByTransactionAsync(networkId, input.TransactionId, cancellationToken);

        if (existing is not null)
        {
            return Map(existing, utcNow);
        }

        // Reservas vencidas liberam saldo antes da avaliação (D-6).
        await releaser.ReleaseExpiredAsync(networkId, utcNow, cancellationToken);

        var purchase = await PurchaseParsing.ResolveAsync(catalog, input, request.CustomerPurchaseCount, cancellationToken);
        var candidates = (await concessions.GetApprovedCampaignsAsync(networkId, cancellationToken))
            .Where(campaign => campaign.IsInScope(establishment, input.Channel))
            .ToList();
        var customerVouchers = await concessions.CountCustomerVouchersAsync(networkId, input.Document, cancellationToken);
        var eligibility = await EvaluateEligibilityAsync(candidates, profile, utcNow, cancellationToken);
        var evaluation = ConcessionEvaluator.Evaluate(candidates, purchase, customerVouchers, utcNow, eligibility);

        var refusal = evaluation.Refusal;
        var refusedCampaignId = evaluation.RefusedCampaignId;

        foreach (var eligible in evaluation.Eligible)
        {
            await using var transaction = await concessions.BeginTransactionAsync(cancellationToken);

            // A reserva é o primeiro comando da transação (D-4): quem perde a corrida pelo último voucher segue para a próxima apta.
            var outcome = await concessions.TryReserveAsync(eligible.Campaign.Id, eligible.Target.Id, cancellationToken);

            if (outcome != ReserveOutcome.Reserved)
            {
                refusal ??= outcome == ReserveOutcome.CampaignSoldOut ? ConcessionRefusal.CampaignSoldOut : ConcessionRefusal.TargetSoldOut;
                refusedCampaignId ??= eligible.Campaign.Id;
                continue;
            }

            var concession = NewConcession(input, purchase, networkId, utcNow);
            concession.Status = ConcessionStatus.Provisional;
            concession.ReservedUntil = utcNow.AddMinutes(options.ReservationMinutes);

            // Snapshot dos atributos usados na decisão (RN11a, spec GH-047 D-12); null no público "todos".
            var decided = eligibility.GetValueOrDefault(eligible.Campaign.Id);
            concession.Eligibility = decided is null ? null : EligibilityParsing.Snapshot(decided);

            var voucher = new Voucher
            {
                Id = Guid.CreateVersion7(new DateTimeOffset(utcNow)),
                Code = VoucherCode.Generate(),
                NetworkId = networkId,
                CampaignId = eligible.Campaign.Id,
                CampaignItemId = eligible.Target.Id,
                ConcessionId = concession.Id,
                CustomerDocument = input.Document,
                Status = VoucherStatus.Provisional,
                IssuedAt = utcNow,
                ValidUntil = VoucherValidity.Compute(eligible.Campaign, utcNow),
                ReservedUntil = concession.ReservedUntil,
                MaxUses = eligible.Campaign.MaxUsesPerVoucher
                    ?? throw new InvalidOperationException("A campanha aprovada não tem as utilizações por voucher."),
                UsesCount = 0,
                EstablishmentId = establishment.Id,
                Channel = input.Channel
            };

            concessions.Add(concession);
            concessions.Add(voucher);
            concessions.Add(NewEvent(voucher, VoucherEventTypes.Issued, utcNow, new IssuedPayload(
                concession.TransactionId,
                eligible.Campaign.Id,
                eligible.Target.Id,
                establishment.Id,
                input.Channel.ToString(),
                eligible.Campaign.AudienceType?.ToString(),
                eligible.EstimatedBenefit,
                voucher.ValidUntil,
                concession.ReservedUntil,
                decided is null ? null : EligibilityParsing.Summary(decided))));
            AppendAudit(AuditEventTypes.VoucherIssued, eligible.Campaign.Id, nameof(Voucher), voucher.Id, utcNow, new VoucherAuditPayload(
                concession.Id, concession.TransactionId, voucher.Code, eligible.Target.Id, voucher.ValidUntil, concession.ReservedUntil,
                decided is null ? null : EligibilityParsing.Summary(decided)));

            if (!await concessions.TrySaveChangesAsync(cancellationToken))
            {
                // Mesma transação em dois pedidos simultâneos: a reserva desta é desfeita e vale a que gravou primeiro.
                break;
            }

            await transaction.CommitAsync(cancellationToken);

            return Map(concession, voucher, eligible.Campaign, eligible.Target, utcNow);
        }

        var replayed = await concessions.FindByTransactionAsync(networkId, input.TransactionId, cancellationToken);

        if (replayed is not null)
        {
            return Map(replayed, utcNow);
        }

        var refused = NewConcession(input, purchase, networkId, utcNow);
        refused.Status = ConcessionStatus.NotGranted;
        refused.Refusal = refusal ?? ConcessionRefusal.NoCampaignMatched;
        refused.RefusedCampaignId = refusedCampaignId;
        var refusedEligibility = refusedCampaignId is { } blockedId ? eligibility.GetValueOrDefault(blockedId) : null;
        refused.Eligibility = refusedEligibility is null ? null : EligibilityParsing.Snapshot(refusedEligibility);
        concessions.Add(refused);

        // Só a campanha satisfeita e bloqueada é auditada (D-20); compra sem campanha não é ação relevante.
        if (refused.Refusal != ConcessionRefusal.NoCampaignMatched)
        {
            // Na recusa por público, o critério que falhou fica na auditoria, não na resposta ao satélite (D-13).
            var failed = refused.Refusal == ConcessionRefusal.CustomerNotEligible ? refusedEligibility?.FirstFailed : null;
            AppendAudit(AuditEventTypes.VoucherIssuanceRefused, refusedCampaignId, nameof(Concession), refused.Id, utcNow,
                new RefusedPayload(refused.TransactionId, refusedCampaignId, refused.Refusal.ToString()!,
                    failed?.Type.ToString(), failed?.Outcome.ToString()));
        }

        if (!await concessions.TrySaveChangesAsync(cancellationToken))
        {
            replayed = await concessions.FindByTransactionAsync(networkId, input.TransactionId, cancellationToken)
                ?? throw new InvalidOperationException("A concessão repetida não foi encontrada depois do conflito.");

            return Map(replayed, utcNow);
        }

        return Map(refused, utcNow);
    }

    public async Task<ConcessionResponse> ConfirmAsync(Guid concessionId, CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var concession = await RequireConcessionAsync(concessionId, cancellationToken);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            switch (concession.EffectiveStatus(utcNow))
            {
                case ConcessionStatus.NotGranted:
                    throw new BusinessRuleException(ConcessionErrors.NotGrantedCode, ConcessionErrors.NotGrantedMessage);
                case ConcessionStatus.Confirmed:
                    return Map(concession, utcNow);
                case ConcessionStatus.Cancelled:
                    throw new BusinessRuleException(ConcessionErrors.AlreadyCancelledCode, ConcessionErrors.AlreadyCancelledMessage);
                case ConcessionStatus.Expired:
                    await releaser.ReleaseExpiredAsync(app.NetworkId, utcNow, cancellationToken);
                    throw new BusinessRuleException(ConcessionErrors.ReservationExpiredCode, ConcessionErrors.ReservationExpiredMessage);
            }

            var voucher = concession.Voucher ?? throw new InvalidOperationException("A concessão provisória não tem voucher.");

            await using (var transaction = await concessions.BeginTransactionAsync(cancellationToken))
            {
                // O voucher vira disponível ou, se já foi resgatado na própria compra até o limite (GH-049, D-10), resgatado.
                if (await concessions.TryConfirmVoucherAsync(voucher.Id, utcNow, cancellationToken))
                {
                    await concessions.ConsumeReservationAsync(voucher.CampaignId, voucher.CampaignItemId, cancellationToken);
                    await concessions.MarkConcessionAsync(concession.Id, ConcessionStatus.Confirmed, utcNow, cancellationToken);
                    concessions.Add(NewEvent(voucher, VoucherEventTypes.Confirmed, utcNow, null));
                    AppendAudit(AuditEventTypes.VoucherConfirmed, voucher.CampaignId, nameof(Voucher), voucher.Id, utcNow,
                        new VoucherAuditPayload(concession.Id, concession.TransactionId, voucher.Code, voucher.CampaignItemId, voucher.ValidUntil, null));
                    await concessions.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return Map(await RequireConcessionAsync(concessionId, cancellationToken), utcNow);
                }
            }

            // Outro pedido mudou o voucher no meio: reavalia pelo estado gravado.
            concession = await RequireConcessionAsync(concessionId, cancellationToken);
        }

        throw new InvalidOperationException("A confirmação não convergiu depois de duas tentativas.");
    }

    public async Task<ConcessionResponse> CancelAsync(Guid concessionId, CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        var concession = await RequireConcessionAsync(concessionId, cancellationToken);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            switch (concession.EffectiveStatus(utcNow))
            {
                case ConcessionStatus.NotGranted:
                    throw new BusinessRuleException(ConcessionErrors.NotGrantedCode, ConcessionErrors.NotGrantedMessage);
                case ConcessionStatus.Confirmed:
                    throw new BusinessRuleException(ConcessionErrors.AlreadyConfirmedCode, ConcessionErrors.AlreadyConfirmedMessage);
                case ConcessionStatus.Cancelled:
                    return Map(concession, utcNow);
                case ConcessionStatus.Expired:
                    // Nada a devolver: a liberação garante o estado gravado e a resposta é o estado atual.
                    await releaser.ReleaseExpiredAsync(app.NetworkId, utcNow, cancellationToken);
                    return Map(await RequireConcessionAsync(concessionId, cancellationToken), utcNow);
            }

            var voucher = concession.Voucher ?? throw new InvalidOperationException("A concessão provisória não tem voucher.");

            await using (var transaction = await concessions.BeginTransactionAsync(cancellationToken))
            {
                if (await concessions.TryDiscardVoucherAsync(voucher.Id, utcNow, cancellationToken))
                {
                    await concessions.ReleaseReservationAsync(voucher.CampaignId, voucher.CampaignItemId, cancellationToken);
                    await concessions.MarkConcessionAsync(concession.Id, ConcessionStatus.Cancelled, utcNow, cancellationToken);
                    // Resgatado na própria compra (GH-049, D-10): a venda desfeita estorna o resgate junto.
                    await redemptionReverser.ReverseAllOfVoucherAsync(voucher.Id, "ConcessionCancelled", app.ApplicationId, utcNow, cancellationToken);
                    concessions.Add(NewEvent(voucher, VoucherEventTypes.Cancelled, utcNow, null));
                    AppendAudit(AuditEventTypes.VoucherCancelled, voucher.CampaignId, nameof(Voucher), voucher.Id, utcNow,
                        new VoucherAuditPayload(concession.Id, concession.TransactionId, voucher.Code, voucher.CampaignItemId, voucher.ValidUntil, null));
                    await concessions.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    return Map(await RequireConcessionAsync(concessionId, cancellationToken), utcNow);
                }
            }

            concession = await RequireConcessionAsync(concessionId, cancellationToken);
        }

        throw new InvalidOperationException("O desfazimento não convergiu depois de duas tentativas.");
    }

    public async Task<ConcessionResponse> GetAsync(Guid concessionId, CancellationToken cancellationToken) =>
        Map(await RequireConcessionAsync(concessionId, cancellationToken), timeProvider.GetUtcNow().UtcDateTime);

    public async Task<IReadOnlyList<VoucherResponse>> GetCustomerVouchersAsync(
        string document, string? status, CancellationToken cancellationToken)
    {
        var normalized = PurchaseParsing.ParseDocument(document, "document");
        var filter = ParseStatusFilter(status);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var vouchers = await concessions.GetCustomerVouchersAsync(app.NetworkId, normalized, cancellationToken);

        return vouchers
            .Select(voucher => (Voucher: voucher, Status: voucher.EffectiveStatus(utcNow)))
            .Where(entry => filter is null ? entry.Status != VoucherStatus.Discarded : entry.Status == filter)
            .Select(entry => VoucherMapper.Map(entry.Voucher, utcNow))
            .ToList();
    }

    /// <summary>
    /// Público restrito das candidatas (spec GH-047, 1.3): uma consulta para a lista de CPFs e uma para o catálogo do
    /// histórico, só quando alguma candidata é restrita. A idade usa a data da compra no fuso da rede (D-11).
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, EligibilityResult>> EvaluateEligibilityAsync(
        IReadOnlyList<Campaign> candidates, ParsedProfile parsed, DateTime utcNow, CancellationToken cancellationToken)
    {
        var restricted = candidates.Where(campaign => campaign.AudienceType == CampaignAudienceType.Restricted).ToList();

        if (restricted.Count == 0)
        {
            return new Dictionary<Guid, EligibilityResult>();
        }

        var profile = await EligibilityParsing.ResolveAsync(catalog, parsed, cancellationToken);
        var withList = restricted
            .Where(campaign => campaign.EligibilityCriteria.Any(criterion => criterion.Type == EligibilityCriterionType.SpecificCustomers))
            .Select(campaign => campaign.Id)
            .ToList();
        var listed = await concessions.GetCampaignsListingCustomerAsync(withList, parsed.Document, cancellationToken);

        return restricted.ToDictionary(
            campaign => campaign.Id,
            campaign =>
            {
                var purchaseDate = DateOnly.FromDateTime(NetworkTime.ToLocal(utcNow, VoucherMapper.TimeZoneOf(campaign)).DateTime);
                return EligibilityEvaluator.Evaluate(campaign.EligibilityCriteria, profile, purchaseDate, listed.Contains(campaign.Id));
            });
    }

    private async Task<Establishment> RequireEstablishmentAsync(ParsedPurchase input, Guid networkId, CancellationToken cancellationToken) =>
        PurchaseParsing.RequireEstablishment(
            await concessions.FindEstablishmentAsync(input.EstablishmentId, networkId, cancellationToken), input.Channel, Errors);

    private async Task<Concession> RequireConcessionAsync(Guid concessionId, CancellationToken cancellationToken) =>
        await concessions.FindByIdAsync(concessionId, app.NetworkId, cancellationToken)
            ?? throw new ResourceNotFoundException(ConcessionContract.ConcessionNotFound);

    private Concession NewConcession(ParsedPurchase input, Purchase purchase, Guid networkId, DateTime utcNow) => new()
    {
        Id = Guid.CreateVersion7(new DateTimeOffset(utcNow)),
        NetworkId = networkId,
        SatelliteAppId = app.ApplicationId,
        TransactionId = input.TransactionId,
        CustomerDocument = input.Document,
        EstablishmentId = input.EstablishmentId,
        Channel = input.Channel,
        TotalAmount = input.TotalAmount,
        ItemCount = purchase.ItemCount,
        CustomerPurchaseCount = purchase.CustomerPurchaseCount,
        RequestedAt = utcNow
    };

    private VoucherEvent NewEvent(Voucher voucher, string eventType, DateTime utcNow, object? payload) => new()
    {
        Id = Guid.CreateVersion7(new DateTimeOffset(utcNow)),
        VoucherId = voucher.Id,
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

    private static ConcessionResponse Map(Concession concession, DateTime utcNow) =>
        Map(concession, concession.Voucher, concession.Voucher?.Campaign, concession.Voucher?.Item, utcNow);

    private static ConcessionResponse Map(Concession concession, Voucher? voucher, Campaign? campaign, CampaignItem? item, DateTime utcNow)
    {
        var status = concession.EffectiveStatus(utcNow);
        VoucherResponse? voucherResponse = null;
        DateTimeOffset? reservedUntil = null;

        if (voucher is not null)
        {
            var mappedCampaign = campaign ?? throw VoucherMapper.MissingNavigation(nameof(Voucher.Campaign));
            var mappedItem = item ?? throw VoucherMapper.MissingNavigation(nameof(Voucher.Item));
            voucherResponse = VoucherMapper.Map(voucher, mappedCampaign, mappedItem, concession, utcNow);

            if (status == ConcessionStatus.Provisional && concession.ReservedUntil is { } until)
            {
                reservedUntil = NetworkTime.ToLocal(until, VoucherMapper.TimeZoneOf(mappedCampaign));
            }
        }

        return new ConcessionResponse(
            concession.Id,
            concession.TransactionId,
            status.ToString(),
            status == ConcessionStatus.NotGranted ? concession.Refusal?.ToString() : null,
            reservedUntil,
            voucherResponse);
    }

    private static VoucherStatus? ParseStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return Enum.TryParse<VoucherStatus>(status.Trim(), ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            && parsed != VoucherStatus.Discarded
            ? parsed
            : throw new RequestValidationException("status", ConcessionContract.VoucherStatusInvalid);
    }

    private sealed record IssuedPayload(
        string TransactionId,
        Guid CampaignId,
        Guid CampaignItemId,
        Guid EstablishmentId,
        string Channel,
        string? AudienceType,
        decimal EstimatedBenefit,
        DateTime ValidUntil,
        DateTime? ReservedUntil,
        object? Eligibility);

    private sealed record VoucherAuditPayload(
        Guid ConcessionId,
        string TransactionId,
        string Code,
        Guid CampaignItemId,
        DateTime ValidUntil,
        DateTime? ReservedUntil,
        object? Eligibility = null);

    private sealed record RefusedPayload(
        string TransactionId, Guid? CampaignId, string Reason, string? FailedCriterion = null, string? FailedOutcome = null);
}
