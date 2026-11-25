/* Brew Haven Coffee Shop - paste into SSMS or MonsterASP SQL panel */
SET XACT_ABORT ON;

IF OBJECT_ID('dbo.FK_Branches_Manager','F') IS NOT NULL ALTER TABLE dbo.Branches DROP CONSTRAINT FK_Branches_Manager;
IF OBJECT_ID('dbo.StockMovements','U') IS NOT NULL DROP TABLE dbo.StockMovements;
IF OBJECT_ID('dbo.RewardRedemptions','U') IS NOT NULL DROP TABLE dbo.RewardRedemptions;
IF OBJECT_ID('dbo.Rewards','U') IS NOT NULL DROP TABLE dbo.Rewards;
IF OBJECT_ID('dbo.Feedback','U') IS NOT NULL DROP TABLE dbo.Feedback;
IF OBJECT_ID('dbo.Inventory','U') IS NOT NULL DROP TABLE dbo.Inventory;
IF OBJECT_ID('dbo.OrderItems','U') IS NOT NULL DROP TABLE dbo.OrderItems;
IF OBJECT_ID('dbo.Payments','U') IS NOT NULL DROP TABLE dbo.Payments;
IF OBJECT_ID('dbo.Orders','U') IS NOT NULL DROP TABLE dbo.Orders;
IF OBJECT_ID('dbo.Promotions','U') IS NOT NULL DROP TABLE dbo.Promotions;
IF OBJECT_ID('dbo.CustomerPreferences','U') IS NOT NULL DROP TABLE dbo.CustomerPreferences;
IF OBJECT_ID('dbo.MenuItems','U') IS NOT NULL DROP TABLE dbo.MenuItems;
IF OBJECT_ID('dbo.Customers','U') IS NOT NULL DROP TABLE dbo.Customers;
IF OBJECT_ID('dbo.Subscriptions','U') IS NOT NULL DROP TABLE dbo.Subscriptions;
IF OBJECT_ID('dbo.Users','U') IS NOT NULL DROP TABLE dbo.Users;
IF OBJECT_ID('dbo.Branches','U') IS NOT NULL DROP TABLE dbo.Branches;
IF OBJECT_ID('dbo.Roles','U') IS NOT NULL DROP TABLE dbo.Roles;

CREATE TABLE Roles(RoleId INT IDENTITY PRIMARY KEY, RoleName NVARCHAR(30) NOT NULL UNIQUE,
 CONSTRAINT CK_Roles_Name CHECK(RoleName IN ('Super Admin','Admin','Manager','Staff')));
CREATE TABLE Branches(BranchId INT IDENTITY PRIMARY KEY, BranchName NVARCHAR(100) NOT NULL UNIQUE,
 Address NVARCHAR(255) NULL, ContactNumber NVARCHAR(11) NULL, ManagerUserId INT NULL, IsActive BIT NOT NULL DEFAULT 1,
 CONSTRAINT CK_Branches_Contact CHECK(ContactNumber IS NULL OR ContactNumber LIKE '[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'));
CREATE TABLE Users(UserId INT IDENTITY PRIMARY KEY, RoleId INT NOT NULL, BranchId INT NULL,
 Username NVARCHAR(50) NOT NULL UNIQUE, FullName NVARCHAR(100) NOT NULL,
 Email NVARCHAR(254) NOT NULL UNIQUE, ContactNumber NVARCHAR(11) NULL, PasswordHash NVARCHAR(512) NOT NULL,
 IsActive BIT NOT NULL DEFAULT 1, CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT CK_Users_Contact CHECK(ContactNumber IS NULL OR (LEN(ContactNumber)=11 AND ContactNumber NOT LIKE '%[^0-9]%')),
 CONSTRAINT FK_Users_Roles FOREIGN KEY(RoleId) REFERENCES Roles(RoleId),
 CONSTRAINT FK_Users_Branches FOREIGN KEY(BranchId) REFERENCES Branches(BranchId));
CREATE TABLE Subscriptions(SubscriptionId INT IDENTITY PRIMARY KEY, Name NVARCHAR(100) NOT NULL,
 TermsAndConditions NVARCHAR(MAX) NOT NULL, MonthlyPrice DECIMAL(12,2) NOT NULL DEFAULT 0,
 StartsAt DATE NOT NULL, EndsAt DATE NOT NULL, IsActive BIT NOT NULL DEFAULT 1,
 CONSTRAINT CK_Subscriptions_Price CHECK(MonthlyPrice>=0), CONSTRAINT CK_Subscriptions_Dates CHECK(EndsAt>=StartsAt));
