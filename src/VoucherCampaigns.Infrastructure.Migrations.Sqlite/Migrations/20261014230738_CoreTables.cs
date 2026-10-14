using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class CoreTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "networks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_networks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    category_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_products_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    network_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    starts_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ends_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    total_voucher_limit = table.Column<int>(type: "INTEGER", nullable: true),
                    voucher_validity_type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    voucher_validity_days = table.Column<int>(type: "INTEGER", nullable: true),
                    voucher_valid_until = table.Column<DateTime>(type: "TEXT", nullable: true),
                    voucher_validity_cap = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaigns", x => x.id);
                    table.CheckConstraint("ck_campaigns_period", "ends_at > starts_at");
                    table.CheckConstraint("ck_campaigns_total_voucher_limit", "total_voucher_limit IS NULL OR total_voucher_limit > 0");
                    table.CheckConstraint("ck_campaigns_voucher_validity_days", "voucher_validity_days IS NULL OR voucher_validity_days > 0");
                    table.ForeignKey(
                        name: "fk_campaigns_networks_network_id",
                        column: x => x.network_id,
                        principalTable: "networks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "regions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    network_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regions", x => x.id);
                    table.ForeignKey(
                        name: "fk_regions_networks_network_id",
                        column: x => x.network_id,
                        principalTable: "networks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "campaign_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    campaign_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    target_type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    category_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    product_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    benefit_type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    benefit_value = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    voucher_limit = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_items", x => x.id);
                    table.CheckConstraint("ck_campaign_items_benefit_value", "CAST(benefit_value AS REAL) > 0 AND (benefit_type <> 'Percentage' OR CAST(benefit_value AS REAL) <= 100)");
                    table.CheckConstraint("ck_campaign_items_target", "(target_type = 'All' AND category_id IS NULL AND product_id IS NULL) OR (target_type = 'Category' AND category_id IS NOT NULL AND product_id IS NULL) OR (target_type = 'Product' AND product_id IS NOT NULL AND category_id IS NULL)");
                    table.CheckConstraint("ck_campaign_items_voucher_limit", "voucher_limit IS NULL OR voucher_limit > 0");
                    table.ForeignKey(
                        name: "fk_campaign_items_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_campaign_items_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_campaign_items_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "establishments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    region_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_establishments", x => x.id);
                    table.ForeignKey(
                        name: "fk_establishments_regions_region_id",
                        column: x => x.region_id,
                        principalTable: "regions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "campaign_scopes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    campaign_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    region_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    establishment_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    channel_type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_scopes", x => x.id);
                    table.CheckConstraint("ck_campaign_scopes_single_node", "region_id IS NULL OR establishment_id IS NULL");
                    table.ForeignKey(
                        name: "fk_campaign_scopes_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_campaign_scopes_establishments_establishment_id",
                        column: x => x.establishment_id,
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_campaign_scopes_regions_region_id",
                        column: x => x.region_id,
                        principalTable: "regions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    establishment_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channels", x => x.id);
                    table.ForeignKey(
                        name: "fk_channels_establishments_establishment_id",
                        column: x => x.establishment_id,
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_campaign_items_campaign_id",
                table: "campaign_items",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_items_category_id",
                table: "campaign_items",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_items_product_id",
                table: "campaign_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_scopes_campaign_id",
                table: "campaign_scopes",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_scopes_establishment_id",
                table: "campaign_scopes",
                column: "establishment_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_scopes_region_id",
                table: "campaign_scopes",
                column: "region_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_network_id",
                table: "campaigns",
                column: "network_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_status",
                table: "campaigns",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_categories_name",
                table: "categories",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_channels_establishment_id_type",
                table: "channels",
                columns: new[] { "establishment_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_establishments_region_id_code",
                table: "establishments",
                columns: new[] { "region_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_networks_name",
                table: "networks",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_products_category_id",
                table: "products",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_code",
                table: "products",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_regions_network_id_name",
                table: "regions",
                columns: new[] { "network_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campaign_items");

            migrationBuilder.DropTable(
                name: "campaign_scopes");

            migrationBuilder.DropTable(
                name: "channels");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "campaigns");

            migrationBuilder.DropTable(
                name: "establishments");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "regions");

            migrationBuilder.DropTable(
                name: "networks");
        }
    }
}
