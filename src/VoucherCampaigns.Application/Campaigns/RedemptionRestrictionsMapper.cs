using System.Globalization;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Application.Campaigns;

/// <summary>Restrições do resgate (spec GH-070) nas respostas do portal e do satélite.</summary>
public static class RedemptionRestrictionsMapper
{
    public static List<RedemptionTargetResponse> Targets(Campaign campaign) =>
        campaign.RedemptionTargets
            .OrderBy(target => target.Id)
            .Select(target => new RedemptionTargetResponse(
                target.Id, target.TargetType.ToString(), target.CategoryId, target.ProductId, target.Product?.Code))
            .ToList();

    public static List<RedemptionWindowResponse> Windows(Campaign campaign) =>
        campaign.RedemptionWindows
            .OrderBy(window => window.Id)
            .Select(window => new RedemptionWindowResponse(Days(window), Time(window.StartMinute), Time(window.EndMinute)))
            .ToList();

    /// <summary>Dias de segunda a domingo, a ordem de leitura do portal.</summary>
    public static List<string> Days(CampaignRedemptionWindow window) =>
        window.Days.OrderBy(day => ((int)day + 6) % 7).Select(day => day.ToString()).ToList();

    public static string Time(int minuteOfDay) =>
        string.Create(CultureInfo.InvariantCulture, $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}");
}
