/* Brew Haven - adds Users.ContactNumber for the Super Admin "Manage Users" dialog.
   Run once on an EXISTING database (SSMS or the MonsterASP SQL panel). Safe to run again.
   New databases created from BrewHaven_Schema.sql already have this column. */

IF COL_LENGTH('dbo.Users', 'ContactNumber') IS NULL
    ALTER TABLE dbo.Users ADD ContactNumber NVARCHAR(11) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Users_Contact')
    ALTER TABLE dbo.Users ADD CONSTRAINT CK_Users_Contact
        CHECK (ContactNumber IS NULL OR (LEN(ContactNumber) = 11 AND ContactNumber NOT LIKE '%[^0-9]%'));
GO
