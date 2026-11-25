using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Drawing;

namespace BrewHaven.Forms;

/// <summary>
/// Admin page: customer feedback, inquiries and complaints. Search + Type / Status / Branch filters + Create,
/// with per-row Edit / Remove. Rows are coloured by status.
/// </summary>
public sealed class FeedbackListForm : Form
{
    private readonly DataGridView dgvFeedback = Ui.Grid();
    private readonly TextBox txtSearch = new() { Width = 230, PlaceholderText = "Search subject, customer..." };
    private readonly ComboBox cboType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly ComboBox cboStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly ComboBox cboBranch = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly RoundedButton btnCreate = Ui.PrimaryButton("+  Create");
    private readonly Label lblCount = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(12, 12, 0, 0) };
    private DataTable table = new();

    public FeedbackListForm()
    {
        Text = "Customer Feedback"; BackColor = Ui.Background;

        cboType.Items.AddRange(["All types", "Feedback", "Inquiry", "Complaint"]); cboType.SelectedIndex = 0;
        cboStatus.Items.AddRange(["All statuses", "Open", "In Progress", "Resolved", "Closed"]); cboStatus.SelectedIndex = 0;

        // Toolbar: Search, filters, Create
        var toolbarCard = Ui.Card(DockStyle.Top, 12); toolbarCard.Height = 64;
        var toolbar = Ui.Toolbar(DockStyle.Top);
        toolbar.SizeChanged += (_, _) => toolbarCard.Height = toolbar.Height + toolbarCard.Padding.Vertical;
        Control Box(Control c) { var b = Ui.Boxed(c); b.Margin = new Padding(0, 3, 14, 0); return b; }
        toolbar.Controls.AddRange([
            Ui.InlineLabel("SEARCH"), Box(txtSearch),
            Ui.InlineLabel("TYPE"), Box(cboType),
            Ui.InlineLabel("STATUS"), Box(cboStatus),
            Ui.InlineLabel("BRANCH"), Box(cboBranch),
            btnCreate, lblCount]);
        toolbarCard.Controls.Add(toolbar);

        // View for everyone; Edit / Remove only for Admin and Manager (Staff can log entries and view details).
        var rowButtons = new List<Ui.RowButton> { new(ViewColumn, "View", Ui.RowButtonStyle.Secondary, 76) };
        if (CanManage) { rowButtons.Add(new(Ui.EditColumn, "Edit", Ui.RowButtonStyle.Primary, 72)); rowButtons.Add(new(Ui.RemoveColumn, "Remove", Ui.RowButtonStyle.Danger, 92)); }
        Ui.AddRowButtons(dgvFeedback, [.. rowButtons]);
        Controls.Add(Ui.GridCard(dgvFeedback)); Controls.Add(Ui.Spacer()); Controls.Add(toolbarCard);

        txtSearch.TextChanged += (_, _) => ApplyFilters();
        cboType.SelectedIndexChanged += (_, _) => ApplyFilters();
        cboStatus.SelectedIndexChanged += (_, _) => ApplyFilters();
        cboBranch.SelectedIndexChanged += (_, _) => ApplyFilters();
        btnCreate.Click += BtnCreate_Click;
        dgvFeedback.CellClick += DgvFeedback_CellClick;
        // Double-clicking a data cell (not a header, not a button column) opens the read-only details.
        dgvFeedback.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && !IsActionColumn(e.ColumnIndex)) ViewRow(e.RowIndex); };
        dgvFeedback.DataBindingComplete += (_, _) => FormatColumns();
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadBranches(); LoadData(); };
    }

    private void LoadBranches()
    {
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new("SELECT BranchName FROM Branches ORDER BY BranchName", connection);
            connection.Open();
            using SqlDataReader reader = command.ExecuteReader();
            cboBranch.Items.Clear(); cboBranch.Items.Add("All branches");
            while (reader.Read()) cboBranch.Items.Add(reader.GetString(0));
            cboBranch.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load branches.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadData()
    {
        const string sql = """
            SELECT f.FeedbackId, f.DateSubmitted, b.BranchName AS Branch, ISNULL(c.FullName, 'Walk-In') AS Customer,
                   f.Type, f.Subject, f.Rating, f.Status
            FROM Feedback f
            INNER JOIN Branches b ON b.BranchId = f.BranchId
            LEFT JOIN Customers c ON c.CustomerId = f.CustomerId
            ORDER BY f.DateSubmitted DESC
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
            dgvFeedback.DataSource = table;
            ApplyFilters();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load feedback.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    private void ApplyFilters()
    {
        var parts = new List<string>();
        string search = Ui.ContainsFilter(txtSearch.Text, "Subject", "Customer", "Branch", "Type", "Status");
        if (search.Length > 0) parts.Add($"({search})");
        if (cboType.SelectedIndex > 0) parts.Add($"Type = {Quote(cboType.Text)}");
        if (cboStatus.SelectedIndex > 0) parts.Add($"Status = {Quote(cboStatus.Text)}");
        if (cboBranch.SelectedIndex > 0) parts.Add($"Branch = {Quote(cboBranch.Text)}");
        try { table.DefaultView.RowFilter = string.Join(" AND ", parts); }
        catch (Exception) { table.DefaultView.RowFilter = ""; }
        lblCount.Text = $"{table.DefaultView.Count} of {table.Rows.Count} entries";
    }

    private void FormatColumns()
    {
        var c = dgvFeedback.Columns;
        if (c.Contains("FeedbackId")) c["FeedbackId"]!.Visible = false;
        (string Name, string Header, float Weight)[] layout =
        [
            ("DateSubmitted", "DATE", 90), ("Branch", "BRANCH", 95), ("Customer", "CUSTOMER", 110), ("Type", "TYPE", 75),
            ("Subject", "SUBJECT", 190), ("Rating", "RATING", 55), ("Status", "STATUS", 80)
        ];
        foreach (var (name, header, weight) in layout)
            if (c.Contains(name)) { c[name]!.HeaderText = header; c[name]!.FillWeight = weight; }
        if (c.Contains("DateSubmitted")) c["DateSubmitted"]!.DefaultCellStyle.Format = "MMM d, yyyy";
        if (c.Contains("Rating")) c["Rating"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
    }


    private const string ViewColumn = "colView";

    /// <summary>Admin and Manager can edit and remove entries; other roles can only create and view.</summary>
    private static bool CanManage => Session.CurrentUser?.RoleName is "Admin" or "Manager";

    private bool IsActionColumn(int columnIndex) => Ui.IsRowButton(dgvFeedback, columnIndex);

    private void DgvFeedback_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;   // header row / row header
        string column = dgvFeedback.Columns[e.ColumnIndex].Name;
        if (column == ViewColumn) ViewRow(e.RowIndex);
        else if (column == Ui.EditColumn && CanManage) EditRow(e.RowIndex);
        else if (column == Ui.RemoveColumn && CanManage) RemoveRow(e.RowIndex);
    }

    private void ViewRow(int rowIndex)
    {
        if (dgvFeedback.Rows[rowIndex].Cells["FeedbackId"].Value is not int id) return;
        DialogResult result;
        using (var details = new FeedbackDetailsDialog(id, CanManage)) result = details.ShowDialog(DialogOwner());
        // "Edit" in the details pop-up closes it with Yes; open the normal edit dialog for the same record.
        if (result == DialogResult.Yes && CanManage)
        {
            using var dialog = new FeedbackDialog(id);
            if (dialog.ShowDialog(DialogOwner()) == DialogResult.OK) LoadData();
        }
    }

    private void BtnCreate_Click(object? sender, EventArgs e)
    {
        using var dialog = new FeedbackDialog();
        if (dialog.ShowDialog(DialogOwner()) == DialogResult.OK) LoadData();
    }

    private void EditRow(int rowIndex)
    {
        if (dgvFeedback.Rows[rowIndex].Cells["FeedbackId"].Value is not int id) return;
        using var dialog = new FeedbackDialog(id);
        if (dialog.ShowDialog(DialogOwner()) == DialogResult.OK) LoadData();
    }

    private void RemoveRow(int rowIndex)
    {
        DataGridViewRow row = dgvFeedback.Rows[rowIndex];
        if (row.Cells["FeedbackId"].Value is not int id) return;
        string subject = row.Cells["Subject"].Value?.ToString() ?? "this entry";
        string type = row.Cells["Type"].Value?.ToString() ?? "entry";
        if (MessageBox.Show($"Remove this {type.ToLowerInvariant()}: \"{subject}\"?\nThis cannot be undone.", "Confirm Remove",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new("DELETE FROM Feedback WHERE FeedbackId = @Id", connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            connection.Open();
            command.ExecuteNonQuery();
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not remove the entry.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>This page is hosted inside the dashboard, so dialogs are owned by the dashboard window.</summary>
    private IWin32Window DialogOwner() => TopLevelControl ?? (IWin32Window)this;
}
