using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class SatelliteAppSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "security_stamp",
                table: "satellite_apps",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Aplicações existentes ganham um carimbo próprio; os tokens emitidos antes, sem carimbo, deixam de valer (GH-071, D-1).
            migrationBuilder.Sql("UPDATE satellite_apps SET security_stamp = md5(random()::text || id::text);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "security_stamp",
                table: "satellite_apps");
        }
    }
}
