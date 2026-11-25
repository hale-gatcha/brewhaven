using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.Forms;

/// <summary>
/// Manager page: inventory with Search, Category / Low-stock filters and Create; per-row Stock In, Stock Out,
/// Edit and Remove. A "Stock Status" column shows Low Stock / Out of Stock (no row colours), plus a low-stock count.
/// </summary>
public sealed class InventoryListForm : Form
{
    private const string StockInColumn = "colStockIn", StockOutColumn = "colStockOut";

    private readonly DataGridView dgvInventory = Ui.Grid();
    private readonly TextBox txtSearch = new() { Width = 220, PlaceholderText = "Search item, supplier..." };
    private readonly ComboBox cboCategory = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox cboBranch = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly CheckBox chkLowStock = new() { Text = "Low stock only", AutoSize = true, Margin = new Padding(0, 10, 14, 0), ForeColor = Ui.Text };
    private readonly RoundedButton btnCreate = Ui.PrimaryButton("+  Create");
    private readonly Label lblLowStock = new() { AutoSize = true, Font = Ui.BodyBold, ForeColor = Ui.Primary, BackColor = Ui.WarmSand, Padding = new Padding(10, 5, 10, 5), Margin = new Padding(8, 5, 0, 0) };
    private DataTable table = new();

