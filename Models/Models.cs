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
