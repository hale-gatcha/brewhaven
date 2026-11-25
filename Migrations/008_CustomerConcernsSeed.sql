/* =================================================================================================
   008_CustomerConcernsSeed.sql  —  sample data for the Customer Concerns module.
   Run AFTER 007_CustomerConcerns.sql, on the tenant that has the 005_FullSeed sample users
   (staff.santos, manager.poblacion, ...). It skips itself on other tenants.
   - Fills Category for the 20 original seed rows (LoggedBy = SubmittedByUserId is already a Staff user,
     SubmittedVia defaults to 'Walk-in').
   - Adds 20 more rows across the 3 branches over the last 60 days with a mix of Type / Category / Status.
   Safe to re-run: the 20 new rows are inserted only once.
   ================================================================================================= */
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username = N'staff.santos')
BEGIN
    PRINT 'Skipped: the sample users from 005_FullSeed.sql are not in this database.';
    RETURN;
END

BEGIN TRAN;

-- 1) Categories for the original 20 rows (matched on Id + Subject, so edited rows are left alone)
UPDATE f SET f.Category = v.Category, f.SubmittedVia = N'Walk-in'
FROM dbo.Feedback f
JOIN (VALUES
    (1,  N'Best Spanish Latte in Poblacion', N'Product Quality'),
    (2,  N'Cozy place to work',              N'Ambiance'),
    (3,  N'Iced coffee was watery',          N'Product Quality'),
    (4,  N'Nut-free pastries?',              N'Product Quality'),
    (5,  N'Long waiting time',               N'Wait Time'),
    (6,  N'Friendly staff',                  N'Staff Behavior'),
    (7,  N'Catering for 40 people',          N'Service'),
    (8,  N'Birthday cake slice',             N'Service'),
    (9,  N'Wrong order',                     N'Service'),
    (10, N'Matcha could be stronger',        N'Product Quality'),
    (11, N'Loyalty points expiry',           N'Other'),
    (12, N'Dirty table',                     N'Cleanliness'),
    (13, N'Great pasta',                     N'Product Quality'),
    (14, N'Senior citizen discount',         N'Price'),
    (15, N'Card machine down',               N'Service'),
    (16, N'Chamomile tea',                   N'Product Quality'),
    (17, N'Ensaymada is a must-try',         N'Product Quality'),
    (18, N'Parking',                         N'Other'),
    (19, N'Milk tea too sweet',              N'Product Quality'),
    (20, N'Quick takeout',                   N'Wait Time')
) AS v(Id, Subject, Category) ON v.Id = f.FeedbackId AND f.Subject LIKE v.Subject + N'%';

