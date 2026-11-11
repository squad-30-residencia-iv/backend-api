using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class UserName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "users",
                type: "TEXT",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE users
                SET name = CASE email
                    WHEN 'analista@empresa.com' THEN 'Ana Lima'
                    WHEN 'gestor@empresa.com' THEN 'Bruno Costa'
                    WHEN 'admin@empresa.com' THEN 'Admin Plataforma'
                    ELSE email
                END
                WHERE name = '';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "users",
                type: "TEXT",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 150,
                oldNullable: false,
                oldDefaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "name",
                table: "users");
        }
    }
}
