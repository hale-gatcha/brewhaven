using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using System.Data;

namespace BrewHaven.Forms;

/// <summary>
/// Staff page "Customer Concerns": the feedback / inquiries / complaints this Staff user logged for walk-in customers.
/// Search + "Log Concern"; each row has View only (no edit, no remove, no status changes).
/// </summary>
public sealed class StaffConcernsForm : Form
{
    private const string ViewColumn = "colView";

    private readonly DataGridView dgvConcerns = Ui.Grid();
    private readonly TextBox txtSearch = new() { Width = 280, PlaceholderText = "Search subject, customer, type..." };
    private readonly RoundedButton btnLogConcern = Ui.PrimaryButton("+  Log Concern");
    private readonly Label lblCount = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(12, 12, 0, 0) };
    private readonly FeedbackRepository repository = new();
    private DataTable table = new();

    public StaffConcernsForm()
    {
        Text = "Customer Concerns"; BackColor = Ui.Background;

        var (toolbarCard, toolbar) = Ui.ToolbarCard();
        toolbar.Controls.AddRange([Ui.InlineLabel("SEARCH"), Ui.ToolbarBox(txtSearch), btnLogConcern, lblCount]);

        Ui.AddRowButtons(dgvConcerns, new Ui.RowButton(ViewColumn, "View", Ui.RowButtonStyle.Secondary, 76));
        Controls.Add(Ui.GridCard(dgvConcerns)); Controls.Add(Ui.Spacer()); Controls.Add(toolbarCard);

        txtSearch.TextChanged += (_, _) => ApplySearch();
        btnLogConcern.Click += (_, _) => LogConcern();
        dgvConcerns.CellClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && dgvConcerns.Columns[e.ColumnIndex].Name == ViewColumn) ViewRow(e.RowIndex); };
        dgvConcerns.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && !Ui.IsRowButton(dgvConcerns, e.ColumnIndex)) ViewRow(e.RowIndex); };
        dgvConcerns.DataBindingComplete += (_, _) => FormatColumns();
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadData(); };
    }

    private void LoadData()
    {
        try
        {
            table = repository.GetMyConcerns();
            dgvConcerns.DataSource = table;
            ApplySearch();
        }
        catch (Exception ex) { MessageBox.Show($"Could not load your logged concerns.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ApplySearch()
    {
        string filter = Ui.ContainsFilter(txtSearch.Text, "Subject", "Customer", "Type", "Category", "Status");
        try { table.DefaultView.RowFilter = filter; } catch (Exception) { table.DefaultView.RowFilter = ""; }
        lblCount.Text = $"{table.DefaultView.Count} of {table.Rows.Count} concerns you logged";
    }

    private void FormatColumns()
    {
        var c = dgvConcerns.Columns;
        foreach (string hidden in new[] { "FeedbackId", "Branch", "IsArchived" }) if (c.Contains(hidden)) c[hidden]!.Visible = false;
        (string Name, string Header, float Weight)[] layout =
        [
            ("DateSubmitted", "DATE", 95), ("Customer", "CUSTOMER", 120), ("Type", "TYPE", 75), ("Category", "CATEGORY", 100),
            ("Subject", "SUBJECT", 190), ("Rating", "RATING", 55), ("Status", "STATUS", 80)
        ];
        foreach (var (name, header, weight) in layout) if (c.Contains(name)) { c[name]!.HeaderText = header; c[name]!.FillWeight = weight; }
        if (c.Contains("DateSubmitted")) c["DateSubmitted"]!.DefaultCellStyle.Format = "MMM d, yyyy h:mm tt";
        if (c.Contains("Rating")) c["Rating"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
    }

    private void LogConcern()
    {
        using var dialog = new LogConcernDialog();
        if (dialog.ShowDialog(Ui.DialogOwner(this)) == DialogResult.OK) LoadData();
    }

    private void ViewRow(int rowIndex)
    {
        if (dgvConcerns.Rows[rowIndex].Cells["FeedbackId"].Value is not int id) return;
        using var details = new FeedbackDetailsDialog(id, canManage: false);
        details.ShowDialog(Ui.DialogOwner(this));
    }
}
