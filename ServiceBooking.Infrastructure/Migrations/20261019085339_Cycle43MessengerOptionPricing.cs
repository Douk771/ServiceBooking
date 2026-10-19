using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Cycle43MessengerOptionPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "ChannelOptionChangeLogs",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Months",
                table: "ChannelOptionChangeLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricePerMonth",
                table: "ChannelOptionChangeLogs",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionOptionPriceChangeLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OptionCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OldPricePerMonth = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    NewPricePerMonth = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    OldIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    NewIsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    ActivePaidAccounts = table.Column<int>(type: "integer", nullable: true),
                    ChangedByUserId = table.Column<string>(type: "text", nullable: true),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionOptionPriceChangeLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionOptionPriceChangeLogs_SubscriptionOptions_Optio~",
                        column: x => x.OptionId,
                        principalTable: "SubscriptionOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionOptionPriceChangeLogs_OptionId_ChangedAtUtc",
                table: "SubscriptionOptionPriceChangeLogs",
                columns: new[] { "OptionId", "ChangedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionOptionPriceChangeLogs");

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "ChannelOptionChangeLogs");

            migrationBuilder.DropColumn(
                name: "Months",
                table: "ChannelOptionChangeLogs");

            migrationBuilder.DropColumn(
                name: "PricePerMonth",
                table: "ChannelOptionChangeLogs");
        }
    }
}
