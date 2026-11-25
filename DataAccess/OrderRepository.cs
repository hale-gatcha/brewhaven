using Microsoft.Data.SqlClient;
using System.Data;
using System.Security.Cryptography;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.DataAccess;

public sealed class PaymentInfo
{
    public string Method { get; set; } = "";
    public decimal AmountPaid { get; set; }
    public decimal ChangeAmount { get; set; }
    public string? CardType { get; set; }
    public string? CardLast4 { get; set; }
    public string? Provider { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime PaidAt { get; set; }
    public string? ReceivedBy { get; set; }
}

/// <summary>Everything about one order: header, items and (if paid) the payment. Used by details, payment and receipt.</summary>
public sealed class OrderInfo
{
    public int OrderId { get; set; }
    public DateTime OrderDate { get; set; }
    public int BranchId { get; set; }
    public string BranchName { get; set; } = "";
    public string? BranchAddress { get; set; }
    public string? BranchContact { get; set; }
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = "Walk-in guest";
    public string? CustomerCode { get; set; }
    public int CustomerPoints { get; set; }
    public string StaffName { get; set; } = "";
    public string OrderType { get; set; } = "";
    public int? TableNumber { get; set; }
    public string Status { get; set; } = "";
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public int PointsEarned { get; set; }
    public int? PromotionId { get; set; }
    public string? PromotionName { get; set; }
    /// <summary>Columns: MenuItemId, Item, Size, Qty, Unit Price, Line Total.</summary>
    public DataTable Items { get; set; } = new();
    public PaymentInfo? Payment { get; set; }
    public string? RedemptionNote { get; set; }
}

/// <summary>One cart line of an order that hasn't been saved yet.</summary>
public sealed record OrderDraftLine(int MenuItemId, string Name, string? Size, int Qty, decimal UnitPrice)
{
    public decimal LineTotal => UnitPrice * Qty;
}

/// <summary>
/// The cart from Place Order, handed to the payment pop-up. Nothing is in the database until the payment
/// succeeds; the order, items, payment, stock and points are then saved together.
/// </summary>
public sealed class OrderDraft
{
    public int BranchId { get; init; }
    public string BranchName { get; init; } = "";
    public int? CustomerId { get; init; }
    public string CustomerName { get; init; } = "Walk-in guest";
    public int CustomerPoints { get; init; }
    public string OrderType { get; init; } = "Dine-In";   // "Dine-In" or "Takeout"
    public int? TableNumber { get; init; }
    public int? PromotionId { get; init; }                  // promo picked on the Place Order page (can be changed at payment)
    public List<OrderDraftLine> Lines { get; init; } = [];
    public decimal Subtotal => Lines.Sum(l => l.LineTotal);
}

public static class OrderRepository
{
    /// <summary>
    /// Throws InvalidOperationException (with a message for the user) if any menu item in the cart is unavailable
    /// or doesn't have enough stock at the branch. Run inside the saving transaction so nothing can change in between.
    /// </summary>
    public static void CheckAvailability(SqlTransaction tx, int branchId, IEnumerable<OrderDraftLine> lines)
    {
        foreach (var item in lines.GroupBy(l => (l.MenuItemId, l.Name)))
        {
            DataTable check = Query(tx, """
                SELECT m.IsAvailable,
                       (SELECT SUM(i.Quantity) FROM Inventory i WITH (UPDLOCK) WHERE i.MenuItemId = m.MenuItemId AND i.BranchId = @BranchId)
                FROM MenuItems m WHERE m.MenuItemId = @Id
                """, P("@Id", SqlDbType.Int, item.Key.MenuItemId), P("@BranchId", SqlDbType.Int, branchId));
            if (check.Rows.Count == 0 || !(bool)check.Rows[0][0])
                throw new InvalidOperationException($"{item.Key.Name} is no longer available. Remove it from the cart.");
            int needed = item.Sum(l => l.Qty);
            if (check.Rows[0][1] is int stock && stock < needed)
                throw new InvalidOperationException(stock <= 0
                    ? $"{item.Key.Name} is out of stock. Remove it from the cart."
                    : $"Only {stock} {item.Key.Name} left in stock (the cart has {needed}).");
        }
    }

