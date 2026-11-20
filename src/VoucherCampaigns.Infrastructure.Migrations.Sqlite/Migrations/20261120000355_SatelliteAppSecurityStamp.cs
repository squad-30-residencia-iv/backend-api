using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Sqlite.Migrations
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
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Aplicações existentes ganham um carimbo próprio; os tokens emitidos antes, sem carimbo, deixam de valer (GH-071, D-1).
            migrationBuilder.Sql("UPDATE satellite_apps SET security_stamp = lower(hex(randomblob(16)));");
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
