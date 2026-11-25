using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.Forms;

/// <summary>Staff page: walk-in customers with Search + "Register Customer" and per-row Edit / Remove.</summary>
public sealed class CustomerListForm : Form
{
    private readonly DataGridView dgvCustomers = Ui.Grid();
    private readonly TextBox txtSearch = new() { Width = 300, PlaceholderText = "Search name, code, contact, email..." };
    private readonly RoundedButton btnRegister = Ui.PrimaryButton("+  Register Customer");
    private readonly Label lblCount = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(12, 12, 0, 0) };
    private DataTable table = new();

    public CustomerListForm()
    {
        Text = "Customer Registration"; BackColor = Ui.Background;
        var (toolbarCard, toolbar) = Ui.ToolbarCard();
        toolbar.Controls.AddRange([Ui.InlineLabel("SEARCH"), Ui.ToolbarBox(txtSearch), btnRegister, lblCount]);

        Ui.AddRowActions(dgvCustomers);
        Controls.Add(Ui.GridCard(dgvCustomers)); Controls.Add(Ui.Spacer()); Controls.Add(toolbarCard);

        txtSearch.TextChanged += (_, _) => ApplySearch();
        btnRegister.Click += (_, _) => OpenDialog(null);
        dgvCustomers.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || dgvCustomers.Rows[e.RowIndex].Cells["CustomerId"].Value is not int id) return;
            string column = dgvCustomers.Columns[e.ColumnIndex].Name;
            if (column == Ui.EditColumn) OpenDialog(id);
            else if (column == Ui.RemoveColumn) Remove(e.RowIndex, id);
        };
        dgvCustomers.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && !Ui.IsRowButton(dgvCustomers, e.ColumnIndex) && dgvCustomers.Rows[e.RowIndex].Cells["CustomerId"].Value is int id) OpenDialog(id);
        };
        dgvCustomers.DataBindingComplete += (_, _) =>
        {
            var c = dgvCustomers.Columns;
            if (c.Contains("CustomerId")) c["CustomerId"]!.Visible = false;
            if (c.Contains("Birthdate")) c["Birthdate"]!.DefaultCellStyle.Format = "MMM d, yyyy";
            if (c.Contains("Registered")) c["Registered"]!.DefaultCellStyle.Format = "MMM d, yyyy";
            if (c.Contains("Points")) c["Points"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            if (c.Contains("Full Name")) c["Full Name"]!.FillWeight = 140;
            if (c.Contains("Email")) c["Email"]!.FillWeight = 150;
            foreach (DataGridViewColumn col in c) if (col.Tag is null) col.HeaderText = col.HeaderText.ToUpperInvariant();
        };
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadData(); };
    }

    private void LoadData()
    {
        try
        {
            EnsureSchema();   // CustomerCode / Birthdate / Notes / CreatedAt columns
            table = Query("""
                SELECT CustomerId, ISNULL(CustomerCode, '') AS Code, FullName AS [Full Name], ISNULL(ContactNumber, '') AS Contact,
                       ISNULL(Email, '') AS Email, Birthdate, LoyaltyPoints AS Points, CreatedAt AS Registered
                FROM Customers WHERE IsActive = 1
                ORDER BY CreatedAt DESC, FullName
                """);
            dgvCustomers.DataSource = table;
            ApplySearch();
        }
        catch (Exception ex) { MessageBox.Show($"Could not load customers.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ApplySearch()
    {
        try { table.DefaultView.RowFilter = Ui.ContainsFilter(txtSearch.Text, "Code", "Full Name", "Contact", "Email"); }
        catch (Exception) { table.DefaultView.RowFilter = ""; }
        lblCount.Text = $"{table.DefaultView.Count} of {table.Rows.Count} customers";
    }

    private void OpenDialog(int? customerId)
    {
        using var dialog = new CustomerDialog(customerId);
        if (dialog.ShowDialog(Ui.DialogOwner(this)) == DialogResult.OK) LoadData();
    }

    private void Remove(int rowIndex, int id)
    {
        string name = dgvCustomers.Rows[rowIndex].Cells["Full Name"].Value?.ToString() ?? "this customer";
        if (MessageBox.Show($"Remove the customer \"{name}\"?\nThis cannot be undone.", "Confirm Remove",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            CustomerRepository.Delete(id);
            LoadData();
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            MessageBox.Show($"\"{name}\" has orders, feedback or redemptions on record, so they can't be removed. Their history is kept.",
                "Cannot Remove", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) { MessageBox.Show($"Could not remove the customer.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
