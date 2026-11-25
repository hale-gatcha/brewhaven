/* Brew Haven - menu, promos, rewards, sample customers and inventory for testing the Manager and Staff pages.
   Run AFTER 003_ManagerStaff.sql (or after logging in to the app once, which applies 003 automatically).
   Safe to run more than once: rows that already exist (same name) are skipped. No GO lines: paste as one block.
   Prices are in Philippine pesos. */

SET NOCOUNT ON;

-- 1. Use the six Place Order categories for the original starter items.
UPDATE MenuItems SET Category = 'Pastries'   WHERE Category = 'Pastry';
UPDATE MenuItems SET Category = 'Meals'      WHERE Category = 'Food';
UPDATE MenuItems SET Category = 'Non-Coffee' WHERE Category = 'Beverage';

-- 2. Menu (50+ items). IsAvailable = 1 except one seasonal item, used to test that unavailable items are blocked.
INSERT INTO MenuItems (ItemName, Category, Description, Price, IsAvailable)
SELECT v.ItemName, v.Category, v.Description, v.Price, v.IsAvailable
FROM (VALUES
    -- Coffee
    ('Espresso',              'Coffee',     'Double shot of our house espresso',                95,  1),
    ('Americano',             'Coffee',     'Espresso with hot water',                          110, 1),
    ('Cappuccino',            'Coffee',     'Espresso, steamed milk and thick foam',            140, 1),
    ('Cafe Latte',            'Coffee',     'Espresso and steamed milk',                        140, 1),
    ('Flat White',            'Coffee',     'Ristretto with velvety microfoam',                 145, 1),
    ('Caramel Macchiato',     'Coffee',     'Vanilla milk, espresso and caramel drizzle',       165, 1),
    ('Cafe Mocha',            'Coffee',     'Espresso, chocolate and steamed milk',             155, 1),
    ('Spanish Latte',         'Coffee',     'Espresso with sweetened condensed milk',           160, 1),
    ('Vanilla Latte',         'Coffee',     'Latte with vanilla syrup',                         155, 1),
    ('Hazelnut Latte',        'Coffee',     'Latte with hazelnut syrup',                        155, 1),
    ('Iced Coffee',           'Coffee',     'Brewed coffee over ice',                           120, 1),
    ('Iced Mocha',            'Coffee',     'Chocolate espresso drink over ice',                165, 1),
    ('Cold Brew',             'Coffee',     'Slow-steeped for 18 hours',                        150, 1),
    ('Brewed Coffee',         'Coffee',     'Classic house coffee',                             95,  1),
    ('Ube Latte (Seasonal)',  'Coffee',     'Seasonal: currently unavailable',                  175, 0),
    -- Non-Coffee
    ('Matcha Latte',          'Non-Coffee', 'Japanese matcha with milk',                        160, 1),
    ('Hot Chocolate',         'Non-Coffee', 'Rich Davao tablea chocolate',                      130, 1),
    ('Iced Chocolate',        'Non-Coffee', 'Chocolate milk over ice',                          140, 1),
    ('Strawberry Milk',       'Non-Coffee', 'Fresh milk with strawberry puree',                 135, 1),
    ('Cookies & Cream Frappe','Non-Coffee', 'Blended cookies and cream',                        175, 1),
    ('Fresh Lemonade',        'Non-Coffee', 'Freshly squeezed calamansi-lemon',                 110, 1),
    ('Mineral Water',         'Non-Coffee', 'Bottled drinking water',                           45,  1),
    -- Tea
    ('Classic Milk Tea',      'Tea',        'Black tea with milk',                              120, 1),
    ('Wintermelon Milk Tea',  'Tea',        'Wintermelon syrup and milk tea',                   125, 1),
    ('Taro Milk Tea',         'Tea',        'Creamy taro milk tea',                             130, 1),
    ('Passion Fruit Tea',     'Tea',        'Green tea with passion fruit',                     115, 1),
    ('Lychee Fruit Tea',      'Tea',        'Jasmine tea with lychee',                          115, 1),
    ('Earl Grey Tea',         'Tea',        'Hot bergamot black tea',                           100, 1),
    ('Chamomile Tea',         'Tea',        'Caffeine-free herbal tea',                         100, 1),
    -- Pastries
    ('Butter Croissant',      'Pastries',   'Flaky all-butter croissant',                       95,  1),
    ('Chocolate Muffin',      'Pastries',   'Double chocolate muffin',                          85,  1),
    ('Blueberry Muffin',      'Pastries',   'Muffin with blueberries',                          85,  1),
    ('Chocolate Chip Cookies','Pastries',   'Two soft-baked cookies',                           65,  1),
    ('Cheesecake',            'Pastries',   'Creamy baked cheesecake',                          120, 1),
    ('Ensaymada',             'Pastries',   'Buttery brioche with cheese and sugar',            75,  1),
    ('Cinnamon Roll',         'Pastries',   'Cinnamon roll with cream cheese icing',            95,  1),
    ('Banana Bread',          'Pastries',   'Moist banana bread slice',                         85,  1),
    -- Meals
    ('Ham & Cheese Sandwich', 'Meals',      'Toasted sandwich',                                 150, 1),
    ('Clubhouse Sandwich',    'Meals',      'Chicken, ham, egg and bacon with fries',           195, 1),
    ('Tuna Melt Sandwich',    'Meals',      'Tuna and melted cheese on sourdough',              175, 1),
    ('Chicken Pesto Pasta',   'Meals',      'Penne in basil pesto with grilled chicken',        245, 1),
    ('Carbonara',             'Meals',      'Creamy bacon carbonara',                           235, 1),
    ('Chicken Adobo Rice Bowl','Meals',     'Classic adobo with garlic rice',                   210, 1),
    ('Tapsilog',              'Meals',      'Beef tapa, garlic rice and fried egg',             225, 1),
    ('Longsilog',             'Meals',      'Sweet longganisa, garlic rice and fried egg',      195, 1),
    -- Add-ons
    ('Extra Espresso Shot',   'Add-ons',    'Add a shot to any drink',                          40,  1),
    ('Oat Milk',              'Add-ons',    'Swap to oat milk',                                 35,  1),
    ('Almond Milk',           'Add-ons',    'Swap to almond milk',                              35,  1),
    ('Whipped Cream',         'Add-ons',    'Topping',                                          25,  1),
    ('Caramel Drizzle',       'Add-ons',    'Topping',                                          20,  1),
    ('Vanilla Syrup',         'Add-ons',    'Extra pump of vanilla',                            20,  1),
    ('Tapioca Pearls',        'Add-ons',    'For milk teas',                                    20,  1)
) AS v (ItemName, Category, Description, Price, IsAvailable)
WHERE NOT EXISTS (SELECT 1 FROM MenuItems m WHERE m.ItemName = v.ItemName);

