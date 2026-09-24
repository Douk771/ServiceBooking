using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <summary>
    /// Proход B, US-125/US-120 (ARCHITECTURE_CYCLE9.md §104.5/§114.3). One column, <c>defaultValue: 0</c>
    /// (<see cref="Core.Enums.NotificationTransport.WhatsApp"/>) — every row written before this cycle
    /// was, by construction, a WhatsApp attempt, so the default alone is already correct data with no
    /// backfill statement needed. Additive and fully reversible.
    /// </summary>
    public partial class AddTransportToOutboundNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Transport",
                table: "OutboundNotifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Transport",
                table: "OutboundNotifications");
        }
    }
}
