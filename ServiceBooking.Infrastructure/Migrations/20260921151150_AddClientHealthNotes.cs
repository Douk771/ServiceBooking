using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClientHealthNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientHealthNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<string>(type: "text", nullable: true),
                    GuestPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Ciphertext = table.Column<string>(type: "text", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientHealthNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientHealthNotes_AspNetUsers_ClientId",
                        column: x => x.ClientId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClientHealthNotes_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ClientHealthNotes_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientHealthNotes_ClientId",
                table: "ClientHealthNotes",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientHealthNotes_CompanyId_ClientId",
                table: "ClientHealthNotes",
                columns: new[] { "CompanyId", "ClientId" },
                unique: true,
                filter: "\"ClientId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClientHealthNotes_CompanyId_GuestPhone",
                table: "ClientHealthNotes",
                columns: new[] { "CompanyId", "GuestPhone" },
                unique: true,
                filter: "\"GuestPhone\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClientHealthNotes_UpdatedByUserId",
                table: "ClientHealthNotes",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientHealthNotes");
        }
    }
}
