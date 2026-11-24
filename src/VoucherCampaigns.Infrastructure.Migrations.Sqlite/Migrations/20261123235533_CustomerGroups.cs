using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class CustomerGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_groups",
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
                    table.PrimaryKey("pk_customer_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_groups_networks_network_id",
                        column: x => x.network_id,
                        principalTable: "networks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "campaign_customer_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    campaign_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    group_id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_customer_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_campaign_customer_groups_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_campaign_customer_groups_customer_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "customer_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_group_members",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    document = table.Column<string>(type: "TEXT", maxLength: 11, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_group_members", x => new { x.group_id, x.document });
                    table.ForeignKey(
                        name: "fk_customer_group_members_customer_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "customer_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_campaign_customer_groups_campaign_id_group_id",
                table: "campaign_customer_groups",
                columns: new[] { "campaign_id", "group_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_campaign_customer_groups_group_id",
                table: "campaign_customer_groups",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_customer_groups_network_id_name",
                table: "customer_groups",
                columns: new[] { "network_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campaign_customer_groups");

            migrationBuilder.DropTable(
                name: "customer_group_members");

            migrationBuilder.DropTable(
                name: "customer_groups");
        }
    }
}
