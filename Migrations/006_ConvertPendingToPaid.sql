/* Brew Haven - Staff no longer has a "Process Payment" page: orders are paid when they are placed.
   This converts every existing PENDING order into a PAID order so nothing is left waiting for payment:
     - adds one Cash payment per order (tendered = total rounded up to the next PHP 100, change computed),
     - marks the order Paid and gives members their loyalty points (1 point per PHP 10, same rule as the app).
   Stock is NOT deducted here: pending orders never reserved stock, and the sample-data inventory was set separately.
   Safe to run more than once (the second run finds no Pending orders). One batch, no GO lines, all-or-nothing. */

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @Pending TABLE (OrderId int PRIMARY KEY, CustomerId int NULL, Total decimal(12,2), StaffUserId int, OrderDate datetime2);
    INSERT INTO @Pending (OrderId, CustomerId, Total, StaffUserId, OrderDate)
    SELECT OrderId, CustomerId, Total, StaffUserId, OrderDate FROM dbo.Orders WITH (UPDLOCK) WHERE Status = 'Pending';

    -- Remove any stray payment rows on these orders first, so each ends up with exactly one.
    DELETE p FROM dbo.Payments p INNER JOIN @Pending x ON x.OrderId = p.OrderId;

    INSERT INTO dbo.Payments (OrderId, Method, AmountPaid, ChangeAmount, PaidAt, ReceivedByUserId)
    SELECT x.OrderId, 'Cash',
           CEILING(x.Total / 100) * 100,
           CEILING(x.Total / 100) * 100 - x.Total,
           CASE WHEN DATEADD(MINUTE, 5, x.OrderDate) > SYSDATETIME() THEN SYSDATETIME() ELSE DATEADD(MINUTE, 5, x.OrderDate) END,
           x.StaffUserId
    FROM @Pending x;

    UPDATE o SET Status = 'Paid',
                 PointsEarned = CASE WHEN o.CustomerId IS NULL THEN 0 ELSE FLOOR(o.Total / 10) END
    FROM dbo.Orders o INNER JOIN @Pending x ON x.OrderId = o.OrderId;

    UPDATE c SET LoyaltyPoints = c.LoyaltyPoints + p.Points
    FROM dbo.Customers c
    INNER JOIN (SELECT CustomerId, SUM(FLOOR(Total / 10)) AS Points FROM @Pending WHERE CustomerId IS NOT NULL GROUP BY CustomerId) p
        ON p.CustomerId = c.CustomerId;

    DECLARE @Converted int = (SELECT COUNT(*) FROM @Pending);
    COMMIT TRANSACTION;
    PRINT CONCAT(@Converted, ' pending order(s) converted to Paid.');
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT 'Conversion FAILED and was rolled back. Nothing was changed.';
    THROW;
END CATCH;

-- Verification (expect 0 rows in both)
SELECT OrderId, Status FROM dbo.Orders WHERE Status = 'Pending';
SELECT o.OrderId, COUNT(p.PaymentId) AS Payments
FROM dbo.Orders o LEFT JOIN dbo.Payments p ON p.OrderId = o.OrderId
WHERE o.Status = 'Paid'
GROUP BY o.OrderId
HAVING COUNT(p.PaymentId) <> 1;
