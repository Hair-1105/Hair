using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HanaMedia.Migrations
{
    /// <inheritdoc />
    public partial class RepairLegalAccountingDepartments : Migration
    {
        // Earlier model snapshots already listed six departments, but no migration
        // updated the original four-department database constraint.
        public const string RepairSql = """
            IF EXISTS (SELECT 1 FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID(N'dbo.employees') AND name = N'chk_emp_dept')
                ALTER TABLE [dbo].[employees] DROP CONSTRAINT [chk_emp_dept];

            ALTER TABLE [dbo].[employees] WITH CHECK ADD CONSTRAINT [chk_emp_dept]
                CHECK ([department] IN ('HCNS', 'Booking', 'Y_tuong', 'IT', 'Phap_Ly', 'Ke_Toan'));

            IF NOT EXISTS (SELECT 1 FROM [dbo].[departments] WHERE UPPER([code]) = 'PHAP_LY')
                INSERT INTO [dbo].[departments] ([code], [name], [status])
                VALUES (N'Phap_Ly', N'Pháp chế', N'active');

            IF NOT EXISTS (SELECT 1 FROM [dbo].[departments] WHERE UPPER([code]) = 'KE_TOAN')
                INSERT INTO [dbo].[departments] ([code], [name], [status])
                VALUES (N'Ke_Toan', N'Kế toán', N'active');
            """;
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RepairSql);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data repair: retain departments and employees on downgrade. The prior
            // EF model also accepts all six codes; restoring four would reject data.

        }
    }
}
