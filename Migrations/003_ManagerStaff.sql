/* Brew Haven - schema for the Manager and Staff pages (also includes the earlier Super Admin / Admin columns).
   The app runs this same script automatically when the dashboard opens, so you only need to paste it if that
   fails (e.g. a permissions error). Safe to run more than once. No GO lines: paste it as one block.
   Adds: Orders.TableNumber/PromotionId + 'Paid' status, OrderItems.Size, Payments card/e-wallet details,
   Promotions.DiscountType/DiscountValue/PointsRequired/BranchId, Inventory.Unit/UnitCost/Supplier,
   StockMovements table, Customers.CustomerCode/Birthdate/Notes/CreatedAt, RewardRedemptions.RedemptionCode. */

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
