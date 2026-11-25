using BrewHaven.Helpers;
using BrewHaven.Models;
using Microsoft.Data.SqlClient;
using System.Data;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.DataAccess;

/// <summary>
/// Customer Concerns (feedback / inquiries / complaints). Every method checks the signed-in role, so a screen
/// can't do more than its role allows even if a button were shown by mistake:
///   Staff   - log a concern (own branch) and view the concerns they logged
///   Admin   - view all, manage (status / resolution / internal notes), archive / restore. No create.
///   Manager - view and monitor their own branch only. No create, edit or remove.
/// </summary>
public sealed class FeedbackRepository
{
    public static readonly string[] Types = ["Feedback", "Inquiry", "Complaint"];
    public static readonly string[] Statuses = ["Open", "In Progress", "Resolved", "Closed"];
    public static readonly string[] Categories =
        ["Service", "Product Quality", "Cleanliness", "Price", "Wait Time", "Ambiance", "Staff Behavior", "Other"];
    public const int MessageMinLength = 10;

    private const string Staff = "Staff", Admin = "Admin", Manager = "Manager";

    // ---- Role checks ---------------------------------------------------------------------------------

    private static UserAccount Require(params string[] roles)
    {
        var user = Session.CurrentUser ?? throw new UnauthorizedAccessException("You are not signed in.");
        if (!roles.Contains(user.RoleName))
            throw new UnauthorizedAccessException($"The {user.RoleName} role can't do this.");
        return user;
    }

    public static bool CurrentRoleIs(string role) => Session.CurrentUser?.RoleName == role;

    /// <summary>Manager: their own branch (-1 when they have none, so nothing matches). Admin: the requested branch.</summary>
    private static int ScopeBranch(UserAccount user, int requested) =>
        user.RoleName == Manager ? user.BranchId ?? -1 : requested;

    // ---- Shared SQL ------------------------------------------------------------------------------------

    private const string CustomerDisplaySql = """
        CASE WHEN c.CustomerId IS NOT NULL THEN c.FullName
             WHEN NULLIF(LTRIM(f.GuestName), '') IS NOT NULL THEN f.GuestName + ' (walk-in)'
             ELSE 'Walk-in guest' END
        """;

    private static readonly string ListSql = $"""
        SELECT f.FeedbackId, f.DateSubmitted, b.BranchName AS Branch, {CustomerDisplaySql} AS Customer,
               f.Type, f.Category, f.Subject, f.Rating, f.Status, f.IsArchived
        FROM Feedback f
        INNER JOIN Branches b ON b.BranchId = f.BranchId
        LEFT JOIN Customers c ON c.CustomerId = f.CustomerId
        WHERE (@IncludeArchived = 1 OR f.IsArchived = 0)
          AND (@From IS NULL OR f.DateSubmitted >= @From) AND (@To IS NULL OR f.DateSubmitted < @To)
          AND (@BranchId = 0 OR f.BranchId = @BranchId)
          AND (@Type IS NULL OR f.Type = @Type) AND (@Status IS NULL OR f.Status = @Status)
          AND (@Category IS NULL OR f.Category = @Category)
          AND (@LoggedBy IS NULL OR f.SubmittedByUserId = @LoggedBy)
        ORDER BY f.DateSubmitted DESC
        """;

    /// <summary>Monitoring rows: not archived, in the date range, branch / type / category filters.</summary>
    private const string MonitorWhere = """
        f.IsArchived = 0 AND f.DateSubmitted >= @From AND f.DateSubmitted < @To
        AND (@BranchId = 0 OR f.BranchId = @BranchId) AND (@Type IS NULL OR f.Type = @Type) AND (@Category IS NULL OR f.Category = @Category)
        """;

    private static SqlParameter[] FilterParams(ConcernFilter f, int branchId, int? loggedBy = null) =>
    [
        P("@IncludeArchived", SqlDbType.Bit, f.IncludeArchived),
        P("@From", SqlDbType.DateTime2, f.From?.Date),
        P("@To", SqlDbType.DateTime2, f.To?.Date.AddDays(1)),   // inclusive of the To day
        P("@BranchId", SqlDbType.Int, branchId),
        P("@Type", SqlDbType.NVarChar, f.Type, 12),
        P("@Status", SqlDbType.NVarChar, f.Status, 15),
        P("@Category", SqlDbType.NVarChar, f.Category, 30),
        P("@LoggedBy", SqlDbType.Int, loggedBy),
    ];