-- 2) 20 more concerns (users and customers looked up by username / customer code)
IF NOT EXISTS (SELECT 1 FROM dbo.Feedback WHERE Subject = N'Order took 25 minutes')
BEGIN
    DECLARE @Today DATETIME2 = CAST(CAST(GETDATE() AS date) AS datetime2);

    INSERT INTO dbo.Feedback (CustomerId, BranchId, SubmittedByUserId, Type, Category, Subject, Message, Rating, DateSubmitted,
                              Status, ResolutionNotes, DateResolved, SubmittedVia, GuestName, GuestContact, UpdatedBy, UpdatedAt)
    SELECT c.CustomerId, lu.BranchId, lu.UserId, v.Type, v.Category, v.Subject, v.Message, v.Rating,
           DATEADD(HOUR, v.Hr, DATEADD(DAY, -v.DaysAgo, @Today)),
           v.Status, v.Resolution,
           CASE WHEN v.Status IN (N'Resolved', N'Closed') THEN DATEADD(HOUR, v.Hr + 3, DATEADD(DAY, -v.ResolvedDaysAgo, @Today)) END,
           N'Walk-in', v.GuestName, v.GuestContact, uu.UserId,
           CASE WHEN v.Status IN (N'Resolved', N'Closed') THEN DATEADD(HOUR, v.Hr + 3, DATEADD(DAY, -v.ResolvedDaysAgo, @Today))
                WHEN uu.UserId IS NOT NULL THEN DATEADD(HOUR, v.Hr + 4, DATEADD(DAY, -v.DaysAgo + 1, @Today)) END
    FROM (VALUES
        -- DaysAgo, Hr, LoggedBy, CustomerCode, GuestName, GuestContact, Type, Category, Subject, Message, Rating, Status, Resolution, ResolvedDaysAgo, UpdatedBy
        (58, 12, N'staff.santos',   N'BH-00003', NULL, NULL, N'Complaint', N'Wait Time', N'Order took 25 minutes',
             N'Customer waited almost 25 minutes for two iced lattes during the lunch rush.', NULL, N'Closed',
             N'Added a second barista on the 11am-2pm shift. Customer was given a free drink voucher.', 55, N'admin.garcia'),
        (55, 15, N'staff.mendoza',  NULL, N'Carlo', N'09181234567', N'Feedback', N'Ambiance', N'Love the new playlist',
             N'Guest said the acoustic playlist makes the place relaxing for studying.', 5, N'Closed',
             N'Thanked the guest and shared the comment with the team.', 54, N'admin.villanueva'),
        (52, 9,  N'staff.flores',   N'BH-00007', NULL, NULL, N'Complaint', N'Product Quality', N'Croissant was stale',
             N'The butter croissant tasted like it was baked the day before.', NULL, N'Resolved',
             N'Pastry delivery moved to 6am; day-old pastries are now pulled at closing.', 50, N'admin.garcia'),
        (49, 10, N'staff.cruz',     NULL, NULL, NULL, N'Inquiry', N'Price', N'Price of large hot drinks',
             N'Customer asked why large hot drinks cost PHP 20 more than the regular size.', NULL, N'Closed',
             N'Explained the cup sizes and added the size price list to the counter board.', 48, N'admin.garcia'),
        (45, 14, N'staff.bautista', N'BH-00011', NULL, NULL, N'Complaint', N'Cleanliness', N'Restroom had no soap',
             N'Customer reported the restroom soap dispenser was empty in the afternoon.', NULL, N'Resolved',
             N'Added restroom checks every 2 hours to the cleaning checklist.', 44, N'admin.villanueva'),
        (42, 8,  N'staff.santos',   N'BH-00013', NULL, NULL, N'Feedback', N'Product Quality', N'Spanish latte is consistent',
             N'Regular customer said the Spanish latte tastes the same every visit.', 5, N'Closed',
             N'Shared with the baristas.', 41, N'admin.garcia'),
        (38, 9,  N'staff.mendoza',  N'BH-00015', NULL, NULL, N'Complaint', N'Wait Time', N'Slow service on weekends',
             N'Customer said the Saturday morning line moved very slowly.', NULL, N'Resolved',
             N'Second register now opens on weekends from 8am to 12nn.', 35, N'admin.villanueva'),
        (35, 16, N'staff.flores',   NULL, N'Ana', NULL, N'Complaint', N'Staff Behavior', N'Cashier was not attentive',
             N'Guest said the cashier was on the phone while taking her order.', NULL, N'Closed',
             N'Talked with the staff member; phones are now kept in lockers during shifts.', 33, N'admin.garcia'),
        (31, 11, N'staff.cruz',     N'BH-00017', NULL, NULL, N'Inquiry', N'Service', N'Reserve a table for a meeting',
             N'Customer asked if they can reserve the big table for 8 people on Friday.', NULL, N'Resolved',
             N'Explained the walk-in only policy and suggested arriving before 2pm.', 31, N'admin.garcia'),
        (28, 8,  N'staff.bautista', N'BH-00019', NULL, NULL, N'Feedback', N'Wait Time', N'Fast morning service',
             N'Customer was happy her order was ready in under 5 minutes.', 4, N'Resolved',
             N'Noted and shared with the morning shift.', 27, N'admin.villanueva'),
        (25, 13, N'staff.mendoza',  NULL, N'Mark', N'09271112222', N'Complaint', N'Product Quality', N'Milk tea pearls too hard',
             N'Guest said the pearls in his milk tea were hard and undercooked.', NULL, N'In Progress',
             NULL, NULL, N'admin.villanueva'),
        (21, 17, N'staff.santos',   N'BH-00021', NULL, NULL, N'Complaint', N'Price', N'Charged for large instead of regular',
             N'Customer was charged for a large drink but ordered a regular.', NULL, N'Resolved',
             N'Refunded the PHP 20 difference; cashiers now confirm the size before payment.', 20, N'admin.garcia'),
        (18, 10, N'staff.flores',   N'BH-00023', NULL, NULL, N'Feedback', N'Service', N'Helpful barista recommendations',
             N'Customer appreciated the barista suggesting a less sweet drink.', 5, N'Open', NULL, NULL, NULL),
        (15, 18, N'staff.cruz',     NULL, NULL, NULL, N'Complaint', N'Wait Time', N'Takeout order not ready on time',
             N'Takeout order placed at the counter was still not ready 15 minutes after the promised time.', NULL, N'In Progress',
             NULL, NULL, N'admin.garcia'),
        (12, 15, N'staff.bautista', N'BH-00009', NULL, NULL, N'Feedback', N'Cleanliness', N'Tables could be cleaner',
             N'Customer said the tables were sticky during the afternoon rush.', 2, N'Open', NULL, NULL, NULL),
        (9,  11, N'staff.mendoza',  N'BH-00004', NULL, NULL, N'Inquiry', N'Product Quality', N'Do you have oat milk?',
             N'Customer asked if oat milk is available for all drinks.', NULL, N'Open', NULL, NULL, NULL),
        (7,  9,  N'staff.santos',   NULL, N'Liza', N'09175556666', N'Complaint', N'Cleanliness', N'Floor wet near entrance',
             N'Guest almost slipped on the wet floor near the door; no warning sign was placed.', NULL, N'Open', NULL, NULL, NULL),
        (5,  14, N'staff.flores',   N'BH-00006', NULL, NULL, N'Complaint', N'Product Quality', N'Hot chocolate lukewarm',
             N'Customer said the hot chocolate was only lukewarm when served.', NULL, N'Open', NULL, NULL, NULL),
        (3,  16, N'staff.cruz',     N'BH-00002', NULL, NULL, N'Feedback', N'Price', N'Good value merienda promo',
             N'Customer liked the merienda promo and said the price is fair.', 4, N'Open', NULL, NULL, NULL),
        (1,  12, N'staff.mendoza',  N'BH-00010', NULL, NULL, N'Complaint', N'Service', N'Order mixed up with another customer',
             N'Customer received another customer''s drink because the names on the cups were similar.', NULL, N'Open', NULL, NULL, NULL)
    ) AS v(DaysAgo, Hr, LoggedBy, CustomerCode, GuestName, GuestContact, Type, Category, Subject, Message, Rating, Status, Resolution, ResolvedDaysAgo, UpdatedBy)
    JOIN dbo.Users lu ON lu.Username = v.LoggedBy
    LEFT JOIN dbo.Users uu ON uu.Username = v.UpdatedBy
    LEFT JOIN dbo.Customers c ON c.CustomerCode = v.CustomerCode;
END

COMMIT;

SELECT Type, Category, COUNT(*) AS Concerns FROM dbo.Feedback GROUP BY Type, Category ORDER BY Type, Concerns DESC;
