namespace BrewHaven.Models;

public sealed class UserAccount
{
    public int UserId { get; set; }
    public int RoleId { get; set; }
    public int? BranchId { get; set; }
    public string RoleName { get; set; } = "";
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
}

public sealed class Customer
{
    public int CustomerId { get; set; }
    public string FullName { get; set; } = "";
}

public sealed class MenuItem
{
    public int MenuItemId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

// ---- Customer Concerns (feedback / inquiries / complaints) -------------------------------------

/// <summary>A new concern logged by Staff on a walk-in customer's behalf.</summary>
public sealed class NewConcern
{
    public int? CustomerId { get; set; }          // null = walk-in guest
    public string? GuestName { get; set; }
    public string? GuestContact { get; set; }     // 11 digits or null
    public string Type { get; set; } = "";        // Feedback / Inquiry / Complaint
    public string Category { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
    public int? Rating { get; set; }              // 1-5, Feedback only
}

/// <summary>Filters shared by the Admin list and the Manager monitoring page. Null / 0 = no filter.</summary>
public sealed class ConcernFilter
{
    public DateTime? From { get; set; }           // inclusive
    public DateTime? To { get; set; }             // inclusive (the whole day)
    public int BranchId { get; set; }             // 0 = all branches
    public string? Type { get; set; }
    public string? Status { get; set; }
    public string? Category { get; set; }
    public bool IncludeArchived { get; set; }
}

/// <summary>Everything shown in the read-only details pop-up and the Admin manage pop-up.</summary>
public sealed class ConcernDetails
{
    public int FeedbackId { get; set; }
    public string Type { get; set; } = "";
    public string Category { get; set; } = "";
    public string Status { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Message { get; set; } = "";
    public int? Rating { get; set; }
    public DateTime DateSubmitted { get; set; }
    public string SubmittedVia { get; set; } = "";
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = "";   // customer name, guest name, or "Walk-in guest"
    public string? CustomerContact { get; set; }
    public string? CustomerEmail { get; set; }
    public bool IsWalkInGuest { get; set; }
    public int BranchId { get; set; }
    public string BranchName { get; set; } = "";
    public string LoggedBy { get; set; } = "";
    public string? ResolutionNotes { get; set; }
    public string? InternalNotes { get; set; }
    public DateTime? DateResolved { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsArchived { get; set; }
}

/// <summary>Figures for the Manager monitoring page's summary cards.</summary>
public sealed class ConcernSummary
{
    public int Total { get; set; }
    public int Open { get; set; }
    public int InProgress { get; set; }
    public int ResolvedOrClosed { get; set; }
    public decimal? AverageRating { get; set; }
    public decimal? AverageResolutionDays { get; set; }
    public decimal ResolutionRate => Total == 0 ? 0 : Math.Round(ResolvedOrClosed * 100m / Total, 1);
}
