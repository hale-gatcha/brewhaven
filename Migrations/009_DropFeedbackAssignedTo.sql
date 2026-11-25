/* =================================================================================================
   009_DropFeedbackAssignedTo.sql  —  run on EVERY tenant database. Safe to re-run.
   Removes the Feedback.AssignedTo column (and its foreign key): concerns are no longer assigned to a person;
   the Admin manages status and resolution directly. The same statements are in DbHelper.SchemaUpgradeSql.
   ================================================================================================= */
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Feedback_Assignee')
    ALTER TABLE dbo.Feedback DROP CONSTRAINT FK_Feedback_Assignee;
IF COL_LENGTH('dbo.Feedback', 'AssignedTo') IS NOT NULL
    ALTER TABLE dbo.Feedback DROP COLUMN AssignedTo;
