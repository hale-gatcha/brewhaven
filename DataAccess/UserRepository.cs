using BrewHaven.Models;
using Microsoft.Data.SqlClient;

namespace BrewHaven.DataAccess;

public sealed class UserRepository
{
    public UserAccount? Authenticate(string username)
    {
        const string sql = """
            SELECT u.UserId,u.RoleId,u.BranchId,u.Username,u.FullName,u.PasswordHash,r.RoleName
            FROM Users u INNER JOIN Roles r ON r.RoleId=u.RoleId
            WHERE u.Username=@Username AND u.IsActive=1
            """;
        using SqlConnection c = DbHelper.CreateConnection();
        using SqlCommand cmd = new(sql, c);
        cmd.Parameters.Add("@Username", System.Data.SqlDbType.NVarChar, 50).Value = username.Trim();
        c.Open();
        using SqlDataReader r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new UserAccount
        {
            UserId = r.GetInt32(0), RoleId = r.GetInt32(1), BranchId = r.IsDBNull(2) ? null : r.GetInt32(2),
            Username = r.GetString(3), FullName = r.GetString(4), RoleName = r.GetString(6)
        };
    }

    public string? GetPasswordHash(string username)
    {
        const string sql = "SELECT PasswordHash FROM Users WHERE Username=@Username AND IsActive=1";
        using SqlConnection c = DbHelper.CreateConnection();
        using SqlCommand cmd = new(sql, c);
        cmd.Parameters.AddWithValue("@Username", username.Trim());
        c.Open();
        return cmd.ExecuteScalar()?.ToString();
    }
}
