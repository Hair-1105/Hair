using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HanaMedia.Migrations
{
    /// <inheritdoc />
    public partial class AccountingReceivablesAndLegalDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "bookings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "legal_documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", maxLength: 100000, nullable: false),
                    BookingId = table.Column<int>(type: "int", nullable: true),
                    EmployeeId = table.Column<int>(type: "int", nullable: true),
                    AttachmentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AttachmentOriginalName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_documents", x => x.Id);
                    table.CheckConstraint("CK_legal_document_kind", "([Kind] = 'booking' AND [BookingId] IS NOT NULL AND [EmployeeId] IS NULL) OR ([Kind] = 'employment' AND [EmployeeId] IS NOT NULL AND [BookingId] IS NULL) OR ([Kind] = 'policy' AND [BookingId] IS NULL AND [EmployeeId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_legal_documents_bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_legal_documents_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_legal_documents_BookingId",
                table: "legal_documents",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_legal_documents_EmployeeId",
                table: "legal_documents",
                column: "EmployeeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "legal_documents");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "bookings");
        }
    }
}
