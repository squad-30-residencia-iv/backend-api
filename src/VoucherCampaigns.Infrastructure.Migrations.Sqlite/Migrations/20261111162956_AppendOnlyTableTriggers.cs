using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AppendOnlyTableTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS trg_audit_entries_no_update
                BEFORE UPDATE ON audit_entries
                BEGIN
                    SELECT RAISE(ABORT, 'Updates are not allowed on append-only tables.');
                END;
            """);

            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS trg_audit_entries_no_delete
                BEFORE DELETE ON audit_entries
                BEGIN
                    SELECT RAISE(ABORT, 'Deletes are not allowed on append-only tables.');
                END;
            """);

            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS trg_campaign_status_events_no_update
                BEFORE UPDATE ON campaign_status_events
                BEGIN
                    SELECT RAISE(ABORT, 'Updates are not allowed on append-only tables.');
                END;
            """);

            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS trg_campaign_status_events_no_delete
                BEFORE DELETE ON campaign_status_events
                BEGIN
                    SELECT RAISE(ABORT, 'Deletes are not allowed on append-only tables.');
                END;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_entries_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_entries_no_delete;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_campaign_status_events_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_campaign_status_events_no_delete;");
        }
    }
}
