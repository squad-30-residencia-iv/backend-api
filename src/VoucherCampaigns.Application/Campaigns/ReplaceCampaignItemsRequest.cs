using System.ComponentModel.DataAnnotations;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Lista completa de alvos da campanha (US-01.2); substitui a atual.</summary>
public record ReplaceCampaignItemsRequest(
    [Required(ErrorMessage = CampaignContract.ItemsRequired)]
    [MinLength(1, ErrorMessage = CampaignContract.ItemsRequired)]
    [MaxLength(CampaignContract.MaxItems, ErrorMessage = CampaignContract.ItemsTooMany)]
    IReadOnlyList<CampaignItemRequest?>? Items);

/// <summary>Alvo e benefício; <c>VoucherLimit</c> é a cota por alvo (US-01.3).</summary>
public record CampaignItemRequest(
    [Required(ErrorMessage = CampaignContract.TargetTypeRequired)]
    string? TargetType,
    Guid? CategoryId,
    Guid? ProductId,
    [Required(ErrorMessage = CampaignContract.BenefitTypeRequired)]
    string? BenefitType,
    [Required(ErrorMessage = CampaignContract.BenefitValueRequired)]
    decimal? BenefitValue,
    [Range(1, int.MaxValue, ErrorMessage = CampaignContract.ItemVoucherLimitPositive)]
    int? VoucherLimit);
