using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class CampaignTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "campaign_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    network_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    normalized_name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_campaign_tags_networks_network_id",
                        column: x => x.network_id,
                        principalTable: "networks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "campaign_tag_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    campaign_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    tag_id = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_campaign_tag_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_campaign_tag_links_campaign_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "campaign_tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_campaign_tag_links_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_campaign_tag_links_campaign_id_tag_id",
                table: "campaign_tag_links",
                columns: new[] { "campaign_id", "tag_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_campaign_tag_links_tag_id",
                table: "campaign_tag_links",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaign_tags_network_id_normalized_name",
                table: "campaign_tags",
                columns: new[] { "network_id", "normalized_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campaign_tag_links");

            migrationBuilder.DropTable(
                name: "campaign_tags");
        }
    }
}
