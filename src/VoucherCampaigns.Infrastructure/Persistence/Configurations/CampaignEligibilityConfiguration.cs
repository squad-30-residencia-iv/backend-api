using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VoucherCampaigns.Domain.Campaigns;
using VoucherCampaigns.Domain.Catalog;

namespace VoucherCampaigns.Infrastructure.Persistence.Configurations;

/// <summary>Critérios do público restrito (spec GH-047, D-19), com o mesmo desenho de <c>campaign_conditions</c>.</summary>
public class CampaignEligibilityCriterionConfiguration : IEntityTypeConfiguration<CampaignEligibilityCriterion>
{
    // UFs e municípios em texto separado por vírgula: listas curtas, sempre lidas com a campanha.
    private static readonly ValueConverter<List<string>, string> ListConverter = new(
        list => string.Join(',', list),
        text => text.Length == 0 ? new List<string>() : text.Split(',', StringSplitOptions.None).ToList());

    private static readonly ValueComparer<List<string>> ListComparer = new(
        (left, right) => left!.SequenceEqual(right!),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        list => list.ToList());

    public void Configure(EntityTypeBuilder<CampaignEligibilityCriterion> builder)
    {
        // Cada tipo só com os seus parâmetros (2.1 da spec). O CAST evita a comparação como texto no SQLite.
        builder.ToTable("campaign_eligibility_criteria", table => table.HasCheckConstraint(
            "ck_campaign_eligibility_criteria_parameters",
            "(type = 'AgeRange' AND (min_age IS NOT NULL OR max_age IS NOT NULL)"
            + " AND min_amount IS NULL AND min_purchases IS NULL AND category_id IS NULL AND window_days IS NULL AND customer_count IS NULL)"
            + " OR (type = 'Location' AND (states <> '' OR city_codes <> '')"
            + " AND min_age IS NULL AND max_age IS NULL AND min_amount IS NULL AND min_purchases IS NULL AND category_id IS NULL AND window_days IS NULL AND customer_count IS NULL)"
            + " OR (type = 'LoyaltyMember'"
            + " AND min_age IS NULL AND max_age IS NULL AND min_amount IS NULL AND min_purchases IS NULL AND category_id IS NULL AND window_days IS NULL AND customer_count IS NULL)"
            + " OR (type = 'TotalSpent' AND min_amount IS NOT NULL AND CAST(min_amount AS REAL) > 0 AND window_days BETWEEN 1 AND 365"
            + " AND min_age IS NULL AND max_age IS NULL AND min_purchases IS NULL AND category_id IS NULL AND customer_count IS NULL)"
            + " OR (type = 'PurchaseFrequency' AND min_purchases IS NOT NULL AND min_purchases > 0 AND window_days BETWEEN 1 AND 365"
            + " AND min_age IS NULL AND max_age IS NULL AND min_amount IS NULL AND category_id IS NULL AND customer_count IS NULL)"
            + " OR (type = 'PurchasedCategory' AND category_id IS NOT NULL AND window_days BETWEEN 1 AND 365"
            + " AND min_age IS NULL AND max_age IS NULL AND min_amount IS NULL AND min_purchases IS NULL AND customer_count IS NULL)"
            + " OR (type = 'SpecificCustomers' AND customer_count IS NOT NULL AND customer_count > 0"
            + " AND min_age IS NULL AND max_age IS NULL AND min_amount IS NULL AND min_purchases IS NULL AND category_id IS NULL AND window_days IS NULL)"));

        builder.HasKey(criterion => criterion.Id);

        // O id vem da aplicação, em sequência, para os critérios voltarem na ordem informada.
        builder.Property(criterion => criterion.Id).ValueGeneratedNever();

        builder.Property(criterion => criterion.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(criterion => criterion.MinAmount).HasPrecision(10, 2);
        builder.Property(criterion => criterion.States).HasConversion(ListConverter, ListComparer).HasMaxLength(400).IsRequired();
        builder.Property(criterion => criterion.CityCodes).HasConversion(ListConverter, ListComparer).HasMaxLength(800).IsRequired();
        builder.Property(criterion => criterion.CustomersDigest).HasMaxLength(64);

        builder.HasOne(criterion => criterion.Campaign)
            .WithMany(campaign => campaign.EligibilityCriteria)
            .HasForeignKey(criterion => criterion.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(criterion => criterion.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Lista de clientes específicos (D-8): chave composta, que também serve à busca do CPF na concessão.</summary>
public class CampaignEligibleCustomerConfiguration : IEntityTypeConfiguration<CampaignEligibleCustomer>
{
    public void Configure(EntityTypeBuilder<CampaignEligibleCustomer> builder)
    {
        builder.ToTable("campaign_eligible_customers");
        builder.HasKey(customer => new { customer.CampaignId, customer.Document });
        builder.Property(customer => customer.Document).HasMaxLength(11);

        builder.HasOne<Campaign>()
            .WithMany(campaign => campaign.EligibleCustomers)
            .HasForeignKey(customer => customer.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
