using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AppendOnlyTableTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_append_only_modification()
                RETURNS TRIGGER AS $$
                BEGIN
                    RAISE EXCEPTION 'Updates and deletes are not allowed on append-only tables.';
                END;
                $$ LANGUAGE plpgsql;
            """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_audit_entries_append_only
                BEFORE UPDATE OR DELETE ON audit_entries
                FOR EACH ROW EXECUTE FUNCTION prevent_append_only_modification();
            """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_campaign_status_events_append_only
                BEFORE UPDATE OR DELETE ON campaign_status_events
                FOR EACH ROW EXECUTE FUNCTION prevent_append_only_modification();
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_entries_append_only ON audit_entries;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_campaign_status_events_append_only ON campaign_status_events;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_append_only_modification();");
        }
    }
}