CREATE TABLE Customers(CustomerId INT IDENTITY PRIMARY KEY, FullName NVARCHAR(100) NOT NULL,
 ContactNumber NVARCHAR(11) NULL, Email NVARCHAR(254) NULL,
 LoyaltyPoints INT NOT NULL DEFAULT 0, IsActive BIT NOT NULL DEFAULT 1,
 CONSTRAINT CK_Customers_Contact CHECK(ContactNumber IS NULL OR LEN(ContactNumber)=11 AND ContactNumber NOT LIKE '%[^0-9]%'),
 CONSTRAINT CK_Customers_Points CHECK(LoyaltyPoints>=0), CONSTRAINT UQ_Customers_Email UNIQUE(Email));
CREATE TABLE CustomerPreferences(PreferenceId INT IDENTITY PRIMARY KEY, CustomerId INT NOT NULL,
 PreferredMenuItemId INT NULL, PreferredOrderType NVARCHAR(10) NOT NULL DEFAULT 'Dine-In', Notes NVARCHAR(500) NULL,
 CONSTRAINT FK_Pref_Customer FOREIGN KEY(CustomerId) REFERENCES Customers(CustomerId),
 CONSTRAINT CK_Pref_Type CHECK(PreferredOrderType IN ('Dine-In','Takeout')));
CREATE TABLE MenuItems(MenuItemId INT IDENTITY PRIMARY KEY, ItemName NVARCHAR(100) NOT NULL UNIQUE,
 Category NVARCHAR(40) NOT NULL, Description NVARCHAR(500) NULL, Price DECIMAL(12,2) NOT NULL,
 IsAvailable BIT NOT NULL DEFAULT 1, CONSTRAINT CK_Menu_Price CHECK(Price>=0));
CREATE TABLE Promotions(PromotionId INT IDENTITY PRIMARY KEY, PromotionName NVARCHAR(100) NOT NULL,
 Description NVARCHAR(500) NULL, DiscountPercent DECIMAL(5,2) NOT NULL DEFAULT 0,
 StartDate DATE NOT NULL, EndDate DATE NOT NULL, IsActive BIT NOT NULL DEFAULT 1,
 CONSTRAINT CK_Promotions_Discount CHECK(DiscountPercent BETWEEN 0 AND 100), CONSTRAINT CK_Promotions_Dates CHECK(EndDate>=StartDate));
CREATE TABLE Orders(OrderId INT IDENTITY PRIMARY KEY, BranchId INT NOT NULL, CustomerId INT NULL, StaffUserId INT NOT NULL,
 OrderType NVARCHAR(10) NOT NULL DEFAULT 'Dine-In', Status NVARCHAR(20) NOT NULL DEFAULT 'Completed',
 Subtotal DECIMAL(12,2) NOT NULL DEFAULT 0, Discount DECIMAL(12,2) NOT NULL DEFAULT 0,
 Total DECIMAL(12,2) NOT NULL DEFAULT 0, PointsEarned INT NOT NULL DEFAULT 0,
 OrderDate DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT FK_Orders_Branch FOREIGN KEY(BranchId) REFERENCES Branches(BranchId),
 CONSTRAINT FK_Orders_Customer FOREIGN KEY(CustomerId) REFERENCES Customers(CustomerId),
 CONSTRAINT FK_Orders_User FOREIGN KEY(StaffUserId) REFERENCES Users(UserId),
 CONSTRAINT CK_Orders_Type CHECK(OrderType IN ('Dine-In','Takeout')),
 CONSTRAINT CK_Orders_Status CHECK(Status IN ('Pending','Completed','Cancelled')),
 CONSTRAINT CK_Orders_NonNegative CHECK(Subtotal>=0 AND Discount>=0 AND Total>=0));
CREATE TABLE OrderItems(OrderItemId INT IDENTITY PRIMARY KEY, OrderId INT NOT NULL, MenuItemId INT NOT NULL,
 Quantity INT NOT NULL, UnitPrice DECIMAL(12,2) NOT NULL, LineTotal AS (Quantity*UnitPrice) PERSISTED,
 CONSTRAINT FK_OrderItems_Order FOREIGN KEY(OrderId) REFERENCES Orders(OrderId),
 CONSTRAINT FK_OrderItems_Menu FOREIGN KEY(MenuItemId) REFERENCES MenuItems(MenuItemId),
 CONSTRAINT CK_OrderItems_Qty CHECK(Quantity>0), CONSTRAINT CK_OrderItems_Price CHECK(UnitPrice>=0));
