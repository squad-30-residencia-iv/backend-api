using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddVoucherMotorEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    action = table.Column<string>(type: "TEXT", nullable: false),
                    entity_name = table.Column<string>(type: "TEXT", nullable: false),
                    entity_id = table.Column<string>(type: "TEXT", nullable: false),
                    timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "CampaignConditions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    campaign_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    condition_type = table.Column<int>(type: "INTEGER", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false),
                    @operator = table.Column<int>(name: "operator", type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_conditions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "EligibilityCriteria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    campaign_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    criterion_type = table.Column<int>(type: "INTEGER", nullable: false),
                    value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_eligibility_criteria", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "Vouchers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    campaign_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    expiration_date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    usage_limit = table.Column<int>(type: "INTEGER", nullable: false),
                    current_usage_count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vouchers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "VoucherEvents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    voucher_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_type = table.Column<int>(type: "INTEGER", nullable: false),
                    timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "CampaignConditions");

            migrationBuilder.DropTable(
                name: "EligibilityCriteria");

            migrationBuilder.DropTable(
                name: "VoucherEvents");

            migrationBuilder.DropTable(
                name: "Vouchers");
        }
    }
}
