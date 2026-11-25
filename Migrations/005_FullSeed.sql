/* ================================================================================================
   Brew Haven Coffee Shop - FULL SAMPLE DATA (every table), Davao City branches
   ------------------------------------------------------------------------------------------------
   !! WARNING: SECTION 1 DELETES ALL EXISTING DATA in every Brew Haven table (users, customers,
   !! orders, everything) and restarts the ID numbers, then SECTION 2 inserts fresh sample data.
   !! Use it on a test / demo database. Back up first if you want to keep anything.

   Requirements: the app's schema upgrades must already be applied (log in to the app once, or run
   Migrations/003_ManagerStaff.sql). Re-runnable: every run wipes and re-creates the same data set.
   One batch, no GO lines, all inside a transaction: on any error everything is rolled back.
   Dates are relative to GETDATE(), so the last 60 days always have sales.

   TEST ACCOUNTS (password hashes below were made with the app's own PasswordHasher, PBKDF2-SHA256)
   ------------------------------------------------------------------------------------------------
   Username            Password          Role         Branch               Status
   superadmin          SuperAdmin@2026   Super Admin  Poblacion            Active
   admin.garcia        Admin@2026        Admin        Poblacion            Active
   admin.villanueva    Admin@2026        Admin        Lanang               Active
   manager.poblacion   Manager@2026      Manager      Poblacion            Active
   manager.lanang      Manager@2026      Manager      Lanang               Active
   manager.matina      Manager@2026      Manager      Matina               Active
   staff.santos        Staff@2026        Staff        Poblacion            Active
   staff.cruz          Staff@2026        Staff        Poblacion            Active
   staff.mendoza       Staff@2026        Staff        Lanang               Active
   staff.ramos         Staff@2026        Staff        Lanang               INACTIVE (can't log in)
   staff.flores        Staff@2026        Staff        Matina               Active
   staff.bautista      Staff@2026        Staff        Matina               Active
   ================================================================================================ */

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    /* =============================================================================================
       SECTION 1 - CLEANUP: delete children before parents, then restart identity numbering at 1
       ============================================================================================= */
    UPDATE dbo.Branches SET ManagerUserId = NULL;          -- Branches <-> Users reference each other
    DELETE FROM dbo.StockMovements;
    DELETE FROM dbo.Payments;
    DELETE FROM dbo.RewardRedemptions;
    DELETE FROM dbo.OrderItems;
    DELETE FROM dbo.Feedback;
    DELETE FROM dbo.Orders;
    DELETE FROM dbo.CustomerPreferences;
    DELETE FROM dbo.Inventory;
    DELETE FROM dbo.Promotions;
    DELETE FROM dbo.Rewards;
    DELETE FROM dbo.Customers;
    DELETE FROM dbo.MenuItems;
    DELETE FROM dbo.Subscriptions;
    DELETE FROM dbo.Users;
    DELETE FROM dbo.Branches;
    DELETE FROM dbo.Roles;

    -- Reseed only tables whose identity has been used (reseeding a never-used table would start it at 0).
    DECLARE @reseed nvarchar(max) = N'';
    SELECT @reseed += N'DBCC CHECKIDENT (''dbo.' + OBJECT_NAME(object_id) + N''', RESEED, 0) WITH NO_INFOMSGS;'
    FROM sys.identity_columns
    WHERE last_value IS NOT NULL
      AND OBJECT_NAME(object_id) IN ('StockMovements','Payments','RewardRedemptions','OrderItems','Feedback','Orders',
                                     'CustomerPreferences','Inventory','Promotions','Rewards','Customers','MenuItems',
                                     'Subscriptions','Users','Branches','Roles');
    EXEC sys.sp_executesql @reseed;

    /* =============================================================================================
       SECTION 2 - SAMPLE DATA
       ============================================================================================= */
    DECLARE @Now datetime2 = CAST(GETDATE() AS datetime2(0));
    DECLARE @Today date = CAST(GETDATE() AS date);

    -- 1. Roles -------------------------------------------------------------------------------------
    INSERT INTO dbo.Roles (RoleName) VALUES ('Super Admin'), ('Admin'), ('Manager'), ('Staff');

    -- 2. Branches (managers are set after the users exist) -----------------------------------------
    INSERT INTO dbo.Branches (BranchName, Address, ContactNumber, IsActive) VALUES
        ('Brew Haven - Poblacion', '88 San Pedro St., Poblacion District, Davao City', '09171110001', 1),
        ('Brew Haven - Lanang',    'J.P. Laurel Ave., Lanang, Davao City',            '09171110002', 1),
        ('Brew Haven - Matina',    'MacArthur Highway, Matina, Davao City',           '09171110003', 1);

    DECLARE @Pob int = (SELECT BranchId FROM dbo.Branches WHERE BranchName = 'Brew Haven - Poblacion');
    DECLARE @Lan int = (SELECT BranchId FROM dbo.Branches WHERE BranchName = 'Brew Haven - Lanang');
    DECLARE @Mat int = (SELECT BranchId FROM dbo.Branches WHERE BranchName = 'Brew Haven - Matina');

    -- 3. Users --------------------------------------------------------------------------------------
    DECLARE @HashSuper   nvarchar(512) = 'E39BF2C14D1509159B21A1FCC19A63A0.A01CB40A0F86770CF4F5BDB6316336508E279B858AAB7BF19E423A1B9DC211FE'; -- SuperAdmin@2026
    DECLARE @HashAdmin   nvarchar(512) = '2766EE73E7D0DF41D040C76052720FBA.5C1FAF57AA9561743587B09139F36091B2F07BACB37E06EBD3A07FFB8E266B74'; -- Admin@2026
    DECLARE @HashManager nvarchar(512) = '5A59D4CCBB75AB854DFF771250CF925D.62B7A7D75E8EA11AB35AC7FBDB9FB49635EF315F40B0627165CF1F606847ADEB'; -- Manager@2026
    DECLARE @HashStaff   nvarchar(512) = 'E5A4164689E494D50B2AD8FC5B92E10D.E4E66F9264F54E2E39781B44A29B6F80D676E22985F62443349FDEC356CDE3D1'; -- Staff@2026

    INSERT INTO dbo.Users (RoleId, BranchId, Username, FullName, Email, ContactNumber, PasswordHash, IsActive, CreatedAt)
    SELECT r.RoleId, v.BranchId, v.Username, v.FullName, v.Email, v.Contact, v.Hash, v.IsActive, DATEADD(DAY, -v.DaysAgo, @Now)
    FROM (VALUES
        ('Super Admin', @Pob, 'superadmin',        'Ramon Dela Cruz',          'superadmin@brewhaven.test',   '09178880001', @HashSuper,   1, 120),
        ('Admin',       @Pob, 'admin.garcia',      'Maria Lourdes Garcia',     'm.garcia@brewhaven.test',     '09178880002', @HashAdmin,   1, 118),
        ('Admin',       @Lan, 'admin.villanueva',  'Roberto Villanueva',       'r.villanueva@brewhaven.test', '09178880003', @HashAdmin,   1, 110),
        ('Manager',     @Pob, 'manager.poblacion', 'Angelica Reyes',           'a.reyes@brewhaven.test',      '09178880004', @HashManager, 1, 100),
        ('Manager',     @Lan, 'manager.lanang',    'Mark Anthony Dizon',       'm.dizon@brewhaven.test',      '09178880005', @HashManager, 1, 100),
        ('Manager',     @Mat, 'manager.matina',    'Kristine Joy Lim',         'k.lim@brewhaven.test',        '09178880006', @HashManager, 1, 95),
        ('Staff',       @Pob, 'staff.santos',      'Jerome Santos',            'j.santos@brewhaven.test',     '09178880007', @HashStaff,   1, 90),
        ('Staff',       @Pob, 'staff.cruz',        'Princess Mae Cruz',        'p.cruz@brewhaven.test',       '09178880008', @HashStaff,   1, 88),
        ('Staff',       @Lan, 'staff.mendoza',     'Carlo Mendoza',            'c.mendoza@brewhaven.test',    '09178880009', @HashStaff,   1, 85),
        ('Staff',       @Lan, 'staff.ramos',       'Joanna Ramos',             'j.ramos@brewhaven.test',      '09178880010', @HashStaff,   0, 84),
        ('Staff',       @Mat, 'staff.flores',      'Rhea Flores',              'r.flores@brewhaven.test',     '09178880011', @HashStaff,   1, 80),
        ('Staff',       @Mat, 'staff.bautista',    'Kevin Bautista',           'k.bautista@brewhaven.test',   '09178880012', @HashStaff,   1, 78)
    ) AS v (RoleName, BranchId, Username, FullName, Email, Contact, Hash, IsActive, DaysAgo)
    INNER JOIN dbo.Roles r ON r.RoleName = v.RoleName;

    UPDATE b SET ManagerUserId = u.UserId
    FROM dbo.Branches b
    INNER JOIN dbo.Users u ON u.Username = CASE b.BranchId WHEN @Pob THEN 'manager.poblacion' WHEN @Lan THEN 'manager.lanang' ELSE 'manager.matina' END;

    -- Staff per branch, numbered 1-2 (used to assign orders, payments and feedback).
    DECLARE @Staff TABLE (BranchId int, Seq int, UserId int);
    INSERT INTO @Staff (BranchId, Seq, UserId)
    SELECT u.BranchId, ROW_NUMBER() OVER (PARTITION BY u.BranchId ORDER BY u.UserId), u.UserId
    FROM dbo.Users u INNER JOIN dbo.Roles r ON r.RoleId = u.RoleId WHERE r.RoleName = 'Staff';

    -- 4. Subscriptions and T&C ----------------------------------------------------------------------
    INSERT INTO dbo.Subscriptions (Name, TermsAndConditions, MonthlyPrice, StartsAt, EndsAt, IsActive) VALUES
        ('Basic', 'Duration: 12 months, billed monthly.' + CHAR(13) + CHAR(10) +
                  'Features: 1 branch, up to 5 user accounts, orders, payments and customer records, daily sales report.' + CHAR(13) + CHAR(10) +
                  'Terms: Fees are payable in advance on the 1st of each month. Cancel any time with 30 days written notice. ' +
                  'Data is kept for 90 days after cancellation. Prices are VAT-exclusive.',
         499.00, DATEADD(MONTH, -3, @Today), DATEADD(MONTH, 9, @Today), 1),
        ('Standard', 'Duration: 12 months, billed monthly.' + CHAR(13) + CHAR(10) +
                  'Features: up to 3 branches, 20 user accounts, inventory and stock movements, promotions and loyalty rewards, ' +
                  'weekly and monthly reports, CSV export.' + CHAR(13) + CHAR(10) +
                  'Terms: Fees are payable in advance. A 15-day grace period applies to late payments, after which access is suspended. ' +
                  'Cancel with 30 days notice. Prices are VAT-exclusive.',
         999.00, DATEADD(MONTH, -3, @Today), DATEADD(MONTH, 9, @Today), 1),
        ('Premium', 'Duration: 24 months, billed monthly.' + CHAR(13) + CHAR(10) +
                  'Features: unlimited branches and users, all Standard features, business intelligence dashboards, ' +
                  'priority support (8 AM - 8 PM), quarterly data backup.' + CHAR(13) + CHAR(10) +
                  'Terms: Minimum term of 6 months. Fees are payable in advance. Early termination fee equal to one month applies. ' +
                  'Prices are VAT-exclusive.',
         1999.00, DATEADD(MONTH, -3, @Today), DATEADD(MONTH, 21, @Today), 1);

    -- 5. Customers (LoyaltyPoints here is the OPENING balance; earned / redeemed points are added at the end) ---
    INSERT INTO dbo.Customers (FullName, ContactNumber, Email, LoyaltyPoints, IsActive, Birthdate, Notes, CreatedAt)
    SELECT v.Name, v.Contact, v.Email, v.Opening, 1, v.Birthdate, v.Notes, DATEADD(DAY, -v.DaysAgo, @Now)
    FROM (VALUES
        ('Juan Dela Cruz',          '09171230101', 'juan.delacruz@example.com',   250, CAST('1994-03-14' AS date), 'Prefers oat milk',                 85),
        ('Maria Clara Santos',      '09171230102', 'mariaclara.s@example.com',    180, CAST('1998-07-02' AS date), 'Less ice, less sugar',             84),
        ('Jose Miguel Mercado',     '09171230103', NULL,                          120, NULL,                       NULL,                               82),
        ('Andrea Bautista',         '09171230104', 'andrea.bautista@example.com', 300, CAST('2001-11-20' AS date), 'Usually takeout before work',      80),
        ('Paolo Villanueva',        '09171230105', 'paolo.v@example.com',         140, CAST('1990-01-09' AS date), 'Allergic to nuts',                 78),
        ('Kimberly Ann Tan',        '09181230106', 'kim.tan@example.com',         210, CAST('1996-05-30' AS date), NULL,                               76),
        ('Rafael Aquino',           '09181230107', 'raf.aquino@example.com',      100, CAST('1988-09-17' AS date), 'Extra hot drinks',                 74),
        ('Liza Marie Gonzales',     '09181230108', 'liza.gonzales@example.com',   160, CAST('1993-12-02' AS date), NULL,                               72),
        ('Christian Ocampo',        '09181230109', NULL,                          90,  NULL,                       NULL,                               70),
        ('Camille Rose Navarro',    '09191230110', 'camille.navarro@example.com', 230, CAST('1999-02-14' AS date), 'Birthday in February: loves cake', 68),
        ('Mark Joseph Lopez',       '09191230111', 'mj.lopez@example.com',        130, CAST('1995-08-08' AS date), NULL,                               66),
        ('Shiela Mae Domingo',      '09191230112', 'shiela.domingo@example.com',  175, CAST('2000-04-25' AS date), 'Matcha fan',                       64),
        ('Vincent Paul Ramirez',    '09201230113', 'vp.ramirez@example.com',      110, CAST('1987-10-05' AS date), NULL,                               60),
        ('Erica Jane Castillo',     '09201230114', 'erica.castillo@example.com',  195, CAST('1997-06-19' AS date), 'Works nearby, quick orders',       58),
        ('Noel Fernandez',          '09201230115', NULL,                          80,  NULL,                       NULL,                               55),
        ('Patricia Lim',            '09271230116', 'patricia.lim@example.com',    260, CAST('1992-03-03' AS date), 'Large drinks only',                52),
        ('Jayson Valdez',           '09271230117', 'jayson.valdez@example.com',   120, CAST('1994-11-11' AS date), NULL,                               50),
        ('Rowena Pascual',          '09271230118', 'rowena.pascual@example.com',  150, CAST('1985-07-21' AS date), 'Senior-friendly seating',          48),
        ('Gabriel Torres',          '09351230119', 'gab.torres@example.com',      100, CAST('2002-01-28' AS date), NULL,                               45),
        ('Hannah Grace Sison',      '09351230120', 'hannah.sison@example.com',    220, CAST('1998-09-09' AS date), 'Decaf after 3 PM',                 40),
        ('Dennis Aguilar',          '09451230121', NULL,                          90,  NULL,                       NULL,                               35),
        ('Ma. Theresa Salazar',     '09451230122', 'theresa.salazar@example.com', 170, CAST('1991-05-05' AS date), NULL,                               30),
        ('Ronaldo Manalo',          '09551230123', 'ronaldo.manalo@example.com',  130, CAST('1989-12-24' AS date), 'Takeout, no straw',                25),
        ('Bea Patricia Morales',    '09551230124', 'bea.morales@example.com',     240, CAST('2003-08-16' AS date), NULL,                               18),
        ('Francis Dimaculangan',    '09951230125', 'francis.dima@example.com',    100, CAST('1996-02-29' AS date), NULL,                               10)
    ) AS v (Name, Contact, Email, Opening, Birthdate, Notes, DaysAgo);

    UPDATE dbo.Customers SET CustomerCode = 'BH-' + RIGHT('00000' + CAST(CustomerId AS varchar(10)), 5);

    DECLARE @Cust TABLE (Seq int PRIMARY KEY, CustomerId int);
    INSERT INTO @Cust SELECT ROW_NUMBER() OVER (ORDER BY CustomerId), CustomerId FROM dbo.Customers;

    -- 6. Menu items (52 items; 2 unavailable for testing) ---------------------------------------------
    INSERT INTO dbo.MenuItems (ItemName, Category, Description, Price, IsAvailable) VALUES
        ('Espresso',                'Coffee',     'Double shot of our house espresso',            95,  1),
        ('Americano',               'Coffee',     'Espresso with hot water',                      110, 1),
        ('Cappuccino',              'Coffee',     'Espresso, steamed milk and thick foam',        140, 1),
        ('Cafe Latte',              'Coffee',     'Espresso and steamed milk',                    140, 1),
        ('Flat White',              'Coffee',     'Ristretto with velvety microfoam',             145, 1),
        ('Caramel Macchiato',       'Coffee',     'Vanilla milk, espresso and caramel drizzle',   165, 1),
        ('Cafe Mocha',              'Coffee',     'Espresso, chocolate and steamed milk',         155, 1),
        ('Spanish Latte',           'Coffee',     'Espresso with sweetened condensed milk',       160, 1),
        ('Vanilla Latte',           'Coffee',     'Latte with vanilla syrup',                     155, 1),
        ('Hazelnut Latte',          'Coffee',     'Latte with hazelnut syrup',                    155, 1),
        ('Iced Coffee',             'Coffee',     'Brewed coffee over ice',                       120, 1),
        ('Iced Mocha',              'Coffee',     'Chocolate espresso drink over ice',            165, 1),
        ('Cold Brew',               'Coffee',     'Slow-steeped for 18 hours',                    150, 1),
        ('Brewed Coffee',           'Coffee',     'Classic house blend from Mt. Apo beans',       95,  1),
        ('Durian Latte (Seasonal)', 'Coffee',     'Seasonal Davao special: currently unavailable',185, 0),
        ('Matcha Latte',            'Non-Coffee', 'Japanese matcha with milk',                    160, 1),
        ('Hot Chocolate',           'Non-Coffee', 'Rich Davao tablea chocolate',                  130, 1),
        ('Iced Chocolate',          'Non-Coffee', 'Tablea chocolate milk over ice',               140, 1),
        ('Strawberry Milk',         'Non-Coffee', 'Fresh milk with strawberry puree',             135, 1),
        ('Cookies & Cream Frappe',  'Non-Coffee', 'Blended cookies and cream',                    175, 1),
        ('Mango Graham Frappe',     'Non-Coffee', 'Seasonal: currently unavailable',              175, 0),
        ('Fresh Lemonade',          'Non-Coffee', 'Freshly squeezed calamansi-lemon',             110, 1),
        ('Mineral Water',           'Non-Coffee', 'Bottled drinking water',                       45,  1),
        ('Classic Milk Tea',        'Tea',        'Black tea with milk',                          120, 1),
        ('Wintermelon Milk Tea',    'Tea',        'Wintermelon syrup and milk tea',               125, 1),
        ('Taro Milk Tea',           'Tea',        'Creamy taro milk tea',                         130, 1),
        ('Passion Fruit Tea',       'Tea',        'Green tea with passion fruit',                 115, 1),
        ('Lychee Fruit Tea',        'Tea',        'Jasmine tea with lychee',                      115, 1),
        ('Earl Grey Tea',           'Tea',        'Hot bergamot black tea',                       100, 1),
        ('Chamomile Tea',           'Tea',        'Caffeine-free herbal tea',                     100, 1),
        ('Butter Croissant',        'Pastries',   'Flaky all-butter croissant',                   95,  1),
        ('Chocolate Muffin',        'Pastries',   'Double chocolate muffin',                      85,  1),
        ('Blueberry Muffin',        'Pastries',   'Muffin with blueberries',                      85,  1),
        ('Chocolate Chip Cookies',  'Pastries',   'Two soft-baked cookies',                       65,  1),
        ('Cheesecake',              'Pastries',   'Creamy New York cheesecake slice',             120, 1),
        ('Ensaymada',               'Pastries',   'Buttery brioche with cheese and sugar',        75,  1),
        ('Cinnamon Roll',           'Pastries',   'Cinnamon roll with cream cheese icing',        95,  1),
        ('Banana Bread',            'Pastries',   'Moist banana bread slice',                     85,  1),
        ('Ham & Cheese Sandwich',   'Meals',      'Toasted sandwich',                             150, 1),
        ('Clubhouse Sandwich',      'Meals',      'Chicken, ham, egg and bacon with fries',       195, 1),
        ('Tuna Melt Sandwich',      'Meals',      'Tuna and melted cheese on sourdough',          175, 1),
        ('Chicken Pesto Pasta',     'Meals',      'Penne in basil pesto with grilled chicken',    245, 1),
        ('Carbonara',               'Meals',      'Creamy bacon carbonara',                       235, 1),
        ('Chicken Adobo Rice Bowl', 'Meals',      'Classic adobo with garlic rice',               210, 1),
        ('Tapsilog',                'Meals',      'Beef tapa, garlic rice and fried egg',         225, 1),
        ('Longsilog',               'Meals',      'Sweet longganisa, garlic rice and fried egg',  195, 1),
        ('Extra Espresso Shot',     'Add-ons',    'Add a shot to any drink',                      40,  1),
        ('Oat Milk',                'Add-ons',    'Swap to oat milk',                             35,  1),
        ('Almond Milk',             'Add-ons',    'Swap to almond milk',                          35,  1),
        ('Whipped Cream',           'Add-ons',    'Topping',                                      25,  1),
        ('Caramel Drizzle',         'Add-ons',    'Topping',                                      20,  1),
        ('Tapioca Pearls',          'Add-ons',    'For milk teas',                                20,  1);

    DECLARE @Menu TABLE (Seq int PRIMARY KEY, MenuItemId int, Category nvarchar(40), Price decimal(12,2));
    INSERT INTO @Menu SELECT ROW_NUMBER() OVER (ORDER BY MenuItemId), MenuItemId, Category, Price FROM dbo.MenuItems WHERE IsAvailable = 1;
    DECLARE @MenuCount int = (SELECT COUNT(*) FROM @Menu);

    -- 7. Customer preferences (18 customers) ----------------------------------------------------------
    INSERT INTO dbo.CustomerPreferences (CustomerId, PreferredMenuItemId, PreferredOrderType, Notes)
    SELECT c.CustomerId, m.MenuItemId, v.OrderType, v.Notes
    FROM (VALUES
        (1, 'Cafe Latte', 'Dine-In', 'Oat milk, less sugar'),            (2, 'Matcha Latte', 'Takeout', 'Less ice'),
        (3, 'Americano', 'Takeout', 'Hot, no sugar'),                    (4, 'Spanish Latte', 'Takeout', 'Large, before 8 AM'),
        (5, 'Iced Coffee', 'Dine-In', 'No nuts in anything'),            (6, 'Caramel Macchiato', 'Dine-In', 'Extra caramel'),
        (7, 'Brewed Coffee', 'Dine-In', 'Extra hot'),                    (8, 'Wintermelon Milk Tea', 'Takeout', '50% sugar'),
        (9, 'Tapsilog', 'Dine-In', 'Egg well done'),                     (10, 'Cheesecake', 'Dine-In', 'Pairs with Americano'),
        (11, 'Cold Brew', 'Takeout', NULL),                              (12, 'Matcha Latte', 'Dine-In', 'Large'),
        (13, 'Chicken Adobo Rice Bowl', 'Dine-In', 'Extra rice'),        (14, 'Butter Croissant', 'Takeout', 'Warmed'),
        (16, 'Cafe Mocha', 'Dine-In', 'Large drinks only'),              (18, 'Hot Chocolate', 'Dine-In', 'Senior-friendly seating'),
        (20, 'Chamomile Tea', 'Dine-In', 'Decaf after 3 PM'),            (24, 'Taro Milk Tea', 'Takeout', 'Extra pearls')
    ) AS v (CustSeq, ItemName, OrderType, Notes)
    INNER JOIN @Cust c ON c.Seq = v.CustSeq
    INNER JOIN dbo.MenuItems m ON m.ItemName = v.ItemName;

    -- 8. Promotions: active (percentage and fixed), branch-only, points promo, expired and upcoming ------------
    INSERT INTO dbo.Promotions (PromotionName, Description, DiscountPercent, DiscountType, DiscountValue, PointsRequired, StartDate, EndDate, BranchId, IsActive) VALUES
        ('Student Treat',          '10% off with a valid school ID',                  10, 'Percentage', 10,  NULL, DATEADD(DAY, -75, @Today), DATEADD(MONTH, 3, @Today), NULL, 1),
        ('Merienda Combo',         'PHP 50 off when you spend PHP 150 or more',        0, 'Fixed',      50,  NULL, DATEADD(DAY, -75, @Today), DATEADD(MONTH, 2, @Today), NULL, 1),
        ('Lanang Opening Week',    '20% off at Lanang branch only',                   20, 'Percentage', 20,  NULL, DATEADD(DAY, -10, @Today), DATEADD(DAY, 20, @Today),   @Lan, 1),
        ('Free Pastry (Points)',   'Redeem 150 points for PHP 95 off a pastry',        0, 'Fixed',      95,  150,  DATEADD(DAY, -60, @Today), DATEADD(MONTH, 6, @Today), NULL, 1),
        ('Kadayawan Special',      'EXPIRED: 15% off all iced drinks',               15, 'Percentage', 15,  NULL, DATEADD(DAY, -90, @Today), DATEADD(DAY, -30, @Today),  NULL, 1),
        ('Christmas Blend Promo',  'UPCOMING: PHP 30 off holiday drinks',              0, 'Fixed',      30,  NULL, DATEADD(DAY, 7, @Today),   DATEADD(DAY, 45, @Today),   NULL, 1);

    DECLARE @PromoStudent int = (SELECT PromotionId FROM dbo.Promotions WHERE PromotionName = 'Student Treat');
    DECLARE @PromoMerienda int = (SELECT PromotionId FROM dbo.Promotions WHERE PromotionName = 'Merienda Combo');
    DECLARE @PromoKadayawan int = (SELECT PromotionId FROM dbo.Promotions WHERE PromotionName = 'Kadayawan Special');
    DECLARE @PromoPoints int = (SELECT PromotionId FROM dbo.Promotions WHERE PromotionName = 'Free Pastry (Points)');

    -- 9. Rewards (redeemed with loyalty points) ---------------------------------------------------------
    INSERT INTO dbo.Rewards (RewardName, PointsRequired, DiscountValue, IsActive) VALUES
        ('Free Drink Upgrade (Large)', 60,  20,  1),
        ('Free Brewed Coffee',         80,  95,  1),
        ('P50 Discount',               100, 50,  1),
        ('Free Pastry',                120, 95,  1),
        ('P100 Discount',              200, 100, 1);

    -- 10. Inventory: 25 items x 3 branches (75 rows), 5+ at or below reorder level per branch ---------------
    INSERT INTO dbo.Inventory (BranchId, MenuItemId, ItemName, Category, Unit, Quantity, ReorderLevel, UnitCost, Supplier, UpdatedAt)
    SELECT b.BranchId, m.MenuItemId, v.ItemName, v.Category, v.Unit,
           -- Poblacion keeps the "low" quantities; the other branches get healthier stock except a few items.
           CASE WHEN b.BranchId = @Pob THEN v.QtyPob WHEN b.BranchId = @Lan THEN v.QtyLan ELSE v.QtyMat END,
           v.Reorder, v.Cost, v.Supplier, DATEADD(HOUR, -((b.BranchId * 7 + LEN(v.ItemName)) % 72), @Now)
    FROM (VALUES
        ('Espresso Beans (Mt. Apo)', NULL,                     'Ingredients', 'kg',     6,   9,   4,   3,   980.00, 'Mt. Apo Coffee Growers'),
        ('Decaf Espresso Beans',     NULL,                     'Ingredients', 'kg',     2,   3,   1,   1,   1150.00,'Mt. Apo Coffee Growers'),
        ('Fresh Milk',               NULL,                     'Ingredients', 'L',      24,  30,  18,  10,  95.00,  'Davao Dairy Farms'),
        ('Oat Milk',                 NULL,                     'Ingredients', 'L',      4,   10,  8,   6,   185.00, 'Davao Dairy Farms'),
        ('Almond Milk',              NULL,                     'Ingredients', 'L',      5,   7,   2,   4,   195.00, 'Davao Dairy Farms'),
        ('Condensed Milk',           NULL,                     'Ingredients', 'can',    18,  20,  15,  8,   62.00,  'Mindanao Grocers'),
        ('Davao Tablea Chocolate',   NULL,                     'Ingredients', 'kg',     3,   5,   4,   2,   420.00, 'Calinan Cacao Co.'),
        ('Caramel Syrup',            NULL,                     'Syrups',      'bottle', 6,   8,   5,   3,   310.00, 'Monin PH Distributor'),
        ('Vanilla Syrup',            NULL,                     'Syrups',      'bottle', 2,   6,   5,   3,   310.00, 'Monin PH Distributor'),
        ('Hazelnut Syrup',           NULL,                     'Syrups',      'bottle', 4,   5,   2,   3,   320.00, 'Monin PH Distributor'),
        ('Matcha Powder',            NULL,                     'Ingredients', 'kg',     1,   2,   2,   1,   1450.00,'Kyoto Tea Imports'),
        ('Black Tea Leaves',         NULL,                     'Ingredients', 'kg',     3,   4,   3,   1,   520.00, 'Bukidnon Tea Estate'),
        ('Tapioca Pearls',           NULL,                     'Ingredients', 'kg',     8,   10,  6,   3,   140.00, 'Mindanao Grocers'),
        ('White Sugar',              NULL,                     'Ingredients', 'kg',     20,  25,  18,  8,   75.00,  'Mindanao Grocers'),
        ('Whipped Cream',            NULL,                     'Ingredients', 'can',    7,   9,   3,   4,   210.00, 'Davao Dairy Farms'),
        ('Paper Cups 12oz',          NULL,                     'Supplies',    'pcs',    450, 600, 380, 200, 3.50,   'PackRight Davao'),
        ('Paper Cups 16oz',          NULL,                     'Supplies',    'pcs',    380, 500, 150, 200, 4.20,   'PackRight Davao'),
        ('Cup Lids',                 NULL,                     'Supplies',    'pcs',    80,  700, 420, 200, 1.80,   'PackRight Davao'),
        ('Paper Straws',             NULL,                     'Supplies',    'pcs',    900, 800, 650, 300, 0.90,   'PackRight Davao'),
        ('Butter Croissant',         'Butter Croissant',       'Pastries',    'pcs',    24,  30,  18,  8,   45.00,  'Golden Crust Bakery'),
        ('Chocolate Muffin',         'Chocolate Muffin',       'Pastries',    'pcs',    3,   20,  14,  5,   35.00,  'Golden Crust Bakery'),
        ('Blueberry Muffin',         'Blueberry Muffin',       'Pastries',    'pcs',    0,   15,  4,   5,   38.00,  'Golden Crust Bakery'),
        ('Chocolate Chip Cookies',   'Chocolate Chip Cookies', 'Pastries',    'pack',   35,  40,  30,  10,  25.00,  'Golden Crust Bakery'),
        ('Cheesecake',               'Cheesecake',             'Pastries',    'slice',  12,  16,  10,  5,   60.00,  'Sweet Spot Davao'),
        ('Mineral Water',            'Mineral Water',          'Non-Coffee',  'bottle', 60,  80,  50,  15,  12.00,  'Aqua Supply Davao')
    ) AS v (ItemName, MenuItem, Category, Unit, QtyPob, QtyLan, QtyMat, Reorder, Cost, Supplier)
    CROSS JOIN dbo.Branches b
    LEFT JOIN dbo.MenuItems m ON m.ItemName = v.MenuItem;

    -- 11. Stock movements (30): deliveries in, preparation / spoilage out ---------------------------------
    INSERT INTO dbo.StockMovements (InventoryId, MovementType, Quantity, Reason, MovementDate, UserId, CreatedAt)
    SELECT i.InventoryId, v.MType, v.Qty, v.Reason, DATEADD(DAY, -v.DaysAgo, @Today), mgr.ManagerUserId, DATEADD(HOUR, 9, CAST(DATEADD(DAY, -v.DaysAgo, @Today) AS datetime2))
    FROM (VALUES
        (@Pob, 'Espresso Beans (Mt. Apo)', 'In',  5,   'Supplier delivery',        40), (@Pob, 'Fresh Milk',            'In',  30,  'Supplier delivery',       38),
        (@Pob, 'Fresh Milk',               'Out', 6,   'Used in preparation',      36), (@Pob, 'Butter Croissant',      'In',  30,  'Supplier delivery',       35),
        (@Pob, 'Butter Croissant',         'Out', 4,   'Spoilage (past best-by)',  33), (@Pob, 'Paper Cups 12oz',       'In',  500, 'Supplier delivery',       31),
        (@Pob, 'Chocolate Muffin',         'Out', 3,   'Wastage (dropped tray)',   20), (@Pob, 'Oat Milk',              'Out', 4,   'Used in preparation',     12),
        (@Pob, 'Cup Lids',                 'Out', 120, 'Used for takeout orders',  6),  (@Pob, 'Vanilla Syrup',         'Out', 2,   'Used in preparation',     3),
        (@Lan, 'Espresso Beans (Mt. Apo)', 'In',  8,   'Supplier delivery',        39), (@Lan, 'Condensed Milk',        'In',  24,  'Supplier delivery',       37),
        (@Lan, 'Matcha Powder',            'In',  2,   'Supplier delivery',        34), (@Lan, 'Tapioca Pearls',        'Out', 3,   'Used in preparation',     30),
        (@Lan, 'Cheesecake',               'Out', 2,   'Spoilage',                 27), (@Lan, 'Paper Cups 16oz',       'In',  400, 'Supplier delivery',       25),
        (@Lan, 'Whipped Cream',            'Out', 2,   'Expired',                  18), (@Lan, 'Chocolate Muffin',      'In',  20,  'Supplier delivery',       10),
        (@Lan, 'Black Tea Leaves',         'Out', 1,   'Used in preparation',      5),  (@Lan, 'Mineral Water',         'In',  48,  'Supplier delivery',       2),
        (@Mat, 'Fresh Milk',               'In',  24,  'Supplier delivery',        41), (@Mat, 'Davao Tablea Chocolate','In',  3,   'Supplier delivery',       36),
        (@Mat, 'Almond Milk',              'Out', 3,   'Used in preparation',      29), (@Mat, 'Blueberry Muffin',      'Out', 6,   'Spoilage (past best-by)', 24),
        (@Mat, 'Hazelnut Syrup',           'Out', 2,   'Used in preparation',      19), (@Mat, 'Paper Cups 16oz',       'Out', 150, 'Transfer to Lanang',      15),
        (@Mat, 'Whipped Cream',            'Out', 3,   'Used in preparation',      9),  (@Mat, 'Cheesecake',            'In',  12,  'Supplier delivery',       7),
        (@Mat, 'White Sugar',              'In',  10,  'Supplier delivery',        4),  (@Mat, 'Matcha Powder',         'Out', 1,   'Count correction',        1)
    ) AS v (BranchId, ItemName, MType, Qty, Reason, DaysAgo)
    INNER JOIN dbo.Inventory i ON i.BranchId = v.BranchId AND i.ItemName = v.ItemName
    INNER JOIN dbo.Branches mgr ON mgr.BranchId = v.BranchId;

    -- 12. Orders (90 over the last 60 days, all Paid; the 5 most recent are from the last few hours) -----------
    DECLARE @O TABLE (n int PRIMARY KEY, BranchId int, CustomerId int NULL, StaffUserId int, OrderType nvarchar(10),
                      Status nvarchar(20), OrderDate datetime2, TableNumber int NULL, PromoId int NULL, OrderId int NULL);
    ;WITH N AS (SELECT TOP (90) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n FROM sys.all_objects)
    INSERT INTO @O (n, BranchId, CustomerId, StaffUserId, OrderType, Status, OrderDate, TableNumber, PromoId)
    SELECT n.n,
           br.BranchId,
           CASE WHEN n.n % 4 = 0 THEN NULL ELSE (SELECT CustomerId FROM @Cust WHERE Seq = ((n.n * 7) % 25) + 1) END,
           st.UserId,
           CASE WHEN n.n % 3 = 0 THEN 'Takeout' ELSE 'Dine-In' END,
           'Paid',   -- staff take payment when the order is placed, so every order is Paid
           -- spread from 59 days ago to today, 7 AM - 8 PM; the last 5 are within the last few hours
           CASE WHEN n.n > 85 THEN DATEADD(MINUTE, -(91 - n.n) * 17, @Now)
                ELSE DATEADD(MINUTE, ((n.n * 37) % 60), DATEADD(HOUR, 7 + (n.n * 5) % 13, CAST(DATEADD(DAY, -(59 - ((n.n - 1) * 59 / 84)), @Today) AS datetime2))) END,
           CASE WHEN n.n % 3 = 0 THEN NULL ELSE (n.n % 12) + 1 END,
           NULL
    FROM N n
    CROSS APPLY (SELECT CASE n.n % 3 WHEN 0 THEN @Pob WHEN 1 THEN @Lan ELSE @Mat END AS BranchId) br
    CROSS APPLY (SELECT TOP 1 s.UserId FROM @Staff s WHERE s.BranchId = br.BranchId AND s.Seq = 1 + (n.n / 3) % 2) st;

    -- No order dated in the future (today's early-hour slots could land later than "now").
    UPDATE @O SET OrderDate = DATEADD(MINUTE, -n, @Now) WHERE OrderDate > @Now;


    -- Insert the orders and remember which new OrderId belongs to which generated order number n.
    DECLARE @O_Map TABLE (n int PRIMARY KEY, OrderId int);
    MERGE dbo.Orders AS target
    USING @O AS src ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (BranchId, CustomerId, StaffUserId, OrderType, Status, Subtotal, Discount, Total, PointsEarned, OrderDate, TableNumber, PromotionId)
        VALUES (src.BranchId, src.CustomerId, src.StaffUserId, src.OrderType, src.Status, 0, 0, 0, 0, src.OrderDate, src.TableNumber, NULL)
    OUTPUT src.n, inserted.OrderId INTO @O_Map (n, OrderId);
    UPDATE o SET OrderId = m.OrderId FROM @O o INNER JOIN @O_Map m ON m.n = o.n;

    -- Order items: 1 to 5 different items per order, qty 1-3; drinks are Regular or Large (+PHP 20).
    ;WITH J AS (SELECT j FROM (VALUES (1), (2), (3), (4), (5)) AS v (j))
    INSERT INTO dbo.OrderItems (OrderId, MenuItemId, Quantity, UnitPrice, Size)
    SELECT o.OrderId, m.MenuItemId, 1 + (o.n + j.j) % 3,
           m.Price + CASE WHEN m.Category IN ('Coffee', 'Non-Coffee', 'Tea') AND (o.n + j.j) % 2 = 0 THEN 20 ELSE 0 END,
           CASE WHEN m.Category IN ('Coffee', 'Non-Coffee', 'Tea') THEN CASE WHEN (o.n + j.j) % 2 = 0 THEN 'Large' ELSE 'Regular' END END
    FROM @O o
    INNER JOIN J j ON j.j <= 1 + (o.n % 5)
    INNER JOIN @Menu m ON m.Seq = ((o.n * 13 + j.j * 11) % @MenuCount) + 1;

    -- Subtotal = sum of the order's lines.
    UPDATE o SET Subtotal = s.Sub
    FROM dbo.Orders o INNER JOIN (SELECT OrderId, SUM(LineTotal) AS Sub FROM dbo.OrderItems GROUP BY OrderId) s ON s.OrderId = o.OrderId;

    -- Promos on some orders (each only inside its valid dates), then Discount, Total and points earned.
    UPDATE o SET PromotionId = CASE
            WHEN x.n % 7 = 0 THEN @PromoStudent
            WHEN x.n % 11 = 0 AND o.Subtotal >= 150 THEN @PromoMerienda
            WHEN x.n % 5 = 0 AND CAST(o.OrderDate AS date) <= DATEADD(DAY, -30, @Today) THEN @PromoKadayawan
        END
    FROM dbo.Orders o INNER JOIN @O x ON x.OrderId = o.OrderId;

    UPDATE o SET Discount = CASE
            WHEN p.DiscountType = 'Fixed' THEN CASE WHEN p.DiscountValue > o.Subtotal THEN o.Subtotal ELSE p.DiscountValue END
            WHEN p.DiscountType = 'Percentage' THEN ROUND(o.Subtotal * p.DiscountValue / 100.0, 2)
            ELSE 0 END
    FROM dbo.Orders o LEFT JOIN dbo.Promotions p ON p.PromotionId = o.PromotionId;

    -- Loyalty rule used by the app: 1 point per PHP 10 paid (members only, paid orders only).
    UPDATE dbo.Orders SET
        Total = Subtotal - Discount,
        PointsEarned = CASE WHEN Status = 'Paid' AND CustomerId IS NOT NULL THEN FLOOR((Subtotal - Discount) / 10) ELSE 0 END;

    -- 13. Payments: exactly one per Paid order; amounts match the order total --------------------------------
    INSERT INTO dbo.Payments (OrderId, Method, AmountPaid, ChangeAmount, PaidAt, CardType, CardLast4, Provider, ReferenceNumber, ReceivedByUserId)
    SELECT o.OrderId, pm.Method,
           CASE WHEN pm.Method = 'Cash' THEN pm.Tendered ELSE o.Total END,
           CASE WHEN pm.Method = 'Cash' THEN pm.Tendered - o.Total ELSE 0 END,
           CASE WHEN DATEADD(MINUTE, 6 + x.n % 15, o.OrderDate) > @Now THEN @Now ELSE DATEADD(MINUTE, 6 + x.n % 15, o.OrderDate) END,
           CASE WHEN pm.Method = 'Card' THEN CHOOSE(x.n % 4 + 1, 'Visa', 'Mastercard', 'JCB', 'BancNet Debit') END,
           CASE WHEN pm.Method = 'Card' THEN RIGHT('0000' + CAST((x.n * 7919) % 10000 AS varchar(4)), 4) END,
           CASE WHEN pm.Method = 'E-Wallet' THEN CASE WHEN x.n % 2 = 0 THEN 'GCash' ELSE 'Maya' END END,
           CASE pm.Method
               WHEN 'Card' THEN 'APV' + RIGHT('000000' + CAST((x.n * 48271) % 1000000 AS varchar(6)), 6)
               WHEN 'E-Wallet' THEN CASE WHEN x.n % 2 = 0
                    THEN '10' + RIGHT('00000000000' + CAST((CAST(x.n AS bigint) * 982451653) % 100000000000 AS varchar(11)), 11)   -- GCash: 13 digits
                    ELSE 'MY' + RIGHT('0000000000' + CAST((CAST(x.n AS bigint) * 674506111) % 10000000000 AS varchar(10)), 10) END  -- Maya
           END,
           o.StaffUserId
    FROM dbo.Orders o
    INNER JOIN @O x ON x.OrderId = o.OrderId
    CROSS APPLY (SELECT CASE x.n % 3 WHEN 1 THEN 'Cash' WHEN 2 THEN 'E-Wallet' ELSE 'Card' END AS Method,
                        -- cash: rounded up to the next PHP 100, sometimes plus a PHP 500 bill
                        CEILING(o.Total / 100) * 100 + CASE WHEN x.n % 4 = 0 THEN 500 ELSE 0 END AS Tendered) pm
    WHERE o.Status = 'Paid';

    -- 14. Reward redemptions (10): each costs no more than the customer's opening balance --------------------
    INSERT INTO dbo.RewardRedemptions (CustomerId, RewardId, PromotionId, PointsRedeemed, RedeemedAt, OrderId, RedemptionCode, ProcessedByUserId)
    SELECT c.CustomerId, r.RewardId, CASE WHEN v.Item = 'Free Pastry (Points)' THEN @PromoPoints END,
           COALESCE(r.PointsRequired, 150),
           DATEADD(HOUR, 10 + v.DaysAgo % 8, CAST(DATEADD(DAY, -v.DaysAgo, @Today) AS datetime2)),
           NULL,
           'RD-' + FORMAT(DATEADD(DAY, -v.DaysAgo, @Today), 'yyMMdd') + '-' + v.Suffix,
           b.ManagerUserId
    FROM (VALUES
        (4,  'P100 Discount',        52, 'K7Q2', 1), (16, 'P100 Discount',        44, 'M3VD', 2),
        (1,  'Free Pastry (Points)', 39, 'P8XT', 1), (24, 'Free Pastry (Points)', 33, 'H2NR', 3),
        (2,  'Free Brewed Coffee',   28, 'W5JC', 2), (12, 'Free Brewed Coffee',   21, 'B9LE', 3),
        (6,  'P50 Discount',         15, 'T4GZ', 1), (10, 'P50 Discount',         9,  'R6UA', 2),
        (14, 'Free Pastry',          5,  'C3YH', 3), (20, 'Free Pastry',          2,  'N8FW', 1)
    ) AS v (CustSeq, Item, DaysAgo, Suffix, BranchNo)
    INNER JOIN @Cust c ON c.Seq = v.CustSeq
    LEFT JOIN dbo.Rewards r ON r.RewardName = v.Item
    INNER JOIN dbo.Branches b ON b.BranchId = CHOOSE(v.BranchNo, @Pob, @Lan, @Mat);

    -- Final points balance = opening + earned on paid orders - redeemed.
    UPDATE c SET LoyaltyPoints = c.LoyaltyPoints
        + ISNULL((SELECT SUM(o.PointsEarned) FROM dbo.Orders o WHERE o.CustomerId = c.CustomerId), 0)
        - ISNULL((SELECT SUM(rr.PointsRedeemed) FROM dbo.RewardRedemptions rr WHERE rr.CustomerId = c.CustomerId), 0)
    FROM dbo.Customers c;

    -- 15. Feedback (20): all types and statuses, ratings for Feedback, notes on resolved / closed complaints -----
    INSERT INTO dbo.Feedback (CustomerId, BranchId, SubmittedByUserId, Type, Subject, Message, Rating, DateSubmitted, Status, AssignedTo, ResolutionNotes, DateResolved)
    SELECT c.CustomerId, br.BranchId, st.UserId, v.Type, v.Subject, v.Message, v.Rating,
           DATEADD(HOUR, 9 + v.DaysAgo % 10, CAST(DATEADD(DAY, -v.DaysAgo, @Today) AS datetime2)),
           v.Status,
           CASE WHEN v.Status = 'Open' AND v.DaysAgo < 3 THEN NULL ELSE br.ManagerUserId END,
           v.Notes,
           CASE WHEN v.Status IN ('Resolved', 'Closed')
                THEN DATEADD(HOUR, 9 + v.DaysAgo % 10, CAST(DATEADD(DAY, -v.DaysAgo + 1 + v.DaysAgo % 2, @Today) AS datetime2)) END
    FROM (VALUES
        (1,    1, 'Feedback',  'Best Spanish Latte in Poblacion', 'Perfect sweetness and the barista remembered my order.',          5,    'Closed',      45, 'Thanked the customer and shared with the team.'),
        (2,    2, 'Feedback',  'Cozy place to work',              'Strong Wi-Fi and enough outlets. Will come back.',               5,    'Resolved',    41, 'Thanked the customer.'),
        (NULL, 3, 'Complaint', 'Iced coffee was watery',          'Too much ice, the drink tasted diluted after 5 minutes.',        NULL, 'Resolved',    38, 'Replaced the drink; reminded staff of the ice-level standard.'),
        (5,    1, 'Inquiry',   'Nut-free pastries?',              'Which of your pastries are safe for nut allergies?',              NULL, 'Closed',      35, 'Gave the allergen list; croissant and ensaymada are nut-free.'),
        (6,    2, 'Complaint', 'Long waiting time',               'Waited 20 minutes for two drinks during lunch.',                  NULL, 'Closed',      31, 'Apologized and gave a free upgrade voucher; added a lunch-time barista.'),
        (NULL, 3, 'Feedback',  'Friendly staff',                  'Rhea was very accommodating with my special request.',            4,    'Closed',      28, NULL),
        (8,    1, 'Inquiry',   'Catering for 40 people',          'Do you accept coffee catering orders for an office event?',      NULL, 'Resolved',    25, 'Sent the catering package and quotation by email.'),
        (10,   2, 'Feedback',  'Birthday cake slice',             'Loved the free cheesecake on my birthday!',                       5,    'Resolved',    22, 'Thanked the customer.'),
        (NULL, 1, 'Complaint', 'Wrong order',                     'Received a hot latte instead of iced.',                           NULL, 'Resolved',    19, 'Remade the drink immediately and apologized.'),
        (12,   3, 'Feedback',  'Matcha could be stronger',        'Taste is good but I prefer a stronger matcha flavor.',            3,    'In Progress', 16, NULL),
        (14,   1, 'Inquiry',   'Loyalty points expiry',           'Do my loyalty points expire?',                                    NULL, 'In Progress', 13, NULL),
        (NULL, 2, 'Complaint', 'Dirty table',                     'The table near the window was sticky when we sat down.',          NULL, 'In Progress', 11, NULL),
        (16,   3, 'Feedback',  'Great pasta',                     'Chicken pesto pasta was fresh and filling.',                      4,    'Open',        9,  NULL),
        (18,   1, 'Inquiry',   'Senior citizen discount',         'Is the 20% senior discount applied to promo items?',             NULL, 'Open',        7,  NULL),
        (NULL, 3, 'Complaint', 'Card machine down',               'Could not pay by card; had to look for an ATM.',                  NULL, 'Open',        6,  NULL),
        (20,   2, 'Feedback',  'Chamomile tea',                   'Relaxing, but the cup was only half full.',                       2,    'Open',        5,  NULL),
        (22,   1, 'Feedback',  'Ensaymada is a must-try',         'Soft and cheesy, like my lola used to make.',                     5,    'Open',        4,  NULL),
        (NULL, 3, 'Inquiry',   'Parking',                         'Is there parking for customers at the Matina branch?',            NULL, 'Open',        2,  NULL),
        (24,   2, 'Complaint', 'Milk tea too sweet',              'Asked for 50% sugar but it tasted like 100%.',                   NULL, 'Open',        1,  NULL),
        (25,   1, 'Feedback',  'Quick takeout',                   'Order was ready in 3 minutes. Very efficient!',                   5,    'Open',        0,  NULL)
    ) AS v (CustSeq, BranchNo, Type, Subject, Message, Rating, Status, DaysAgo, Notes)
    LEFT JOIN @Cust c ON c.Seq = v.CustSeq
    INNER JOIN dbo.Branches br ON br.BranchId = CHOOSE(v.BranchNo, @Pob, @Lan, @Mat)
    CROSS APPLY (SELECT TOP 1 s.UserId FROM @Staff s WHERE s.BranchId = br.BranchId ORDER BY s.Seq) st;

    COMMIT TRANSACTION;
    PRINT 'Brew Haven sample data loaded successfully.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT 'Seed FAILED and was rolled back. Nothing was changed.';
    THROW;
END CATCH;

/* =================================================================================================
   SECTION 3 - VERIFICATION (read-only)
   ================================================================================================= */
SELECT 'Roles' AS [Table], COUNT(*) AS [Rows] FROM dbo.Roles
UNION ALL SELECT 'Branches', COUNT(*) FROM dbo.Branches
UNION ALL SELECT 'Users', COUNT(*) FROM dbo.Users
UNION ALL SELECT 'Subscriptions', COUNT(*) FROM dbo.Subscriptions
UNION ALL SELECT 'Customers', COUNT(*) FROM dbo.Customers
UNION ALL SELECT 'CustomerPreferences', COUNT(*) FROM dbo.CustomerPreferences
UNION ALL SELECT 'MenuItems', COUNT(*) FROM dbo.MenuItems
UNION ALL SELECT 'Promotions', COUNT(*) FROM dbo.Promotions
UNION ALL SELECT 'Rewards', COUNT(*) FROM dbo.Rewards
UNION ALL SELECT 'RewardRedemptions', COUNT(*) FROM dbo.RewardRedemptions
UNION ALL SELECT 'Inventory', COUNT(*) FROM dbo.Inventory
UNION ALL SELECT 'StockMovements', COUNT(*) FROM dbo.StockMovements
UNION ALL SELECT 'Orders', COUNT(*) FROM dbo.Orders
UNION ALL SELECT 'OrderItems', COUNT(*) FROM dbo.OrderItems
UNION ALL SELECT 'Payments', COUNT(*) FROM dbo.Payments
UNION ALL SELECT 'Feedback', COUNT(*) FROM dbo.Feedback;

-- Sanity check 1: every order's Subtotal equals the sum of its items, and Total = Subtotal - Discount (expect 0 rows)
SELECT o.OrderId, o.Subtotal, SUM(oi.LineTotal) AS ItemsTotal, o.Discount, o.Total
FROM dbo.Orders o LEFT JOIN dbo.OrderItems oi ON oi.OrderId = o.OrderId
GROUP BY o.OrderId, o.Subtotal, o.Discount, o.Total
HAVING o.Subtotal <> ISNULL(SUM(oi.LineTotal), 0) OR o.Total <> o.Subtotal - o.Discount;

-- Sanity check 2: every Paid order has exactly one payment covering its total; Pending orders have none (expect 0 rows)
SELECT o.OrderId, o.Status, COUNT(p.PaymentId) AS Payments, o.Total, SUM(p.AmountPaid - p.ChangeAmount) AS NetPaid
FROM dbo.Orders o LEFT JOIN dbo.Payments p ON p.OrderId = o.OrderId
GROUP BY o.OrderId, o.Status, o.Total
HAVING (o.Status = 'Paid' AND (COUNT(p.PaymentId) <> 1 OR SUM(p.AmountPaid - p.ChangeAmount) <> o.Total))
    OR (o.Status = 'Pending' AND COUNT(p.PaymentId) <> 0);

-- Sanity check 3: no negative stock and no negative loyalty points (expect 0 rows)
SELECT 'Inventory' AS Source, InventoryId AS Id, Quantity AS Value FROM dbo.Inventory WHERE Quantity < 0
UNION ALL SELECT 'Customer points', CustomerId, LoyaltyPoints FROM dbo.Customers WHERE LoyaltyPoints < 0;

-- Extra: what the test pages will show
SELECT Status, COUNT(*) AS Orders, SUM(Total) AS Total FROM dbo.Orders GROUP BY Status;
SELECT b.BranchName, COUNT(*) AS LowStockItems
FROM dbo.Inventory i INNER JOIN dbo.Branches b ON b.BranchId = i.BranchId
WHERE i.Quantity <= i.ReorderLevel GROUP BY b.BranchName;
