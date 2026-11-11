-- Standalone department repair; safe to rerun before/after EF migrations.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
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
COMMIT;
