using System.Drawing;
using System.Drawing.Drawing2D;

namespace BrewHaven.Forms;

/// <summary>Clickable card for one menu item on the Place Order page: name, category, price and availability.</summary>
public sealed class MenuCard : Control
{
    private bool hover;
    public int MenuItemId { get; }
    public string ItemName { get; }
    public string Category { get; }
    public decimal Price { get; }
    /// <summary>"Unavailable", "Out of stock", "5 left", or "" (available, not stock-tracked).</summary>
    public string StatusText { get; }
    public bool CanOrder { get; }

    public MenuCard(int menuItemId, string name, string category, decimal price, string description, bool available, int? stock)
    {
        MenuItemId = menuItemId; ItemName = name; Category = category; Price = price;
        CanOrder = available && (stock is null || stock > 0);
        StatusText = !available ? "Unavailable" : stock is <= 0 ? "Out of stock" : stock is int s && s <= 10 ? $"{s} left" : "";
        Size = new Size(184, 108); Margin = new Padding(0, 0, 12, 12);
        Cursor = CanOrder ? Cursors.Hand : Cursors.No;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        var tip = new ToolTip();
        tip.SetToolTip(this, string.IsNullOrWhiteSpace(description) ? name : $"{name}\n{description}");
        Disposed += (_, _) => tip.Dispose();
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Ui.EffectiveBack(Parent));
        Color fill = !CanOrder ? Ui.Cream : hover ? Ui.AccentSoft : Ui.Ivory;
        using (var path = Ui.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10))
        {
            using var brush = new SolidBrush(fill); g.FillPath(brush, path);
            using var pen = new Pen(hover && CanOrder ? Ui.WarmSand : Ui.Border, hover && CanOrder ? 1.6f : 1f); g.DrawPath(pen, path);
        }
        Color text = CanOrder ? Ui.Espresso : Ui.Muted;
        TextRenderer.DrawText(g, ItemName, Ui.BodyBold, new Rectangle(12, 10, Width - 24, 42), text,
            TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Category.ToUpperInvariant(), Ui.SmallBold, new Point(12, 54), Ui.Muted, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, $"PHP {Price:N2}", Ui.SubHeading, new Point(10, 74), CanOrder ? Ui.Primary : Ui.Muted, TextFormatFlags.NoPrefix);
        if (StatusText.Length > 0)
        {
            Color statusColor = CanOrder ? Ui.Muted : Ui.Danger;
            var size = TextRenderer.MeasureText(StatusText, Ui.SmallBold);
            TextRenderer.DrawText(g, StatusText, Ui.SmallBold, new Point(Width - size.Width - 10, 80), statusColor, TextFormatFlags.NoPrefix);
        }
    }
}