    /// <summary>Parameters for the monitoring queries (date range required; defaults to the last 30 days).</summary>
    private static SqlParameter[] MonitorParams(ConcernFilter f, int branchId) =>
    [
        P("@From", SqlDbType.DateTime2, (f.From ?? DateTime.Today.AddDays(-30)).Date),
        P("@To", SqlDbType.DateTime2, (f.To ?? DateTime.Today).Date.AddDays(1)),
        P("@BranchId", SqlDbType.Int, branchId),
        P("@Type", SqlDbType.NVarChar, f.Type, 12),
        P("@Category", SqlDbType.NVarChar, f.Category, 30),
    ];

    // ---- Staff -----------------------------------------------------------------------------------------

    /// <summary>Staff: the concerns this Staff user logged (archived ones hidden).</summary>
    public DataTable GetMyConcerns()
    {
        var user = Require(Staff);
        return Query(ListSql, FilterParams(new ConcernFilter(), 0, user.UserId));
    }

    /// <summary>Staff: active customers for the Customer dropdown.</summary>
    public DataTable GetActiveCustomers()
    {
        Require(Staff);
        return Query("SELECT CustomerId, FullName + ISNULL(' (' + CustomerCode + ')', '') AS Label FROM Customers WHERE IsActive = 1 ORDER BY FullName");
    }

    /// <summary>Staff: the name of the signed-in Staff user's branch, or null when they have none.</summary>
    public string? GetMyBranchName()
    {
        var user = Require(Staff);
        if (user.BranchId is not int branchId) return null;
        return Scalar("SELECT BranchName FROM Branches WHERE BranchId = @Id", P("@Id", SqlDbType.Int, branchId)) as string;
    }

