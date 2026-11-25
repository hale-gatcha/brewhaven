using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.Forms;

/// <summary>
/// Pop-up for creating ("Save") and editing ("Update") an inventory item. Quantity is set only when creating
/// (recorded as a Stock In movement); afterwards it changes through Stock In / Stock Out.
/// </summary>
public sealed class InventoryDialog : PopupDialog
{
    private readonly int? inventoryId;
    private int? loadedBranchId, loadedMenuItemId;

    private readonly TextBox txtItemName = new() { MaxLength = 100 };
    private readonly TextBox txtCategory = new() { MaxLength = 40, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.CustomSource };
    private readonly TextBox txtUnit = new() { MaxLength = 20, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.CustomSource };
    private readonly NumericUpDown numQuantity = new() { Minimum = 0, Maximum = 1_000_000, ThousandsSeparator = true };
    private readonly NumericUpDown numReorder = new() { Minimum = 0, Maximum = 1_000_000, ThousandsSeparator = true, Value = 5 };
    private readonly NumericUpDown numUnitCost = new() { Minimum = 0, Maximum = 1_000_000, DecimalPlaces = 2, ThousandsSeparator = true };
    private readonly ComboBox cboBranch = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboMenuItem = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtSupplier = new() { MaxLength = 100 };

    public InventoryDialog(int? inventoryId = null)
        : base(inventoryId is null ? "Create Inventory Item" : "Edit Inventory Item", inventoryId is null ? "New inventory item" : "Edit inventory item",
               inventoryId is null ? "Save" : "Update", 600)
    {
        this.inventoryId = inventoryId;
        txtUnit.AutoCompleteCustomSource.AddRange(["pcs", "kg", "g", "L", "ml", "pack", "box", "bottle", "bag", "slice"]);

        AddField("ITEM NAME *", txtItemName, 0);
        AddField("CATEGORY *", txtCategory, 1);
        AddField("UNIT *", txtUnit, 0);
        AddField(inventoryId is null ? "STARTING QUANTITY" : "QUANTITY (USE STOCK IN / OUT)", numQuantity, 1);
        AddField("REORDER LEVEL *", numReorder, 0);
        AddField("UNIT COST (PHP)", numUnitCost, 1);
        AddField("BRANCH *", cboBranch, 0);
        AddField("SUPPLIER (OPTIONAL)", txtSupplier, 1);
        AddField("LINKED MENU ITEM (OPTIONAL)", cboMenuItem, 0, span: true);
        AddRow(Note("Link a menu item to deduct this stock automatically when that item is sold, and to block ordering it when it runs out."));
        numQuantity.Enabled = inventoryId is null;

        BtnSave.Click += BtnSave_Click;
        Load += (_, _) =>
        {
            bool ok = (inventoryId is null || LoadRecord(inventoryId.Value)) && LoadLookups();
            if (!ok) CancelAfterLoad(); else txtItemName.Focus();
        };
    }

