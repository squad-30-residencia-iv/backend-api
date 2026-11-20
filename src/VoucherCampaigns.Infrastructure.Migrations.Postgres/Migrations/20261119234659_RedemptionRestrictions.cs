using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RedemptionRestrictions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "campaign_redemption_targets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_redemption_targets", x => x.id);
                    table.CheckConstraint("ck_campaign_redemption_targets_target", "(target_type = 'Category' AND category_id IS NOT NULL AND product_id IS NULL) OR (target_type = 'Product' AND product_id IS NOT NULL AND category_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_campaign_redemption_targets_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_campaign_redemption_targets_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_campaign_redemption_targets_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "campaign_redemption_windows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    days_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_minute = table.Column<int>(type: "integer", nullable: false),
                    end_minute = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_redemption_windows", x => x.id);
                    table.CheckConstraint("ck_campaign_redemption_windows_days", "days_of_week > 0 AND days_of_week < 128");
                    table.CheckConstraint("ck_campaign_redemption_windows_minutes", "start_minute >= 0 AND start_minute < 1440 AND end_minute >= 0 AND end_minute < 1440");
                    table.ForeignKey(
                        name: "fk_campaign_redemption_windows_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_campaign_redemption_targets_campaign_id",
                table: "campaign_redemption_targets",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_redemption_targets_category_id",
                table: "campaign_redemption_targets",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_redemption_targets_product_id",
                table: "campaign_redemption_targets",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_redemption_windows_campaign_id",
                table: "campaign_redemption_windows",
                column: "campaign_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campaign_redemption_targets");

            migrationBuilder.DropTable(
                name: "campaign_redemption_windows");
        }
    }
}
