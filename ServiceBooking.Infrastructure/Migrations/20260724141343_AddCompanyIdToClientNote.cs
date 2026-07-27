using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyIdToClientNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Client notes are now company-scoped, but pre-existing rows have no company attribution
            // (a note only recorded its author, who may belong to several companies). They can't be
            // reliably backfilled, so clear them before adding the required CompanyId + FK. Safe for a
            // dev database; in production this migration would be paired with a real backfill instead.
            migrationBuilder.Sql("DELETE FROM \"ClientNotes\";");

            migrationBuilder.AddColumn<Guid>(
                name: "CompanyId",
                table: "ClientNotes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_ClientNotes_CompanyId_ClientId",
                table: "ClientNotes",
                columns: new[] { "CompanyId", "ClientId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientNotes_CompanyId_GuestPhone",
                table: "ClientNotes",
                columns: new[] { "CompanyId", "GuestPhone" });

            migrationBuilder.AddForeignKey(
                name: "FK_ClientNotes_Companies_CompanyId",
                table: "ClientNotes",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientNotes_Companies_CompanyId",
                table: "ClientNotes");

            migrationBuilder.DropIndex(
                name: "IX_ClientNotes_CompanyId_ClientId",
                table: "ClientNotes");

            migrationBuilder.DropIndex(
                name: "IX_ClientNotes_CompanyId_GuestPhone",
                table: "ClientNotes");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "ClientNotes");
        }
    }
}
