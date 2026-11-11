using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HanaMedia.Migrations
{
    /// <inheritdoc />
    public partial class DirectDirectorContractReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [bookings]
                SET [contract_status] = CASE
                    WHEN [contract_status] = 'cho_phap_ly' AND NULLIF([contract_file_url], '') IS NOT NULL THEN 'cho_ky'
                    ELSE 'da_duyet' END
                WHERE [contract_status] IN ('cho_phap_ly', 'phap_ly_tu_choi');
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