    private bool LoadRecord(int id)
    {
        try
        {
            DataTable t = Query("""
                SELECT ItemName, Category, Unit, Quantity, ReorderLevel, UnitCost, BranchId, Supplier, MenuItemId
                FROM Inventory WHERE InventoryId = @Id
                """, P("@Id", SqlDbType.Int, id));
            if (t.Rows.Count == 0)
            {
                MessageBox.Show("This item no longer exists.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            DataRow r = t.Rows[0];
            txtItemName.Text = (string)r[0]; txtCategory.Text = (string)r[1]; txtUnit.Text = r[2] as string ?? "";
            numQuantity.Value = Math.Clamp((int)r[3], 0, (int)numQuantity.Maximum);
            numReorder.Value = Math.Clamp((int)r[4], 0, (int)numReorder.Maximum);
            numUnitCost.Value = r[5] is decimal cost ? Math.Clamp(cost, 0, numUnitCost.Maximum) : 0;
            loadedBranchId = (int)r[6]; txtSupplier.Text = r[7] as string ?? ""; loadedMenuItemId = r[8] as int?;
            return true;
        }
        catch (Exception ex) { ShowError("Could not load the item.", ex); return false; }
    }

    private bool LoadLookups()
    {
        try
        {
            DataTable branches = Query("SELECT BranchId, BranchName FROM Branches WHERE IsActive = 1 OR BranchId = @Current ORDER BY BranchName",
                P("@Current", SqlDbType.Int, loadedBranchId));
            cboBranch.DisplayMember = "BranchName"; cboBranch.ValueMember = "BranchId"; cboBranch.DataSource = branches;
            int? defaultBranch = loadedBranchId ?? Session.CurrentUser?.BranchId;
            if (defaultBranch is int b && branches.Select($"BranchId = {b}").Length > 0) cboBranch.SelectedValue = b;

            DataTable menu = Query("SELECT MenuItemId, ItemName + ' (' + Category + ')' AS Name FROM MenuItems ORDER BY ItemName");
            DataRow none = menu.NewRow(); none["MenuItemId"] = 0; none["Name"] = "(Not linked to a menu item)";
            menu.Rows.InsertAt(none, 0);
            cboMenuItem.DisplayMember = "Name"; cboMenuItem.ValueMember = "MenuItemId"; cboMenuItem.DataSource = menu;
            cboMenuItem.SelectedValue = loadedMenuItemId ?? 0;

            foreach (DataRow r in Query("SELECT DISTINCT Category FROM Inventory UNION SELECT DISTINCT Category FROM MenuItems").Rows)
                txtCategory.AutoCompleteCustomSource.Add((string)r[0]);
            return true;
        }
        catch (Exception ex) { ShowError("Could not load branches and menu items.", ex); return false; }
    }

    private bool ValidateInputs()
    {
        string name = txtItemName.Text.Trim();
        if (name.Length == 0) return Invalid("Item name is required.", txtItemName);
        if (txtCategory.Text.Trim().Length == 0) return Invalid("Category is required.", txtCategory);
        if (txtUnit.Text.Trim().Length == 0) return Invalid("Unit is required (e.g. pcs, kg, L).", txtUnit);
        if (cboBranch.SelectedValue is null) return Invalid("Select a branch.", cboBranch);
        try
        {
            int duplicates = Convert.ToInt32(Scalar("SELECT COUNT(*) FROM Inventory WHERE ItemName = @Name AND BranchId = @BranchId AND InventoryId <> @Id",
                P("@Name", SqlDbType.NVarChar, name, 100), P("@BranchId", SqlDbType.Int, Convert.ToInt32(cboBranch.SelectedValue)),
                P("@Id", SqlDbType.Int, inventoryId ?? 0)));
            if (duplicates > 0) return Invalid($"\"{name}\" is already in this branch's inventory. Use Stock In to add to it.", txtItemName);
        }
        catch (Exception ex) { ShowError("Could not check for duplicates.", ex); return false; }
        return true;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (!ValidateInputs()) return;   // dialog stays open
        int menuItemId = Convert.ToInt32(cboMenuItem.SelectedValue ?? 0);
        string supplier = txtSupplier.Text.Trim();
        SqlParameter[] Fields() =>
        [
            P("@Name", SqlDbType.NVarChar, txtItemName.Text.Trim(), 100), P("@Category", SqlDbType.NVarChar, txtCategory.Text.Trim(), 40),
            P("@Unit", SqlDbType.NVarChar, txtUnit.Text.Trim(), 20), P("@Reorder", SqlDbType.Int, (int)numReorder.Value),
            P("@Cost", SqlDbType.Decimal, numUnitCost.Value > 0 ? numUnitCost.Value : null),
            P("@BranchId", SqlDbType.Int, Convert.ToInt32(cboBranch.SelectedValue)),
            P("@Supplier", SqlDbType.NVarChar, supplier.Length > 0 ? supplier : null, 100),
            P("@MenuItemId", SqlDbType.Int, menuItemId == 0 ? null : menuItemId)
        ];
        try
        {
            using SqlConnection connection = CreateConnection();
            connection.Open();
            using SqlTransaction tx = connection.BeginTransaction();
            if (inventoryId is null)
            {
                int quantity = (int)numQuantity.Value;
                int newId = Convert.ToInt32(Scalar(tx, """
                    INSERT INTO Inventory (ItemName, Category, Unit, Quantity, ReorderLevel, UnitCost, BranchId, Supplier, MenuItemId)
                    OUTPUT INSERTED.InventoryId
                    VALUES (@Name, @Category, @Unit, @Quantity, @Reorder, @Cost, @BranchId, @Supplier, @MenuItemId)
                    """, [.. Fields(), P("@Quantity", SqlDbType.Int, quantity)]));
                if (quantity > 0)
                    Execute(tx, """
                        INSERT INTO StockMovements (InventoryId, MovementType, Quantity, Reason, MovementDate, UserId)
                        VALUES (@Id, 'In', @Qty, 'Starting stock', CAST(GETDATE() AS date), @UserId)
                        """, P("@Id", SqlDbType.Int, newId), P("@Qty", SqlDbType.Int, quantity), P("@UserId", SqlDbType.Int, Session.CurrentUser?.UserId));
            }
            else
            {
                int rows = Execute(tx, """
                    UPDATE Inventory SET ItemName = @Name, Category = @Category, Unit = @Unit, ReorderLevel = @Reorder, UnitCost = @Cost,
                           BranchId = @BranchId, Supplier = @Supplier, MenuItemId = @MenuItemId, UpdatedAt = SYSUTCDATETIME()
                    WHERE InventoryId = @Id
                    """, [.. Fields(), P("@Id", SqlDbType.Int, inventoryId.Value)]);
                if (rows == 0) { MessageBox.Show("Nothing was saved: the item no longer exists.", "Not Saved", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            tx.Commit();
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) { ShowError("Could not save the item.", ex); }
    }
}
