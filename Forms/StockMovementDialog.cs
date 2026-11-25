using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.Forms;

/// <summary>
/// Small pop-up for Stock In / Stock Out: quantity, reason and date. Updates the item's quantity and records a
/// StockMovements row in one transaction. Stock Out can't exceed the quantity on hand.
/// </summary>
public sealed class StockMovementDialog : PopupDialog
{
    private readonly int inventoryId;
    private readonly bool stockIn;
    private readonly string itemName;

    private readonly NumericUpDown numQuantity = new() { Minimum = 1, Maximum = 1_000_000, ThousandsSeparator = true, Value = 1 };
    private readonly TextBox txtReason = new() { MaxLength = 200, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.CustomSource };
    private readonly DateTimePicker dtpDate = new();

    public StockMovementDialog(int inventoryId, string itemName, int onHand, string unit, bool stockIn)
        : base(stockIn ? "Stock In" : "Stock Out", $"{(stockIn ? "Stock in" : "Stock out")}: {itemName}", stockIn ? "Add Stock" : "Remove Stock", 310)
    {
        this.inventoryId = inventoryId; this.stockIn = stockIn; this.itemName = itemName;
        if (!stockIn) numQuantity.Maximum = Math.Max(1, onHand);
        txtReason.AutoCompleteCustomSource.AddRange(stockIn
            ? ["Supplier delivery", "Transfer in", "Return to stock", "Count correction"]
            : ["Used in preparation", "Spoilage", "Wastage", "Transfer out", "Count correction"]);
        txtReason.Text = stockIn ? "Supplier delivery" : "Used in preparation";
        dtpDate.Value = DateTime.Today; dtpDate.MaxDate = DateTime.Today;

        AddRow(Note($"On hand: {onHand:N0} {unit}".TrimEnd()));
        AddField("QUANTITY *", numQuantity, 0);
        AddField("DATE *", dtpDate, 1);
        AddField("REASON *", txtReason, 0, span: true);

        BtnSave.Click += BtnSave_Click;
        Load += (_, _) => { numQuantity.Select(0, numQuantity.Text.Length); numQuantity.Focus(); };
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        int quantity = (int)numQuantity.Value;
        if (txtReason.Text.Trim().Length == 0) { Invalid("Enter a reason.", txtReason); return; }
        if (dtpDate.Value.Date > DateTime.Today) { Invalid("The date can't be in the future.", dtpDate); return; }
        try
        {
            using SqlConnection connection = CreateConnection();
            connection.Open();
            using SqlTransaction tx = connection.BeginTransaction();
            // Stock Out only succeeds while enough is on hand (checked in the same statement, so it can't go negative).
            int rows = Execute(tx, stockIn
                ? "UPDATE Inventory SET Quantity = Quantity + @Qty, UpdatedAt = SYSUTCDATETIME() WHERE InventoryId = @Id"
                : "UPDATE Inventory SET Quantity = Quantity - @Qty, UpdatedAt = SYSUTCDATETIME() WHERE InventoryId = @Id AND Quantity >= @Qty",
                P("@Qty", SqlDbType.Int, quantity), P("@Id", SqlDbType.Int, inventoryId));
            if (rows == 0)
            {
                int onHand = Convert.ToInt32(Scalar(tx, "SELECT ISNULL(MAX(Quantity), 0) FROM Inventory WHERE InventoryId = @Id", P("@Id", SqlDbType.Int, inventoryId)));
                Invalid($"Only {onHand:N0} of {itemName} on hand. You can't take out more than that.", numQuantity);
                return;
            }
            Execute(tx, """
                INSERT INTO StockMovements (InventoryId, MovementType, Quantity, Reason, MovementDate, UserId)
                VALUES (@Id, @Type, @Qty, @Reason, @Date, @UserId)
                """,
                P("@Id", SqlDbType.Int, inventoryId), P("@Type", SqlDbType.NVarChar, stockIn ? "In" : "Out", 3),
                P("@Qty", SqlDbType.Int, quantity), P("@Reason", SqlDbType.NVarChar, txtReason.Text.Trim(), 200),
                P("@Date", SqlDbType.Date, dtpDate.Value.Date), P("@UserId", SqlDbType.Int, Session.CurrentUser?.UserId));
            tx.Commit();
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) { ShowError("Could not record the stock movement.", ex); }
    }
}