    /// <summary>
    /// Staff: logs a concern for the Staff user's own branch. Status = Open, DateSubmitted = now, LoggedBy = current user,
    /// SubmittedVia = Walk-in. Returns the new FeedbackId.
    /// </summary>
    public int LogConcern(NewConcern c)
    {
        var user = Require(Staff);
        if (user.BranchId is not int branchId) throw new InvalidOperationException("Your account isn't assigned to a branch. Ask an Admin to set it.");

        string subject = c.Subject.Trim(), message = c.Message.Trim();
        string? guestName = c.CustomerId is null ? NullIfBlank(c.GuestName) : null;
        string? guestContact = c.CustomerId is null ? NullIfBlank(c.GuestContact) : null;
        if (!Types.Contains(c.Type)) throw new ArgumentException("Select a valid type.");
        if (!Categories.Contains(c.Category)) throw new ArgumentException("Select a valid category.");
        if (subject.Length == 0 || subject.Length > 150) throw new ArgumentException("Subject is required (up to 150 characters).");
        if (message.Length < MessageMinLength) throw new ArgumentException($"Message must be at least {MessageMinLength} characters.");
        if (c.Type == "Feedback" && c.Rating is not (>= 1 and <= 5)) throw new ArgumentException("Feedback needs a rating from 1 to 5.");
        if (guestContact is not null && !Validation.IsValidPhMobile(guestContact)) throw new ArgumentException("Contact number must be 11 digits starting with 09.");

        const string sql = """
            INSERT INTO Feedback (CustomerId, BranchId, SubmittedByUserId, Type, Category, Subject, Message, Rating,
                                  Status, DateSubmitted, SubmittedVia, GuestName, GuestContact)
            OUTPUT INSERTED.FeedbackId
            VALUES (@CustomerId, @BranchId, @UserId, @Type, @Category, @Subject, @Message, @Rating,
                    'Open', @Now, 'Walk-in', @GuestName, @GuestContact)
            """;
        return Convert.ToInt32(Scalar(sql,
            P("@CustomerId", SqlDbType.Int, c.CustomerId),
            P("@BranchId", SqlDbType.Int, branchId),
            P("@UserId", SqlDbType.Int, user.UserId),
            P("@Type", SqlDbType.NVarChar, c.Type, 12),
            P("@Category", SqlDbType.NVarChar, c.Category, 30),
            P("@Subject", SqlDbType.NVarChar, subject, 150),
            P("@Message", SqlDbType.NVarChar, message, -1),
            P("@Rating", SqlDbType.Int, c.Type == "Feedback" ? c.Rating : null),
            P("@Now", SqlDbType.DateTime2, DateTime.Now),
            P("@GuestName", SqlDbType.NVarChar, guestName, 100),
            P("@GuestContact", SqlDbType.NVarChar, guestContact, 11)));
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ---- Admin / Manager: list and details ---------------------------------------------------------------

    /// <summary>Admin: all branches (optionally archived). Manager: own branch only, never archived.</summary>
    public DataTable GetConcerns(ConcernFilter filter)
    {
        var user = Require(Admin, Manager);
        if (user.RoleName == Manager) filter.IncludeArchived = false;
        return Query(ListSql, FilterParams(filter, ScopeBranch(user, filter.BranchId)));
    }

    /// <summary>Branches for the filter dropdowns (Manager: only their own).</summary>
    public DataTable GetBranches()
    {
        var user = Require(Admin, Manager);
        return Query("SELECT BranchId, BranchName FROM Branches WHERE @BranchId = 0 OR BranchId = @BranchId ORDER BY BranchName",
            P("@BranchId", SqlDbType.Int, ScopeBranch(user, 0)));
    }

    /// <summary>
    /// Full details of one concern, or null when it doesn't exist or the role may not see it
    /// (Staff: only what they logged; Manager: only their branch; Admin: any).
    /// </summary>
    public ConcernDetails? GetDetails(int feedbackId)
    {
        var user = Require(Staff, Admin, Manager);
        string sql = $"""
            SELECT f.FeedbackId, f.Type, f.Category, f.Status, f.Subject, f.Message, f.Rating, f.DateSubmitted, f.SubmittedVia,
                   f.CustomerId, {CustomerDisplaySql} AS CustomerName,
                   COALESCE(c.ContactNumber, f.GuestContact) AS CustomerContact, c.Email AS CustomerEmail,
                   f.BranchId, b.BranchName, s.FullName AS LoggedBy,
                   f.ResolutionNotes, f.InternalNotes, f.DateResolved, u.FullName AS UpdatedBy, f.UpdatedAt, f.IsArchived
            FROM Feedback f
            INNER JOIN Branches b ON b.BranchId = f.BranchId
            LEFT JOIN Customers c ON c.CustomerId = f.CustomerId
            LEFT JOIN Users s ON s.UserId = f.SubmittedByUserId
            LEFT JOIN Users u ON u.UserId = f.UpdatedBy
            WHERE f.FeedbackId = @Id
              AND (@Role = 'Admin'
                   OR (@Role = 'Manager' AND f.BranchId = @BranchId AND f.IsArchived = 0)
                   OR (@Role = 'Staff' AND f.SubmittedByUserId = @UserId AND f.IsArchived = 0))
            """;
        DataTable t = Query(sql,
            P("@Id", SqlDbType.Int, feedbackId),
            P("@Role", SqlDbType.NVarChar, user.RoleName, 30),
            P("@BranchId", SqlDbType.Int, user.BranchId ?? -1),
            P("@UserId", SqlDbType.Int, user.UserId));
        if (t.Rows.Count == 0) return null;
        DataRow r = t.Rows[0];
        return new ConcernDetails
        {
            FeedbackId = (int)r["FeedbackId"], Type = (string)r["Type"], Category = (string)r["Category"], Status = (string)r["Status"],
            Subject = (string)r["Subject"], Message = (string)r["Message"], Rating = r["Rating"] as int?,
            DateSubmitted = (DateTime)r["DateSubmitted"], SubmittedVia = (string)r["SubmittedVia"],
            CustomerId = r["CustomerId"] as int?, CustomerName = (string)r["CustomerName"], IsWalkInGuest = r["CustomerId"] is DBNull,
            CustomerContact = r["CustomerContact"] as string, CustomerEmail = r["CustomerEmail"] as string,
            BranchId = (int)r["BranchId"], BranchName = (string)r["BranchName"], LoggedBy = r["LoggedBy"] as string ?? "-",
            ResolutionNotes = r["ResolutionNotes"] as string, InternalNotes = r["InternalNotes"] as string,
            DateResolved = r["DateResolved"] as DateTime?, UpdatedBy = r["UpdatedBy"] as string, UpdatedAt = r["UpdatedAt"] as DateTime?,
            IsArchived = (bool)r["IsArchived"],
        };
    }

    // ---- Admin: manage ----------------------------------------------------------------------------------

    /// <summary>
    /// Admin: updates only the management fields. A Complaint can't be Resolved / Closed without resolution notes.
    /// DateResolved is stamped when the status becomes Resolved / Closed (cleared if reopened); UpdatedBy / UpdatedAt are recorded.
    /// <paramref name="newInternalNote"/> (optional) is appended to the internal notes with a timestamp and the Admin's name.
    /// </summary>
    public void Manage(int feedbackId, string status, string? resolutionNotes, string? newInternalNote)
    {
        var user = Require(Admin);
        if (!Statuses.Contains(status)) throw new ArgumentException("Select a valid status.");
        string? resolution = NullIfBlank(resolutionNotes), note = NullIfBlank(newInternalNote);

        object? type = Scalar("SELECT Type FROM Feedback WHERE FeedbackId = @Id AND IsArchived = 0", P("@Id", SqlDbType.Int, feedbackId));
        if (type is null) throw new InvalidOperationException("This entry no longer exists or was removed.");
        if ((string)type == "Complaint" && status is "Resolved" or "Closed" && resolution is null)
            throw new ArgumentException("Add resolution notes before setting a complaint to Resolved or Closed.");

        DateTime now = DateTime.Now;
        string? stampedNote = note is null ? null : $"[{now:MMM d, yyyy h:mm tt}] {user.FullName}: {note}";
        const string sql = """
            UPDATE Feedback SET Status = @Status, ResolutionNotes = @Resolution,
                   InternalNotes = CASE WHEN @Note IS NULL THEN InternalNotes ELSE CONCAT(InternalNotes + CHAR(13) + CHAR(10), @Note) END,
                   DateResolved = CASE WHEN @Status IN ('Resolved', 'Closed') THEN COALESCE(DateResolved, @Now) ELSE NULL END,
                   UpdatedBy = @UserId, UpdatedAt = @Now
            WHERE FeedbackId = @Id AND IsArchived = 0
            """;
        int rows = Execute(sql,
            P("@Status", SqlDbType.NVarChar, status, 15),
            P("@Resolution", SqlDbType.NVarChar, resolution, -1),
            P("@Note", SqlDbType.NVarChar, stampedNote, -1),
            P("@Now", SqlDbType.DateTime2, now),
            P("@UserId", SqlDbType.Int, user.UserId),
            P("@Id", SqlDbType.Int, feedbackId));
        if (rows == 0) throw new InvalidOperationException("Nothing was saved: the entry no longer exists or was removed.");
    }

    /// <summary>Admin: soft delete (hidden by default, still counted in Reports) or restore.</summary>
    public void SetArchived(int feedbackId, bool archived)
    {
        var user = Require(Admin);
        int rows = Execute("UPDATE Feedback SET IsArchived = @Archived, UpdatedBy = @UserId, UpdatedAt = @Now WHERE FeedbackId = @Id",
            P("@Archived", SqlDbType.Bit, archived), P("@UserId", SqlDbType.Int, user.UserId),
            P("@Now", SqlDbType.DateTime2, DateTime.Now), P("@Id", SqlDbType.Int, feedbackId));
        if (rows == 0) throw new InvalidOperationException("This entry no longer exists.");
    }

    // ---- Manager: monitoring insights (Admin may read them too) -------------------------------------------

    public ConcernSummary GetSummary(ConcernFilter filter)
    {
        var user = Require(Manager, Admin);
        string sql = $"""
            SELECT COUNT(*) AS Total,
                   SUM(CASE WHEN f.Status = 'Open' THEN 1 ELSE 0 END) AS OpenCount,
                   SUM(CASE WHEN f.Status = 'In Progress' THEN 1 ELSE 0 END) AS InProgressCount,
                   SUM(CASE WHEN f.Status IN ('Resolved', 'Closed') THEN 1 ELSE 0 END) AS DoneCount,
                   AVG(CAST(f.Rating AS decimal(5, 2))) AS AvgRating,
                   AVG(CASE WHEN f.Status IN ('Resolved', 'Closed') AND f.DateResolved IS NOT NULL
                            THEN DATEDIFF(MINUTE, f.DateSubmitted, f.DateResolved) / 1440.0 END) AS AvgResolutionDays
            FROM Feedback f
            WHERE {MonitorWhere}
            """;
        DataRow r = Query(sql, MonitorParams(filter, ScopeBranch(user, filter.BranchId))).Rows[0];
        return new ConcernSummary
        {
            Total = Convert.ToInt32(r["Total"]),
            Open = r["OpenCount"] is DBNull ? 0 : Convert.ToInt32(r["OpenCount"]),
            InProgress = r["InProgressCount"] is DBNull ? 0 : Convert.ToInt32(r["InProgressCount"]),
            ResolvedOrClosed = r["DoneCount"] is DBNull ? 0 : Convert.ToInt32(r["DoneCount"]),
            AverageRating = r["AvgRating"] is DBNull ? null : Math.Round(Convert.ToDecimal(r["AvgRating"]), 2),
            AverageResolutionDays = r["AvgResolutionDays"] is DBNull ? null : Math.Round(Convert.ToDecimal(r["AvgResolutionDays"]), 1),
        };
    }

    /// <summary>Concerns per category with their share of the total, highest first.</summary>
    public DataTable GetCategoryBreakdown(ConcernFilter filter)
    {
        var user = Require(Manager, Admin);
        string sql = $"""
            SELECT f.Category, COUNT(*) AS Concerns,
                   CAST(COUNT(*) * 100.0 / NULLIF(SUM(COUNT(*)) OVER (), 0) AS decimal(5, 1)) AS Percentage,
                   SUM(CASE WHEN f.Type = 'Complaint' THEN 1 ELSE 0 END) AS Complaints
            FROM Feedback f
            WHERE {MonitorWhere}
            GROUP BY f.Category
            ORDER BY Concerns DESC, f.Category
            """;
        return Query(sql, MonitorParams(filter, ScopeBranch(user, filter.BranchId)));
    }

    /// <summary>Average rating (Feedback entries) per category, lowest first so weak spots are on top.</summary>
    public DataTable GetRatingByCategory(ConcernFilter filter)
    {
        var user = Require(Manager, Admin);
        string sql = $"""
            SELECT f.Category, COUNT(f.Rating) AS Ratings, CAST(AVG(CAST(f.Rating AS decimal(5, 2))) AS decimal(3, 2)) AS AverageRating
            FROM Feedback f
            WHERE {MonitorWhere} AND f.Rating IS NOT NULL
            GROUP BY f.Category
            ORDER BY AverageRating, f.Category
            """;
        return Query(sql, MonitorParams(filter, ScopeBranch(user, filter.BranchId)));
    }

    /// <summary>Average rating and open complaints per branch.</summary>
    public DataTable GetRatingByBranch(ConcernFilter filter)
    {
        var user = Require(Manager, Admin);
        string sql = $"""
            SELECT b.BranchName AS Branch, COUNT(*) AS Concerns, COUNT(f.Rating) AS Ratings,
                   CAST(AVG(CAST(f.Rating AS decimal(5, 2))) AS decimal(3, 2)) AS AverageRating,
                   SUM(CASE WHEN f.Type = 'Complaint' AND f.Status IN ('Open', 'In Progress') THEN 1 ELSE 0 END) AS OpenComplaints
            FROM Feedback f INNER JOIN Branches b ON b.BranchId = f.BranchId
            WHERE {MonitorWhere}
            GROUP BY b.BranchName
            ORDER BY b.BranchName
            """;
        return Query(sql, MonitorParams(filter, ScopeBranch(user, filter.BranchId)));
    }

    /// <summary>Top 5 complaint categories (ignores the Type filter: always complaints).</summary>
    public DataTable GetTopComplaintCategories(ConcernFilter filter)
    {
        var user = Require(Manager, Admin);
        const string sql = """
            SELECT TOP 5 f.Category, COUNT(*) AS Complaints,
                   SUM(CASE WHEN f.Status IN ('Open', 'In Progress') THEN 1 ELSE 0 END) AS Unresolved
            FROM Feedback f
            WHERE f.IsArchived = 0 AND f.Type = 'Complaint' AND f.DateSubmitted >= @From AND f.DateSubmitted < @To
              AND (@BranchId = 0 OR f.BranchId = @BranchId) AND (@Category IS NULL OR f.Category = @Category)
            GROUP BY f.Category
            ORDER BY Complaints DESC, Unresolved DESC, f.Category
            """;
        return Query(sql, MonitorParams(filter, ScopeBranch(user, filter.BranchId)));
    }

    /// <summary>
    /// "Needs attention": Open / In Progress concerns submitted more than 3 days ago, oldest first.
    /// Ignores the date range on purpose (an old open complaint still needs attention).
    /// </summary>
    public DataTable GetNeedsAttention(ConcernFilter filter)
    {
        var user = Require(Manager, Admin);
        const string sql = """
            SELECT f.FeedbackId, f.DateSubmitted, b.BranchName AS Branch, f.Type, f.Category, f.Subject, f.Status,
                   DATEDIFF(DAY, f.DateSubmitted, @Now) AS DaysOpen
            FROM Feedback f
            INNER JOIN Branches b ON b.BranchId = f.BranchId
            WHERE f.IsArchived = 0 AND f.Status IN ('Open', 'In Progress') AND f.DateSubmitted < DATEADD(DAY, -3, @Now)
              AND (@BranchId = 0 OR f.BranchId = @BranchId) AND (@Type IS NULL OR f.Type = @Type) AND (@Category IS NULL OR f.Category = @Category)
            ORDER BY f.DateSubmitted
            """;
        return Query(sql,
            P("@Now", SqlDbType.DateTime2, DateTime.Now),
            P("@BranchId", SqlDbType.Int, ScopeBranch(user, filter.BranchId)),
            P("@Type", SqlDbType.NVarChar, filter.Type, 12),
            P("@Category", SqlDbType.NVarChar, filter.Category, 30));
    }

    /// <summary>Count of Feedback / Inquiry / Complaint per week (Monday start) or per month.</summary>
    public DataTable GetTrend(ConcernFilter filter, bool monthly)
    {
        var user = Require(Manager, Admin);
        string period = monthly
            ? "DATEFROMPARTS(YEAR(f.DateSubmitted), MONTH(f.DateSubmitted), 1)"
            : "DATEADD(DAY, -((DATEPART(WEEKDAY, f.DateSubmitted) + @@DATEFIRST - 2) % 7), CAST(f.DateSubmitted AS date))";
        string sql = $"""
            SELECT {period} AS Period,
                   SUM(CASE WHEN f.Type = 'Feedback' THEN 1 ELSE 0 END) AS Feedback,
                   SUM(CASE WHEN f.Type = 'Inquiry' THEN 1 ELSE 0 END) AS Inquiry,
                   SUM(CASE WHEN f.Type = 'Complaint' THEN 1 ELSE 0 END) AS Complaint,
                   COUNT(*) AS Total
            FROM Feedback f
            WHERE {MonitorWhere}
            GROUP BY {period}
            ORDER BY Period
            """;
        return Query(sql, MonitorParams(filter, ScopeBranch(user, filter.BranchId)));
    }

    /// <summary>All concerns for the monitoring list (same filters as the insights, never archived).</summary>
    public DataTable GetMonitorList(ConcernFilter filter)
    {
        Require(Manager, Admin);
        var f = new ConcernFilter
        {
            From = filter.From ?? DateTime.Today.AddDays(-30), To = filter.To ?? DateTime.Today,
            BranchId = filter.BranchId, Type = filter.Type, Category = filter.Category, IncludeArchived = false
        };
        return GetConcerns(f);
    }
}
