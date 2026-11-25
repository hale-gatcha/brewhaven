using Microsoft.Data.SqlClient;
using System.Configuration;
using System.Data;

namespace BrewHaven.DataAccess;

public static class DbHelper
{
    /// <summary>MasterCRM: the directory of shops (tenants). The only connection string in App.config.</summary>
    public static string MasterConnectionString
    {
        get
        {
            var setting = ConfigurationManager.ConnectionStrings["MasterCRM"];
            if (setting is null || string.IsNullOrWhiteSpace(setting.ConnectionString))
                throw new InvalidOperationException("The MasterCRM connection string is missing from App.config.");
            return setting.ConnectionString;
        }
    }

    public static SqlConnection CreateMasterConnection() => new(MasterConnectionString);

    /// <summary>The shop picked on the login screen. Every query below runs against this shop's database.</summary>
    public static Tenant? CurrentTenant { get; private set; }

    /// <summary>Switches all data access to <paramref name="tenant"/>'s database (null = none selected).</summary>
    public static void UseTenant(Tenant? tenant)
    {
        CurrentTenant = tenant;
        schemaChecked = false;   // each tenant database gets its own schema check
    }

    public static string ConnectionString =>
        CurrentTenant?.ConnectionString ?? throw new InvalidOperationException("No shop is selected. Log in again.");

    public static SqlConnection CreateConnection() => new(ConnectionString);

    // ---- Small helpers for parameterized queries --------------------------------------------------

    /// <summary>Creates a typed parameter. Pass null for DBNull.</summary>
    public static SqlParameter P(string name, SqlDbType type, object? value, int size = 0)
    {
        var p = new SqlParameter(name, type) { Value = value ?? DBNull.Value };
        if (size != 0) p.Size = size;
        if (type == SqlDbType.Decimal) { p.Precision = 12; p.Scale = 2; }
        return p;
    }

