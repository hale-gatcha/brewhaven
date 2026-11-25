using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using System.Data;
using System.Drawing;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.Forms;

/// <summary>
/// Manager page: pick a customer, see their points, pick an available reward / points promo and Redeem
/// (confirmation pop-up + redemption code). Redemption history below with search and date filter.
/// </summary>
public sealed class RedeemRewardsForm : Form
{
    private readonly TextBox txtCustomerSearch = new() { Width = 230, PlaceholderText = "Name, contact or email..." };
    private readonly DataGridView dgvCustomers = Ui.Grid();
    private readonly Label lblBalance = new() { Dock = DockStyle.Top, Height = 40, Font = Ui.SubHeading, ForeColor = Ui.Primary, UseMnemonic = false };
    private readonly DataGridView dgvRewards = Ui.Grid();
    private readonly RoundedButton btnRedeem = Ui.PrimaryButton("Redeem");
    private readonly TextBox txtHistorySearch = new() { Width = 220, PlaceholderText = "Search code, customer, item..." };
    private readonly DateTimePicker dtpFrom = new() { Width = 140 };
    private readonly DateTimePicker dtpTo = new() { Width = 140 };
    private readonly DataGridView dgvHistory = Ui.Grid();
    private DataTable customers = new(), history = new();

    public RedeemRewardsForm()
    {
        Text = "Redeem Rewards"; BackColor = Ui.Background;
        dtpFrom.Value = DateTime.Today.AddMonths(-1); dtpTo.Value = DateTime.Today;

        // ---- Top: customers (left) + balance and available rewards (right) ----
        var top = new Panel { Dock = DockStyle.Top, Height = 330, BackColor = Ui.Background };
        var customerCard = Ui.Card(DockStyle.Left, 12); customerCard.Width = 360;
        var findBar = Ui.Toolbar(DockStyle.Top); findBar.Padding = new Padding(0, 0, 0, 8);
        findBar.Controls.AddRange([Ui.InlineLabel("CUSTOMER"), Ui.ToolbarBox(txtCustomerSearch)]);
        dgvCustomers.Dock = DockStyle.Fill;
        customerCard.Controls.Add(dgvCustomers); customerCard.Controls.Add(findBar);

        var rewardsCard = Ui.Card(DockStyle.Fill, 12);
        var redeemBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 48, BackColor = Ui.Surface, Padding = new Padding(0, 8, 0, 0) };
        redeemBar.Controls.Add(btnRedeem);
        redeemBar.Controls.Add(new Label { Text = "Select a reward or promo, then click Redeem.", AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 12, 12, 0) });
        dgvRewards.Dock = DockStyle.Fill;
        rewardsCard.Controls.Add(dgvRewards); rewardsCard.Controls.Add(Ui.SectionTitle("Available rewards and promos")); rewardsCard.Controls.Add(lblBalance); rewardsCard.Controls.Add(redeemBar);
        top.Controls.Add(rewardsCard); top.Controls.Add(Ui.Spacer(DockStyle.Left, 16)); top.Controls.Add(customerCard);

        // ---- Bottom: redemption history ----
        var historyCard = Ui.Card(DockStyle.Fill, 12);
        var historyBar = Ui.Toolbar(DockStyle.Top); historyBar.Padding = new Padding(0, 0, 0, 8);
        var btnFilter = Ui.SecondaryButton("Apply");
        historyBar.Controls.AddRange([Ui.InlineLabel("HISTORY"), Ui.ToolbarBox(txtHistorySearch), Ui.InlineLabel("FROM"), Ui.ToolbarBox(dtpFrom), Ui.InlineLabel("TO"), Ui.ToolbarBox(dtpTo), btnFilter]);
        dgvHistory.Dock = DockStyle.Fill;
        historyCard.Controls.Add(dgvHistory); historyCard.Controls.Add(historyBar);

        Controls.Add(historyCard); Controls.Add(Ui.Spacer(DockStyle.Top, 16)); Controls.Add(top);

        lblBalance.Text = "Select a customer to see their points";
        btnRedeem.Enabled = false;
        txtCustomerSearch.TextChanged += (_, _) => Filter(customers, txtCustomerSearch.Text, "FullName", "ContactNumber", "Email");
        txtHistorySearch.TextChanged += (_, _) => Filter(history, txtHistorySearch.Text, "Code", "Customer", "Item", "Processed By");
        btnFilter.Click += (_, _) => LoadHistory();
        dgvCustomers.SelectionChanged += (_, _) => ShowBalance();
        dgvRewards.SelectionChanged += (_, _) => btnRedeem.Enabled = SelectedCustomerId() is not null && dgvRewards.CurrentRow is not null;
        btnRedeem.Click += (_, _) => Redeem();
        dgvCustomers.DataBindingComplete += (_, _) =>
        {
            foreach (string hidden in new[] { "CustomerId", "Email", "LoyaltyPoints" }) if (dgvCustomers.Columns.Contains(hidden)) dgvCustomers.Columns[hidden]!.Visible = false;
            if (dgvCustomers.Columns.Contains("FullName")) dgvCustomers.Columns["FullName"]!.HeaderText = "CUSTOMER";
            if (dgvCustomers.Columns.Contains("ContactNumber")) dgvCustomers.Columns["ContactNumber"]!.HeaderText = "CONTACT";
        };
        dgvRewards.DataBindingComplete += (_, _) =>
        {
            foreach (string hidden in new[] { "ItemId" }) if (dgvRewards.Columns.Contains(hidden)) dgvRewards.Columns[hidden]!.Visible = false;
            if (dgvRewards.Columns.Contains("Valid Until")) dgvRewards.Columns["Valid Until"]!.DefaultCellStyle.Format = "MMM d, yyyy";
            if (dgvRewards.Columns.Contains("Points Required")) dgvRewards.Columns["Points Required"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            foreach (DataGridViewColumn c in dgvRewards.Columns) c.HeaderText = c.HeaderText.ToUpperInvariant();
        };
        dgvHistory.DataBindingComplete += (_, _) =>
        {
            if (dgvHistory.Columns.Contains("Date")) dgvHistory.Columns["Date"]!.DefaultCellStyle.Format = "MMM d, yyyy h:mm tt";
            foreach (DataGridViewColumn c in dgvHistory.Columns) c.HeaderText = c.HeaderText.ToUpperInvariant();
        };
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadCustomers(); LoadRewards(); LoadHistory(); };
    }

    private static void Filter(DataTable table, string term, params string[] columns)
    {
        try { table.DefaultView.RowFilter = Ui.ContainsFilter(term, columns); }
        catch (Exception) { table.DefaultView.RowFilter = ""; }
    }

    private int? SelectedCustomerId() => dgvCustomers.CurrentRow?.Cells["CustomerId"].Value as int?;

    private void LoadCustomers()
    {
        try
        {
            int? keep = SelectedCustomerId();
            customers = Query("SELECT CustomerId, FullName, ISNULL(ContactNumber, '') AS ContactNumber, ISNULL(Email, '') AS Email, LoyaltyPoints FROM Customers WHERE IsActive = 1 ORDER BY FullName");
            dgvCustomers.DataSource = customers;
            Filter(customers, txtCustomerSearch.Text, "FullName", "ContactNumber", "Email");
            if (keep is int id)
                foreach (DataGridViewRow row in dgvCustomers.Rows)
                    if (row.Cells["CustomerId"].Value is int rid && rid == id) { dgvCustomers.CurrentCell = row.Cells["FullName"]; break; }
            ShowBalance();
        }
        catch (Exception ex) { MessageBox.Show($"Could not load customers.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LoadRewards()
    {
        try
        {
            // Active rewards, plus active, in-date promos that cost points.
            dgvRewards.DataSource = Query("""
                SELECT 'Reward' AS Kind, RewardId AS ItemId, RewardName AS Name, PointsRequired AS [Points Required],
                       'PHP ' + CONVERT(nvarchar(20), DiscountValue) + ' off' AS Benefit, CAST(NULL AS date) AS [Valid Until]
                FROM Rewards WHERE IsActive = 1
                UNION ALL
                SELECT 'Promo', PromotionId, PromotionName, PointsRequired,
                       CASE WHEN DiscountType = 'Fixed' THEN 'PHP ' + CONVERT(nvarchar(20), DiscountValue) + ' off'
                            ELSE CONVERT(nvarchar(20), CAST(DiscountValue AS decimal(5,2))) + '% off' END,
                       EndDate
                FROM Promotions
                WHERE IsActive = 1 AND ISNULL(PointsRequired, 0) > 0 AND CAST(GETDATE() AS date) BETWEEN StartDate AND EndDate
                ORDER BY [Points Required]
                """);
        }
        catch (Exception ex) { MessageBox.Show($"Could not load rewards.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LoadHistory()
    {
        if (dtpTo.Value.Date < dtpFrom.Value.Date)
        {
            MessageBox.Show("'To' must be on or after 'From'.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            history = Query("""
                SELECT rr.RedeemedAt AS [Date], ISNULL(rr.RedemptionCode, '-') AS Code, c.FullName AS Customer,
                       COALESCE(r.RewardName, p.PromotionName, '-') AS Item, rr.PointsRedeemed AS Points,
                       ISNULL(CAST(rr.OrderId AS nvarchar(10)), '-') AS [Order], ISNULL(u.FullName, '-') AS [Processed By]
                FROM RewardRedemptions rr
                INNER JOIN Customers c ON c.CustomerId = rr.CustomerId
                LEFT JOIN Rewards r ON r.RewardId = rr.RewardId
                LEFT JOIN Promotions p ON p.PromotionId = rr.PromotionId
                LEFT JOIN Users u ON u.UserId = rr.ProcessedByUserId
                WHERE rr.RedeemedAt >= @From AND rr.RedeemedAt < @To
                ORDER BY rr.RedeemedAt DESC
                """, P("@From", SqlDbType.DateTime2, dtpFrom.Value.Date), P("@To", SqlDbType.DateTime2, dtpTo.Value.Date.AddDays(1)));
            dgvHistory.DataSource = history;
            Filter(history, txtHistorySearch.Text, "Code", "Customer", "Item", "Processed By");
        }
        catch (Exception ex) { MessageBox.Show($"Could not load redemption history.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ShowBalance()
    {
        if (dgvCustomers.CurrentRow is not { } row || row.Cells["CustomerId"].Value is not int)
        {
            lblBalance.Text = "Select a customer to see their points";
            btnRedeem.Enabled = false;
            return;
        }
        lblBalance.Text = $"{row.Cells["FullName"].Value}:  {(row.Cells["LoyaltyPoints"].Value is int pts ? pts : 0):N0} points";
        btnRedeem.Enabled = dgvRewards.CurrentRow is not null;
        dgvRewards.Invalidate();
    }

    private void Redeem()
    {
        if (dgvCustomers.CurrentRow is not { } customer || customer.Cells["CustomerId"].Value is not int customerId)
        { MessageBox.Show("Select a customer first.", "Redeem", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (dgvRewards.CurrentRow is not { } reward)
        { MessageBox.Show("Select a reward or promo.", "Redeem", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        string name = customer.Cells["FullName"].Value?.ToString() ?? "";
        int balance = customer.Cells["LoyaltyPoints"].Value is int b ? b : 0;
        if (reward.Cells["Kind"].Value is not string kind || reward.Cells["ItemId"].Value is not int itemId) return;
        int points = reward.Cells["Points Required"].Value is int p ? p : 0;

        using var dialog = new RedeemConfirmDialog(customerId, name, balance, kind, itemId,
            reward.Cells["Name"].Value?.ToString() ?? "", reward.Cells["Benefit"].Value?.ToString() ?? "", points);
        if (dialog.ShowDialog(Ui.DialogOwner(this)) != DialogResult.OK) return;

        MessageBox.Show(
            $"Redemption successful.\n\nRedemption code:  {dialog.RedemptionCode}\n" +
            $"Customer:  {name}\n{kind}:  {reward.Cells["Name"].Value}  ({reward.Cells["Benefit"].Value})\n" +
            $"Points deducted:  {points:N0}\nRemaining balance:  {dialog.RemainingPoints:N0} points\n\n" +
            "Give the code to the customer.",
            "Redemption Receipt", MessageBoxButtons.OK, MessageBoxIcon.Information);
        LoadCustomers(); LoadRewards(); LoadHistory();
    }
}
