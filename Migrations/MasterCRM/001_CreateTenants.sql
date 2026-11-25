-- MasterCRM: directory of shops (tenants) and the database each one uses.
-- Run against the MasterCRM database only. Tenant databases use BrewHaven_Schema.sql + Migrations/.
IF OBJECT_ID('dbo.Tenants', 'U') IS NULL
CREATE TABLE dbo.Tenants(
    TenantId     INT IDENTITY PRIMARY KEY,
    TenantCode   NVARCHAR(20)  NOT NULL CONSTRAINT UQ_Tenants_Code UNIQUE,
    ShopName     NVARCHAR(100) NOT NULL,
    ServerName   NVARCHAR(200) NOT NULL,
    DatabaseName NVARCHAR(128) NOT NULL,
    DbUser       NVARCHAR(128) NOT NULL,
    DbPassword   NVARCHAR(256) NOT NULL,
    IsActive     BIT NOT NULL CONSTRAINT DF_Tenants_IsActive DEFAULT 1,
    CreatedAt    DATETIME2 NOT NULL CONSTRAINT DF_Tenants_CreatedAt DEFAULT SYSUTCDATETIME()
);
GO
-- Example row (fill in real values for each tenant database):
-- INSERT INTO dbo.Tenants(TenantCode, ShopName, ServerName, DatabaseName, DbUser, DbPassword)
-- VALUES (N'TENANTA', N'Tenant A', N'db00000.public.databaseasp.net', N'db00000', N'db00000', N'password');
