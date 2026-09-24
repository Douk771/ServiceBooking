using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyAddressVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "AddressLatitude",
                table: "Companies",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AddressLongitude",
                table: "Companies",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AddressPrecision",
                table: "Companies",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AddressVerifiedAt",
                table: "Companies",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressVerifiedInputKey",
                table: "Companies",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressLatitude",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AddressLongitude",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AddressPrecision",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AddressVerifiedAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "AddressVerifiedInputKey",
                table: "Companies");
        }
    }
}
