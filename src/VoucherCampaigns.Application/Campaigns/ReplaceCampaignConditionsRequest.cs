using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Lista completa de condições de geração (US-01.4), cumulativas; substitui a atual e pode ser vazia.</summary>
public record ReplaceCampaignConditionsRequest(
    [Required(ErrorMessage = CampaignContract.ConditionsRequired)]
    [MaxLength(CampaignContract.MaxConditions, ErrorMessage = CampaignContract.ConditionsTooMany)]
    IReadOnlyList<CampaignConditionRequest?>? Conditions,
    [Required(ErrorMessage = CampaignContract.AllowUseInSamePurchaseRequired)]
    bool? AllowUseInSamePurchase);

/// <summary>
/// Condição; cada tipo usa só o seu parâmetro: <c>PurchaseTotal</c> → <c>MinAmount</c>,
/// <c>ItemQuantity</c> → <c>MinQuantity</c>, <c>Category</c> → <c>CategoryId</c>, <c>Product</c> → <c>ProductId</c>.
/// </summary>
public record CampaignConditionRequest(
    [Required(ErrorMessage = CampaignContract.ConditionTypeRequired)]
    string? Type,
    decimal? MinAmount,
    int? MinQuantity,
    Guid? CategoryId,
    Guid? ProductId);