-- 3. Promos. Dates are relative to today so they work whenever the script is run.
DECLARE @Today date = CAST(GETDATE() AS date);
DECLARE @Main int = (SELECT TOP 1 BranchId FROM Branches WHERE BranchName = 'Main Branch');

INSERT INTO Promotions (PromotionName, Description, DiscountPercent, DiscountType, DiscountValue, PointsRequired, StartDate, EndDate, BranchId, IsActive)
SELECT v.Name, v.Description, v.Pct, v.DType, v.DValue, v.Points, v.StartDate, v.EndDate, v.BranchId, v.IsActive
FROM (VALUES
    ('Student Treat',         '10% off for students with a valid ID',              10, 'Percentage', 10, NULL, DATEADD(MONTH, -1, @Today), DATEADD(MONTH, 3, @Today), NULL,  1),
    ('Merienda Combo',        'PHP 50 off any drink + pastry',                      0, 'Fixed',      50, NULL, DATEADD(MONTH, -1, @Today), DATEADD(MONTH, 2, @Today), NULL,  1),
    ('Main Branch Exclusive', '20% off at Main Branch only',                       20, 'Percentage', 20, NULL, DATEADD(DAY, -7, @Today),   DATEADD(MONTH, 1, @Today), @Main, 1),
    ('Free Pastry (Points)',  'Redeem 150 points for PHP 95 off (one pastry)',      0, 'Fixed',      95, 150,  DATEADD(MONTH, -1, @Today), DATEADD(MONTH, 6, @Today), NULL,  1),
    ('Rainy Day Special',     'EXPIRED: 15% off hot drinks (to test expiry)',      15, 'Percentage', 15, NULL, DATEADD(MONTH, -2, @Today), DATEADD(DAY, -10, @Today), NULL,  1),
    ('Holiday Blend Promo',   'INACTIVE: 12% off (to test inactive promos)',       12, 'Percentage', 12, NULL, DATEADD(MONTH, -1, @Today), DATEADD(MONTH, 1, @Today), NULL,  0)
) AS v (Name, Description, Pct, DType, DValue, Points, StartDate, EndDate, BranchId, IsActive)
WHERE NOT EXISTS (SELECT 1 FROM Promotions p WHERE p.PromotionName = v.Name);