    public InventoryListForm()
    {
        Text = "Inventory"; BackColor = Ui.Background;
        var (toolbarCard, toolbar) = Ui.ToolbarCard();
        toolbar.Controls.AddRange([
            Ui.InlineLabel("SEARCH"), Ui.ToolbarBox(txtSearch), Ui.InlineLabel("CATEGORY"), Ui.ToolbarBox(cboCategory),
            Ui.InlineLabel("BRANCH"), Ui.ToolbarBox(cboBranch), chkLowStock, btnCreate, lblLowStock]);
        lblLowStock.ForeColor = Ui.Espresso;

        Ui.AddRowButtons(dgvInventory,
            new Ui.RowButton(StockInColumn, "Stock In", Ui.RowButtonStyle.Secondary, 92),
            new Ui.RowButton(StockOutColumn, "Stock Out", Ui.RowButtonStyle.Secondary, 98),
            new Ui.RowButton(Ui.EditColumn, "Edit", Ui.RowButtonStyle.Primary, 72),
            new Ui.RowButton(Ui.RemoveColumn, "Remove", Ui.RowButtonStyle.Danger, 92));
        Controls.Add(Ui.GridCard(dgvInventory)); Controls.Add(Ui.Spacer()); Controls.Add(toolbarCard);

        txtSearch.TextChanged += (_, _) => ApplyFilters();
        cboCategory.SelectedIndexChanged += (_, _) => ApplyFilters();
        cboBranch.SelectedIndexChanged += (_, _) => ApplyFilters();
        chkLowStock.CheckedChanged += (_, _) => ApplyFilters();
        btnCreate.Click += (_, _) => OpenDialog(null);
        dgvInventory.CellClick += DgvInventory_CellClick;
        dgvInventory.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && !Ui.IsRowButton(dgvInventory, e.ColumnIndex)) EditRow(e.RowIndex); };
        dgvInventory.DataBindingComplete += (_, _) => FormatColumns();
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadData(); };
    }

    private void LoadData()
    {
        try
        {
            EnsureSchema();   // Unit / UnitCost / Supplier columns and the StockMovements table
            table = Query("""
                SELECT i.InventoryId, i.ItemName AS Item, i.Category, ISNULL(i.Unit, '') AS Unit, i.Quantity, i.ReorderLevel AS [Reorder Level],
                       CASE WHEN i.Quantity <= 0 THEN 'Out of Stock' WHEN i.Quantity <= i.ReorderLevel THEN 'Low Stock' ELSE 'OK' END AS [Stock Status],
                       i.UnitCost AS [Unit Cost], b.BranchName AS Branch, ISNULL(i.Supplier, '') AS Supplier,
                       ISNULL(m.ItemName, '') AS [Menu Item], i.UpdatedAt AS Updated,
                       CASE WHEN i.Quantity <= i.ReorderLevel THEN 1 ELSE 0 END AS Low
                FROM Inventory i
                INNER JOIN Branches b ON b.BranchId = i.BranchId
                LEFT JOIN MenuItems m ON m.MenuItemId = i.MenuItemId
                ORDER BY Low DESC, i.ItemName
                """);
            dgvInventory.DataSource = table;
            RefillFilter(cboCategory, "All categories", "Category");
            RefillFilter(cboBranch, "All branches", "Branch");
            ApplyFilters();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load inventory.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Fills a filter combo with "All" + the distinct values of a column, keeping the current choice.</summary>
    private void RefillFilter(ComboBox combo, string allText, string column)
    {
        string current = combo.Text;
        combo.Items.Clear(); combo.Items.Add(allText);
        foreach (string v in table.Rows.Cast<DataRow>().Select(r => r[column] as string ?? "").Where(v => v.Length > 0).Distinct().OrderBy(v => v))
            combo.Items.Add(v);
        int keep = combo.Items.IndexOf(current);
        combo.SelectedIndex = keep >= 0 ? keep : 0;
    }

    private void ApplyFilters()
    {
        var parts = new List<string>();
        string search = Ui.ContainsFilter(txtSearch.Text, "Item", "Category", "Supplier", "Menu Item", "Branch", "Stock Status");
        if (search.Length > 0) parts.Add($"({search})");
        if (cboCategory.SelectedIndex > 0) parts.Add($"Category = '{cboCategory.Text.Replace("'", "''")}'");
        if (cboBranch.SelectedIndex > 0) parts.Add($"Branch = '{cboBranch.Text.Replace("'", "''")}'");
        if (chkLowStock.Checked) parts.Add("Low = 1");
        try { table.DefaultView.RowFilter = string.Join(" AND ", parts); }
        catch (Exception) { table.DefaultView.RowFilter = ""; }

        // Low-stock count for the current branch filter (independent of the search / low-only filters).
        int low = table.Rows.Cast<DataRow>().Count(r => (int)r["Low"] == 1 && (cboBranch.SelectedIndex <= 0 || r["Branch"] as string == cboBranch.Text));
        lblLowStock.Text = low == 0 ? "No low-stock items" : $"{low} low-stock item{(low == 1 ? "" : "s")}";
    }

    private void FormatColumns()
    {
        var c = dgvInventory.Columns;
        foreach (string hidden in new[] { "InventoryId", "Low" }) if (c.Contains(hidden)) c[hidden]!.Visible = false;
        if (c.Contains("Unit Cost")) { c["Unit Cost"]!.DefaultCellStyle.Format = "N2"; c["Unit Cost"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
        foreach (string n in new[] { "Quantity", "Reorder Level" }) if (c.Contains(n)) c[n]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        if (c.Contains("Updated")) c["Updated"]!.DefaultCellStyle.Format = "MMM d, yyyy";
        if (c.Contains("Item")) c["Item"]!.FillWeight = 140;
        foreach (DataGridViewColumn col in c) if (col.Tag is null) col.HeaderText = col.HeaderText.ToUpperInvariant();
    }

    private void DgvInventory_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        DataGridViewRow row = dgvInventory.Rows[e.RowIndex];
        if (row.Cells["InventoryId"].Value is not int id) return;
        switch (dgvInventory.Columns[e.ColumnIndex].Name)
        {
            case StockInColumn: MoveStock(row, id, stockIn: true); break;
            case StockOutColumn: MoveStock(row, id, stockIn: false); break;
            case Ui.EditColumn: OpenDialog(id); break;
            case Ui.RemoveColumn: Remove(row, id); break;
        }
    }

    private void EditRow(int rowIndex)
    {
        if (dgvInventory.Rows[rowIndex].Cells["InventoryId"].Value is int id) OpenDialog(id);
    }

    private void OpenDialog(int? inventoryId)
    {
        using var dialog = new InventoryDialog(inventoryId);
        if (dialog.ShowDialog(Ui.DialogOwner(this)) == DialogResult.OK) LoadData();
    }

    private void MoveStock(DataGridViewRow row, int id, bool stockIn)
    {
        string item = row.Cells["Item"].Value?.ToString() ?? "";
        int quantity = row.Cells["Quantity"].Value is int q ? q : 0;
        string unit = row.Cells["Unit"].Value?.ToString() ?? "";
        if (!stockIn && quantity <= 0)
        {
            MessageBox.Show($"{item} is out of stock, so nothing can be taken out.", "Stock Out", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new StockMovementDialog(id, item, quantity, unit, stockIn);
        if (dialog.ShowDialog(Ui.DialogOwner(this)) == DialogResult.OK) LoadData();
    }

    private void Remove(DataGridViewRow row, int id)
    {
        string item = row.Cells["Item"].Value?.ToString() ?? "this item";
        if (MessageBox.Show($"Remove \"{item}\" ({row.Cells["Branch"].Value}) from inventory?\nIts stock movement history is removed too. This cannot be undone.",
                "Confirm Remove", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            Execute("DELETE FROM Inventory WHERE InventoryId = @Id", P("@Id", SqlDbType.Int, id));
            LoadData();
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            MessageBox.Show("This item is still linked to other records, so it can't be removed.", "Cannot Remove", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not remove the item.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
