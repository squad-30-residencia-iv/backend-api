using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class VoucherRedemptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "redemptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    network_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    satellite_app_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    campaign_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    transaction_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    customer_document = table.Column<string>(type: "TEXT", maxLength: 11, nullable: false),
                    establishment_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    channel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    total_amount = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    discount_amount = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    item_count = table.Column<int>(type: "INTEGER", nullable: false),
                    redeemed_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    reversed_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_redemptions", x => x.id);
                    table.CheckConstraint("ck_redemptions_amounts", "total_amount >= 0 AND discount_amount >= 0");
                    table.ForeignKey(
                        name: "fk_redemptions_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_redemptions_establishments_establishment_id",
                        column: x => x.establishment_id,
                        principalTable: "establishments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_redemptions_vouchers_voucher_id",
                        column: x => x.voucher_id,
                        principalTable: "vouchers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_campaign_id",
                table: "redemptions",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_establishment_id",
                table: "redemptions",
                column: "establishment_id");

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_network_id_redeemed_at",
                table: "redemptions",
                columns: new[] { "network_id", "redeemed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_network_id_transaction_id",
                table: "redemptions",
                columns: new[] { "network_id", "transaction_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_redemptions_voucher_id_status",
                table: "redemptions",
                columns: new[] { "voucher_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "redemptions");
        }
    }
}
