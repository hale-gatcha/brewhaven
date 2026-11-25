using Microsoft.Data.SqlClient;

namespace BrewHaven.DataAccess;

/// <summary>One shop (tenant) listed in MasterCRM, with the connection string of its own database.</summary>
public sealed record Tenant(int TenantId, string TenantCode, string ShopName, string ConnectionString)
{
    public override string ToString() => ShopName;   // shown in the login screen's Shop dropdown
}

/// <summary>Reads the shop directory from the MasterCRM database.</summary>
public sealed class TenantRepository
{
    public List<Tenant> GetActiveTenants()
    {
        const string sql = """
            SELECT TenantId, TenantCode, ShopName, ServerName, DatabaseName, DbUser, DbPassword
            FROM dbo.Tenants WHERE IsActive = 1 ORDER BY ShopName
            """;
        var tenants = new List<Tenant>();
        using SqlConnection connection = DbHelper.CreateMasterConnection();
        using SqlCommand command = new(sql, connection);
        connection.Open();
        using SqlDataReader r = command.ExecuteReader();
        while (r.Read())
        {
            var cs = new SqlConnectionStringBuilder
            {
                DataSource = r.GetString(3),
                InitialCatalog = r.GetString(4),
                UserID = r.GetString(5),
                Password = r.GetString(6),
                Encrypt = true,
                TrustServerCertificate = true,
                MultipleActiveResultSets = true,
            };
            tenants.Add(new Tenant(r.GetInt32(0), r.GetString(1), r.GetString(2), cs.ConnectionString));
        }
        return tenants;
    }
}
