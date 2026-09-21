using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingGuardianFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BookedForOther",
                table: "Bookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BookingNoticeVersion",
                table: "Bookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuardianConfirmationVersion",
                table: "Bookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GuardianConfirmedAtUtc",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookedForOther",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "BookingNoticeVersion",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "GuardianConfirmationVersion",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "GuardianConfirmedAtUtc",
                table: "Bookings");
        }
    }
}