    /// <summary>Runs a SELECT and returns the rows.</summary>
    public static DataTable Query(string sql, params SqlParameter[] parameters)
    {
        using SqlConnection connection = CreateConnection();
        using SqlCommand command = new(sql, connection);
        command.Parameters.AddRange(parameters);
        connection.Open();
        using SqlDataReader reader = command.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    /// <summary>Runs a SELECT inside an open transaction and returns the rows.</summary>
    public static DataTable Query(SqlTransaction tx, string sql, params SqlParameter[] parameters)
    {
        using SqlCommand command = new(sql, tx.Connection, tx);
        command.Parameters.AddRange(parameters);
        using SqlDataReader reader = command.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    /// <summary>Runs a query and returns the first column of the first row (or null).</summary>
    public static object? Scalar(string sql, params SqlParameter[] parameters)
    {
        using SqlConnection connection = CreateConnection();
        using SqlCommand command = new(sql, connection);
        command.Parameters.AddRange(parameters);
        connection.Open();
        object? value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    public static object? Scalar(SqlTransaction tx, string sql, params SqlParameter[] parameters)
    {
        using SqlCommand command = new(sql, tx.Connection, tx);
        command.Parameters.AddRange(parameters);
        object? value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }

    /// <summary>Runs an INSERT / UPDATE / DELETE and returns the number of rows affected.</summary>
    public static int Execute(string sql, params SqlParameter[] parameters)
    {
        using SqlConnection connection = CreateConnection();
        using SqlCommand command = new(sql, connection);
        command.Parameters.AddRange(parameters);
        connection.Open();
        return command.ExecuteNonQuery();
    }

    public static int Execute(SqlTransaction tx, string sql, params SqlParameter[] parameters)
    {
        using SqlCommand command = new(sql, tx.Connection, tx);
        command.Parameters.AddRange(parameters);
        return command.ExecuteNonQuery();
    }

    // ---- Schema upgrades -------------------------------------------------------------------------

    /// <summary>
    /// Every column/table the newer screens rely on, added only when missing. The same script is in
    /// Migrations/003_ManagerStaff.sql. EXEC(...) is used so SQL Server doesn't reject the batch for
    /// referencing columns that don't exist yet.
    /// </summary>
    public const string SchemaUpgradeSql = """
        -- Users.ContactNumber (Super Admin / Admin: Manage Users)
        IF COL_LENGTH('dbo.Users', 'ContactNumber') IS NULL
            EXEC('ALTER TABLE dbo.Users ADD ContactNumber NVARCHAR(11) NULL');
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Users_Contact')
            EXEC('ALTER TABLE dbo.Users ADD CONSTRAINT CK_Users_Contact CHECK (ContactNumber IS NULL OR (LEN(ContactNumber) = 11 AND ContactNumber NOT LIKE ''%[^0-9]%''))');

        -- Branches.ManagerUserId (Admin: Manage Branches)
        IF COL_LENGTH('dbo.Branches', 'ManagerUserId') IS NULL
            EXEC('ALTER TABLE dbo.Branches ADD ManagerUserId INT NULL');
        IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Branches_Manager')
            EXEC('ALTER TABLE dbo.Branches ADD CONSTRAINT FK_Branches_Manager FOREIGN KEY (ManagerUserId) REFERENCES dbo.Users(UserId)');

        -- Orders: 'Paid' status, table number, promotion applied
        IF COL_LENGTH('dbo.Orders', 'TableNumber') IS NULL
            EXEC('ALTER TABLE dbo.Orders ADD TableNumber INT NULL');
        IF COL_LENGTH('dbo.Orders', 'PromotionId') IS NULL
            EXEC('ALTER TABLE dbo.Orders ADD PromotionId INT NULL CONSTRAINT FK_Orders_Promotion REFERENCES dbo.Promotions(PromotionId)');
        IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Orders_Status' AND definition NOT LIKE '%Paid%')
        BEGIN
            EXEC('ALTER TABLE dbo.Orders DROP CONSTRAINT CK_Orders_Status');
            EXEC('ALTER TABLE dbo.Orders ADD CONSTRAINT CK_Orders_Status CHECK (Status IN (''Pending'', ''Paid'', ''Completed'', ''Cancelled''))');
        END

        -- OrderItems.Size (Regular / Large drinks)
        IF COL_LENGTH('dbo.OrderItems', 'Size') IS NULL
            EXEC('ALTER TABLE dbo.OrderItems ADD Size NVARCHAR(10) NULL');

        -- Payments: card / e-wallet details and cashier
        IF COL_LENGTH('dbo.Payments', 'CardType') IS NULL
            EXEC('ALTER TABLE dbo.Payments ADD CardType NVARCHAR(20) NULL, CardLast4 CHAR(4) NULL, Provider NVARCHAR(20) NULL,
                  ReferenceNumber NVARCHAR(50) NULL, ReceivedByUserId INT NULL');

        -- Promotions: discount type/value, points required, applicable branch
        IF COL_LENGTH('dbo.Promotions', 'DiscountType') IS NULL
            EXEC('ALTER TABLE dbo.Promotions ADD DiscountType NVARCHAR(10) NOT NULL CONSTRAINT DF_Promotions_DiscountType DEFAULT ''Percentage''
                  CONSTRAINT CK_Promotions_DiscountType CHECK (DiscountType IN (''Percentage'', ''Fixed''))');
        IF COL_LENGTH('dbo.Promotions', 'DiscountValue') IS NULL
        BEGIN
            EXEC('ALTER TABLE dbo.Promotions ADD DiscountValue DECIMAL(12,2) NULL');
            EXEC('UPDATE dbo.Promotions SET DiscountValue = DiscountPercent');
        END
        EXEC('UPDATE dbo.Promotions SET DiscountValue = DiscountPercent WHERE DiscountValue IS NULL');
        IF COL_LENGTH('dbo.Promotions', 'PointsRequired') IS NULL
            EXEC('ALTER TABLE dbo.Promotions ADD PointsRequired INT NULL');
        IF COL_LENGTH('dbo.Promotions', 'BranchId') IS NULL
            EXEC('ALTER TABLE dbo.Promotions ADD BranchId INT NULL CONSTRAINT FK_Promotions_Branch REFERENCES dbo.Branches(BranchId)');

        -- Inventory: unit, unit cost, supplier
        IF COL_LENGTH('dbo.Inventory', 'Unit') IS NULL
            EXEC('ALTER TABLE dbo.Inventory ADD Unit NVARCHAR(20) NULL, UnitCost DECIMAL(12,2) NULL, Supplier NVARCHAR(100) NULL');

        -- StockMovements: every Stock In / Stock Out (manual or from a sale)
        IF OBJECT_ID('dbo.StockMovements', 'U') IS NULL
            EXEC('CREATE TABLE dbo.StockMovements(
                    MovementId INT IDENTITY PRIMARY KEY,
                    InventoryId INT NOT NULL CONSTRAINT FK_StockMovements_Inventory REFERENCES dbo.Inventory(InventoryId) ON DELETE CASCADE,
                    MovementType NVARCHAR(3) NOT NULL CONSTRAINT CK_StockMovements_Type CHECK (MovementType IN (''In'', ''Out'')),
                    Quantity INT NOT NULL CONSTRAINT CK_StockMovements_Qty CHECK (Quantity > 0),
                    Reason NVARCHAR(200) NOT NULL,
                    MovementDate DATE NOT NULL,
                    UserId INT NULL CONSTRAINT FK_StockMovements_User REFERENCES dbo.Users(UserId),
                    OrderId INT NULL,
                    CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_StockMovements_CreatedAt DEFAULT SYSUTCDATETIME())');

        -- Customers: code, birthdate, notes, registration date; email unique only when filled in
        IF COL_LENGTH('dbo.Customers', 'CustomerCode') IS NULL
            EXEC('ALTER TABLE dbo.Customers ADD CustomerCode NVARCHAR(20) NULL, Birthdate DATE NULL, Notes NVARCHAR(500) NULL,
                  CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME()');
        EXEC('UPDATE dbo.Customers SET CustomerCode = ''BH-'' + RIGHT(''00000'' + CAST(CustomerId AS varchar(10)), 5) WHERE CustomerCode IS NULL');
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Customers_Code')
            EXEC('CREATE UNIQUE INDEX UX_Customers_Code ON dbo.Customers(CustomerCode) WHERE CustomerCode IS NOT NULL');
        IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_Customers_Email')
            EXEC('ALTER TABLE dbo.Customers DROP CONSTRAINT UQ_Customers_Email');
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Customers_Email')
            EXEC('CREATE UNIQUE INDEX UX_Customers_Email ON dbo.Customers(Email) WHERE Email IS NOT NULL');

        -- RewardRedemptions: redemption code and who processed it
        IF COL_LENGTH('dbo.RewardRedemptions', 'RedemptionCode') IS NULL
            EXEC('ALTER TABLE dbo.RewardRedemptions ADD RedemptionCode NVARCHAR(20) NULL, ProcessedByUserId INT NULL');

        -- Feedback: Customer Concerns module (same as Migrations/007_CustomerConcerns.sql)
        IF COL_LENGTH('dbo.Feedback', 'Category') IS NULL
            EXEC('ALTER TABLE dbo.Feedback ADD Category NVARCHAR(30) NOT NULL CONSTRAINT DF_Feedback_Category DEFAULT N''Other''');
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Feedback_Category')
            EXEC('ALTER TABLE dbo.Feedback ADD CONSTRAINT CK_Feedback_Category CHECK (Category IN
                  (N''Service'', N''Product Quality'', N''Cleanliness'', N''Price'', N''Wait Time'', N''Ambiance'', N''Staff Behavior'', N''Other''))');
        IF COL_LENGTH('dbo.Feedback', 'SubmittedVia') IS NULL
            EXEC('ALTER TABLE dbo.Feedback ADD SubmittedVia NVARCHAR(20) NOT NULL CONSTRAINT DF_Feedback_SubmittedVia DEFAULT N''Walk-in''');
        IF COL_LENGTH('dbo.Feedback', 'GuestName') IS NULL
            EXEC('ALTER TABLE dbo.Feedback ADD GuestName NVARCHAR(100) NULL, GuestContact NVARCHAR(11) NULL');
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Feedback_GuestContact')
            EXEC('ALTER TABLE dbo.Feedback ADD CONSTRAINT CK_Feedback_GuestContact CHECK
                  (GuestContact IS NULL OR (LEN(GuestContact) = 11 AND GuestContact NOT LIKE ''%[^0-9]%''))');
        IF COL_LENGTH('dbo.Feedback', 'InternalNotes') IS NULL
            EXEC('ALTER TABLE dbo.Feedback ADD InternalNotes NVARCHAR(MAX) NULL');
        IF COL_LENGTH('dbo.Feedback', 'UpdatedBy') IS NULL
            EXEC('ALTER TABLE dbo.Feedback ADD UpdatedBy INT NULL CONSTRAINT FK_Feedback_UpdatedBy REFERENCES dbo.Users(UserId), UpdatedAt DATETIME2 NULL');
        IF COL_LENGTH('dbo.Feedback', 'IsArchived') IS NULL
            EXEC('ALTER TABLE dbo.Feedback ADD IsArchived BIT NOT NULL CONSTRAINT DF_Feedback_IsArchived DEFAULT 0');
        IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Feedback_RatingOnlyFeedback')
        BEGIN
            EXEC('UPDATE dbo.Feedback SET Rating = NULL WHERE Type <> N''Feedback'' AND Rating IS NOT NULL');
            EXEC('ALTER TABLE dbo.Feedback ADD CONSTRAINT CK_Feedback_RatingOnlyFeedback CHECK (Type = N''Feedback'' OR Rating IS NULL)');
        END
        -- Feedback.AssignedTo removed (same as Migrations/009_DropFeedbackAssignedTo.sql)
        IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Feedback_Assignee')
            EXEC('ALTER TABLE dbo.Feedback DROP CONSTRAINT FK_Feedback_Assignee');
        IF COL_LENGTH('dbo.Feedback', 'AssignedTo') IS NOT NULL
            EXEC('ALTER TABLE dbo.Feedback DROP COLUMN AssignedTo');
        IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Feedback_Branch_Date')
            EXEC('CREATE INDEX IX_Feedback_Branch_Date ON dbo.Feedback(BranchId, DateSubmitted)
                  INCLUDE (Type, Category, Status, Rating, IsArchived, DateResolved)');
        """;

    private static bool schemaChecked;

    /// <summary>
    /// Applies <see cref="SchemaUpgradeSql"/> once per app run. Safe to call repeatedly; it only changes
    /// the database when something is missing.
    /// </summary>
    public static void EnsureSchema()
    {
        if (schemaChecked) return;
        Execute(SchemaUpgradeSql);
        schemaChecked = true;
    }

    /// <summary>Kept for the pages written before EnsureSchema covered everything.</summary>
    public static void EnsureUserContactColumn() => EnsureSchema();

    public static void TestConnection()
    {
        using SqlConnection connection = CreateConnection();
        connection.Open();
    }
}
