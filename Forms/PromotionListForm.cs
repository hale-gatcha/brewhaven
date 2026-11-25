using BrewHaven.Helpers;
using System.Data;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.Forms;

/// <summary>Manager page: promotions with Search + Create and an Edit button per row (no Remove: set Inactive instead).</summary>
public sealed class PromotionListForm : Form
{
    private readonly DataGridView dgvPromotions = Ui.Grid();
    private readonly TextBox txtSearch = new() { Width = 300, PlaceholderText = "Search promo name or description..." };
    private readonly RoundedButton btnCreate = Ui.PrimaryButton("+  Create");
    private readonly Label lblCount = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(12, 12, 0, 0) };
    private DataTable table = new();

    public PromotionListForm()
    {
        Text = "Manage Promotions"; BackColor = Ui.Background;
        var (toolbarCard, toolbar) = Ui.ToolbarCard();
        toolbar.Controls.AddRange([Ui.InlineLabel("SEARCH"), Ui.ToolbarBox(txtSearch), btnCreate, lblCount]);

        Ui.AddRowActions(dgvPromotions, includeRemove: false);
        Controls.Add(Ui.GridCard(dgvPromotions)); Controls.Add(Ui.Spacer()); Controls.Add(toolbarCard);

        txtSearch.TextChanged += (_, _) => ApplySearch();
        btnCreate.Click += (_, _) => OpenDialog(null);
        dgvPromotions.CellClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && dgvPromotions.Columns[e.ColumnIndex].Name == Ui.EditColumn) EditRow(e.RowIndex);
        };
        dgvPromotions.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && !Ui.IsRowButton(dgvPromotions, e.ColumnIndex)) EditRow(e.RowIndex); };
        dgvPromotions.DataBindingComplete += (_, _) => FormatColumns();
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadData(); };
    }

    private void LoadData()
    {
        try
        {
            table = Query("""
                SELECT p.PromotionId, p.PromotionName AS Name, ISNULL(p.Description, '') AS Description,
                       CASE WHEN p.DiscountType = 'Fixed' THEN 'PHP ' + CONVERT(nvarchar(20), ISNULL(p.DiscountValue, 0)) + ' off'
                            ELSE CONVERT(nvarchar(20), CAST(ISNULL(p.DiscountValue, p.DiscountPercent) AS decimal(5,2))) + '% off' END AS Discount,
                       ISNULL(p.PointsRequired, 0) AS [Points Required], p.StartDate AS Starts, p.EndDate AS Ends,
                       ISNULL(b.BranchName, 'All branches') AS Branch,
                       CASE WHEN p.IsActive = 0 THEN 'Inactive'
                            WHEN p.EndDate < CAST(GETDATE() AS date) THEN 'Expired'
                            WHEN p.StartDate > CAST(GETDATE() AS date) THEN 'Scheduled'
                            ELSE 'Active' END AS Status
                FROM Promotions p LEFT JOIN Branches b ON b.BranchId = p.BranchId
                ORDER BY p.EndDate DESC, p.PromotionName
                """);
            dgvPromotions.DataSource = table;
            ApplySearch();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load promotions.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplySearch()
    {
        try { table.DefaultView.RowFilter = Ui.ContainsFilter(txtSearch.Text, "Name", "Description", "Branch", "Status"); }
        catch (Exception) { table.DefaultView.RowFilter = ""; }
        lblCount.Text = $"{table.DefaultView.Count} of {table.Rows.Count} promotions";
    }

    private void FormatColumns()
    {
        var c = dgvPromotions.Columns;
        if (c.Contains("PromotionId")) c["PromotionId"]!.Visible = false;
        foreach (string date in new[] { "Starts", "Ends" }) if (c.Contains(date)) c[date]!.DefaultCellStyle.Format = "MMM d, yyyy";
        if (c.Contains("Description")) c["Description"]!.FillWeight = 190;
        if (c.Contains("Points Required")) c["Points Required"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        foreach (DataGridViewColumn col in c) if (col.Tag is null) col.HeaderText = col.HeaderText.ToUpperInvariant();
    }

    private void EditRow(int rowIndex)
    {
        if (dgvPromotions.Rows[rowIndex].Cells["PromotionId"].Value is int id) OpenDialog(id);
    }

    private void OpenDialog(int? promotionId)
    {
        using var dialog = new PromotionDialog(promotionId);
        if (dialog.ShowDialog(Ui.DialogOwner(this)) == DialogResult.OK) LoadData();
    }
}
