using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class VoucherConcessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Saem as tabelas do #36 (vazias): o modelo do motor é o da spec GH-045.
            migrationBuilder.DropTable(
                name: "VoucherEvents");

            migrationBuilder.DropTable(
                name: "Vouchers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_campaign_conditions_parameters",
                table: "campaign_conditions");

            migrationBuilder.AddColumn<int>(
                name: "issued_vouchers",
                table: "campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "reserved_vouchers",
                table: "campaigns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "issued_vouchers",
                table: "campaign_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "reserved_vouchers",
                table: "campaign_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "concessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    network_id = table.Column<Guid>(type: "uuid", nullable: false),
                    satellite_app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    customer_document = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    customer_purchase_count = table.Column<int>(type: "integer", nullable: true),
                    requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    refusal = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    refused_campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reserved_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_concessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_concessions_establishments_establishment_id",
                        column: x => x.establishment_id,
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vouchers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    network_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concession_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_document = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    valid_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reserved_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    max_uses = table.Column<int>(type: "integer", nullable: false),
                    uses_count = table.Column<int>(type: "integer", nullable: false),
                    establishment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    discarded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vouchers", x => x.id);
                    table.CheckConstraint("ck_vouchers_max_uses", "max_uses > 0");
                    table.CheckConstraint("ck_vouchers_uses", "uses_count >= 0 AND uses_count <= max_uses");
                    table.ForeignKey(
                        name: "fk_vouchers_campaign_items_campaign_item_id",
                        column: x => x.campaign_item_id,
                        principalTable: "campaign_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vouchers_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_vouchers_concessions_concession_id",
                        column: x => x.concession_id,
                        principalTable: "concessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "voucher_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actor_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voucher_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_voucher_events_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_code",
                table: "vouchers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_campaign_id_status",
                table: "vouchers",
                columns: new[] { "campaign_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_campaign_item_id",
                table: "vouchers",
                column: "campaign_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_concession_id",
                table: "vouchers",
                column: "concession_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_network_id_customer_document_issued_at",
                table: "vouchers",
                columns: new[] { "network_id", "customer_document", "issued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_status_reserved_until",
                table: "vouchers",
                columns: new[] { "status", "reserved_until" });

            migrationBuilder.CreateIndex(
                name: "ix_concessions_establishment_id",
                table: "concessions",
                column: "establishment_id");

            migrationBuilder.CreateIndex(
                name: "ix_concessions_network_id_customer_document_requested_at",
                table: "concessions",
                columns: new[] { "network_id", "customer_document", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_concessions_network_id_transaction_id",
                table: "concessions",
                columns: new[] { "network_id", "transaction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_voucher_events_voucher_id_occurred_at",
                table: "voucher_events",
                columns: new[] { "voucher_id", "occurred_at" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_campaigns_voucher_counters",
                table: "campaigns",
                sql: "issued_vouchers >= 0 AND reserved_vouchers >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_campaign_items_voucher_counters",
                table: "campaign_items",
                sql: "issued_vouchers >= 0 AND reserved_vouchers >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_campaign_conditions_parameters",
                table: "campaign_conditions",
                sql: "(type = 'PurchaseTotal' AND min_amount IS NOT NULL AND CAST(min_amount AS REAL) > 0 AND min_quantity IS NULL AND category_id IS NULL AND product_id IS NULL) OR (type = 'ItemQuantity' AND min_quantity IS NOT NULL AND min_quantity > 0 AND min_amount IS NULL AND category_id IS NULL AND product_id IS NULL) OR (type = 'Category' AND category_id IS NOT NULL AND min_amount IS NULL AND min_quantity IS NULL AND product_id IS NULL) OR (type = 'Product' AND product_id IS NOT NULL AND min_amount IS NULL AND min_quantity IS NULL AND category_id IS NULL) OR (type = 'CustomerPurchaseCount' AND min_quantity IS NOT NULL AND min_quantity > 0 AND min_amount IS NULL AND category_id IS NULL AND product_id IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "voucher_events");

            migrationBuilder.DropTable(
                name: "vouchers");

            migrationBuilder.DropTable(
                name: "concessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_campaigns_voucher_counters",
                table: "campaigns");

            migrationBuilder.DropCheckConstraint(
                name: "ck_campaign_items_voucher_counters",
                table: "campaign_items");

            migrationBuilder.DropCheckConstraint(
                name: "ck_campaign_conditions_parameters",
                table: "campaign_conditions");

            migrationBuilder.DropColumn(
                name: "issued_vouchers",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "reserved_vouchers",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "issued_vouchers",
                table: "campaign_items");

            migrationBuilder.DropColumn(
                name: "reserved_vouchers",
                table: "campaign_items");

            migrationBuilder.AddCheckConstraint(
                name: "ck_campaign_conditions_parameters",
                table: "campaign_conditions",
                sql: "(type = 'PurchaseTotal' AND min_amount IS NOT NULL AND CAST(min_amount AS REAL) > 0 AND min_quantity IS NULL AND category_id IS NULL AND product_id IS NULL) OR (type = 'ItemQuantity' AND min_quantity IS NOT NULL AND min_quantity > 0 AND min_amount IS NULL AND category_id IS NULL AND product_id IS NULL) OR (type = 'Category' AND category_id IS NOT NULL AND min_amount IS NULL AND min_quantity IS NULL AND product_id IS NULL) OR (type = 'Product' AND product_id IS NOT NULL AND min_amount IS NULL AND min_quantity IS NULL AND category_id IS NULL)");

            migrationBuilder.CreateTable(
                name: "Vouchers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    expiration_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    usage_limit = table.Column<int>(type: "integer", nullable: false),
                    current_usage_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vouchers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "VoucherEvents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    voucher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_voucher_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_voucher_events_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalTable: "Vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_voucher_events_voucher_id",
                table: "VoucherEvents",
                column: "voucher_id");

            migrationBuilder.CreateIndex(
                name: "ix_vouchers_code",
                table: "Vouchers",
                column: "code",
                unique: true);
        }
    }
}
