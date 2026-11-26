using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VoucherCampaigns.Domain.Campaigns;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

/// <summary>Tags da rede (spec GH-077, D-1 e D-7): nome único por rede sem diferenciar maiúsculas.</summary>
public class CampaignTagConfiguration : IEntityTypeConfiguration<CampaignTag>
{
    public void Configure(EntityTypeBuilder<CampaignTag> builder)
    {
        builder.ToTable("campaign_tags");
        builder.HasKey(tag => tag.Id);
        builder.Property(tag => tag.Id).ValueGeneratedNever();
        builder.Property(tag => tag.Name).HasMaxLength(CampaignTag.NameMaxLength).IsRequired();
        builder.Property(tag => tag.NormalizedName).HasMaxLength(CampaignTag.NameMaxLength).IsRequired();
        builder.HasIndex(tag => new { tag.NetworkId, tag.NormalizedName }).IsUnique();
        builder.HasOne<Domain.Organization.Network>()
            .WithMany()
            .HasForeignKey(tag => tag.NetworkId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Tag aplicada à campanha (spec GH-077, D-7): sai com a campanha; a tag não sai enquanto houver vínculo.</summary>
public class CampaignTagLinkConfiguration : IEntityTypeConfiguration<CampaignTagLink>
{
    public void Configure(EntityTypeBuilder<CampaignTagLink> builder)
    {
        builder.ToTable("campaign_tag_links");
        builder.HasKey(link => link.Id);
        builder.Property(link => link.Id).ValueGeneratedNever();
        builder.HasIndex(link => new { link.CampaignId, link.TagId }).IsUnique();
        builder.HasIndex(link => link.TagId);
        builder.HasOne(link => link.Campaign)
            .WithMany(campaign => campaign.TagLinks)
            .HasForeignKey(link => link.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(link => link.Tag)
            .WithMany()
            .HasForeignKey(link => link.TagId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