    /// <summary>Same check without saving anything (used before opening the payment pop-up).</summary>
    public static void CheckAvailability(int branchId, IEnumerable<OrderDraftLine> lines)
    {
        using SqlConnection connection = CreateConnection();
        connection.Open();
        using SqlTransaction tx = connection.BeginTransaction();
        CheckAvailability(tx, branchId, lines);
        tx.Rollback();   // read-only: release the locks
    }

    /// <summary>Loyalty rule: 1 point for every PHP 10 paid.</summary>
    public static int PointsFor(decimal total) => (int)Math.Floor(Math.Max(0m, total) / 10m);

    /// <summary>Discount a promotion gives on a subtotal (never more than the subtotal).</summary>
    public static decimal PromoDiscount(string discountType, decimal discountValue, decimal subtotal) =>
        Math.Min(subtotal, discountType == "Fixed" ? discountValue : decimal.Round(subtotal * discountValue / 100m, 2));

    /// <summary>A short human-friendly code, e.g. RD-261124-K7Q2.</summary>
    public static string NewCode(string prefix)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var suffix = new string(Enumerable.Range(0, 4).Select(_ => chars[RandomNumberGenerator.GetInt32(chars.Length)]).ToArray());
        return $"{prefix}-{DateTime.Now:yyMMdd}-{suffix}";
    }

    public static OrderInfo? Load(int orderId)
    {
        DataTable h = Query("""
            SELECT o.OrderId, o.OrderDate, o.BranchId, b.BranchName, b.Address, b.ContactNumber,
                   o.CustomerId, c.FullName, c.CustomerCode, ISNULL(c.LoyaltyPoints, 0), u.FullName,
                   o.OrderType, o.TableNumber, o.Status, o.Subtotal, o.Discount, o.Total, o.PointsEarned,
                   o.PromotionId, p.PromotionName
            FROM Orders o
            INNER JOIN Branches b ON b.BranchId = o.BranchId
            INNER JOIN Users u ON u.UserId = o.StaffUserId
            LEFT JOIN Customers c ON c.CustomerId = o.CustomerId
            LEFT JOIN Promotions p ON p.PromotionId = o.PromotionId
            WHERE o.OrderId = @Id
            """, P("@Id", SqlDbType.Int, orderId));
        if (h.Rows.Count == 0) return null;
        DataRow r = h.Rows[0];
        var info = new OrderInfo
        {
            OrderId = (int)r[0], OrderDate = (DateTime)r[1], BranchId = (int)r[2], BranchName = (string)r[3],
            BranchAddress = r[4] as string, BranchContact = r[5] as string,
            CustomerId = r[6] as int?, CustomerName = r[7] as string ?? "Walk-in guest", CustomerCode = r[8] as string,
            CustomerPoints = (int)r[9], StaffName = (string)r[10], OrderType = (string)r[11], TableNumber = r[12] as int?,
            Status = (string)r[13], Subtotal = (decimal)r[14], Discount = (decimal)r[15], Total = (decimal)r[16],
            PointsEarned = (int)r[17], PromotionId = r[18] as int?, PromotionName = r[19] as string
        };

        info.Items = Query("""
            SELECT oi.MenuItemId, mi.ItemName AS Item, ISNULL(oi.Size, '-') AS Size, oi.Quantity AS Qty,
                   oi.UnitPrice AS [Unit Price], oi.LineTotal AS [Line Total]
            FROM OrderItems oi INNER JOIN MenuItems mi ON mi.MenuItemId = oi.MenuItemId
            WHERE oi.OrderId = @Id ORDER BY oi.OrderItemId
            """, P("@Id", SqlDbType.Int, orderId));

        DataTable pay = Query("""
            SELECT TOP 1 p.Method, p.AmountPaid, p.ChangeAmount, p.CardType, p.CardLast4, p.Provider, p.ReferenceNumber, p.PaidAt, u.FullName
            FROM Payments p LEFT JOIN Users u ON u.UserId = p.ReceivedByUserId
            WHERE p.OrderId = @Id ORDER BY p.PaidAt DESC
            """, P("@Id", SqlDbType.Int, orderId));
        if (pay.Rows.Count > 0)
        {
            DataRow p = pay.Rows[0];
            info.Payment = new PaymentInfo
            {
                Method = (string)p[0], AmountPaid = (decimal)p[1], ChangeAmount = (decimal)p[2], CardType = p[3] as string,
                CardLast4 = p[4] as string, Provider = p[5] as string, ReferenceNumber = p[6] as string,
                PaidAt = (DateTime)p[7], ReceivedBy = p[8] as string
            };
        }

        object? redemption = Scalar("""
            SELECT TOP 1 COALESCE(r.RewardName, pr.PromotionName) + ' (-' + CAST(rr.PointsRedeemed AS nvarchar(10)) + ' pts)'
            FROM RewardRedemptions rr LEFT JOIN Rewards r ON r.RewardId = rr.RewardId LEFT JOIN Promotions pr ON pr.PromotionId = rr.PromotionId
            WHERE rr.OrderId = @Id
            """, P("@Id", SqlDbType.Int, orderId));
        info.RedemptionNote = redemption as string;
        return info;
    }

