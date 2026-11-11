using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HanaMedia.Migrations
{
    /// <inheritdoc />
    public partial class StandaloneBookingAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BookingId",
                table: "ideas",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CustomerPaidAt",
                table: "bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerPaidByUserId",
                table: "bookings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AllocationAmount",
                table: "booking_kols",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_ideas_BookingId",
                table: "ideas",
                column: "BookingId");

            migrationBuilder.AddForeignKey(
                name: "FK_ideas_bookings_BookingId",
                table: "ideas",
                column: "BookingId",
                principalTable: "bookings",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ideas_bookings_BookingId",
                table: "ideas");

            migrationBuilder.DropIndex(
                name: "IX_ideas_BookingId",
                table: "ideas");

            migrationBuilder.DropColumn(
                name: "BookingId",
                table: "ideas");

            migrationBuilder.DropColumn(
                name: "CustomerPaidAt",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "CustomerPaidByUserId",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "AllocationAmount",
                table: "booking_kols");
        }
    }
}