CREATE TABLE Payments(PaymentId INT IDENTITY PRIMARY KEY, OrderId INT NOT NULL, Method NVARCHAR(10) NOT NULL,
 AmountPaid DECIMAL(12,2) NOT NULL, ChangeAmount DECIMAL(12,2) NOT NULL DEFAULT 0,
 PaidAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT FK_Payments_Order FOREIGN KEY(OrderId) REFERENCES Orders(OrderId),
 CONSTRAINT CK_Payments_Method CHECK(Method IN ('Cash','Card','E-Wallet')),
 CONSTRAINT CK_Payments_Amount CHECK(AmountPaid>=0 AND ChangeAmount>=0));
CREATE TABLE Rewards(RewardId INT IDENTITY PRIMARY KEY, RewardName NVARCHAR(100) NOT NULL,
 PointsRequired INT NOT NULL, DiscountValue DECIMAL(12,2) NOT NULL, IsActive BIT NOT NULL DEFAULT 1,
 CONSTRAINT CK_Rewards_Points CHECK(PointsRequired>=0), CONSTRAINT CK_Rewards_Value CHECK(DiscountValue>=0));
CREATE TABLE RewardRedemptions(RedemptionId INT IDENTITY PRIMARY KEY, CustomerId INT NOT NULL, RewardId INT NULL,
 PromotionId INT NULL, PointsRedeemed INT NOT NULL DEFAULT 0, RedeemedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
 OrderId INT NULL,
 CONSTRAINT FK_Redemptions_Customer FOREIGN KEY(CustomerId) REFERENCES Customers(CustomerId),
 CONSTRAINT FK_Redemptions_Reward FOREIGN KEY(RewardId) REFERENCES Rewards(RewardId),
 CONSTRAINT FK_Redemptions_Promotion FOREIGN KEY(PromotionId) REFERENCES Promotions(PromotionId),
 CONSTRAINT FK_Redemptions_Order FOREIGN KEY(OrderId) REFERENCES Orders(OrderId));
CREATE TABLE Inventory(InventoryId INT IDENTITY PRIMARY KEY, BranchId INT NOT NULL, MenuItemId INT NULL,
 ItemName NVARCHAR(100) NOT NULL, Category NVARCHAR(40) NOT NULL, Quantity INT NOT NULL DEFAULT 0,
 ReorderLevel INT NOT NULL DEFAULT 5, UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT FK_Inventory_Branch FOREIGN KEY(BranchId) REFERENCES Branches(BranchId),
 CONSTRAINT FK_Inventory_Menu FOREIGN KEY(MenuItemId) REFERENCES MenuItems(MenuItemId),
 CONSTRAINT CK_Inventory_Quantity CHECK(Quantity>=0), CONSTRAINT CK_Inventory_Reorder CHECK(ReorderLevel>=0));
CREATE TABLE Feedback(FeedbackId INT IDENTITY PRIMARY KEY, CustomerId INT NULL, BranchId INT NOT NULL,
 SubmittedByUserId INT NOT NULL, Type NVARCHAR(12) NOT NULL, Subject NVARCHAR(150) NOT NULL,
 Message NVARCHAR(MAX) NOT NULL, Rating INT NULL, DateSubmitted DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
 Status NVARCHAR(15) NOT NULL DEFAULT 'Open', AssignedTo INT NULL, ResolutionNotes NVARCHAR(MAX) NULL,
 DateResolved DATETIME2 NULL,
 CONSTRAINT FK_Feedback_Customer FOREIGN KEY(CustomerId) REFERENCES Customers(CustomerId),
 CONSTRAINT FK_Feedback_Branch FOREIGN KEY(BranchId) REFERENCES Branches(BranchId),
 CONSTRAINT FK_Feedback_Submitter FOREIGN KEY(SubmittedByUserId) REFERENCES Users(UserId),
 CONSTRAINT FK_Feedback_Assignee FOREIGN KEY(AssignedTo) REFERENCES Users(UserId),
 CONSTRAINT CK_Feedback_Type CHECK(Type IN ('Feedback','Inquiry','Complaint')),
 CONSTRAINT CK_Feedback_Rating CHECK(Rating IS NULL OR Rating BETWEEN 1 AND 5),
 CONSTRAINT CK_Feedback_FeedbackRating CHECK(Type<> 'Feedback' OR Rating IS NOT NULL),
 CONSTRAINT CK_Feedback_Status CHECK(Status IN ('Open','In Progress','Resolved','Closed')));
