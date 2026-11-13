using VoucherCampaigns.Application.Common;
using VoucherCampaigns.Application.Ports;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Common;
using VoucherCampaigns.Domain.Organization;
using VoucherCampaigns.Domain.Vouchers;

namespace VoucherCampaigns.Application.Vouchers;

/// <summary>Compra com o carrinho fechado, como a concessão, a verificação e o resgate a recebem (spec GH-049, D-3).</summary>
public interface IPurchaseRequest
{
    string? TransactionId { get; }

    ConcessionCustomerRequest? Customer { get; }

    Guid? EstablishmentId { get; }

    string? Channel { get; }

    decimal? TotalAmount { get; }

    IReadOnlyList<PurchaseItemRequest?>? Items { get; }
}

public sealed record ParsedPurchase(
    string TransactionId,
    string Document,
    Guid EstablishmentId,
    ChannelType Channel,
    decimal TotalAmount,
    IReadOnlyList<ParsedItem> Items);

public sealed record ParsedItem(string ProductCode, int Quantity, decimal UnitPrice);

/// <summary>Códigos dos <c>422</c> de loja e canal; cada operação tem os seus (GH-045 D-13, GH-049 D-4).</summary>
public sealed record EstablishmentErrors(
    string NotFoundCode,
    string NotFoundMessage,
    string InactiveCode,
    string InactiveMessage,
    string ChannelNotEnabledCode,
    string ChannelNotEnabledMessage);

/// <summary>Validação e resolução da compra, iguais na concessão e no resgate.</summary>
public static class PurchaseParsing
{
    public static ParsedPurchase Parse(IPurchaseRequest request)
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

        return new ParsedPurchase(transactionId, document, establishmentId, channel, totalAmount, items);
    }

    /// <summary>CPF com ou sem máscara; grava só os 11 dígitos (GH-045, D-16).</summary>
    public static string ParseDocument(string? value, string field)
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

    public static ChannelType ParseChannel(string? value, string field = nameof(IPurchaseRequest.Channel))
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            throw new RequestValidationException(field, ConcessionContract.ChannelRequired);
        }

        return Enum.TryParse<ChannelType>(trimmed, ignoreCase: true, out var channel) && Enum.IsDefined(channel)
            ? channel
            : throw new RequestValidationException(field, ConcessionContract.ChannelInvalid);
    }

    public static decimal ParseAmount(decimal? value, string field)
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

    /// <summary>Loja da rede, ativa e que opera o canal (D7 e PR-11); senão o <c>422</c> da operação.</summary>
    public static Establishment RequireEstablishment(Establishment? establishment, ChannelType channel, EstablishmentErrors errors)
    {
        if (establishment is null)
        {
            throw new BusinessRuleException(errors.NotFoundCode, errors.NotFoundMessage);
        }

        if (!establishment.IsActive)
        {
            throw new BusinessRuleException(errors.InactiveCode, errors.InactiveMessage);
        }

        if (!establishment.EnabledChannels().Contains(channel))
        {
            throw new BusinessRuleException(
                errors.ChannelNotEnabledCode,
                string.Format(errors.ChannelNotEnabledMessage, establishment.Code, ChannelName(channel)));
        }

        return establishment;
    }

    /// <summary>Resolve os códigos no catálogo (GH-045, D-12): o código desconhecido conta só no total e na quantidade.</summary>
    public static async Task<Purchase> ResolveAsync(
        ICatalogRepository catalog, ParsedPurchase input, int? customerPurchaseCount, CancellationToken cancellationToken)
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

        return new Purchase(input.TotalAmount, items, customerPurchaseCount);
    }

    public static string ChannelName(ChannelType channel) => channel == ChannelType.Physical ? "físico" : "virtual";
}

/// <summary>Voucher como a aplicação satélite o vê, com as datas no fuso da rede.</summary>
public static class VoucherMapper
{
    public static VoucherResponse Map(Voucher voucher, Campaign campaign, CampaignItem item, Concession concession, DateTime utcNow)
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

    /// <summary>Mapeia pelo que o repositório carregou: campanha (com a rede), alvo e concessão.</summary>
    public static VoucherResponse Map(Voucher voucher, DateTime utcNow) =>
        Map(
            voucher,
            voucher.Campaign ?? throw MissingNavigation(nameof(Voucher.Campaign)),
            voucher.Item ?? throw MissingNavigation(nameof(Voucher.Item)),
            voucher.Concession ?? throw MissingNavigation(nameof(Voucher.Concession)),
            utcNow);

    public static TimeZoneInfo TimeZoneOf(Campaign campaign) =>
        NetworkTime.Resolve((campaign.Network ?? throw MissingNavigation(nameof(Campaign.Network))).TimeZone);

    public static InvalidOperationException MissingNavigation(string navigation) =>
        new($"O repositório não carregou '{navigation}'.");
}
