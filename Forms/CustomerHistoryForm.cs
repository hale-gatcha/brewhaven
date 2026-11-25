using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using System.Data;
using System.Drawing;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.Forms;

/// <summary>
/// Manager page (read-only): pick a customer (search by name, contact or email), then see their preferences and
/// past orders. Double-click an order for its items and payment.
/// </summary>
public sealed class CustomerHistoryForm : Form
{
    private readonly TextBox txtSearch = new() { Width = 260, PlaceholderText = "Name, contact or email..." };
    private readonly DataGridView dgvCustomers = Ui.Grid();
    private readonly DataGridView dgvOrders = Ui.Grid();
    private readonly Label lblCustomer = new() { Dock = DockStyle.Top, Height = 34, Font = Ui.SubHeading, ForeColor = Ui.Text, UseMnemonic = false };
    private readonly Label lblPreferences = new() { Dock = DockStyle.Fill, Font = Ui.Body, ForeColor = Ui.Text, UseMnemonic = false };
    private DataTable customers = new();

    public CustomerHistoryForm()
    {
        Text = "View Customer History"; BackColor = Ui.Background;

        // Left: customer search + list
        var left = Ui.Card(DockStyle.Left, 12); left.Width = 360;
        var searchBar = Ui.Toolbar(DockStyle.Top); searchBar.Padding = new Padding(0, 0, 0, 8);
        searchBar.Controls.AddRange([Ui.InlineLabel("FIND"), Ui.ToolbarBox(txtSearch)]);
        dgvCustomers.Dock = DockStyle.Fill;
        left.Controls.Add(dgvCustomers); left.Controls.Add(searchBar);

        // Right: preferences card + past orders card
        var prefCard = Ui.Card(DockStyle.Top, 16); prefCard.Height = 170;
        prefCard.Controls.Add(lblPreferences); prefCard.Controls.Add(lblCustomer);
        var ordersCard = Ui.Card(DockStyle.Fill, 12);
        var ordersTitle = Ui.SectionTitle("Past orders  (double-click an order for details)");
        dgvOrders.Dock = DockStyle.Fill;
        ordersCard.Controls.Add(dgvOrders); ordersCard.Controls.Add(ordersTitle);
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Background };
        right.Controls.Add(ordersCard); right.Controls.Add(Ui.Spacer(DockStyle.Top, 12)); right.Controls.Add(prefCard);

        Controls.Add(right); Controls.Add(Ui.Spacer(DockStyle.Left, 16)); Controls.Add(left);

