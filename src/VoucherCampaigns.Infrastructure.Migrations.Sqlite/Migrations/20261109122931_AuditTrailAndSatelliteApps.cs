using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AuditTrailAndSatelliteApps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    event_type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    actor_type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    actor_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    network_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    campaign_id = table.Column<Guid>(type: "TEXT", nullable: true),
                    entity_type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    entity_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    payload = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "satellite_apps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    client_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    secret_hash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    network_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    scopes = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_satellite_apps", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_campaign_id_occurred_at",
                table: "audit_entries",
                columns: new[] { "campaign_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_network_id_occurred_at",
                table: "audit_entries",
                columns: new[] { "network_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_satellite_apps_client_id",
                table: "satellite_apps",
                column: "client_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropTable(
                name: "satellite_apps");

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    action = table.Column<string>(type: "TEXT", nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: false),
                    entity_id = table.Column<string>(type: "TEXT", nullable: false),
                    entity_name = table.Column<string>(type: "TEXT", nullable: false),
                    timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entries", x => x.id);
                });
        }
    }
}
