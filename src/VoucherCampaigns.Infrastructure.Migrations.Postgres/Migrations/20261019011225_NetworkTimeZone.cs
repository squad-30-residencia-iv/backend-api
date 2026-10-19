using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class NetworkTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Redes já cadastradas passam a usar o fuso de Brasília (US-01.1, decisão 4).
            migrationBuilder.AddColumn<string>(
                name: "time_zone",
                table: "networks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "America/Sao_Paulo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "time_zone",
                table: "networks");
        }
    }
}