    /// <summary>
    /// Takes the sold quantities of an order out of the branch's inventory (only for menu items that are linked
    /// to an inventory item) and records a Stock Out movement for each. Throws InvalidOperationException
    /// when there isn't enough stock, so the caller can roll back.
    /// </summary>
    public static void DeductStock(SqlTransaction tx, int orderId, int branchId, int userId)
    {
        DataTable sold = Query(tx, """
            SELECT oi.MenuItemId, mi.ItemName, SUM(oi.Quantity) AS Qty
            FROM OrderItems oi INNER JOIN MenuItems mi ON mi.MenuItemId = oi.MenuItemId
            WHERE oi.OrderId = @OrderId GROUP BY oi.MenuItemId, mi.ItemName
            """, P("@OrderId", SqlDbType.Int, orderId));

        foreach (DataRow item in sold.Rows)
        {
            int menuItemId = (int)item["MenuItemId"], needed = (int)item["Qty"];
            DataTable stock = Query(tx, """
                SELECT InventoryId, Quantity FROM Inventory WITH (UPDLOCK, ROWLOCK)
                WHERE BranchId = @BranchId AND MenuItemId = @MenuItemId ORDER BY Quantity DESC
                """, P("@BranchId", SqlDbType.Int, branchId), P("@MenuItemId", SqlDbType.Int, menuItemId));
            if (stock.Rows.Count == 0) continue;   // this menu item isn't stock-tracked at this branch

            int available = stock.Rows.Cast<DataRow>().Sum(s => (int)s["Quantity"]);
            if (available < needed)
                throw new InvalidOperationException($"Not enough stock for {item["ItemName"]}: {needed} needed, {available} left.");

            foreach (DataRow s in stock.Rows)
            {
                if (needed == 0) break;
                int take = Math.Min(needed, (int)s["Quantity"]);
                if (take == 0) continue;
                int inventoryId = (int)s["InventoryId"];
                Execute(tx, "UPDATE Inventory SET Quantity = Quantity - @Qty, UpdatedAt = SYSUTCDATETIME() WHERE InventoryId = @Id",
                    P("@Qty", SqlDbType.Int, take), P("@Id", SqlDbType.Int, inventoryId));
                Execute(tx, """
                    INSERT INTO StockMovements (InventoryId, MovementType, Quantity, Reason, MovementDate, UserId, OrderId)
                    VALUES (@Id, 'Out', @Qty, @Reason, CAST(GETDATE() AS date), @UserId, @OrderId)
                    """,
                    P("@Id", SqlDbType.Int, inventoryId), P("@Qty", SqlDbType.Int, take),
                    P("@Reason", SqlDbType.NVarChar, $"Sale - Order #{orderId}", 200),
                    P("@UserId", SqlDbType.Int, userId), P("@OrderId", SqlDbType.Int, orderId));
                needed -= take;
            }
        }
    }
}
