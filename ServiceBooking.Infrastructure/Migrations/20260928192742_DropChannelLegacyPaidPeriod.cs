using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <summary>
    /// Cycle 22 (ARCHITECTURE_CYCLE22.md §379, SPEC Р2): NotificationChannels.PaidFromUtc/PaidUntilUtc were
    /// not written since cycle 7 (the paid period lives on the account's notifications.whatsapp option /
    /// subscription, read through ChannelFundingReader). Down() restores both as nullable timestamps
    /// (the data itself is not restorable — it was never meaningful).
    /// </summary>
    public partial class DropChannelLegacyPaidPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaidFromUtc",
                table: "NotificationChannels");

            migrationBuilder.DropColumn(
                name: "PaidUntilUtc",
                table: "NotificationChannels");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PaidFromUtc",
                table: "NotificationChannels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidUntilUtc",
                table: "NotificationChannels",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
