using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using System.Drawing;
using System.Drawing.Printing;

namespace BrewHaven.Forms;

/// <summary>Prints (with a preview) an 80 mm-style receipt for a paid order: branch, items, totals, payment, change, points.</summary>
public static class ReceiptPrinter
{
    private sealed record Line(string Left, string Right, Font Font, bool Center = false, bool Rule = false);

    public static void ShowReceipt(IWin32Window owner, int orderId)
    {
        OrderInfo? order;
        try { order = OrderRepository.Load(orderId); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load the receipt.\n{ex.Message}", "Receipt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (order is null) return;

        List<Line> lines = BuildLines(order);
        int heightHundredths = 60 + lines.Sum(l => l.Rule ? 10 : (int)Math.Ceiling(l.Font.GetHeight(100f) * 1.15f));

        using var document = new PrintDocument { DocumentName = $"Receipt - Order #{order.OrderId}" };
        // 80 mm thermal roll = 3.15 in wide; height grows with the number of lines.
        document.DefaultPageSettings.PaperSize = new PaperSize("Receipt 80mm", 315, Math.Max(400, heightHundredths));
        document.DefaultPageSettings.Margins = new Margins(14, 14, 20, 20);
        document.PrintPage += (_, e) =>
        {
            if (e.Graphics is null) return;
            Graphics g = e.Graphics;
            RectangleF area = e.MarginBounds;
            float y = area.Top;
            using var brush = new SolidBrush(Color.Black);
            using var pen = new Pen(Color.Black) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            var left = new StringFormat { Alignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            var right = new StringFormat { Alignment = StringAlignment.Far };
            var center = new StringFormat { Alignment = StringAlignment.Center };
            foreach (Line line in lines)
            {
                if (line.Rule) { g.DrawLine(pen, area.Left, y + 4, area.Right, y + 4); y += 10; continue; }
                float h = line.Font.GetHeight(g) * 1.15f;
                if (line.Center) g.DrawString(line.Left, line.Font, brush, new RectangleF(area.Left, y, area.Width, h), center);
                else
                {
                    float rightWidth = string.IsNullOrEmpty(line.Right) ? 0 : g.MeasureString(line.Right, line.Font).Width + 4;
                    g.DrawString(line.Left, line.Font, brush, new RectangleF(area.Left, y, area.Width - rightWidth, h), left);
                    if (rightWidth > 0) g.DrawString(line.Right, line.Font, brush, new RectangleF(area.Left, y, area.Width, h), right);
                }
                y += h;
            }
            e.HasMorePages = false;
        };
        using var preview = new PrintPreviewDialog { Document = document, Width = 480, Height = 760, StartPosition = FormStartPosition.CenterParent, Text = "Receipt" };
        ((Form)preview).ShowInTaskbar = false;
        try { preview.ShowDialog(owner); }
        catch (Exception ex) { MessageBox.Show($"Could not show the receipt.\n{ex.Message}", "Receipt", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static List<Line> BuildLines(OrderInfo o)
    {
        Font title = FontHelper.Bold(12f), body = FontHelper.Regular(8f), bold = FontHelper.SemiBold(8.5f), small = FontHelper.Regular(7f), total = FontHelper.Bold(10f);
        var lines = new List<Line>
        {
            new("BREW HAVEN", "", title, Center: true),
            new("Coffee Shop", "", small, Center: true),
            new(o.BranchName, "", bold, Center: true)
        };
        if (!string.IsNullOrWhiteSpace(o.BranchAddress)) lines.Add(new(o.BranchAddress, "", small, Center: true));
        if (!string.IsNullOrWhiteSpace(o.BranchContact)) lines.Add(new($"Tel. {o.BranchContact}", "", small, Center: true));
        lines.Add(new("", "", body, Rule: true));
        lines.Add(new($"Order #{o.OrderId}", o.OrderType + (o.TableNumber is int t ? $" · Table {t}" : ""), bold));
        lines.Add(new((o.Payment?.PaidAt ?? o.OrderDate).ToString("MMM d, yyyy h:mm tt"), "", body));
        lines.Add(new($"Customer: {o.CustomerName}{(o.CustomerCode is null ? "" : $" ({o.CustomerCode})")}", "", body));
        lines.Add(new($"Cashier: {o.Payment?.ReceivedBy ?? o.StaffName}", "", body));
        lines.Add(new("", "", body, Rule: true));
        foreach (System.Data.DataRow r in o.Items.Rows)
        {
            string size = r["Size"] as string is { } s && s != "-" ? $" ({s})" : "";
            lines.Add(new($"{r["Qty"]} x {r["Item"]}{size}", $"{Convert.ToDecimal(r["Line Total"]):N2}", body));
            lines.Add(new($"     @ {Convert.ToDecimal(r["Unit Price"]):N2}", "", small));
        }
        lines.Add(new("", "", body, Rule: true));
        lines.Add(new("Subtotal", $"{o.Subtotal:N2}", body));
        if (o.Discount > 0)
            lines.Add(new($"Discount{(o.PromotionName is null ? "" : $" ({o.PromotionName})")}", $"-{o.Discount:N2}", body));
        if (o.RedemptionNote is not null) lines.Add(new($"Points redeemed: {o.RedemptionNote}", "", small));
        lines.Add(new("TOTAL (PHP)", $"{o.Total:N2}", total));
        if (o.Payment is { } p)
        {
            lines.Add(new("", "", body, Rule: true));
            switch (p.Method)
            {
                case "Card":
                    lines.Add(new($"Card ({p.CardType} ****{p.CardLast4})", $"{p.AmountPaid:N2}", body));
                    lines.Add(new($"Approval: {p.ReferenceNumber}", "", small));
                    break;
                case "E-Wallet":
                    lines.Add(new($"{p.Provider}", $"{p.AmountPaid:N2}", body));
                    lines.Add(new($"Ref: {p.ReferenceNumber}", "", small));
                    break;
                default:
                    lines.Add(new("Cash tendered", $"{p.AmountPaid:N2}", body));
                    lines.Add(new("Change", $"{p.ChangeAmount:N2}", bold));
                    break;
            }
        }
        if (o.CustomerId is not null)
        {
            lines.Add(new("", "", body, Rule: true));
            lines.Add(new("Points earned", $"{o.PointsEarned:N0}", body));
            lines.Add(new("Points balance", $"{o.CustomerPoints:N0}", body));
        }
        lines.Add(new("", "", body, Rule: true));
        lines.Add(new("Thank you! Come again.", "", bold, Center: true));
        lines.Add(new("Please keep this receipt for your records.", "", small, Center: true));
        return lines;
    }
}
