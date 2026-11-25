using System.Drawing;
using System.Drawing.Drawing2D;

namespace BrewHaven.Forms;

/// <summary>Read-only 1-5 star display (filled Warm Sand stars), with the number beside it, e.g. ★★★★☆ 4 / 5.</summary>
public sealed class StarRating : Control
{
    private int? rating;

    /// <summary>1-5, or null to show "Not rated".</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int? Rating { get => rating; set { rating = value is null ? null : Math.Clamp(value.Value, 0, 5); Invalidate(); } }

    public StarRating()
    {
        Size = new Size(220, 30);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Ui.EffectiveBack(Parent));
        if (rating is not int value)
        {
            TextRenderer.DrawText(g, "Not rated", Ui.Body, new Point(0, 5), Ui.Muted);
            return;
        }
        const float size = 20f, gap = 4f;
        using var fill = new SolidBrush(Ui.WarmSand);
        using var empty = new SolidBrush(Ui.Cream);
        using var outline = new Pen(Ui.WarmSand, 1.2f);
        for (int i = 0; i < 5; i++)
        {
            using GraphicsPath star = Star(new RectangleF(i * (size + gap), (Height - size) / 2f, size, size));
            g.FillPath(i < value ? fill : empty, star);
            g.DrawPath(outline, star);
        }
        TextRenderer.DrawText(g, $"{value} / 5", Ui.BodyBold, new Point((int)(5 * (size + gap) + 6), 5), Ui.Text);
    }

    private static GraphicsPath Star(RectangleF r)
    {
        var points = new PointF[10];
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2 + 1, outer = r.Width / 2, inner = outer * 0.45f;
        for (int i = 0; i < 10; i++)
        {
            double angle = -Math.PI / 2 + i * Math.PI / 5;
            float radius = i % 2 == 0 ? outer : inner;
            points[i] = new PointF(cx + (float)(Math.Cos(angle) * radius), cy + (float)(Math.Sin(angle) * radius));
        }
        var path = new GraphicsPath();
        path.AddPolygon(points);
        return path;
    }
}
