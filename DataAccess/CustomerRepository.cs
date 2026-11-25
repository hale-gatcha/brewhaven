using Microsoft.Data.SqlClient;
using System.Data;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.DataAccess;

/// <summary>Customer record used by Customer Registration, Record Customer Information and Place Order.</summary>
public sealed class CustomerRecord
{
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public string ContactNumber { get; set; } = "";
    public string? Email { get; set; }
    public DateTime? Birthdate { get; set; }
    public string? Notes { get; set; }
    public int LoyaltyPoints { get; set; }
}

/// <summary>An existing customer that already uses a contact number or email.</summary>
public sealed record CustomerConflict(int CustomerId, string FullName, string CustomerCode, bool SameContact, bool SameEmail);

public static class CustomerRepository
{
    public static CustomerRecord? Load(int customerId)
    {
        DataTable t = Query("""
            SELECT CustomerId, ISNULL(CustomerCode, ''), FullName, ISNULL(ContactNumber, ''), Email, Birthdate, Notes, LoyaltyPoints
            FROM Customers WHERE CustomerId = @Id
            """, P("@Id", SqlDbType.Int, customerId));
        if (t.Rows.Count == 0) return null;
        DataRow r = t.Rows[0];
        return new CustomerRecord
        {
            CustomerId = (int)r[0], CustomerCode = (string)r[1], FullName = (string)r[2], ContactNumber = (string)r[3],
            Email = r[4] as string, Birthdate = r[5] is DateTime d ? d : null, Notes = r[6] as string, LoyaltyPoints = (int)r[7]
        };
    }

    /// <summary>Returns other customers (not <paramref name="excludeId"/>) that already use this contact number or email.</summary>
    public static List<CustomerConflict> FindConflicts(string contact, string? email, int excludeId)
    {
        DataTable t = Query("""
            SELECT CustomerId, FullName, ISNULL(CustomerCode, ''),
                   CASE WHEN ContactNumber = @Contact THEN 1 ELSE 0 END,
                   CASE WHEN @Email IS NOT NULL AND Email = @Email THEN 1 ELSE 0 END
            FROM Customers
            WHERE CustomerId <> @Id AND (ContactNumber = @Contact OR (@Email IS NOT NULL AND Email = @Email))
            """,
            P("@Contact", SqlDbType.NVarChar, contact, 11), P("@Email", SqlDbType.NVarChar, email, 254), P("@Id", SqlDbType.Int, excludeId));
        return t.Rows.Cast<DataRow>()
            .Select(r => new CustomerConflict((int)r[0], (string)r[1], (string)r[2], (int)r[3] == 1, (int)r[4] == 1))
            .ToList();
    }

    /// <summary>Inserts a customer with 0 loyalty points and an auto-generated code (BH-00001). Returns the new id.</summary>
    public static int Insert(CustomerRecord c)
    {
        object? id = Scalar("""
            DECLARE @NewId INT;
            INSERT INTO Customers (FullName, ContactNumber, Email, Birthdate, Notes, LoyaltyPoints, IsActive)
            VALUES (@Name, @Contact, @Email, @Birthdate, @Notes, 0, 1);
            SET @NewId = SCOPE_IDENTITY();
            UPDATE Customers SET CustomerCode = 'BH-' + RIGHT('00000' + CAST(@NewId AS varchar(10)), 5) WHERE CustomerId = @NewId;
            SELECT @NewId;
            """, Params(c));
        return Convert.ToInt32(id);
    }

    public static int Update(CustomerRecord c)
    {
        var ps = Params(c).Append(P("@Id", SqlDbType.Int, c.CustomerId)).ToArray();
        return Execute("""
            UPDATE Customers SET FullName = @Name, ContactNumber = @Contact, Email = @Email, Birthdate = @Birthdate, Notes = @Notes
            WHERE CustomerId = @Id
            """, ps);
    }

    /// <summary>Updates only name / contact / email (used by Record Customer Information). A blank email keeps the existing one.</summary>
    public static int UpdateBasic(int customerId, string name, string contact, string? email) => Execute(
        "UPDATE Customers SET FullName = @Name, ContactNumber = @Contact, Email = COALESCE(@Email, Email) WHERE CustomerId = @Id",
        P("@Name", SqlDbType.NVarChar, name, 100), P("@Contact", SqlDbType.NVarChar, contact, 11),
        P("@Email", SqlDbType.NVarChar, email, 254), P("@Id", SqlDbType.Int, customerId));

    /// <summary>
    /// Deletes a customer and their saved preferences. Throws SqlException 547 when they have orders,
    /// feedback or redemptions (history is kept).
    /// </summary>
    public static void Delete(int customerId)
    {
        using SqlConnection connection = CreateConnection();
        connection.Open();
        using SqlTransaction tx = connection.BeginTransaction();
        Execute(tx, "DELETE FROM CustomerPreferences WHERE CustomerId = @Id", P("@Id", SqlDbType.Int, customerId));
        Execute(tx, "DELETE FROM Customers WHERE CustomerId = @Id", P("@Id", SqlDbType.Int, customerId));
        tx.Commit();
    }

    private static SqlParameter[] Params(CustomerRecord c) =>
    [
        P("@Name", SqlDbType.NVarChar, c.FullName, 100),
        P("@Contact", SqlDbType.NVarChar, c.ContactNumber, 11),
        P("@Email", SqlDbType.NVarChar, string.IsNullOrWhiteSpace(c.Email) ? null : c.Email, 254),
        P("@Birthdate", SqlDbType.Date, c.Birthdate),
        P("@Notes", SqlDbType.NVarChar, string.IsNullOrWhiteSpace(c.Notes) ? null : c.Notes, 500)
    ];
}
