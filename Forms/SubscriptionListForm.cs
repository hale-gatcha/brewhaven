using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;

namespace BrewHaven.Forms;

/// <summary>Super Admin page: list of subscription plans with Search, Create, and per-row Edit / Remove.</summary>
public sealed class SubscriptionListForm : Form
{
    private readonly DataGridView dgvSubscriptions = Ui.Grid();
    private readonly TextBox txtSearch = new() { Width = 300, PlaceholderText = "Search plan name or terms..." };
    private readonly RoundedButton btnCreate = Ui.PrimaryButton("+  Create");
    private readonly Label lblCount = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(12, 12, 0, 0) };
    private DataTable table = new();

    public SubscriptionListForm()
    {
        Text = "Subscription and T&C"; BackColor = Ui.Background;

        // Toolbar: Search + Create (no input fields on the page itself)
        var toolbarCard = Ui.Card(DockStyle.Top, 12); toolbarCard.Height = 64;
        var toolbar = Ui.Toolbar(DockStyle.Top);
        toolbar.SizeChanged += (_, _) => toolbarCard.Height = toolbar.Height + toolbarCard.Padding.Vertical;
        var searchBox = Ui.Boxed(txtSearch); searchBox.Margin = new Padding(0, 3, 16, 0);
        toolbar.Controls.AddRange([Ui.InlineLabel("SEARCH"), searchBox, btnCreate, lblCount]);
        toolbarCard.Controls.Add(toolbar);

        Ui.AddRowActions(dgvSubscriptions);
        Controls.Add(Ui.GridCard(dgvSubscriptions)); Controls.Add(Ui.Spacer()); Controls.Add(toolbarCard);

        txtSearch.TextChanged += (_, _) => ApplySearch();
        btnCreate.Click += BtnCreate_Click;
        dgvSubscriptions.CellClick += DgvSubscriptions_CellClick;
        dgvSubscriptions.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && !IsActionColumn(e.ColumnIndex)) EditRow(e.RowIndex); };
        dgvSubscriptions.DataBindingComplete += (_, _) => FormatColumns();
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadData(); };
    }

    private void LoadData()
    {
        const string sql = """
            SELECT SubscriptionId, Name,
                   REPLACE(REPLACE(CASE WHEN LEN(TermsAndConditions) > 90 THEN LEFT(TermsAndConditions, 90) + '...' ELSE TermsAndConditions END,
                           CHAR(13), ' '), CHAR(10), ' ') AS TermsPreview,
                   TermsAndConditions, MonthlyPrice, StartsAt, EndsAt,
                   CASE WHEN IsActive = 1 THEN 'Active' ELSE 'Inactive' END AS Status
            FROM Subscriptions
            ORDER BY Name
            """;
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new(sql, connection);
            connection.Open();
            using SqlDataReader reader = command.ExecuteReader();
            var data = new DataTable();
            data.Load(reader);
            table = data;
            dgvSubscriptions.DataSource = table;
            ApplySearch();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load subscriptions.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplySearch()
    {
        try { table.DefaultView.RowFilter = Ui.ContainsFilter(txtSearch.Text, "Name", "TermsAndConditions", "MonthlyPrice", "Status"); }
        catch (Exception) { table.DefaultView.RowFilter = ""; }
        lblCount.Text = $"{table.DefaultView.Count} of {table.Rows.Count} plans";
    }

    private void FormatColumns()
    {
        var c = dgvSubscriptions.Columns;
        if (c.Contains("SubscriptionId")) c["SubscriptionId"]!.Visible = false;
        if (c.Contains("TermsAndConditions")) c["TermsAndConditions"]!.Visible = false;
        if (c.Contains("Name")) { c["Name"]!.HeaderText = "PLAN NAME"; c["Name"]!.FillWeight = 120; }
        if (c.Contains("TermsPreview")) { c["TermsPreview"]!.HeaderText = "TERMS & CONDITIONS"; c["TermsPreview"]!.FillWeight = 260; }
        if (c.Contains("MonthlyPrice"))
        {
            c["MonthlyPrice"]!.HeaderText = "MONTHLY PRICE"; c["MonthlyPrice"]!.FillWeight = 90;
            c["MonthlyPrice"]!.DefaultCellStyle.Format = "N2";
            c["MonthlyPrice"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        foreach (string date in new[] { "StartsAt", "EndsAt" })
            if (c.Contains(date)) { c[date]!.DefaultCellStyle.Format = "MMM d, yyyy"; c[date]!.FillWeight = 85; }
        if (c.Contains("StartsAt")) c["StartsAt"]!.HeaderText = "STARTS";
        if (c.Contains("EndsAt")) c["EndsAt"]!.HeaderText = "ENDS";
        if (c.Contains("Status")) { c["Status"]!.HeaderText = "STATUS"; c["Status"]!.FillWeight = 65; }
    }

    private bool IsActionColumn(int columnIndex) =>
        columnIndex >= 0 && dgvSubscriptions.Columns[columnIndex].Name is Ui.EditColumn or Ui.RemoveColumn;

    private void DgvSubscriptions_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        string column = dgvSubscriptions.Columns[e.ColumnIndex].Name;
        if (column == Ui.EditColumn) EditRow(e.RowIndex);
        else if (column == Ui.RemoveColumn) RemoveRow(e.RowIndex);
    }

    private void BtnCreate_Click(object? sender, EventArgs e)
    {
        using var dialog = new SubscriptionDialog();
        if (dialog.ShowDialog(DialogOwner()) == DialogResult.OK) LoadData();
    }

    private void EditRow(int rowIndex)
    {
        if (dgvSubscriptions.Rows[rowIndex].Cells["SubscriptionId"].Value is not int id) return;
        using var dialog = new SubscriptionDialog(id);
        if (dialog.ShowDialog(DialogOwner()) == DialogResult.OK) LoadData();
    }

    private void RemoveRow(int rowIndex)
    {
        DataGridViewRow row = dgvSubscriptions.Rows[rowIndex];
        if (row.Cells["SubscriptionId"].Value is not int id) return;
        string name = row.Cells["Name"].Value?.ToString() ?? "this plan";
        if (MessageBox.Show($"Remove the subscription plan \"{name}\"?\nThis cannot be undone.", "Confirm Remove",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new("DELETE FROM Subscriptions WHERE SubscriptionId = @Id", connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            connection.Open();
            command.ExecuteNonQuery();
            LoadData();
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            MessageBox.Show("This plan is still linked to other records, so it can't be removed. Edit it and set Status to Inactive instead.",
                "Cannot Remove", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not remove the subscription.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>This page is hosted inside the dashboard, so dialogs are owned by the dashboard window.</summary>
    private IWin32Window DialogOwner() => TopLevelControl ?? (IWin32Window)this;
}
