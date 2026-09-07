using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClientNoteBookingId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BookingId",
                table: "ClientNotes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientNotes_BookingId",
                table: "ClientNotes",
                column: "BookingId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientNotes_Bookings_BookingId",
                table: "ClientNotes",
                column: "BookingId",
                principalTable: "Bookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientNotes_Bookings_BookingId",
                table: "ClientNotes");

            migrationBuilder.DropIndex(
                name: "IX_ClientNotes_BookingId",
                table: "ClientNotes");

            migrationBuilder.DropColumn(
                name: "BookingId",
                table: "ClientNotes");
        }
    }
}
