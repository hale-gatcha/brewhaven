/* =================================================================================================
   007_CustomerConcerns.sql  —  run on EVERY tenant database (TenantA, TenantB, TenantC, ...).
   Safe to re-run: each change is applied only when it is missing.
   The same statements are in DbHelper.SchemaUpgradeSql, so the app also applies them on login.

   Column mapping for the Customer Concerns module (Feedback table):
     LoggedBy      -> existing SubmittedByUserId (FK to Users, NOT NULL): the Staff user who logged it
     AssignedTo    -> not used (removed in 009_DropFeedbackAssignedTo.sql)
     ResolutionNotes, DateResolved -> existing
     NEW: Category, SubmittedVia, GuestName, GuestContact, InternalNotes, UpdatedBy, UpdatedAt, IsArchived
   ================================================================================================= */

-- Category (allowed list), defaulting to 'Other' for existing rows
IF COL_LENGTH('dbo.Feedback', 'Category') IS NULL
    EXEC('ALTER TABLE dbo.Feedback ADD Category NVARCHAR(30) NOT NULL CONSTRAINT DF_Feedback_Category DEFAULT N''Other''');
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Feedback_Category')
    EXEC('ALTER TABLE dbo.Feedback ADD CONSTRAINT CK_Feedback_Category CHECK (Category IN
          (N''Service'', N''Product Quality'', N''Cleanliness'', N''Price'', N''Wait Time'', N''Ambiance'', N''Staff Behavior'', N''Other''))');

-- How the concern reached us (walk-in system: always Walk-in for now)
IF COL_LENGTH('dbo.Feedback', 'SubmittedVia') IS NULL
    EXEC('ALTER TABLE dbo.Feedback ADD SubmittedVia NVARCHAR(20) NOT NULL CONSTRAINT DF_Feedback_SubmittedVia DEFAULT N''Walk-in''');

-- Walk-in guest (no customer record): optional name and 11-digit contact number
IF COL_LENGTH('dbo.Feedback', 'GuestName') IS NULL
    EXEC('ALTER TABLE dbo.Feedback ADD GuestName NVARCHAR(100) NULL, GuestContact NVARCHAR(11) NULL');
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Feedback_GuestContact')
    EXEC('ALTER TABLE dbo.Feedback ADD CONSTRAINT CK_Feedback_GuestContact CHECK
          (GuestContact IS NULL OR (LEN(GuestContact) = 11 AND GuestContact NOT LIKE ''%[^0-9]%''))');

-- Admin management fields
IF COL_LENGTH('dbo.Feedback', 'InternalNotes') IS NULL
    EXEC('ALTER TABLE dbo.Feedback ADD InternalNotes NVARCHAR(MAX) NULL');
IF COL_LENGTH('dbo.Feedback', 'UpdatedBy') IS NULL
    EXEC('ALTER TABLE dbo.Feedback ADD UpdatedBy INT NULL CONSTRAINT FK_Feedback_UpdatedBy REFERENCES dbo.Users(UserId), UpdatedAt DATETIME2 NULL');

-- Soft delete: "Remove" archives the row so reports stay accurate
IF COL_LENGTH('dbo.Feedback', 'IsArchived') IS NULL
    EXEC('ALTER TABLE dbo.Feedback ADD IsArchived BIT NOT NULL CONSTRAINT DF_Feedback_IsArchived DEFAULT 0');

-- Rating 1-5 only for Feedback (CK_Feedback_Rating already limits 1-5; CK_Feedback_FeedbackRating requires it for Feedback)
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Feedback_RatingOnlyFeedback')
BEGIN
    EXEC('UPDATE dbo.Feedback SET Rating = NULL WHERE Type <> N''Feedback'' AND Rating IS NOT NULL');
    EXEC('ALTER TABLE dbo.Feedback ADD CONSTRAINT CK_Feedback_RatingOnlyFeedback CHECK (Type = N''Feedback'' OR Rating IS NULL)');
END

-- Speeds up the Manager monitoring queries (branch + date range)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Feedback_Branch_Date')
    EXEC('CREATE INDEX IX_Feedback_Branch_Date ON dbo.Feedback(BranchId, DateSubmitted)
          INCLUDE (Type, Category, Status, Rating, IsArchived, DateResolved)');
