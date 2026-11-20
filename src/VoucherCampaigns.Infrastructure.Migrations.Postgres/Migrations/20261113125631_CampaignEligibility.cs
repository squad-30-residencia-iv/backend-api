using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class CampaignEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EligibilityCriteria");

            migrationBuilder.AddColumn<string>(
                name: "eligibility",
                table: "concessions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "campaign_eligibility_criteria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    min_age = table.Column<int>(type: "integer", nullable: true),
                    max_age = table.Column<int>(type: "integer", nullable: true),
                    states = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    city_codes = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    min_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    min_purchases = table.Column<int>(type: "integer", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    window_days = table.Column<int>(type: "integer", nullable: true),
                    customer_count = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_eligibility_criteria", x => x.id);
                    table.CheckConstraint("ck_campaign_eligibility_criteria_parameters", "(type = 'AgeRange' AND (min_age IS NOT NULL OR max_age IS NOT NULL) AND min_amount IS NULL AND min_purchases IS NULL AND category_id IS NULL AND window_days IS NULL AND customer_count IS NULL) OR (type = 'Location' AND (states <> '' OR city_codes <> '') AND min_age IS NULL AND max_age IS NULL AND min_amount IS NULL AND min_purchases IS NULL AND category_id IS NULL AND window_days IS NULL AND customer_count IS NULL) OR (type = 'LoyaltyMember' AND min_age IS NULL AND max_age IS NULL AND min_amount IS NULL AND min_purchases IS NULL AND category_id IS NULL AND window_days IS NULL AND customer_count IS NULL) OR (type = 'TotalSpent' AND min_amount IS NOT NULL AND CAST(min_amount AS REAL) > 0 AND window_days BETWEEN 1 AND 365 AND min_age IS NULL AND max_age IS NULL AND min_purchases IS NULL AND category_id IS NULL AND customer_count IS NULL) OR (type = 'PurchaseFrequency' AND min_purchases IS NOT NULL AND min_purchases > 0 AND window_days BETWEEN 1 AND 365 AND min_age IS NULL AND max_age IS NULL AND min_amount IS NULL AND category_id IS NULL AND customer_count IS NULL) OR (type = 'PurchasedCategory' AND category_id IS NOT NULL AND window_days BETWEEN 1 AND 365 AND min_age IS NULL AND max_age IS NULL AND min_amount IS NULL AND min_purchases IS NULL AND customer_count IS NULL) OR (type = 'SpecificCustomers' AND customer_count IS NOT NULL AND customer_count > 0 AND min_age IS NULL AND max_age IS NULL AND min_amount IS NULL AND min_purchases IS NULL AND category_id IS NULL AND window_days IS NULL)");
                    table.ForeignKey(
                        name: "fk_campaign_eligibility_criteria_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_campaign_eligibility_criteria_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "campaign_eligible_customers",
                columns: table => new
                {
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_eligible_customers", x => new { x.campaign_id, x.document });
                    table.ForeignKey(
                        name: "fk_campaign_eligible_customers_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_campaign_eligibility_criteria_campaign_id",
                table: "campaign_eligibility_criteria",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_eligibility_criteria_category_id",
                table: "campaign_eligibility_criteria",
                column: "category_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campaign_eligibility_criteria");

            migrationBuilder.DropTable(
                name: "campaign_eligible_customers");

            migrationBuilder.DropColumn(
                name: "eligibility",
                table: "concessions");

            migrationBuilder.CreateTable(
                name: "EligibilityCriteria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criterion_type = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eligibility_criteria", x => x.id);
                });
        }
    }
}
