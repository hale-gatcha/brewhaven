using BrewHaven.DataAccess;
using System.Drawing;

namespace BrewHaven.Forms;

/// <summary>Read-only pop-up with an order's items, totals and payment details.</summary>
public sealed class OrderDetailsDialog : PopupDialog
{
    private readonly int orderId;
    private readonly DataGridView dgvItems = Ui.Grid();
    private readonly Label lblHeader = new() { AutoSize = true, MaximumSize = new Size(Full, 0), Font = Ui.Body, ForeColor = Ui.Text, Margin = new Padding(0, 0, 0, 12), UseMnemonic = false };
    private readonly Label lblPayment = new() { AutoSize = true, MaximumSize = new Size(Full, 0), Font = Ui.Body, ForeColor = Ui.Text, Margin = new Padding(0, 12, 0, 0), UseMnemonic = false };

    public OrderDetailsDialog(int orderId) : base("Order Details", $"Order #{orderId}", null, 600)
    {
        this.orderId = orderId;
        var itemsCard = new Panel { Size = new Size(Full, 230), BackColor = Ui.Surface, Margin = new Padding(0, 0, 0, 4) };
        dgvItems.Dock = DockStyle.Fill; itemsCard.Controls.Add(dgvItems);
        AddRow(lblHeader);
        AddRow(itemsCard);
        AddRow(lblPayment);
        dgvItems.DataBindingComplete += (_, _) =>
        {
            if (dgvItems.Columns.Contains("MenuItemId")) dgvItems.Columns["MenuItemId"]!.Visible = false;
            foreach (string money in new[] { "Unit Price", "Line Total" })
                if (dgvItems.Columns.Contains(money))
                {
                    dgvItems.Columns[money]!.DefaultCellStyle.Format = "N2";
                    dgvItems.Columns[money]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
            if (dgvItems.Columns.Contains("Item")) dgvItems.Columns["Item"]!.FillWeight = 200;
        };
        Load += (_, _) => LoadOrder();
    }

    private void LoadOrder()
    {
        try
        {
            OrderInfo? o = OrderRepository.Load(orderId);
            if (o is null)
            {
                MessageBox.Show("This order no longer exists.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                CancelAfterLoad();
                return;
            }
            string table = o.TableNumber is int t ? $"  ·  Table {t}" : "";
            lblHeader.Text =
                $"{o.OrderDate:MMM d, yyyy h:mm tt}  ·  {o.BranchName}  ·  {o.OrderType}{table}  ·  Status: {o.Status}\n" +
                $"Customer: {o.CustomerName}{(o.CustomerCode is null ? "" : $" ({o.CustomerCode})")}  ·  Staff: {o.StaffName}";
            dgvItems.DataSource = o.Items;

            var lines = new List<string>
            {
                $"Subtotal: PHP {o.Subtotal:N2}     Discount: PHP {o.Discount:N2}{(o.PromotionName is null ? "" : $" ({o.PromotionName})")}     Total: PHP {o.Total:N2}"
            };
            if (o.RedemptionNote is not null) lines.Add($"Points redeemed: {o.RedemptionNote}");
            if (o.Payment is { } p)
            {
                string detail = p.Method switch
                {
                    "Card" => $"{p.CardType} ****{p.CardLast4}, approval {p.ReferenceNumber}",
                    "E-Wallet" => $"{p.Provider}, ref {p.ReferenceNumber}",
                    _ => $"tendered PHP {p.AmountPaid:N2}, change PHP {p.ChangeAmount:N2}"
                };
                lines.Add($"Payment: {p.Method} ({detail}) on {p.PaidAt:MMM d, yyyy h:mm tt}{(p.ReceivedBy is null ? "" : $" by {p.ReceivedBy}")}");
                if (o.PointsEarned > 0) lines.Add($"Points earned: {o.PointsEarned}");
            }
            else lines.Add("Payment: not paid yet");
            lblPayment.Text = string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            ShowError("Could not load the order.", ex);
            CancelAfterLoad();
        }
    }
}
