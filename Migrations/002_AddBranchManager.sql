/* Brew Haven - adds Branches.ManagerUserId for the Admin "Manage Branches" dialog.
   The app also adds this automatically the first time it opens a page that needs it.
   Run once on an EXISTING database (SSMS or the MonsterASP SQL panel). Safe to run again.
   No GO lines, so it can be pasted into web SQL panels as one block. */

IF COL_LENGTH('dbo.Branches', 'ManagerUserId') IS NULL
    EXEC('ALTER TABLE dbo.Branches ADD ManagerUserId INT NULL');

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Branches_Manager')
    EXEC('ALTER TABLE dbo.Branches ADD CONSTRAINT FK_Branches_Manager FOREIGN KEY (ManagerUserId) REFERENCES dbo.Users(UserId)');
