using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoucherCampaigns.Infrastructure.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class VoucherEventsAppendOnlyTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // voucher_events é IAppendOnly (issue #45) e ficou fora da AppendOnlyTableTriggers (GH-028).
            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS trg_voucher_events_no_update
                BEFORE UPDATE ON voucher_events
                BEGIN
                    SELECT RAISE(ABORT, 'Updates are not allowed on append-only tables.');
                END;
            """);

            migrationBuilder.Sql("""
                CREATE TRIGGER IF NOT EXISTS trg_voucher_events_no_delete
                BEFORE DELETE ON voucher_events
                BEGIN
                    SELECT RAISE(ABORT, 'Deletes are not allowed on append-only tables.');
                END;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_voucher_events_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_voucher_events_no_delete;");
        }
    }
}
