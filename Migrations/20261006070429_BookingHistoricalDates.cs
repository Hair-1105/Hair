using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HanaMedia.Migrations
{
    /// <inheritdoc />
    public partial class BookingHistoricalDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CustomerPaymentDate",
                table: "bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevenueDate",
                table: "bookings",
                type: "datetime2",
                nullable: true);
            // Preserve existing reporting dates; do not invent historical business dates.
            migrationBuilder.Sql("UPDATE [bookings] SET [RevenueDate] = CAST([created_at] AS date), [CustomerPaymentDate] = CAST([CustomerPaidAt] AS date);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerPaymentDate",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "RevenueDate",
                table: "bookings");
        }
    }
}
