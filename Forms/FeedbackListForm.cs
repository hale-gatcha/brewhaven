using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using BrewHaven.Models;
using System.Data;

namespace BrewHaven.Forms;

/// <summary>
/// Admin page "Customer Feedback": checks and manages the concerns Staff logged. No Create (Admins don't input concerns).
/// Search + Type / Status / Category / Branch / Date From-To filters; per-row View, Manage and Remove (soft delete:
/// archived rows are hidden unless "Show archived" is ticked, and can be restored).
/// </summary>
public sealed class FeedbackListForm : Form
{
    private const string ViewColumn = "colView", ManageColumn = "colManage";

    private readonly DataGridView dgvFeedback = Ui.Grid();
    private readonly TextBox txtSearch = new() { Width = 210, PlaceholderText = "Search subject, customer..." };
    private readonly ComboBox cboType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox cboStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
    private readonly ComboBox cboCategory = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 135 };
    private readonly ComboBox cboBranch = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly DateTimePicker dtpFrom = new() { Width = 150, ShowCheckBox = true, Checked = false };
    private readonly DateTimePicker dtpTo = new() { Width = 150, ShowCheckBox = true, Checked = false };
    private readonly CheckBox chkArchived = new() { Text = "Show archived", AutoSize = true, Margin = new Padding(0, 11, 14, 0), ForeColor = Ui.Muted };
    private readonly Label lblCount = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(4, 12, 0, 0) };
    private readonly FeedbackRepository repository = new();
    private DataTable table = new();
    private bool loading;

    public FeedbackListForm()
    {
        Text = "Customer Feedback"; BackColor = Ui.Background;

        cboType.Items.Add("All types"); cboType.Items.AddRange(FeedbackRepository.Types); cboType.SelectedIndex = 0;
        cboStatus.Items.Add("All statuses"); cboStatus.Items.AddRange(FeedbackRepository.Statuses); cboStatus.SelectedIndex = 0;
        cboCategory.Items.Add("All categories"); cboCategory.Items.AddRange(FeedbackRepository.Categories); cboCategory.SelectedIndex = 0;
        dtpFrom.Value = DateTime.Today.AddDays(-30); dtpTo.Value = DateTime.Today;
        dtpFrom.Checked = dtpTo.Checked = false;   // unticked = no date limit

        var (toolbarCard, toolbar) = Ui.ToolbarCard();
        toolbar.Controls.AddRange([
            Ui.InlineLabel("SEARCH"), Ui.ToolbarBox(txtSearch),
            Ui.InlineLabel("TYPE"), Ui.ToolbarBox(cboType),
            Ui.InlineLabel("STATUS"), Ui.ToolbarBox(cboStatus),
            Ui.InlineLabel("CATEGORY"), Ui.ToolbarBox(cboCategory),
            Ui.InlineLabel("BRANCH"), Ui.ToolbarBox(cboBranch),
            Ui.InlineLabel("FROM"), Ui.ToolbarBox(dtpFrom),
            Ui.InlineLabel("TO"), Ui.ToolbarBox(dtpTo),
            chkArchived, lblCount]);

        Ui.AddRowButtons(dgvFeedback,
            new Ui.RowButton(ViewColumn, "View", Ui.RowButtonStyle.Secondary, 76),
            new Ui.RowButton(ManageColumn, "Manage", Ui.RowButtonStyle.Primary, 90),
            new Ui.RowButton(Ui.RemoveColumn, "Remove", Ui.RowButtonStyle.Danger, 92));
        Controls.Add(Ui.GridCard(dgvFeedback)); Controls.Add(Ui.Spacer()); Controls.Add(toolbarCard);

        txtSearch.TextChanged += (_, _) => ApplySearch();
        foreach (var combo in new[] { cboType, cboStatus, cboCategory, cboBranch }) combo.SelectedIndexChanged += (_, _) => LoadData();
        dtpFrom.ValueChanged += (_, _) => LoadData();
        dtpTo.ValueChanged += (_, _) => LoadData();
        chkArchived.CheckedChanged += (_, _) => LoadData();
        dgvFeedback.CellClick += DgvFeedback_CellClick;
        // Double-clicking a data cell (not a header, not a button column) opens the read-only details.
        dgvFeedback.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && !Ui.IsRowButton(dgvFeedback, e.ColumnIndex)) ViewRow(e.RowIndex); };
        dgvFeedback.DataBindingComplete += (_, _) => FormatColumns();
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadBranches(); LoadData(); };
    }

    private void LoadBranches()
    {
        try
        {
            loading = true;
            DataTable branches = repository.GetBranches();
            DataRow all = branches.NewRow(); all["BranchId"] = 0; all["BranchName"] = "All branches";
            branches.Rows.InsertAt(all, 0);
            cboBranch.DisplayMember = "BranchName"; cboBranch.ValueMember = "BranchId"; cboBranch.DataSource = branches;
            cboBranch.SelectedValue = 0;
        }
        catch (Exception ex) { MessageBox.Show($"Could not load branches.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { loading = false; }
    }

    private ConcernFilter CurrentFilter() => new()
    {
        From = dtpFrom.Checked ? dtpFrom.Value.Date : null,
        To = dtpTo.Checked ? dtpTo.Value.Date : null,
        BranchId = Convert.ToInt32(cboBranch.SelectedValue ?? 0),
        Type = cboType.SelectedIndex > 0 ? cboType.Text : null,
        Status = cboStatus.SelectedIndex > 0 ? cboStatus.Text : null,
        Category = cboCategory.SelectedIndex > 0 ? cboCategory.Text : null,
        IncludeArchived = chkArchived.Checked,
    };

    private void LoadData()
    {
        if (loading) return;
        if (dtpFrom.Checked && dtpTo.Checked && dtpTo.Value.Date < dtpFrom.Value.Date)
        {
            MessageBox.Show("'To' must be on or after 'From'.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            table = repository.GetConcerns(CurrentFilter());
            dgvFeedback.DataSource = table;
            ApplySearch();
        }
        catch (Exception ex) { MessageBox.Show($"Could not load concerns.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ApplySearch()
    {
        string filter = Ui.ContainsFilter(txtSearch.Text, "Subject", "Customer", "Branch", "Type", "Category", "Status");
        try { table.DefaultView.RowFilter = filter; } catch (Exception) { table.DefaultView.RowFilter = ""; }
        int archived = table.Rows.Cast<DataRow>().Count(r => r["IsArchived"] is true);
        lblCount.Text = $"{table.DefaultView.Count} of {table.Rows.Count} entries" + (archived > 0 ? $" ({archived} archived)" : "");
    }

    private void FormatColumns()
    {
        var c = dgvFeedback.Columns;
        foreach (string hidden in new[] { "FeedbackId", "IsArchived" }) if (c.Contains(hidden)) c[hidden]!.Visible = false;
        (string Name, string Header, float Weight)[] layout =
        [
            ("DateSubmitted", "DATE", 85), ("Branch", "BRANCH", 95), ("Customer", "CUSTOMER", 105), ("Type", "TYPE", 70),
            ("Category", "CATEGORY", 95), ("Subject", "SUBJECT", 200), ("Rating", "RATING", 50), ("Status", "STATUS", 80)
        ];
        foreach (var (name, header, weight) in layout) if (c.Contains(name)) { c[name]!.HeaderText = header; c[name]!.FillWeight = weight; }
        if (c.Contains("DateSubmitted")) c["DateSubmitted"]!.DefaultCellStyle.Format = "MMM d, yyyy";
        if (c.Contains("Rating")) c["Rating"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        // Archived rows are greyed out (text only; GridStyler keeps the one row highlight).
        foreach (DataGridViewRow row in dgvFeedback.Rows)
            if (row.DataBoundItem is DataRowView v && v["IsArchived"] is true) row.DefaultCellStyle.ForeColor = Ui.Muted;
    }

    private void DgvFeedback_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        switch (dgvFeedback.Columns[e.ColumnIndex].Name)
        {
            case ViewColumn: ViewRow(e.RowIndex); break;
            case ManageColumn: ManageRow(e.RowIndex); break;
            case Ui.RemoveColumn: RemoveRow(e.RowIndex); break;
        }
    }

    private (int Id, bool Archived)? RowKey(int rowIndex)
    {
        DataGridViewRow row = dgvFeedback.Rows[rowIndex];
        return row.Cells["FeedbackId"].Value is int id ? (id, row.Cells["IsArchived"].Value is true) : null;
    }

    private void ViewRow(int rowIndex)
    {
        if (RowKey(rowIndex) is not { } key) return;
        int id = key.Id;
        DialogResult result;
        using (var details = new FeedbackDetailsDialog(id, canManage: true)) result = details.ShowDialog(Ui.DialogOwner(this));
        if (result == DialogResult.Yes) OpenManage(id);   // "Manage" in the details pop-up
    }

    private void ManageRow(int rowIndex)
    {
        if (RowKey(rowIndex) is not { } key) return;
        var (id, archived) = key;
        if (archived)
        {
            MessageBox.Show("This entry is archived. Restore it first (Remove button) to manage it.", "Archived", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        OpenManage(id);
    }

    private void OpenManage(int id)
    {
        using var dialog = new ManageConcernDialog(id);
        if (dialog.ShowDialog(Ui.DialogOwner(this)) == DialogResult.OK) LoadData();
    }

    private void RemoveRow(int rowIndex)
    {
        if (RowKey(rowIndex) is not { } key) return;
        var (id, archived) = key;
        DataGridViewRow row = dgvFeedback.Rows[rowIndex];
        string subject = row.Cells["Subject"].Value?.ToString() ?? "this entry";
        string type = (row.Cells["Type"].Value?.ToString() ?? "entry").ToLowerInvariant();
        string question = archived
            ? $"Restore this archived {type}: \"{subject}\"?\nIt will show in the list again."
            : $"Remove this {type}: \"{subject}\"?\nIt is archived (hidden) and still counted in Reports. Tick \"Show archived\" to restore it later.";
        if (MessageBox.Show(question, archived ? "Confirm Restore" : "Confirm Remove", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            repository.SetArchived(id, !archived);
            LoadData();
        }
        catch (Exception ex) { MessageBox.Show($"Could not update the entry.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
