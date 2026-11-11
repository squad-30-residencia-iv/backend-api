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

/// <summary>Motor de concessão (spec GH-045). A rede é sempre a da aplicação satélite autenticada.</summary>
public class ConcessionService(
    IConcessionRepository concessions,
    ICatalogRepository catalog,
    ICurrentSatelliteApp app,
    IAuditTrail audit,
    IReservationReleaser releaser,
    VoucherOptions options,
    TimeProvider timeProvider) : IConcessionService
{
    public async Task<ConcessionResponse> GrantAsync(ConcessionRequest request, CancellationToken cancellationToken)
    {
        var input = ParseRequest(request);
        var networkId = app.NetworkId;
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var establishment = await RequireEstablishmentAsync(input, networkId, cancellationToken);

        // Idempotência (PR-07): a transação já avaliada devolve a decisão original, no estado atual.
        var existing = await concessions.FindByTransactionAsync(networkId, input.TransactionId, cancellationToken);

        if (existing is not null)
        {
            return Map(existing, utcNow);
        }

        // Reservas vencidas liberam saldo antes da avaliação (D-6).
        await releaser.ReleaseExpiredAsync(networkId, utcNow, cancellationToken);

        var purchase = await ResolvePurchaseAsync(input, cancellationToken);
        var candidates = (await concessions.GetApprovedCampaignsAsync(networkId, cancellationToken))
            .Where(campaign => campaign.IsInScope(establishment, input.Channel))
            .ToList();
        var customerVouchers = await concessions.CountCustomerVouchersAsync(networkId, input.Document, cancellationToken);
        var evaluation = ConcessionEvaluator.Evaluate(candidates, purchase, customerVouchers, utcNow);

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
                concession.ReservedUntil)));
            AppendAudit(AuditEventTypes.VoucherIssued, eligible.Campaign.Id, nameof(Voucher), voucher.Id, utcNow, new VoucherAuditPayload(
                concession.Id, concession.TransactionId, voucher.Code, eligible.Target.Id, voucher.ValidUntil, concession.ReservedUntil));

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
        concessions.Add(refused);

        // Só a campanha satisfeita e bloqueada é auditada (D-20); compra sem campanha não é ação relevante.
        if (refused.Refusal != ConcessionRefusal.NoCampaignMatched)
        {
            AppendAudit(AuditEventTypes.VoucherIssuanceRefused, refusedCampaignId, nameof(Concession), refused.Id, utcNow,
                new RefusedPayload(refused.TransactionId, refusedCampaignId, refused.Refusal.ToString()!));
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
        var normalized = ParseDocument(document, "document");
        var filter = ParseStatusFilter(status);
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var vouchers = await concessions.GetCustomerVouchersAsync(app.NetworkId, normalized, cancellationToken);

        return vouchers
            .Select(voucher => (Voucher: voucher, Status: voucher.EffectiveStatus(utcNow)))
            .Where(entry => filter is null ? entry.Status != VoucherStatus.Discarded : entry.Status == filter)
            .Select(entry => MapVoucher(
                entry.Voucher,
                entry.Voucher.Campaign ?? throw MissingNavigation(nameof(Voucher.Campaign)),
                entry.Voucher.Item ?? throw MissingNavigation(nameof(Voucher.Item)),
                entry.Voucher.Concession ?? throw MissingNavigation(nameof(Voucher.Concession)),
                utcNow))
            .ToList();
    }

    private async Task<Establishment> RequireEstablishmentAsync(ParsedRequest input, Guid networkId, CancellationToken cancellationToken)
    {
        var establishment = await concessions.FindEstablishmentAsync(input.EstablishmentId, networkId, cancellationToken)
            ?? throw new BusinessRuleException(ConcessionErrors.EstablishmentNotFoundCode, ConcessionErrors.EstablishmentNotFoundMessage);

        // D7: a loja inativada não encerra a campanha; só a concessão nela é recusada.
        if (!establishment.IsActive)
        {
            throw new BusinessRuleException(ConcessionErrors.EstablishmentInactiveCode, ConcessionErrors.EstablishmentInactiveMessage);
        }

        if (!establishment.EnabledChannels().Contains(input.Channel))
        {
            throw new BusinessRuleException(
                ConcessionErrors.ChannelNotEnabledCode,
                string.Format(ConcessionErrors.ChannelNotEnabledMessage, establishment.Code, ChannelName(input.Channel)));
        }

        return establishment;
    }

    private async Task<Concession> RequireConcessionAsync(Guid concessionId, CancellationToken cancellationToken) =>
        await concessions.FindByIdAsync(concessionId, app.NetworkId, cancellationToken)
            ?? throw new ResourceNotFoundException(ConcessionContract.ConcessionNotFound);

    /// <summary>Resolve os códigos no catálogo (D-12): o código desconhecido conta só no total e na quantidade.</summary>
    private async Task<Purchase> ResolvePurchaseAsync(ParsedRequest input, CancellationToken cancellationToken)
    {
        var codes = input.Items.Select(item => item.ProductCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var products = (await catalog.GetProductsByCodesAsync(codes, cancellationToken))
            .ToDictionary(product => product.Code, StringComparer.OrdinalIgnoreCase);

        var items = input.Items
            .Select(item =>
            {
                var product = products.GetValueOrDefault(item.ProductCode);
                return new PurchaseItem(item.ProductCode, item.Quantity, item.UnitPrice, product?.Id, product?.CategoryId);
            })
            .ToList();

        return new Purchase(input.TotalAmount, items, input.CustomerPurchaseCount);
    }

    private Concession NewConcession(ParsedRequest input, Purchase purchase, Guid networkId, DateTime utcNow) => new()
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
        CustomerPurchaseCount = input.CustomerPurchaseCount,
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
            var mappedCampaign = campaign ?? throw MissingNavigation(nameof(Voucher.Campaign));
            var mappedItem = item ?? throw MissingNavigation(nameof(Voucher.Item));
            voucherResponse = MapVoucher(voucher, mappedCampaign, mappedItem, concession, utcNow);

            if (status == ConcessionStatus.Provisional && concession.ReservedUntil is { } until)
            {
                reservedUntil = NetworkTime.ToLocal(until, TimeZoneOf(mappedCampaign));
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

    private static VoucherResponse MapVoucher(Voucher voucher, Campaign campaign, CampaignItem item, Concession concession, DateTime utcNow)
    {
        var timeZone = TimeZoneOf(campaign);

        return new VoucherResponse(
            voucher.Id,
            voucher.Code,
            campaign.Id,
            campaign.Name,
            voucher.EffectiveStatus(utcNow).ToString(),
            NetworkTime.ToLocal(voucher.IssuedAt, timeZone),
            NetworkTime.ToLocal(voucher.ValidUntil, timeZone),
            voucher.MaxUses,
            voucher.RemainingUses,
            campaign.AllowUseInSamePurchase,
            new VoucherTargetResponse(item.TargetType.ToString(), item.CategoryId, item.ProductId, item.BenefitType.ToString(), item.BenefitValue),
            concession.Id,
            concession.TransactionId);
    }

    private static TimeZoneInfo TimeZoneOf(Campaign campaign) =>
        NetworkTime.Resolve((campaign.Network ?? throw MissingNavigation(nameof(Campaign.Network))).TimeZone);

    private static InvalidOperationException MissingNavigation(string navigation) =>
        new($"O repositório não carregou '{navigation}'.");

    private static string ChannelName(ChannelType channel) => channel == ChannelType.Physical ? "físico" : "virtual";

    private static ParsedRequest ParseRequest(ConcessionRequest request)
    {
        var transactionId = InputValues.RequiredText(
            request.TransactionId, nameof(request.TransactionId), ConcessionContract.TransactionIdRequired,
            ConcessionContract.MaxTransactionIdLength, ConcessionContract.TransactionIdTooLong);
        var document = ParseDocument(request.Customer?.Document, $"{nameof(request.Customer)}.{nameof(ConcessionCustomerRequest.Document)}");
        var establishmentId = InputValues.RequiredId(request.EstablishmentId, nameof(request.EstablishmentId), ConcessionContract.EstablishmentRequired);
        var channel = ParseChannel(request.Channel);
        var totalAmount = ParseAmount(request.TotalAmount, nameof(request.TotalAmount));

        var items = new List<ParsedItem>();

        for (var index = 0; index < request.Items!.Count; index++)
        {
            var item = request.Items[index];
            var field = $"{nameof(request.Items)}[{index}]";

            if (item is null)
            {
                throw new RequestValidationException($"{field}.{nameof(PurchaseItemRequest.ProductCode)}", ConcessionContract.ItemProductCodeRequired);
            }

            var code = InputValues.RequiredText(
                item.ProductCode, $"{field}.{nameof(item.ProductCode)}", ConcessionContract.ItemProductCodeRequired,
                ConcessionContract.MaxProductCodeLength, ConcessionContract.ItemProductCodeTooLong);
            var unitPrice = item.UnitPrice!.Value;

            if (unitPrice < 0 || decimal.Round(unitPrice, 2) != unitPrice || unitPrice > ConcessionContract.MaxAmount)
            {
                throw new RequestValidationException($"{field}.{nameof(item.UnitPrice)}", ConcessionContract.ItemUnitPriceInvalid);
            }

            items.Add(new ParsedItem(code, item.Quantity!.Value, unitPrice));
        }

        return new ParsedRequest(transactionId, document, establishmentId, channel, totalAmount, items, request.CustomerPurchaseCount);
    }

    /// <summary>CPF com ou sem máscara; grava só os 11 dígitos (D-16).</summary>
    private static string ParseDocument(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new RequestValidationException(field, ConcessionContract.CustomerDocumentRequired);
        }

        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        var others = value.Count(character => !char.IsAsciiDigit(character) && character is not ('.' or '-' or ' '));

        if (digits.Length != ConcessionContract.DocumentLength || others > 0)
        {
            throw new RequestValidationException(field, ConcessionContract.CustomerDocumentInvalid);
        }

        return digits;
    }

    private static ChannelType ParseChannel(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new RequestValidationException(nameof(ConcessionRequest.Channel), ConcessionContract.ChannelRequired);
        }

        return Enum.TryParse<ChannelType>(trimmed, ignoreCase: true, out var channel) && Enum.IsDefined(channel)
            ? channel
            : throw new RequestValidationException(nameof(ConcessionRequest.Channel), ConcessionContract.ChannelInvalid);
    }

    private static decimal ParseAmount(decimal? value, string field)
    {
        var amount = value ?? throw new RequestValidationException(field, ConcessionContract.TotalAmountRequired);

        if (amount <= 0)
        {
            throw new RequestValidationException(field, ConcessionContract.TotalAmountPositive);
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new RequestValidationException(field, ConcessionContract.TotalAmountScale);
        }

        if (amount > ConcessionContract.MaxAmount)
        {
            throw new RequestValidationException(field, ConcessionContract.TotalAmountTooLarge);
        }

        return amount;
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

    private sealed record ParsedRequest(
        string TransactionId,
        string Document,
        Guid EstablishmentId,
        ChannelType Channel,
        decimal TotalAmount,
        IReadOnlyList<ParsedItem> Items,
        int? CustomerPurchaseCount);

    private sealed record ParsedItem(string ProductCode, int Quantity, decimal UnitPrice);

    private sealed record IssuedPayload(
        string TransactionId,
        Guid CampaignId,
        Guid CampaignItemId,
        Guid EstablishmentId,
        string Channel,
        string? AudienceType,
        decimal EstimatedBenefit,
        DateTime ValidUntil,
        DateTime? ReservedUntil);

    private sealed record VoucherAuditPayload(
        Guid ConcessionId,
        string TransactionId,
        string Code,
        Guid CampaignItemId,
        DateTime ValidUntil,
        DateTime? ReservedUntil);

    private sealed record RefusedPayload(string TransactionId, Guid? CampaignId, string Reason);
}
