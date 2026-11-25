using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Drawing;
using System.Drawing.Printing;
using System.Text;

namespace BrewHaven.Forms;

/// <summary>
/// Admin page (read-only): Sales / Orders / Customer Feedback / Branch Summary reports, filtered by branch and
/// date range, with a summary strip and Export to CSV / Print.
/// </summary>
public sealed class ReportsForm : Form
{
    private readonly ComboBox cboReportType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly ComboBox cboBranch = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly DateTimePicker dtpFrom = new() { Width = 140 };
    private readonly DateTimePicker dtpTo = new() { Width = 140 };
    private readonly RoundedButton btnGenerate = Ui.PrimaryButton("Generate");
    private readonly RoundedButton btnExport = Ui.SecondaryButton("Export to CSV");
    private readonly RoundedButton btnPrint = Ui.SecondaryButton("Print");
    private readonly FlowLayoutPanel pnlSummary = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Ui.Background, Padding = new Padding(0, 12, 0, 0) };
    private readonly DataGridView dgvReport = Ui.Grid();

    private string generatedTitle = "";
    private string generatedFilters = "";
    private readonly List<string> summaryLines = [];
    private int printRow;
    private readonly int? lockedBranchId;

    /// <param name="lockedBranchId">
    /// Manager: their own branch, which can't be changed (pass 0 or less when they have no branch: nothing is shown).
    /// Admin: null, any branch.
    /// </param>
    public ReportsForm(int? lockedBranchId = null)
    {
        this.lockedBranchId = lockedBranchId;
        Text = "Reports"; BackColor = Ui.Background;

        cboReportType.Items.AddRange(["Sales", "Orders", "Customer Feedback", "Branch Summary"]); cboReportType.SelectedIndex = 0;
        dtpFrom.Value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        dtpTo.Value = DateTime.Today;
        btnExport.Enabled = btnPrint.Enabled = false;

        var filterCard = Ui.Card(DockStyle.Top, 12); filterCard.Height = 64;
        var filters = Ui.Toolbar(DockStyle.Top);
        filters.SizeChanged += (_, _) => filterCard.Height = filters.Height + filterCard.Padding.Vertical;
        Control Box(Control c) { var b = Ui.Boxed(c); b.Margin = new Padding(0, 3, 14, 0); return b; }
        filters.Controls.AddRange([
            Ui.InlineLabel("REPORT"), Box(cboReportType),
            Ui.InlineLabel("BRANCH"), Box(cboBranch),
            Ui.InlineLabel("FROM"), Box(dtpFrom),
            Ui.InlineLabel("TO"), Box(dtpTo),
            btnGenerate, btnExport, btnPrint]);
        filterCard.Controls.Add(filters);

        Controls.Add(Ui.GridCard(dgvReport)); Controls.Add(Ui.Spacer(DockStyle.Top, 12)); Controls.Add(pnlSummary); Controls.Add(filterCard);

        btnGenerate.Click += (_, _) => Generate();
        btnExport.Click += BtnExport_Click;
        btnPrint.Click += BtnPrint_Click;
        dgvReport.DataBindingComplete += (_, _) => FormatColumns();
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadBranches(); Generate(); };
    }

    private void LoadBranches()
    {
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new("SELECT BranchId, BranchName FROM Branches ORDER BY BranchName", connection);
            connection.Open();
            var branches = new DataTable();
            using (SqlDataReader reader = command.ExecuteReader()) branches.Load(reader);
            DataRow all = branches.NewRow(); all["BranchId"] = 0; all["BranchName"] = "All branches";
            branches.Rows.InsertAt(all, 0);
            cboBranch.DisplayMember = "BranchName"; cboBranch.ValueMember = "BranchId"; cboBranch.DataSource = branches;
            cboBranch.SelectedValue = 0;

            if (lockedBranchId is int locked)
            {
                // Manager: only their own branch.
                if (locked > 0 && branches.Select($"BranchId = {locked}").Length > 0)
                {
                    cboBranch.SelectedValue = locked;
                }
                else
                {
                    btnGenerate.Enabled = false;
                    pnlSummary.Controls.Add(new Label
                    {
                        Text = "Your account isn't assigned to a branch, so there are no reports to show. Ask an Admin to set your branch.",
                        AutoSize = true, Font = Ui.BodyBold, ForeColor = Ui.Danger, Margin = new Padding(0, 0, 0, 8)
                    });
                }
                cboBranch.Enabled = false;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load branches.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---- Report queries (all filtered by @BranchId (0 = all) and @From <= date < @To) -------------
    private const string SalesSql = """
        SELECT CAST(o.OrderDate AS date) AS [Date], b.BranchName AS Branch, COUNT(*) AS Orders,
               SUM(o.Subtotal) AS Subtotal, SUM(o.Discount) AS Discount, SUM(o.Total) AS Sales
        FROM Orders o INNER JOIN Branches b ON b.BranchId = o.BranchId
        WHERE o.Status IN ('Completed', 'Paid') AND o.OrderDate >= @From AND o.OrderDate < @To AND (@BranchId = 0 OR o.BranchId = @BranchId)
        GROUP BY CAST(o.OrderDate AS date), b.BranchName
        ORDER BY [Date], Branch
        """;

    private const string OrdersSql = """
        SELECT o.OrderId AS [Order #], o.OrderDate AS [Date], b.BranchName AS Branch, ISNULL(c.FullName, 'Walk-In') AS Customer,
               u.FullName AS Staff, o.OrderType AS [Type], o.Status,
               (SELECT TOP 1 p.Method FROM Payments p WHERE p.OrderId = o.OrderId ORDER BY p.PaidAt DESC) AS Payment,
               o.Total
        FROM Orders o
        INNER JOIN Branches b ON b.BranchId = o.BranchId
        INNER JOIN Users u ON u.UserId = o.StaffUserId
        LEFT JOIN Customers c ON c.CustomerId = o.CustomerId
        WHERE o.OrderDate >= @From AND o.OrderDate < @To AND (@BranchId = 0 OR o.BranchId = @BranchId)
        ORDER BY o.OrderDate DESC
        """;

    private const string FeedbackSql = """
        SELECT f.FeedbackId AS [#], f.DateSubmitted AS [Date], b.BranchName AS Branch, ISNULL(c.FullName, 'Walk-In') AS Customer,
               f.Type, f.Subject, f.Rating, f.Status, f.DateResolved AS Resolved
        FROM Feedback f
        INNER JOIN Branches b ON b.BranchId = f.BranchId
        LEFT JOIN Customers c ON c.CustomerId = f.CustomerId
        WHERE f.DateSubmitted >= @From AND f.DateSubmitted < @To AND (@BranchId = 0 OR f.BranchId = @BranchId)
        ORDER BY f.DateSubmitted DESC
        """;

    private const string BranchSummarySql = """
        SELECT b.BranchName AS Branch, CASE WHEN b.IsActive = 1 THEN 'Active' ELSE 'Inactive' END AS Status,
               ISNULL(m.FullName, '-') AS Manager,
               (SELECT COUNT(*) FROM Orders o WHERE o.BranchId = b.BranchId AND o.Status IN ('Completed', 'Paid') AND o.OrderDate >= @From AND o.OrderDate < @To) AS Orders,
               (SELECT ISNULL(SUM(o.Total), 0) FROM Orders o WHERE o.BranchId = b.BranchId AND o.Status IN ('Completed', 'Paid') AND o.OrderDate >= @From AND o.OrderDate < @To) AS Sales,
               (SELECT COUNT(*) FROM Feedback f WHERE f.BranchId = b.BranchId AND f.DateSubmitted >= @From AND f.DateSubmitted < @To) AS Feedback,
               (SELECT COUNT(*) FROM Feedback f WHERE f.BranchId = b.BranchId AND f.Type = 'Complaint' AND f.Status IN ('Open', 'In Progress')) AS [Open Complaints],
               (SELECT COUNT(*) FROM Users u WHERE u.BranchId = b.BranchId AND u.IsActive = 1) AS [Active Staff],
               (SELECT COUNT(*) FROM Inventory i WHERE i.BranchId = b.BranchId AND i.Quantity <= i.ReorderLevel) AS [Low Stock Items]
        FROM Branches b
        LEFT JOIN Users m ON m.UserId = b.ManagerUserId
        WHERE @BranchId = 0 OR b.BranchId = @BranchId
        ORDER BY b.BranchName
        """;

    private void Generate()
    {
        if (!btnGenerate.Enabled) return;   // Manager without a branch
        if (dtpTo.Value.Date < dtpFrom.Value.Date)
        {
            MessageBox.Show("'Date To' must be on or after 'Date From'.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            dtpTo.Focus();
            return;
        }
        string type = cboReportType.Text;
        string sql = type switch
        {
            "Orders" => OrdersSql,
            "Customer Feedback" => FeedbackSql,
            "Branch Summary" => BranchSummarySql,
            _ => SalesSql
        };
        try
        {
            if (type == "Branch Summary") DbHelper.EnsureSchema();   // needs Branches.ManagerUserId
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@BranchId", SqlDbType.Int).Value = Convert.ToInt32(cboBranch.SelectedValue ?? 0);
            command.Parameters.Add("@From", SqlDbType.DateTime2).Value = dtpFrom.Value.Date;
            command.Parameters.Add("@To", SqlDbType.DateTime2).Value = dtpTo.Value.Date.AddDays(1);   // inclusive of the To day
            connection.Open();
            var data = new DataTable();
            using (SqlDataReader reader = command.ExecuteReader()) data.Load(reader);
            dgvReport.DataSource = data;

            generatedTitle = $"{type} Report";
            generatedFilters = $"{cboBranch.Text}  |  {dtpFrom.Value:MMM d, yyyy} to {dtpTo.Value:MMM d, yyyy}";
            BuildSummary(type, data);
            btnExport.Enabled = btnPrint.Enabled = data.Rows.Count > 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not generate the report.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static decimal Sum(DataTable t, string column) =>
        t.Rows.Cast<DataRow>().Where(r => r[column] != DBNull.Value).Sum(r => Convert.ToDecimal(r[column]));

    private static int Count(DataTable t, string column, string value) =>
        t.Rows.Cast<DataRow>().Count(r => r[column] as string == value);

    private void BuildSummary(string type, DataTable t)
    {
        summaryLines.Clear();
        switch (type)
        {
            case "Sales":
            {
                decimal sales = Sum(t, "Sales"), orders = Sum(t, "Orders");
                summaryLines.Add($"Total sales: PHP {sales:N2}");
                summaryLines.Add($"Orders: {orders:N0}");
                summaryLines.Add($"Average order: PHP {(orders > 0 ? sales / orders : 0):N2}");
                summaryLines.Add($"Discounts given: PHP {Sum(t, "Discount"):N2}");
                break;
            }
            case "Orders":
            {
                var completed = t.Rows.Cast<DataRow>().Where(r => r["Status"] is "Completed" or "Paid");
                summaryLines.Add($"Orders: {t.Rows.Count:N0}");
                summaryLines.Add($"Paid / completed: {Count(t, "Status", "Paid") + Count(t, "Status", "Completed")}");
                summaryLines.Add($"Pending: {Count(t, "Status", "Pending")}");
                summaryLines.Add($"Cancelled: {Count(t, "Status", "Cancelled")}");
                summaryLines.Add($"Completed sales: PHP {completed.Sum(r => Convert.ToDecimal(r["Total"])):N2}");
                break;
            }
            case "Customer Feedback":
            {
                summaryLines.Add($"Total entries: {t.Rows.Count}");
                foreach (string kind in new[] { "Feedback", "Inquiry", "Complaint" }) summaryLines.Add($"{kind}: {Count(t, "Type", kind)}");
                foreach (string status in new[] { "Open", "In Progress", "Resolved", "Closed" }) summaryLines.Add($"{status}: {Count(t, "Status", status)}");
                var ratings = t.Rows.Cast<DataRow>().Where(r => r["Rating"] != DBNull.Value).Select(r => Convert.ToDouble(r["Rating"])).ToList();
                summaryLines.Add(ratings.Count > 0 ? $"Average rating: {ratings.Average():0.0} / 5" : "Average rating: -");
                break;
            }
            case "Branch Summary":
                summaryLines.Add($"Branches: {t.Rows.Count}");
                summaryLines.Add($"Total sales: PHP {Sum(t, "Sales"):N2}");
                summaryLines.Add($"Completed orders: {Sum(t, "Orders"):N0}");
                summaryLines.Add($"Feedback entries: {Sum(t, "Feedback"):N0}");
                summaryLines.Add($"Open complaints: {Sum(t, "Open Complaints"):N0}");
                summaryLines.Add($"Low-stock items: {Sum(t, "Low Stock Items"):N0}");
                break;
        }
        if (t.Rows.Count == 0) summaryLines.Insert(0, "No records for these filters");

        pnlSummary.SuspendLayout();
        pnlSummary.Controls.Clear();
        foreach (string line in summaryLines)
            pnlSummary.Controls.Add(new Label
            {
                Text = line, AutoSize = true, Font = Ui.BodyBold, ForeColor = Ui.Primary, BackColor = Ui.AccentSoft,
                Padding = new Padding(12, 6, 12, 6), Margin = new Padding(0, 0, 8, 8), UseMnemonic = false
            });
        pnlSummary.ResumeLayout(true);
    }

    private void FormatColumns()
    {
        bool dateOnly = cboReportType.Text == "Sales";
        foreach (DataGridViewColumn column in dgvReport.Columns)
        {
            column.HeaderText = column.HeaderText.ToUpperInvariant();
            Type? valueType = column.ValueType;
            if (valueType == typeof(decimal))
            {
                column.DefaultCellStyle.Format = "N2";
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
            else if (valueType == typeof(int))
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            else if (valueType == typeof(DateTime))
                column.DefaultCellStyle.Format = dateOnly ? "MMM d, yyyy" : "MMM d, yyyy h:mm tt";
        }
        if (dgvReport.Columns.Contains("Subject")) dgvReport.Columns["Subject"]!.FillWeight = 200;
    }

    // ---- Export to CSV ---------------------------------------------------------------------------
    private void BtnExport_Click(object? sender, EventArgs e)
    {
        if (dgvReport.Rows.Count == 0) return;
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"BrewHaven-{cboReportType.Text.Replace(" ", "")}-{dtpFrom.Value:yyyyMMdd}-{dtpTo.Value:yyyyMMdd}.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            static string Csv(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
            var columns = dgvReport.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
            var csv = new StringBuilder();
            csv.AppendLine(Csv(generatedTitle));
            csv.AppendLine(Csv(generatedFilters));
            csv.AppendLine();
            csv.AppendLine(string.Join(",", columns.Select(c => Csv(c.HeaderText))));
            foreach (DataGridViewRow row in dgvReport.Rows)
                csv.AppendLine(string.Join(",", columns.Select(c => Csv(row.Cells[c.Index].FormattedValue?.ToString()))));
            csv.AppendLine();
            foreach (string line in summaryLines) csv.AppendLine(Csv(line));
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));   // BOM so Excel opens it as UTF-8
            MessageBox.Show("Report exported.", "Export to CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export the report.\n{ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---- Print (with preview) ----------------------------------------------------------------------
    private void BtnPrint_Click(object? sender, EventArgs e)
    {
        if (dgvReport.Rows.Count == 0) return;
        using var document = new PrintDocument { DocumentName = generatedTitle };
        document.DefaultPageSettings.Landscape = true;
        document.DefaultPageSettings.Margins = new Margins(50, 50, 50, 50);
        document.BeginPrint += (_, _) => printRow = 0;
        document.PrintPage += PrintPage;
        using var preview = new PrintPreviewDialog { Document = document, Width = 1100, Height = 800, StartPosition = FormStartPosition.CenterParent };
        try { preview.ShowDialog(this); }
        catch (Exception ex) { MessageBox.Show($"Could not print.\n{ex.Message}", "Print Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void PrintPage(object? sender, PrintPageEventArgs e)
    {
        if (e.Graphics is null) return;
        Graphics g = e.Graphics;
        Rectangle area = e.MarginBounds;
        Font titleFont = FontHelper.SemiBold(14f), bodyFont = FontHelper.Regular(8f), headFont = FontHelper.SemiBold(8f);
        using var textBrush = new SolidBrush(Ui.Espresso);
        using var headBrush = new SolidBrush(Ui.DarkChocolate);
        using var altBrush = new SolidBrush(Ui.Ivory);
        using var linePen = new Pen(Ui.Border);
        var cellFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };

        float y = area.Top;
        if (printRow == 0)
        {
            g.DrawString($"Brew Haven — {generatedTitle}", titleFont, textBrush, area.Left, y); y += titleFont.GetHeight(g) + 4;
            g.DrawString(generatedFilters, bodyFont, textBrush, area.Left, y); y += bodyFont.GetHeight(g) + 4;
            g.DrawString(string.Join("    ", summaryLines), headFont, textBrush, new RectangleF(area.Left, y, area.Width, 40)); y += 36;
        }

        var columns = dgvReport.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
        float totalWidth = columns.Sum(c => c.Width);
        float[] widths = columns.Select(c => c.Width / totalWidth * area.Width).ToArray();
        float rowHeight = bodyFont.GetHeight(g) + 10;

        // Header row
        g.FillRectangle(headBrush, area.Left, y, area.Width, rowHeight);
        float x = area.Left;
        for (int i = 0; i < columns.Count; i++)
        {
            g.DrawString(columns[i].HeaderText, headFont, Brushes.White, new RectangleF(x + 4, y, widths[i] - 8, rowHeight), cellFormat);
            x += widths[i];
        }
        y += rowHeight;

        // Data rows until the page is full
        while (printRow < dgvReport.Rows.Count)
        {
            if (y + rowHeight > area.Bottom) { e.HasMorePages = true; return; }
            DataGridViewRow row = dgvReport.Rows[printRow];
            if (printRow % 2 == 1) g.FillRectangle(altBrush, area.Left, y, area.Width, rowHeight);
            x = area.Left;
            for (int i = 0; i < columns.Count; i++)
            {
                cellFormat.Alignment = columns[i].DefaultCellStyle.Alignment switch
                {
                    DataGridViewContentAlignment.MiddleRight => StringAlignment.Far,
                    DataGridViewContentAlignment.MiddleCenter => StringAlignment.Center,
                    _ => StringAlignment.Near
                };
                g.DrawString(row.Cells[columns[i].Index].FormattedValue?.ToString() ?? "", bodyFont, textBrush,
                    new RectangleF(x + 4, y, widths[i] - 8, rowHeight), cellFormat);
                x += widths[i];
            }
            g.DrawLine(linePen, area.Left, y + rowHeight, area.Right, y + rowHeight);
            y += rowHeight;
            printRow++;
        }
        e.HasMorePages = false;
    }
}