        lblCustomer.Text = "Select a customer";
        lblPreferences.Text = "Their preferences and past orders will appear here.";
        txtSearch.TextChanged += (_, _) => FilterCustomers();
        dgvCustomers.SelectionChanged += (_, _) => ShowSelectedCustomer();
        dgvCustomers.DataBindingComplete += (_, _) =>
        {
            foreach (string hidden in new[] { "CustomerId", "Email" }) if (dgvCustomers.Columns.Contains(hidden)) dgvCustomers.Columns[hidden]!.Visible = false;
            if (dgvCustomers.Columns.Contains("FullName")) dgvCustomers.Columns["FullName"]!.HeaderText = "CUSTOMER";
            if (dgvCustomers.Columns.Contains("ContactNumber")) dgvCustomers.Columns["ContactNumber"]!.HeaderText = "CONTACT";
        };
        dgvOrders.DataBindingComplete += (_, _) => FormatOrders();
        dgvOrders.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && dgvOrders.Rows[e.RowIndex].Cells["Order No"].Value is int id)
            {
                using var dialog = new OrderDetailsDialog(id);
                dialog.ShowDialog(Ui.DialogOwner(this));
            }
        };
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadCustomers(); };
    }

    private void LoadCustomers()
    {
        try
        {
            customers = Query("SELECT CustomerId, FullName, ISNULL(ContactNumber, '') AS ContactNumber, ISNULL(Email, '') AS Email FROM Customers ORDER BY FullName");
            dgvCustomers.DataSource = customers;
            FilterCustomers();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load customers.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void FilterCustomers()
    {
        try { customers.DefaultView.RowFilter = Ui.ContainsFilter(txtSearch.Text, "FullName", "ContactNumber", "Email"); }
        catch (Exception) { customers.DefaultView.RowFilter = ""; }
    }

    private void ShowSelectedCustomer()
    {
        if (dgvCustomers.CurrentRow?.Cells["CustomerId"].Value is not int id) return;
        try
        {
            CustomerRecord? c = CustomerRepository.Load(id);
            if (c is null) return;
            lblCustomer.Text = $"{c.FullName}   ·   {c.CustomerCode}   ·   {c.LoyaltyPoints:N0} points";

            var idParam = new Func<Microsoft.Data.SqlClient.SqlParameter>(() => P("@Id", SqlDbType.Int, id));
            DataTable favorites = Query("""
                SELECT TOP 3 mi.ItemName, SUM(oi.Quantity) AS Qty
                FROM OrderItems oi INNER JOIN Orders o ON o.OrderId = oi.OrderId INNER JOIN MenuItems mi ON mi.MenuItemId = oi.MenuItemId
                WHERE o.CustomerId = @Id AND o.Status <> 'Cancelled'
                GROUP BY mi.ItemName ORDER BY Qty DESC
                """, idParam());
            DataTable saved = Query("""
                SELECT TOP 1 m.ItemName, p.PreferredOrderType, p.Notes
                FROM CustomerPreferences p LEFT JOIN MenuItems m ON m.MenuItemId = p.PreferredMenuItemId
                WHERE p.CustomerId = @Id ORDER BY p.PreferenceId DESC
                """, idParam());
            object? usualType = Scalar("""
                SELECT TOP 1 OrderType FROM Orders WHERE CustomerId = @Id AND Status <> 'Cancelled'
                GROUP BY OrderType ORDER BY COUNT(*) DESC
                """, idParam());

            string favs = favorites.Rows.Count > 0
                ? string.Join(", ", favorites.Rows.Cast<DataRow>().Select(r => $"{r["ItemName"]} ({r["Qty"]})"))
                : saved.Rows.Count > 0 && saved.Rows[0][0] is string pref ? pref + " (saved preference)" : "No orders yet";
            string type = usualType as string ?? (saved.Rows.Count > 0 ? saved.Rows[0][1] as string : null) ?? "-";
            var notes = new[] { c.Notes, saved.Rows.Count > 0 ? saved.Rows[0][2] as string : null }.Where(n => !string.IsNullOrWhiteSpace(n));
            lblPreferences.Text =
                $"Favorite items:  {favs}\n" +
                $"Usual order type:  {type}\n" +
                $"Notes:  {(notes.Any() ? string.Join("; ", notes) : "-")}\n" +
                $"Loyalty points balance:  {c.LoyaltyPoints:N0}\n" +
                $"Contact:  {c.ContactNumber}{(string.IsNullOrEmpty(c.Email) ? "" : "  ·  " + c.Email)}";

            dgvOrders.DataSource = Query("""
                SELECT o.OrderId AS [Order No], o.OrderDate AS [Date], b.BranchName AS Branch, o.OrderType AS [Order Type],
                       ISNULL(STRING_AGG(mi.ItemName + ' x' + CAST(oi.Quantity AS nvarchar(10)), ', '), '') AS Items,
                       o.Total,
                       ISNULL((SELECT TOP 1 p.Method FROM Payments p WHERE p.OrderId = o.OrderId ORDER BY p.PaidAt DESC), '-') AS [Payment Method],
                       o.Status
                FROM Orders o
                INNER JOIN Branches b ON b.BranchId = o.BranchId
                LEFT JOIN OrderItems oi ON oi.OrderId = o.OrderId
                LEFT JOIN MenuItems mi ON mi.MenuItemId = oi.MenuItemId
                WHERE o.CustomerId = @Id
                GROUP BY o.OrderId, o.OrderDate, b.BranchName, o.OrderType, o.Total, o.Status
                ORDER BY o.OrderDate DESC
                """, idParam());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load the customer's history.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void FormatOrders()
    {
        var c = dgvOrders.Columns;
        if (c.Contains("Date")) c["Date"]!.DefaultCellStyle.Format = "MMM d, yyyy h:mm tt";
        if (c.Contains("Total")) { c["Total"]!.DefaultCellStyle.Format = "N2"; c["Total"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
        if (c.Contains("Items")) c["Items"]!.FillWeight = 240;
        if (c.Contains("Order No")) c["Order No"]!.FillWeight = 60;
        foreach (DataGridViewColumn col in c) col.HeaderText = col.HeaderText.ToUpperInvariant();
    }
}
