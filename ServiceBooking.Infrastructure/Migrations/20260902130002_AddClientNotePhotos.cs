using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClientNotePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientNotePhotos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientNoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompanyId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: false),
                    ThumbnailPath = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientNotePhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientNotePhotos_AspNetUsers_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ClientNotePhotos_ClientNotes_ClientNoteId",
                        column: x => x.ClientNoteId,
                        principalTable: "ClientNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientNotePhotos_ClientNoteId",
                table: "ClientNotePhotos",
                column: "ClientNoteId");

            // Idempotent re-upload (ARCHITECTURE.md §6.3): a double form submission / network retry for
            // the same processed bytes on the same note hits this unique index instead of creating a
            // duplicate row and double-charging the company's quota.
            migrationBuilder.CreateIndex(
                name: "IX_ClientNotePhotos_ClientNoteId_ContentHash",
                table: "ClientNotePhotos",
                columns: new[] { "ClientNoteId", "ContentHash" },
                unique: true);

            // Covers both the quota sum (SUM(SizeBytes) WHERE CompanyId = @id) and the retention cleanup
            // scan (WHERE CompanyId = ANY(@ids) AND CreatedAt < @cutoff) — ARCHITECTURE.md §6.1, §7.2.
            migrationBuilder.CreateIndex(
                name: "IX_ClientNotePhotos_CompanyId_CreatedAt",
                table: "ClientNotePhotos",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientNotePhotos_UploadedByUserId",
                table: "ClientNotePhotos",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientNotePhotos");
        }
    }
}
