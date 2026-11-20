using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class VoucherEventsAppendOnlyTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // voucher_events é IAppendOnly (issue #45) e ficou fora da AppendOnlyTableTriggers (GH-028).
            // A função prevent_append_only_modification() já existe desde aquela migração.
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_voucher_events_append_only
                BEFORE UPDATE OR DELETE ON voucher_events
                FOR EACH ROW EXECUTE FUNCTION prevent_append_only_modification();
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_voucher_events_append_only ON voucher_events;");
        }
    }
}