ALTER TABLE CustomerPreferences ADD CONSTRAINT FK_Pref_Menu FOREIGN KEY(PreferredMenuItemId) REFERENCES MenuItems(MenuItemId);
ALTER TABLE Branches ADD CONSTRAINT FK_Branches_Manager FOREIGN KEY(ManagerUserId) REFERENCES Users(UserId);

INSERT Roles(RoleName) VALUES('Super Admin'),('Admin'),('Manager'),('Staff');
INSERT Branches(BranchName,Address,ContactNumber) VALUES
('Main Branch','123 Coffee Ave, Manila','09170000001'),('Mall Branch','456 Mall Road, Cebu','09170000002');
-- Password for every test account below: Password123!
INSERT Users(RoleId,BranchId,Username,FullName,Email,PasswordHash) VALUES
(1,1,'superadmin','Super Administrator','super@brewhaven.test','1B9A38AE536D2E01E0BBE1E900AF294D.ECCE272F4832815988EE7FCD4BBEF3B8CADDF21C75D609DF1932511F9A36568A'),
(2,1,'admin','Branch Administrator','admin@brewhaven.test','1B9A38AE536D2E01E0BBE1E900AF294D.ECCE272F4832815988EE7FCD4BBEF3B8CADDF21C75D609DF1932511F9A36568A'),
(3,1,'manager','Store Manager','manager@brewhaven.test','1B9A38AE536D2E01E0BBE1E900AF294D.ECCE272F4832815988EE7FCD4BBEF3B8CADDF21C75D609DF1932511F9A36568A'),
(4,1,'staff','Walk-In Staff','staff@brewhaven.test','1B9A38AE536D2E01E0BBE1E900AF294D.ECCE272F4832815988EE7FCD4BBEF3B8CADDF21C75D609DF1932511F9A36568A');
INSERT MenuItems(ItemName,Category,Description,Price) VALUES
('Brewed Coffee','Coffee','Classic house coffee',95),('Cafe Latte','Coffee','Espresso and steamed milk',140),
('Iced Mocha','Coffee','Chocolate espresso drink',165),('Cheesecake','Pastry','Creamy baked cheesecake',120),
('Ham & Cheese Sandwich','Food','Toasted sandwich',150),('Mineral Water','Beverage','Bottled drinking water',45);
INSERT Promotions(PromotionName,Description,DiscountPercent,StartDate,EndDate) VALUES
('Morning Brew','10 percent off coffee before noon',10,'2026-01-01','2026-12-31'),
('Weekend Treat','15 percent off pastry combos',15,'2026-01-01','2026-12-31');
INSERT Customers(FullName,ContactNumber,Email,LoyaltyPoints) VALUES
('Ana Santos','09181234567','ana@example.test',120),('Mark Reyes','09181234568','mark@example.test',50);
INSERT CustomerPreferences(CustomerId,PreferredMenuItemId,PreferredOrderType,Notes) VALUES
(1,2,'Dine-In','Less sugar'),(2,1,'Takeout','Hot coffee');
INSERT Inventory(BranchId,MenuItemId,ItemName,Category,Quantity,ReorderLevel) VALUES
(1,1,'Brewed Coffee','Coffee',30,10),(1,2,'Cafe Latte','Coffee',24,8),(1,4,'Cheesecake','Pastry',4,5),(1,6,'Mineral Water','Beverage',50,10);
INSERT Rewards(RewardName,PointsRequired,DiscountValue) VALUES('P50 Discount',100,50),('P100 Discount',200,100);
INSERT Subscriptions(Name,TermsAndConditions,MonthlyPrice,StartsAt,EndsAt) VALUES('Standard','Standard service terms apply.',999,'2026-01-01','2026-12-31');
INSERT Feedback(CustomerId,BranchId,SubmittedByUserId,Type,Subject,Message,Rating,Status,ResolutionNotes) VALUES
(1,1,4,'Feedback','Great service','Friendly and quick',5,'Resolved','Thanked the customer'),
(2,1,4,'Inquiry','Operating hours','Asked if open on holidays',NULL,'Open',NULL),
(1,1,4,'Complaint','Cold drink','The iced coffee arrived warm',NULL,'Open',NULL);
-- The seeded feedback row above leaves Rating NULL for Inquiry, satisfying the feedback-only rating rule.
GO
