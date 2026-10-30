using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class CampaignConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "per_customer_voucher_limit",
                table: "campaigns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_campaigns_per_customer_voucher_limit",
                table: "campaigns",
                sql: "per_customer_voucher_limit IS NULL OR per_customer_voucher_limit > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_campaigns_per_customer_voucher_limit",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "per_customer_voucher_limit",
                table: "campaigns");
        }
    }
}
