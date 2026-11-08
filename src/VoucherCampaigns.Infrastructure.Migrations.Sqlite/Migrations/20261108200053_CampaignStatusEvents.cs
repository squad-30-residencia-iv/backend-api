using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class CampaignStatusEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "decided_at",
                table: "campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by_user_id",
                table: "campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                table: "campaigns",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "submitted_at",
                table: "campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "submitted_by_user_id",
                table: "campaigns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "campaign_status_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    campaign_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    from_status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    to_status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_status_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_campaign_status_events_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_campaign_status_events_campaign_id",
                table: "campaign_status_events",
                column: "campaign_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campaign_status_events");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "decided_by_user_id",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "campaigns");

            migrationBuilder.DropColumn(
                name: "submitted_by_user_id",
                table: "campaigns");
        }
    }
}
