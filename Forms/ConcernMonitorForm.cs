using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using BrewHaven.Models;
using System.Data;
using System.Drawing;

namespace BrewHaven.Forms;

/// <summary>
/// Manager page "Customer Feedback" (monitoring only: no Create, Edit or Remove).
/// Summary cards, insights on what to improve (concerns per category with bars, average rating per category and branch,
/// top 5 complaint categories, unresolved concerns older than 3 days, weekly / monthly trend) and a read-only list of
/// all concerns. Filters: Date From/To (last 30 days by default), Branch (locked to the Manager's own), Type, Category.
/// Export to CSV and Print cover every section.
/// </summary>
public sealed class ConcernMonitorForm : Form
{
    private const string ViewColumn = "colView", BarColumn = "colBar";

    // Filters
    private readonly DateTimePicker dtpFrom = new() { Width = 140 };
    private readonly DateTimePicker dtpTo = new() { Width = 140 };
    private readonly ComboBox cboBranch = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly ComboBox cboType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 115 };
    private readonly ComboBox cboCategory = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ComboBox cboTrend = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly RoundedButton btnRefresh = Ui.PrimaryButton("Refresh");
    private readonly RoundedButton btnExport = Ui.SecondaryButton("Export to CSV");
    private readonly RoundedButton btnPrint = Ui.SecondaryButton("Print");

    // Summary cards
    private readonly Label lblTotal = CardValue(), lblOpen = CardValue(), lblInProgress = CardValue(), lblDone = CardValue(),
                           lblAvgRating = CardValue(), lblResolutionRate = CardValue(), lblAvgDays = CardValue();

    // Insight grids
    private readonly DataGridView dgvCategory = Ui.Grid();
    private readonly DataGridView dgvTopComplaints = Ui.Grid();
    private readonly DataGridView dgvRatingCategory = Ui.Grid();
    private readonly DataGridView dgvRatingBranch = Ui.Grid();
    private readonly DataGridView dgvNeedsAttention = Ui.Grid();
    private readonly DataGridView dgvTrend = Ui.Grid();
    private readonly DataGridView dgvConcerns = Ui.Grid();
    private readonly Label lblNotice = new() { AutoSize = true, Font = Ui.BodyBold, ForeColor = Ui.Danger, Visible = false, Margin = new Padding(0, 0, 0, 10) };

    private readonly FeedbackRepository repository = new();
    private ConcernSummary summary = new();
    private bool loading = true;

    public ConcernMonitorForm()
    {
        Text = "Customer Feedback Monitoring"; BackColor = Ui.Background;

        dtpFrom.Value = DateTime.Today.AddDays(-30); dtpTo.Value = DateTime.Today;
        cboType.Items.Add("All types"); cboType.Items.AddRange(FeedbackRepository.Types); cboType.SelectedIndex = 0;
        cboCategory.Items.Add("All categories"); cboCategory.Items.AddRange(FeedbackRepository.Categories); cboCategory.SelectedIndex = 0;
        cboTrend.Items.AddRange(["Weekly", "Monthly"]); cboTrend.SelectedIndex = 0;

        // ---- Filter toolbar ----
        var (filterCard, filters) = Ui.ToolbarCard();
        filters.Controls.AddRange([
            Ui.InlineLabel("FROM"), Ui.ToolbarBox(dtpFrom),
            Ui.InlineLabel("TO"), Ui.ToolbarBox(dtpTo),
            Ui.InlineLabel("BRANCH"), Ui.ToolbarBox(cboBranch),
            Ui.InlineLabel("TYPE"), Ui.ToolbarBox(cboType),
            Ui.InlineLabel("CATEGORY"), Ui.ToolbarBox(cboCategory),
            Ui.InlineLabel("TREND"), Ui.ToolbarBox(cboTrend),
            btnRefresh, btnExport, btnPrint]);

        // ---- Scrolling content: cards, insights, list ----
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Background, Padding = new Padding(0, 12, 8, 0) };
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, BackColor = Ui.Background
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var cards = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, BackColor = Ui.Background, Margin = new Padding(0, 0, 0, 6) };
        cards.Controls.AddRange([
            SummaryCard("TOTAL CONCERNS", lblTotal), SummaryCard("OPEN", lblOpen), SummaryCard("IN PROGRESS", lblInProgress),
            SummaryCard("RESOLVED / CLOSED", lblDone), SummaryCard("AVERAGE RATING", lblAvgRating),
            SummaryCard("RESOLUTION RATE", lblResolutionRate), SummaryCard("AVG RESOLUTION TIME", lblAvgDays)]);

        int row = 0;
        void AddSpan(Control c) { content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); content.Controls.Add(c, 0, row); content.SetColumnSpan(c, 2); row++; }
        void AddPair(Control left, Control right) { content.RowStyles.Add(new RowStyle(SizeType.AutoSize)); content.Controls.Add(left, 0, row); content.Controls.Add(right, 1, row); row++; }

        AddSpan(lblNotice);
        AddSpan(cards);
        AddPair(Section("Concerns per category", "Where most concerns come from (share of all concerns)", dgvCategory, 300, right: false),
                Section("Top 5 recurring complaint categories", "Fix these first", dgvTopComplaints, 300, right: true));
        AddPair(Section("Average rating per category", "Feedback ratings, lowest first", dgvRatingCategory, 270, right: false),
                Section("Average rating per branch", "Ratings and open complaints by branch", dgvRatingBranch, 270, right: true));
        AddSpan(Section("Needs attention", "Open or In Progress concerns submitted more than 3 days ago (any date)", dgvNeedsAttention, 260));
        AddSpan(Section("Trend", "Feedback, inquiries and complaints per week or month", dgvTrend, 260));
        AddSpan(Section("All concerns", "Read-only list for the filters above (double-click or View for details)", dgvConcerns, 400));
        scroller.Controls.Add(content);

        Controls.Add(scroller); Controls.Add(filterCard);

        // Category bar column (painted), not exported
        dgvCategory.Columns.Add(new DataGridViewTextBoxColumn { Name = BarColumn, HeaderText = "SHARE", Tag = GridReport.SkipTag, FillWeight = 140, SortMode = DataGridViewColumnSortMode.NotSortable });
        dgvCategory.DataBindingComplete += (_, _) => dgvCategory.Columns[BarColumn]!.DisplayIndex = dgvCategory.Columns.Count - 1;
        dgvCategory.CellPainting += PaintShareBar;

        Ui.AddRowButtons(dgvNeedsAttention, new Ui.RowButton(ViewColumn, "View", Ui.RowButtonStyle.Secondary, 76));
        Ui.AddRowButtons(dgvConcerns, new Ui.RowButton(ViewColumn, "View", Ui.RowButtonStyle.Secondary, 76));
        foreach (var grid in new[] { dgvNeedsAttention, dgvConcerns })
        {
            grid.CellClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && grid.Columns[e.ColumnIndex].Name == ViewColumn) ViewRow(grid, e.RowIndex); };
            grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && !Ui.IsRowButton(grid, e.ColumnIndex)) ViewRow(grid, e.RowIndex); };
        }
        foreach (var grid in new[] { dgvCategory, dgvTopComplaints, dgvRatingCategory, dgvRatingBranch, dgvNeedsAttention, dgvTrend, dgvConcerns })
            grid.DataBindingComplete += (_, _) => FormatGrid(grid);

        dtpFrom.ValueChanged += (_, _) => RefreshAll();
        dtpTo.ValueChanged += (_, _) => RefreshAll();
        foreach (var combo in new[] { cboBranch, cboType, cboCategory, cboTrend }) combo.SelectedIndexChanged += (_, _) => RefreshAll();
        btnRefresh.Click += (_, _) => RefreshAll();
        btnExport.Click += (_, _) => BuildReport().ExportCsv(Ui.DialogOwner(this), $"BrewHaven-ConcernInsights-{dtpFrom.Value:yyyyMMdd}-{dtpTo.Value:yyyyMMdd}.csv");
        btnPrint.Click += (_, _) => BuildReport().Print(Ui.DialogOwner(this));
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadBranches(); loading = false; RefreshAll(); };
    }

    // ---- Layout helpers ------------------------------------------------------------------------------

    private static Label CardValue() => new()
    {
        Text = "-", AutoSize = true, Font = FontHelper.SemiBold(18f), ForeColor = Ui.Primary, BackColor = Color.Transparent,
        Margin = new Padding(0, 2, 0, 0), UseMnemonic = false
    };

    private static CardPanel SummaryCard(string caption, Label value)
    {
        var card = Ui.Card(DockStyle.None, 14); card.Size = new Size(176, 86); card.Margin = new Padding(0, 0, 12, 12);
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };
        stack.Controls.Add(new Label { Text = caption, AutoSize = true, Font = Ui.SmallBold, ForeColor = Ui.Muted, BackColor = Color.Transparent, Margin = Padding.Empty, UseMnemonic = false });
        stack.Controls.Add(value);
        card.Controls.Add(stack);
        return card;
    }

    /// <summary>A titled card holding a grid, with a fixed height so the page scrolls.</summary>
    private static CardPanel Section(string title, string subtitle, DataGridView grid, int height, bool? right = null)
    {
        var card = Ui.Card(DockStyle.None, 14);
        card.Height = height; card.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        card.Margin = right switch { false => new Padding(0, 0, 8, 14), true => new Padding(8, 0, 0, 14), _ => new Padding(0, 0, 0, 14) };
        var header = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Ui.Surface };
        header.Controls.Add(new Label { Text = subtitle, Dock = DockStyle.Top, Height = 20, Font = Ui.Small, ForeColor = Ui.Muted, BackColor = Ui.Surface, UseMnemonic = false });
        header.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 26, Font = Ui.SubHeading, ForeColor = Ui.Text, BackColor = Ui.Surface, UseMnemonic = false });
        grid.Dock = DockStyle.Fill;
        card.Controls.Add(grid); card.Controls.Add(header);
        return card;
    }

    // ---- Data ----------------------------------------------------------------------------------------

    private void LoadBranches()
    {
        try
        {
            DataTable branches = repository.GetBranches();
            bool isManager = FeedbackRepository.CurrentRoleIs("Manager");
            if (!isManager)
            {
                DataRow all = branches.NewRow(); all["BranchId"] = 0; all["BranchName"] = "All branches";
                branches.Rows.InsertAt(all, 0);
            }
            cboBranch.DisplayMember = "BranchName"; cboBranch.ValueMember = "BranchId"; cboBranch.DataSource = branches;
            cboBranch.Enabled = !isManager;   // Manager: own branch only
            if (isManager && branches.Rows.Count == 0)
            {
                lblNotice.Text = "Your account isn't assigned to a branch, so there is nothing to monitor. Ask an Admin to set your branch.";
                lblNotice.Visible = true;
            }
        }
        catch (Exception ex) { MessageBox.Show($"Could not load branches.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private ConcernFilter CurrentFilter() => new()
    {
        From = dtpFrom.Value.Date,
        To = dtpTo.Value.Date,
        BranchId = Convert.ToInt32(cboBranch.SelectedValue ?? 0),
        Type = cboType.SelectedIndex > 0 ? cboType.Text : null,
        Category = cboCategory.SelectedIndex > 0 ? cboCategory.Text : null,
    };

    private void RefreshAll()
    {
        if (loading) return;
        if (dtpTo.Value.Date < dtpFrom.Value.Date)
        {
            MessageBox.Show("'To' must be on or after 'From'.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            Cursor = Cursors.WaitCursor;
            ConcernFilter filter = CurrentFilter();
            summary = repository.GetSummary(filter);
            lblTotal.Text = summary.Total.ToString("N0");
            lblOpen.Text = summary.Open.ToString("N0");
            lblInProgress.Text = summary.InProgress.ToString("N0");
            lblDone.Text = summary.ResolvedOrClosed.ToString("N0");
            lblAvgRating.Text = summary.AverageRating is decimal r ? $"{r:0.00} / 5" : "-";
            lblResolutionRate.Text = $"{summary.ResolutionRate:0.#}%";
            lblAvgDays.Text = summary.AverageResolutionDays is decimal d ? $"{d:0.#} days" : "-";

            dgvCategory.DataSource = repository.GetCategoryBreakdown(filter);
            dgvTopComplaints.DataSource = repository.GetTopComplaintCategories(filter);
            dgvRatingCategory.DataSource = repository.GetRatingByCategory(filter);
            dgvRatingBranch.DataSource = repository.GetRatingByBranch(filter);
            dgvNeedsAttention.DataSource = repository.GetNeedsAttention(filter);
            dgvTrend.DataSource = repository.GetTrend(filter, monthly: cboTrend.Text == "Monthly");
            dgvConcerns.DataSource = repository.GetMonitorList(filter);
        }
        catch (Exception ex) { MessageBox.Show($"Could not load the monitoring data.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { Cursor = Cursors.Default; }
    }

    private static readonly Dictionary<string, string> Headers = new()
    {
        ["DateSubmitted"] = "DATE", ["AverageRating"] = "AVG RATING", ["OpenComplaints"] = "OPEN COMPLAINTS",
        ["DaysOpen"] = "DAYS OPEN", ["Percentage"] = "%",
    };

    private void FormatGrid(DataGridView grid)
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (column is DataGridViewButtonColumn || column.Name == BarColumn) continue;
            column.HeaderText = Headers.TryGetValue(column.DataPropertyName, out var h) ? h : column.DataPropertyName.ToUpperInvariant();
            if (column.ValueType == typeof(int) || column.ValueType == typeof(decimal))
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        var c = grid.Columns;
        foreach (string hidden in new[] { "FeedbackId", "IsArchived" }) if (c.Contains(hidden)) c[hidden]!.Visible = false;
        if (c.Contains("DateSubmitted")) c["DateSubmitted"]!.DefaultCellStyle.Format = "MMM d, yyyy";
        if (c.Contains("Subject")) c["Subject"]!.FillWeight = 180;
        if (c.Contains("Percentage")) c["Percentage"]!.DefaultCellStyle.Format = "0.0'%'";
        if (c.Contains("AverageRating")) c["AverageRating"]!.DefaultCellStyle.Format = "0.00";
        if (grid == dgvTrend && c.Contains("Period"))
            c["Period"]!.DefaultCellStyle.Format = cboTrend.Text == "Monthly" ? "MMMM yyyy" : "'Week of' MMM d, yyyy";
    }

    /// <summary>Draws the share-of-concerns bar for the category grid (Warm Sand bar scaled to the largest category).</summary>
    private void PaintShareBar(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is null || dgvCategory.Columns[e.ColumnIndex].Name != BarColumn) return;
        e.PaintBackground(e.CellBounds, true);
        if (dgvCategory.DataSource is DataTable t && t.Rows.Count > 0 && dgvCategory.Rows[e.RowIndex].DataBoundItem is DataRowView v)
        {
            decimal max = t.Rows.Cast<DataRow>().Max(r => Convert.ToDecimal(r["Percentage"]));
            decimal value = Convert.ToDecimal(v["Percentage"]);
            float width = max <= 0 ? 0 : (float)(value / max) * (e.CellBounds.Width - 16);
            var bar = new RectangleF(e.CellBounds.X + 8, e.CellBounds.Y + 11, Math.Max(width, 3), e.CellBounds.Height - 22);
            var g = e.Graphics; var old = g.SmoothingMode; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var path = Ui.RoundRect(bar, 4)) using (var brush = new SolidBrush(Ui.WarmSand)) g.FillPath(brush, path);
            g.SmoothingMode = old;
        }
        e.Handled = true;
    }

    private void ViewRow(DataGridView grid, int rowIndex)
    {
        if (grid.Rows[rowIndex].Cells["FeedbackId"].Value is not int id) return;
        using var details = new FeedbackDetailsDialog(id, canManage: false);   // Manager: read-only, no Manage button
        details.ShowDialog(Ui.DialogOwner(this));
    }

    private GridReport BuildReport() => new()
    {
        Title = "Customer Concerns Insights",
        Filters = $"{cboBranch.Text}  |  {dtpFrom.Value:MMM d, yyyy} to {dtpTo.Value:MMM d, yyyy}  |  {cboType.Text}  |  {cboCategory.Text}",
        SummaryLines =
        [
            $"Total concerns: {lblTotal.Text}", $"Open: {lblOpen.Text}", $"In progress: {lblInProgress.Text}",
            $"Resolved / closed: {lblDone.Text}", $"Average rating: {lblAvgRating.Text}",
            $"Resolution rate: {lblResolutionRate.Text}", $"Average resolution time: {lblAvgDays.Text}"
        ],
        Sections =
        [
            ("Concerns per category", dgvCategory), ("Top 5 recurring complaint categories", dgvTopComplaints),
            ("Average rating per category", dgvRatingCategory), ("Average rating per branch", dgvRatingBranch),
            ("Needs attention (unresolved, older than 3 days)", dgvNeedsAttention), ($"Trend ({cboTrend.Text.ToLowerInvariant()})", dgvTrend),
            ("All concerns", dgvConcerns)
        ],
    };
}
