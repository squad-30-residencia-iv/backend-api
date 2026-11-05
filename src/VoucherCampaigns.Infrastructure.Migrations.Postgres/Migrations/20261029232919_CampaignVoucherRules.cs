using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class CampaignVoucherRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_campaigns_voucher_validity_days",
                table: "campaigns");

            migrationBuilder.RenameColumn(
                name: "voucher_validity_days",
                table: "campaigns",
                newName: "voucher_validity_amount");

            migrationBuilder.AddColumn<bool>(
                name: "allow_use_in_same_purchase",
                table: "campaigns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "max_purchase_amount",
                table: "campaigns",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_uses_per_voucher",
                table: "campaigns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "min_purchase_amount",
                table: "campaigns",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "voucher_validity_unit",
                table: "campaigns",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "campaign_conditions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    min_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    min_quantity = table.Column<int>(type: "integer", nullable: true),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_conditions", x => x.id);
                    table.CheckConstraint("ck_campaign_conditions_parameters", "(type = 'PurchaseTotal' AND min_amount IS NOT NULL AND CAST(min_amount AS REAL) > 0 AND min_quantity IS NULL AND category_id IS NULL AND product_id IS NULL) OR (type = 'ItemQuantity' AND min_quantity IS NOT NULL AND min_quantity > 0 AND min_amount IS NULL AND category_id IS NULL AND product_id IS NULL) OR (type = 'Category' AND category_id IS NOT NULL AND min_amount IS NULL AND min_quantity IS NULL AND product_id IS NULL) OR (type = 'Product' AND product_id IS NOT NULL AND min_amount IS NULL AND min_quantity IS NULL AND category_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_campaign_conditions_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_campaign_conditions_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_campaign_conditions_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_campaigns_max_purchase_amount",
                table: "campaigns",
                sql: "max_purchase_amount IS NULL OR CAST(max_purchase_amount AS REAL) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_campaigns_max_uses_per_voucher",
                table: "campaigns",
                sql: "max_uses_per_voucher IS NULL OR max_uses_per_voucher > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_campaigns_min_purchase_amount",
                table: "campaigns",
                sql: "min_purchase_amount IS NULL OR CAST(min_purchase_amount AS REAL) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_campaigns_voucher_validity_amount",
                table: "campaigns",
                sql: "voucher_validity_amount IS NULL OR voucher_validity_amount > 0");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_conditions_campaign_id",
                table: "campaign_conditions",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_conditions_category_id",
                table: "campaign_conditions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_conditions_product_id",
                table: "campaign_conditions",
                column: "product_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campaign_conditions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_campaigns_max_purchase_amount",
                table: "campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "ck_campaigns_max_uses_per_voucher",
                table: "campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "ck_campaigns_min_purchase_amount",
                table: "campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "ck_campaigns_voucher_validity_amount",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "allow_use_in_same_purchase",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "max_purchase_amount",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "max_uses_per_voucher",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "min_purchase_amount",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "voucher_validity_unit",
                table: "campaigns");

            migrationBuilder.RenameColumn(
                name: "voucher_validity_amount",
                table: "campaigns",
                newName: "voucher_validity_days");

            migrationBuilder.AddCheckConstraint(
                name: "ck_campaigns_voucher_validity_days",
                table: "campaigns",
                sql: "voucher_validity_days IS NULL OR voucher_validity_days > 0");
        }
    }
}
