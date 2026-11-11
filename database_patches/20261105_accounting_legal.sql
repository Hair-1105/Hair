BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105171111_AccountingReceivablesAndLegalDocuments'
)
BEGIN
    ALTER TABLE [bookings] ADD [CompletedAt] datetime2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105171111_AccountingReceivablesAndLegalDocuments'
)
BEGIN
    CREATE TABLE [legal_documents] (
        [Id] int NOT NULL IDENTITY,
        [Kind] nvarchar(20) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [BookingId] int NULL,
        [EmployeeId] int NULL,
        [AttachmentName] nvarchar(100) NULL,
        [AttachmentOriginalName] nvarchar(255) NULL,
        [Revision] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NOT NULL,
        [UpdatedByUserId] int NOT NULL,
        [SentAt] datetime2 NULL,
        [SentByUserId] int NULL,
        CONSTRAINT [PK_legal_documents] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_legal_document_kind] CHECK (([Kind] = 'booking' AND [BookingId] IS NOT NULL AND [EmployeeId] IS NULL) OR ([Kind] = 'employment' AND [EmployeeId] IS NOT NULL AND [BookingId] IS NULL) OR ([Kind] = 'policy' AND [BookingId] IS NULL AND [EmployeeId] IS NULL)),
        CONSTRAINT [FK_legal_documents_bookings_BookingId] FOREIGN KEY ([BookingId]) REFERENCES [bookings] ([id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_legal_documents_employees_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [employees] ([id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105171111_AccountingReceivablesAndLegalDocuments'
)
BEGIN
    CREATE INDEX [IX_legal_documents_BookingId] ON [legal_documents] ([BookingId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105171111_AccountingReceivablesAndLegalDocuments'
)
BEGIN
    CREATE INDEX [IX_legal_documents_EmployeeId] ON [legal_documents] ([EmployeeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261105171111_AccountingReceivablesAndLegalDocuments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261105171111_AccountingReceivablesAndLegalDocuments', N'8.0.8');
END;
GO

COMMIT;
GO