-- 4. Rewards (redeemed with loyalty points).
INSERT INTO Rewards (RewardName, PointsRequired, DiscountValue, IsActive)
SELECT v.Name, v.Points, v.Value, 1
FROM (VALUES ('Free Brewed Coffee', 80, 95), ('P50 Discount', 100, 50), ('P100 Discount', 200, 100), ('Free Pastry', 120, 95)) AS v (Name, Points, Value)
WHERE NOT EXISTS (SELECT 1 FROM Rewards r WHERE r.RewardName = v.Name);

-- 5. Sample customers (codes are generated below, the same way the app does).
INSERT INTO Customers (FullName, ContactNumber, Email, LoyaltyPoints, IsActive, Birthdate, Notes)
SELECT v.Name, v.Contact, v.Email, v.Points, 1, v.Birthdate, v.Notes
FROM (VALUES
    ('Juan Dela Cruz',     '09171230001', 'juan.delacruz@example.test', 250, CAST('1995-03-14' AS date), 'Prefers oat milk'),
    ('Maria Clara Santos', '09171230002', 'maria.santos@example.test',  90,  CAST('1998-07-02' AS date), 'Less ice'),
    ('Jose Rizal Mercado', '09171230003', NULL,                         0,   NULL,                       NULL),
    ('Andrea Bautista',    '09171230004', 'andrea.b@example.test',      160, CAST('2001-11-20' AS date), 'Usually takeout'),
    ('Paolo Villanueva',   '09171230005', 'paolo.v@example.test',       40,  NULL,                       'Allergic to nuts')
) AS v (Name, Contact, Email, Points, Birthdate, Notes)
WHERE NOT EXISTS (SELECT 1 FROM Customers c WHERE c.ContactNumber = v.Contact);

UPDATE Customers SET CustomerCode = 'BH-' + RIGHT('00000' + CAST(CustomerId AS varchar(10)), 5) WHERE CustomerCode IS NULL;

-- 6. Inventory at Main Branch: some items linked to the menu (deducted when sold), some plain ingredients.
--    Chocolate Muffin and Oat Milk are low, Blueberry Muffin is out of stock (to test highlighting and blocking).
INSERT INTO Inventory (BranchId, MenuItemId, ItemName, Category, Unit, Quantity, ReorderLevel, UnitCost, Supplier)
SELECT @Main, (SELECT TOP 1 MenuItemId FROM MenuItems WHERE ItemName = v.MenuItem), v.ItemName, v.Category, v.Unit, v.Qty, v.Reorder, v.Cost, v.Supplier
FROM (VALUES
    ('Butter Croissant',       'Butter Croissant',       'Pastries',    'pcs',    12, 5,  45.00, 'Golden Crust Bakery'),
    ('Chocolate Muffin',       'Chocolate Muffin',       'Pastries',    'pcs',    3,  5,  35.00, 'Golden Crust Bakery'),
    ('Blueberry Muffin',       'Blueberry Muffin',       'Pastries',    'pcs',    0,  5,  38.00, 'Golden Crust Bakery'),
    ('Chocolate Chip Cookies', 'Chocolate Chip Cookies', 'Pastries',    'pack',   30, 10, 25.00, 'Golden Crust Bakery'),
    ('Ensaymada',              'Ensaymada',              'Pastries',    'pcs',    8,  4,  30.00, 'Lola''s Ensaymada'),
    ('Mineral Water',          'Mineral Water',          'Non-Coffee',  'bottle', 50, 10, 12.00, 'Aqua Supply PH'),
    ('Espresso Beans',         NULL,                     'Ingredients', 'kg',     5,  2,  950.00, 'Benguet Coffee Co.'),
    ('Fresh Milk',             NULL,                     'Ingredients', 'L',      20, 8,  95.00, 'Dairy Fresh PH'),
    ('Oat Milk (carton)',      NULL,                     'Ingredients', 'L',      4,  6,  180.00, 'Dairy Fresh PH'),
    ('Paper Cups 12oz',        NULL,                     'Supplies',    'pcs',    300, 100, 3.50, 'PackRight')
) AS v (ItemName, MenuItem, Category, Unit, Qty, Reorder, Cost, Supplier)
WHERE @Main IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM Inventory i WHERE i.BranchId = @Main AND i.ItemName = v.ItemName);

-- 7. Summary
SELECT Category, COUNT(*) AS MenuItems FROM MenuItems GROUP BY Category ORDER BY Category;
