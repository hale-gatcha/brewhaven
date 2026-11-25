using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.Forms;

/// <summary>
/// Manager page (read-only): paid / completed sales by Daily (days of the month), Weekly (weeks of the month) or
/// Monthly (months of the year), with totals, Top Selling Items, Promo Usage and Export to CSV.
/// </summary>
public sealed class SalesViewForm : Form
{
    private readonly ComboBox cboPeriod = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly DateTimePicker dtpDate = new() { Width = 140 };
    private readonly ComboBox cboBranch = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly RoundedButton btnView = Ui.PrimaryButton("View");
    private readonly RoundedButton btnExport = Ui.SecondaryButton("Export to CSV");
    private readonly Label lblRange = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(8, 12, 0, 0) };
    private readonly FlowLayoutPanel pnlTotals = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Ui.Background, Padding = new Padding(0, 12, 0, 0) };
    private readonly DataGridView dgvSales = Ui.Grid();
    private readonly DataGridView dgvTopItems = Ui.Grid();
    private readonly DataGridView dgvPromoUsage = Ui.Grid();
    private readonly List<string> totals = [];
    private string rangeText = "";

    public SalesViewForm()
    {
        Text = "View Sales"; BackColor = Ui.Background;
        cboPeriod.Items.AddRange(["Daily", "Weekly", "Monthly"]); cboPeriod.SelectedIndex = 0;
        dtpDate.Value = DateTime.Today;
        btnExport.Enabled = false;

        var (filterCard, filters) = Ui.ToolbarCard();
        filters.Controls.AddRange([
            Ui.InlineLabel("PERIOD"), Ui.ToolbarBox(cboPeriod), Ui.InlineLabel("DATE"), Ui.ToolbarBox(dtpDate),
            Ui.InlineLabel("BRANCH"), Ui.ToolbarBox(cboBranch), btnView, btnExport, lblRange]);

        // Bottom: Top Selling Items + Promo Usage side by side
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 250, ColumnCount = 2, BackColor = Ui.Background, Padding = new Padding(0, 12, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        bottom.Controls.Add(TitledGrid("Top selling items", dgvTopItems, new Padding(0, 0, 8, 0)), 0, 0);
        bottom.Controls.Add(TitledGrid("Promo usage", dgvPromoUsage, new Padding(8, 0, 0, 0)), 1, 0);

        Controls.Add(Ui.GridCard(dgvSales)); Controls.Add(bottom); Controls.Add(Ui.Spacer(DockStyle.Top, 4)); Controls.Add(pnlTotals); Controls.Add(filterCard);

        btnView.Click += (_, _) => LoadSales();
        btnExport.Click += BtnExport_Click;
        foreach (var grid in new[] { dgvSales, dgvTopItems, dgvPromoUsage }) grid.DataBindingComplete += (_, _) => FormatGrid(grid);
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadBranches(); LoadSales(); };
    }

    private static Control TitledGrid(string title, DataGridView grid, Padding margin)
    {
        var card = Ui.Card(DockStyle.Fill, 12); card.Margin = margin;
        grid.Dock = DockStyle.Fill;
        card.Controls.Add(grid); card.Controls.Add(Ui.SectionTitle(title));
        return card;
    }

    private void LoadBranches()
    {
        try
        {
            DataTable branches = Query("SELECT BranchId, BranchName FROM Branches ORDER BY BranchName");
            DataRow all = branches.NewRow(); all["BranchId"] = 0; all["BranchName"] = "All branches";
            branches.Rows.InsertAt(all, 0);
            cboBranch.DisplayMember = "BranchName"; cboBranch.ValueMember = "BranchId"; cboBranch.DataSource = branches;
            cboBranch.SelectedValue = Session.CurrentUser?.BranchId is int mine && branches.Select($"BranchId = {mine}").Length > 0 ? mine : 0;
        }
        catch (Exception ex) { MessageBox.Show($"Could not load branches.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    /// <summary>Daily = days of the selected month, Weekly = weeks (Mon-Sun) of that month, Monthly = months of that year.</summary>
    private (DateTime From, DateTime To, string GroupExpr, string Label) Range()
    {
        DateTime d = dtpDate.Value.Date;
        var monthStart = new DateTime(d.Year, d.Month, 1);
        return cboPeriod.Text switch
        {
            "Weekly" => (monthStart, monthStart.AddMonths(1),
                // Monday of the order's week (DATEDIFF from a known Monday, 1900-01-01, is independent of DATEFIRST)
                "DATEADD(DAY, -(DATEDIFF(DAY, '19000101', CAST(o.OrderDate AS date)) % 7), CAST(o.OrderDate AS date))",
                $"Weeks of {monthStart:MMMM yyyy}"),
            "Monthly" => (new DateTime(d.Year, 1, 1), new DateTime(d.Year + 1, 1, 1),
                "DATEFROMPARTS(YEAR(o.OrderDate), MONTH(o.OrderDate), 1)", $"Months of {d.Year}"),
            _ => (monthStart, monthStart.AddMonths(1), "CAST(o.OrderDate AS date)", $"Days of {monthStart:MMMM yyyy}")
        };
    }

    private SqlParameter[] Params(DateTime from, DateTime to) =>
    [
        P("@From", SqlDbType.DateTime2, from), P("@To", SqlDbType.DateTime2, to),
        P("@BranchId", SqlDbType.Int, Convert.ToInt32(cboBranch.SelectedValue ?? 0))
    ];

    private void LoadSales()
    {
        var (from, to, groupExpr, label) = Range();
        const string paid = "o.Status IN ('Paid', 'Completed') AND o.OrderDate >= @From AND o.OrderDate < @To AND (@BranchId = 0 OR o.BranchId = @BranchId)";
        try
        {
            DataTable sales = Query($"""
                SELECT {groupExpr} AS Period, COUNT(*) AS Orders, SUM(o.Subtotal) AS [Gross Sales],
                       SUM(o.Discount) AS [Discounts Applied], SUM(o.Total) AS [Net Sales]
                FROM Orders o WHERE {paid}
                GROUP BY {groupExpr} ORDER BY Period
                """, Params(from, to));
            dgvSales.DataSource = sales;

            dgvTopItems.DataSource = Query($"""
                SELECT TOP 10 mi.ItemName AS Item, mi.Category, SUM(oi.Quantity) AS [Qty Sold], SUM(oi.LineTotal) AS Sales
                FROM OrderItems oi INNER JOIN Orders o ON o.OrderId = oi.OrderId INNER JOIN MenuItems mi ON mi.MenuItemId = oi.MenuItemId
                WHERE {paid}
                GROUP BY mi.ItemName, mi.Category ORDER BY [Qty Sold] DESC, Sales DESC
                """, Params(from, to));

            dgvPromoUsage.DataSource = Query($"""
                SELECT p.PromotionName AS Promo, 'Order discount' AS [Used As], COUNT(*) AS Uses, SUM(o.Discount) AS [Discount Given]
                FROM Orders o INNER JOIN Promotions p ON p.PromotionId = o.PromotionId
                WHERE {paid}
                GROUP BY p.PromotionName
                UNION ALL
                SELECT COALESCE(p.PromotionName, r.RewardName), 'Points redemption', COUNT(*), SUM(ISNULL(r.DiscountValue, 0))
                FROM RewardRedemptions rr
                LEFT JOIN Rewards r ON r.RewardId = rr.RewardId
                LEFT JOIN Promotions p ON p.PromotionId = rr.PromotionId
                WHERE rr.RedeemedAt >= @From AND rr.RedeemedAt < @To
                  AND (@BranchId = 0 OR rr.OrderId IN (SELECT OrderId FROM Orders WHERE BranchId = @BranchId))
                GROUP BY COALESCE(p.PromotionName, r.RewardName)
                ORDER BY Uses DESC
                """, Params(from, to));

            decimal gross = Sum(sales, "Gross Sales"), discounts = Sum(sales, "Discounts Applied"), net = Sum(sales, "Net Sales");
            decimal orders = Sum(sales, "Orders");
            totals.Clear();
            totals.AddRange([
                $"Orders: {orders:N0}", $"Gross sales: PHP {gross:N2}", $"Discounts: PHP {discounts:N2}",
                $"Net sales: PHP {net:N2}", $"Average order: PHP {(orders > 0 ? net / orders : 0):N2}"]);
            pnlTotals.SuspendLayout(); pnlTotals.Controls.Clear();
            foreach (string t in totals) pnlTotals.Controls.Add(Ui.Badge(t));
            pnlTotals.ResumeLayout(true);

            rangeText = $"{label}  ·  {cboBranch.Text}";
            lblRange.Text = rangeText;
            btnExport.Enabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load sales.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static decimal Sum(DataTable t, string column) =>
        t.Rows.Cast<DataRow>().Where(r => r[column] != DBNull.Value).Sum(r => Convert.ToDecimal(r[column]));

    private void FormatGrid(DataGridView grid)
    {
        string dateFormat = cboPeriod.Text == "Monthly" ? "MMMM yyyy" : cboPeriod.Text == "Weekly" ? "'Week of' MMM d" : "ddd, MMM d";
        foreach (DataGridViewColumn c in grid.Columns)
        {
            c.HeaderText = c.HeaderText.ToUpperInvariant();
            if (c.ValueType == typeof(decimal)) { c.DefaultCellStyle.Format = "N2"; c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
            else if (c.ValueType == typeof(int)) c.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            else if (c.ValueType == typeof(DateTime)) c.DefaultCellStyle.Format = dateFormat;
        }
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog { Filter = "CSV file (*.csv)|*.csv", FileName = $"BrewHaven-Sales-{cboPeriod.Text}-{dtpDate.Value:yyyyMMdd}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            static string Csv(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
            var csv = new StringBuilder();
            csv.AppendLine(Csv($"Brew Haven - {cboPeriod.Text} Sales")).AppendLine(Csv(rangeText)).AppendLine();
            foreach (var (title, grid) in new[] { ("Sales", dgvSales), ("Top Selling Items", dgvTopItems), ("Promo Usage", dgvPromoUsage) })
            {
                csv.AppendLine(Csv(title));
                var cols = grid.Columns.Cast<DataGridViewColumn>().Where(c => c.Visible).OrderBy(c => c.DisplayIndex).ToList();
                csv.AppendLine(string.Join(",", cols.Select(c => Csv(c.HeaderText))));
                foreach (DataGridViewRow row in grid.Rows)
                    csv.AppendLine(string.Join(",", cols.Select(c => Csv(row.Cells[c.Index].FormattedValue?.ToString()))));
                csv.AppendLine();
            }
            foreach (string t in totals) csv.AppendLine(Csv(t));
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
            MessageBox.Show("Sales exported.", "Export to CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show($"Could not export.\n{ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
