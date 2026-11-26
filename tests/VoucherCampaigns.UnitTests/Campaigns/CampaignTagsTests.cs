using VoucherCampaigns.Domain.Campaigns;
using static VoucherCampaigns.UnitTests.Vouchers.MotorFixtures;

namespace VoucherCampaigns.UnitTests.Campaigns;

/// <summary>Tags da campanha no domínio (spec GH-077, D-2 e D-3).</summary>
public class CampaignTagsTests
{
    [Fact]
    public void ReplaceTags_WorksOutsideTheDraft_KeepsExistingLinks_AndDropsRepeated()
    {
        var campaign = ActiveCampaign();
        var (first, second, third) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        campaign.ReplaceTags([first, second, first], Created);
        var kept = campaign.TagLinks.Single(link => link.TagId == second);

        campaign.ReplaceTags([second, third], Created.AddMinutes(1));

        Assert.Equal([second, third], campaign.TagLinks.Select(link => link.TagId).OrderBy(id => id == second ? 0 : 1));
        Assert.Same(kept, campaign.TagLinks.Single(link => link.TagId == second));
        Assert.Equal(Created.AddMinutes(1), campaign.UpdatedAt);
    }

    [Fact]
    public void ReplaceTags_RefusesMoreThanFive()
    {
        var campaign = ActiveCampaign();

        Assert.Throws<ArgumentException>(() => campaign.ReplaceTags(Enumerable.Range(0, Campaign.MaxTags + 1).Select(_ => Guid.NewGuid()).ToList(), Created));
        Assert.Empty(campaign.TagLinks);
    }

    [Fact]
    public void Duplicate_CopiesOnlyActiveTags()
    {
        var source = ActiveCampaign();
        var active = new CampaignTag { Id = Guid.NewGuid(), Name = "Natal", IsActive = true };
        var inactive = new CampaignTag { Id = Guid.NewGuid(), Name = "Antiga", IsActive = false };
        source.TagLinks.Add(new CampaignTagLink { Id = NextId(), TagId = active.Id, Tag = active });
        source.TagLinks.Add(new CampaignTagLink { Id = NextId(), TagId = inactive.Id, Tag = inactive });

        var copy = source.Duplicate("Cópia", Utc(2026, 11, 1, 11, 0), Utc(2026, 11, 30, 23, 0), Created);

        Assert.Equal([active.Id], copy.TagLinks.Select(link => link.TagId));
    }
}
