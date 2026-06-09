using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DateBasedSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DayOfWeek",
                table: "WorkingHours");

            migrationBuilder.AddColumn<DateOnly>(
                name: "Date",
                table: "WorkingHours",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Date",
                table: "WorkingHours");

            migrationBuilder.AddColumn<int>(
                name: "DayOfWeek",
                table: "WorkingHours",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
