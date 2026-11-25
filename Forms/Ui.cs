using System.Drawing;
using System.Drawing.Drawing2D;
using BrewHaven.Helpers;

namespace BrewHaven.Forms;

/// <summary>Shared theme: palette, fonts and reusable styled controls.</summary>
public static class Ui
{
    // ---- Brand palette -------------------------------------------------
    public static readonly Color Espresso      = ColorTranslator.FromHtml("#381C08"); // sidebar; text on light backgrounds
    public static readonly Color DarkChocolate = ColorTranslator.FromHtml("#46220A"); // header; grid header
    public static readonly Color CoffeeBrown   = ColorTranslator.FromHtml("#53290B"); // primary buttons
    public static readonly Color CocoaBrown    = ColorTranslator.FromHtml("#885734"); // primary button hover
    public static readonly Color WarmSand      = ColorTranslator.FromHtml("#AE7D5A"); // accent
    public static readonly Color Cream         = ColorTranslator.FromHtml("#E8DAD0"); // page / dialog background
    public static readonly Color Ivory         = ColorTranslator.FromHtml("#F3ECE7"); // cards, inputs, grid rows

    // ---- Semantic colours (what the rest of the UI uses) ---------------
    public static readonly Color Background   = Cream;
    public static readonly Color Surface      = Ivory;
    public static readonly Color SurfaceAlt   = Cream;   // alternating grid rows (Ivory / Cream)
    public static readonly Color Border       = ColorTranslator.FromHtml("#D6C1B1");
    public static readonly Color Sidebar      = Espresso;
    public static readonly Color SidebarHover = DarkChocolate;
    public static readonly Color SidebarActive= CoffeeBrown;
    public static readonly Color Header       = DarkChocolate;
    public static readonly Color GridHeader   = DarkChocolate;
    public static readonly Color Primary      = CoffeeBrown;
    public static readonly Color PrimaryHover = CocoaBrown;
    public static readonly Color PrimaryPress = DarkChocolate;
    public static readonly Color Accent       = WarmSand;
    public static readonly Color AccentSoft   = ColorTranslator.FromHtml("#E2CCBB");
    public static readonly Color Text         = Espresso;
    public static readonly Color Muted        = ColorTranslator.FromHtml("#77594A");
    public static readonly Color OnDarkMuted  = ColorTranslator.FromHtml("#D2BCAB");
    public static readonly Color Danger       = ColorTranslator.FromHtml("#A63A24");
    public static readonly Color DangerHover  = ColorTranslator.FromHtml("#C9583F");
    public static readonly Color Selection    = ColorTranslator.FromHtml("#DCC3AF");

    // Status tints for grid rows
    public static readonly Color TintWarn    = ColorTranslator.FromHtml("#FBEBD8");
    public static readonly Color TintInfo    = ColorTranslator.FromHtml("#E9F0F8");
    public static readonly Color TintSuccess = ColorTranslator.FromHtml("#E6F2E8");
    public static readonly Color TintDanger  = ColorTranslator.FromHtml("#FBE1DA");

    // Legacy name kept so older code still compiles
    public static Color CocoaHover => CocoaBrown;

    // ---- Fonts: Poppins via FontHelper (Segoe UI if it can't be loaded) --
    public static readonly Font Body       = FontHelper.Regular(9.5f);
    public static readonly Font BodyMedium = FontHelper.Medium(9.5f);
    public static readonly Font BodyBold   = FontHelper.SemiBold(9.5f);
    public static readonly Font Small      = FontHelper.Regular(8.5f);
    public static readonly Font SmallBold  = FontHelper.SemiBold(8.25f);
    public static readonly Font Heading    = FontHelper.SemiBold(17f);
    public static readonly Font SubHeading = FontHelper.SemiBold(11.5f);
    public static readonly Font Display    = FontHelper.Bold(24f);

    // ---- Buttons -------------------------------------------------------
    public static RoundedButton PrimaryButton(string text) => new(text)
    { BaseColor = Primary, HoverColor = PrimaryHover, PressColor = PrimaryPress, TextColor = Color.White };

    public static RoundedButton SecondaryButton(string text) => new(text)
    { BaseColor = Surface, HoverColor = AccentSoft, PressColor = Selection, TextColor = Primary, BorderColor = Border };

    public static RoundedButton DangerButton(string text) => new(text)
    { BaseColor = Surface, HoverColor = TintDanger, PressColor = TintDanger, TextColor = Danger, BorderColor = ColorTranslator.FromHtml("#EBC3B8") };

    public static RoundedButton GhostButton(string text) => new(text)
    { BaseColor = Color.Transparent, HoverColor = AccentSoft, PressColor = Selection, TextColor = Muted };

    // ---- Layout helpers ------------------------------------------------
    /// <summary>White rounded card with a hairline border.</summary>
    public static CardPanel Card(DockStyle dock = DockStyle.None, int padding = 16) =>
        new() { Dock = dock, Padding = new Padding(padding) };

    /// <summary>Vertical gap used between docked cards.</summary>
    public static Panel Spacer(DockStyle dock = DockStyle.Top, int size = 16) =>
        new() { Dock = dock, Height = size, Width = size, BackColor = Color.Transparent };

    public static Label SectionTitle(string text) => new()
    { Text = text, Dock = DockStyle.Top, Height = 30, Font = SubHeading, ForeColor = Text, TextAlign = ContentAlignment.TopLeft, UseMnemonic = false };

    public static Label FieldLabel(string text) => new()
    { Text = text, AutoSize = true, Font = SmallBold, ForeColor = Muted, Margin = new Padding(0, 0, 0, 4), UseMnemonic = false };

    /// <summary>Label stacked above an input. Inputs get a soft bordered host.</summary>
    public static Control Field(string label, Control input, int width = 240, int multilineHeight = 70)
    {
        var block = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 18, 14), BackColor = Color.Transparent,
            Width = width // TableLayoutPanel defaults to 200px and gives slack to the last column
        };
        block.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        block.RowStyles.Add(new RowStyle(SizeType.AutoSize)); block.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        block.Controls.Add(FieldLabel(label), 0, 0);
        Control body = Boxed(input, multilineHeight);
        body.Width = width; body.Margin = Padding.Empty; body.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        block.Controls.Add(body, 0, 1);
        return block;
    }

    /// <summary>
    /// Returns the control to place in a layout: TextBox, ComboBox and NumericUpDown are wrapped
    /// in a rounded bordered host (width taken from the input); other controls are styled in place.
    /// </summary>
    public static Control Boxed(Control input, int multilineHeight = 70)
    {
        StyleInput(input);
        int w = input.Width;
        InputPanel host;
        switch (input)
        {
            case TextBox tb:
                tb.BorderStyle = BorderStyle.None;
                host = new InputPanel { Padding = new Padding(10, 7, 10, 5) };
                host.Height = tb.Multiline ? multilineHeight : tb.PreferredHeight + 12;
                break;
            case ComboBox cb:
            {
                // The flat ComboBox paints its own square border; nest it in a clipping panel
                // offset by 1px so only the rounded host border shows.
                host = new InputPanel { Padding = new Padding(8, 4, 4, 4), BackColor = Surface, Width = w + 12 };
                var clip = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
                host.Height = cb.Height - 2 + host.Padding.Vertical;
                cb.Dock = DockStyle.None; cb.Location = new Point(-1, -1);
                clip.Resize += (_, _) => cb.Width = clip.Width + 2;
                clip.Controls.Add(cb); host.Controls.Add(clip);
                cb.GotFocus += (_, _) => { host.Focused_ = true; host.Invalidate(); };
                cb.LostFocus += (_, _) => { host.Focused_ = false; host.Invalidate(); };
                host.Click += (_, _) => cb.Focus(); clip.Click += (_, _) => cb.Focus();
                return host;
            }
            case NumericUpDown n:
                n.BorderStyle = BorderStyle.None;
                host = new InputPanel { Padding = new Padding(10, 7, 4, 5) };
                host.Height = n.PreferredHeight + 12;
                break;
            default:
                return input;
        }
        host.BackColor = Surface; host.Width = w + host.Padding.Horizontal;
        input.Dock = DockStyle.Fill;
        host.Controls.Add(input);
        if (input is TextBox { ReadOnly: true })
        {
            // Read-only: arrow pointer and no "editing" focus border, so it doesn't look typeable.
            host.Cursor = Cursors.Default;
            return host;
        }
        input.GotFocus += (_, _) => { host.Focused_ = true; host.Invalidate(); };
        input.LostFocus += (_, _) => { host.Focused_ = false; host.Invalidate(); };
        host.Click += (_, _) => input.Focus();
        return host;
    }

    /// <summary>Applies theme fonts/colours to an input control.</summary>
    public static Control StyleInput(Control c)
    {
        c.Font = Body; c.ForeColor = Text;
        switch (c)
        {
            case TextBox tb: tb.BackColor = Surface; break;
            case ComboBox cb:
                if (cb.DrawMode != DrawMode.OwnerDrawFixed)
                {
                    cb.FlatStyle = FlatStyle.Flat; cb.BackColor = Surface; cb.DrawMode = DrawMode.OwnerDrawFixed; cb.ItemHeight = 24;
                    cb.DrawItem += DrawComboItem;
                }
                break;
            case NumericUpDown n: n.BackColor = Surface; break;
            case DateTimePicker d: d.Format = DateTimePickerFormat.Custom; d.CustomFormat = "MMM d, yyyy"; d.Font = FontHelper.SystemUi(9.75f); break;
        }
        return c;
    }

    private static void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
        bool selected = !edit && (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(selected ? Selection : Surface)) e.Graphics.FillRectangle(b, e.Bounds);
        if (e.Index >= 0)
        {
            var r = new Rectangle(e.Bounds.X + (edit ? 2 : 8), e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), cb.Font, r, Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Horizontal row of controls (toolbar / filter bar).</summary>
    public static FlowLayoutPanel Toolbar(DockStyle dock = DockStyle.Top) => new()
    {
        Dock = dock, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
        BackColor = Color.Transparent, Padding = Padding.Empty, Margin = Padding.Empty
    };

    /// <summary>Small label used inline in a toolbar, vertically centred against buttons.</summary>
    public static Label InlineLabel(string text) => new()
    { Text = text, AutoSize = true, Font = SmallBold, ForeColor = Muted, Margin = new Padding(0, 12, 8, 0), UseMnemonic = false };

    // ---- Grid ----------------------------------------------------------
    public static DataGridView Grid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing, ColumnHeadersHeight = 42,
            DefaultCellStyle = new DataGridViewCellStyle { Font = Body, Padding = new Padding(8, 0, 8, 0) },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                Font = SmallBold, Alignment = DataGridViewContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 8, 0)
            }
        };
        g.RowTemplate.Height = 38;
        GridStyler.Apply(g);   // colours, selection, header, behaviour: all in one place
        // Double-buffer to avoid flicker while scrolling.
        typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(g, true);
        return g;
    }

    // ---- Row action buttons (Edit / Remove) -----------------------------
    public const string EditColumn = "colEdit";
    public const string RemoveColumn = "colRemove";

    public enum RowButtonStyle { Primary, Secondary, Danger }

    /// <summary>A pill button drawn in its own grid column (see <see cref="AddRowButtons"/>).</summary>
    public sealed record RowButton(string Name, string Text, RowButtonStyle Style, int Width = 88);

    /// <summary>
    /// Adds Edit and (optionally) Remove button columns that stay at the right end of the grid, drawn as small pills.
    /// Handle clicks in the grid's CellClick event by checking the column name against EditColumn / RemoveColumn.
    /// </summary>
    public static void AddRowActions(DataGridView grid, bool includeRemove = true)
    {
        var buttons = new List<RowButton> { new(EditColumn, "Edit", RowButtonStyle.Primary, 80) };
        if (includeRemove) buttons.Add(new(RemoveColumn, "Remove", RowButtonStyle.Danger, 96));
        AddRowButtons(grid, [.. buttons]);
    }

    /// <summary>
    /// Adds pill-button columns (in the given order) that stay at the right end of the grid.
    /// Handle clicks in CellClick by checking grid.Columns[e.ColumnIndex].Name; use <see cref="IsRowButton"/> to skip them elsewhere.
    /// </summary>
    public static void AddRowButtons(DataGridView grid, params RowButton[] buttons)
    {
        var columns = buttons.Select(b => new DataGridViewButtonColumn
        {
            Name = b.Name, HeaderText = "", Text = b.Text, UseColumnTextForButtonValue = true, FlatStyle = FlatStyle.Flat,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None, Width = b.Width, Resizable = DataGridViewTriState.False, Tag = b
        }).ToList();
        foreach (var column in columns) grid.Columns.Add(column);
        // Re-binding adds the data columns after these, so move the buttons back to the end every time.
        grid.DataBindingComplete += (_, _) =>
        {
            for (int i = 0; i < columns.Count; i++) columns[i].DisplayIndex = grid.Columns.Count - columns.Count + i;
        };
        grid.CellPainting -= PaintRowAction; grid.CellPainting += PaintRowAction;
        grid.CellMouseMove += (_, e) =>
            grid.Cursor = e.RowIndex >= 0 && IsRowButton(grid, e.ColumnIndex) ? Cursors.Hand : Cursors.Default;
        grid.MouseLeave += (_, _) => grid.Cursor = Cursors.Default;
    }

    /// <summary>True when the column is one of the pill-button columns added by AddRowButtons / AddRowActions.</summary>
    public static bool IsRowButton(DataGridView grid, int columnIndex) =>
        columnIndex >= 0 && columnIndex < grid.Columns.Count && grid.Columns[columnIndex].Tag is RowButton;

    private static void PaintRowAction(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is null) return;
        if (grid.Columns[e.ColumnIndex].Tag is not RowButton button) return;

        // Button cells use the same light-brown highlight as the rest of the selected row; the pills themselves never change.
        bool rowSelected = (e.State & DataGridViewElementStates.Selected) != 0;
        using (var back = new SolidBrush(rowSelected ? GridStyler.SelectedBack : GridStyler.RowBack)) e.Graphics.FillRectangle(back, e.CellBounds);
        e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
        var r = new RectangleF(e.CellBounds.X + 6, e.CellBounds.Y + 6, e.CellBounds.Width - 12, e.CellBounds.Height - 12);
        var (fill, border, text) = button.Style switch
        {
            RowButtonStyle.Primary => (Primary, Color.Empty, Color.White),
            RowButtonStyle.Danger => (Surface, Danger, Danger),
            _ => (Surface, Primary, Primary)
        };
        var g = e.Graphics; var oldMode = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = RoundRect(r, 6))
        {
            using var brush = new SolidBrush(fill); g.FillPath(brush, path);
            if (!border.IsEmpty) { using var pen = new Pen(border); g.DrawPath(pen, path); }
        }
        g.SmoothingMode = oldMode;
        TextRenderer.DrawText(g, button.Text, SmallBold, Rectangle.Round(r), text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        e.Handled = true;
    }

    /// <summary>Dialogs opened from a page hosted inside the dashboard are owned by the dashboard window.</summary>
    public static IWin32Window DialogOwner(Control page) => page.TopLevelControl ?? (IWin32Window)page;

    /// <summary>Small rounded "badge" label, used for summary figures above grids.</summary>
    public static Label Badge(string text) => new()
    {
        Text = text, AutoSize = true, Font = BodyBold, ForeColor = Primary, BackColor = AccentSoft,
        Padding = new Padding(12, 6, 12, 6), Margin = new Padding(0, 0, 8, 8), UseMnemonic = false
    };

    /// <summary>A page toolbar card (Search, filters, buttons) that grows to fit its wrapped contents.</summary>
    public static (CardPanel Card, FlowLayoutPanel Bar) ToolbarCard()
    {
        var card = Card(DockStyle.Top, 12); card.Height = 64;
        var bar = Toolbar(DockStyle.Top);
        bar.SizeChanged += (_, _) => card.Height = bar.Height + card.Padding.Vertical;
        card.Controls.Add(bar);
        return (card, bar);
    }

    /// <summary>Wraps an input for use inside a toolbar (rounded border, toolbar spacing).</summary>
    public static Control ToolbarBox(Control input)
    {
        var box = Boxed(input); box.Margin = new Padding(0, 3, 14, 0);
        return box;
    }

    /// <summary>Builds a DataView.RowFilter that matches <paramref name="term"/> anywhere in any of the columns.</summary>
    public static string ContainsFilter(string term, params string[] columns)
    {
        term = term.Trim();
        if (term.Length == 0) return "";
        var escaped = new System.Text.StringBuilder();
        foreach (char ch in term)
        {
            if (ch is '*' or '%' or '[' or ']') escaped.Append('[').Append(ch).Append(']');
            else if (ch == '\'') escaped.Append("''");
            else escaped.Append(ch);
        }
        return string.Join(" OR ", columns.Select(c => $"CONVERT([{c}], 'System.String') LIKE '%{escaped}%'"));
    }

    /// <summary>Grid wrapped in a card so it sits on the canvas like everything else.</summary>
    public static CardPanel GridCard(DataGridView grid)
    {
        var card = Card(DockStyle.Fill, 12);
        grid.Dock = DockStyle.Fill; card.Controls.Add(grid);
        return card;
    }

    // ---- Drawing -------------------------------------------------------
    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Background colour of the nearest ancestor that actually paints one.</summary>
    internal static Color EffectiveBack(Control? c)
    {
        while (c != null) { if (c.BackColor.A == 255) return c.BackColor; c = c.Parent; }
        return Background;
    }
}

/// <summary>
/// Display-only text box: can't be typed in, shows the normal arrow pointer and never shows the blinking text cursor.
/// Long text can still be scrolled (multiline) and selected / copied.
/// </summary>
public sealed class ReadOnlyTextBox : TextBox
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool HideCaret(IntPtr hWnd);

    private const int WM_SETFOCUS = 0x0007, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101,
                      WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203;

    public ReadOnlyTextBox()
    {
        ReadOnly = true;
        Cursor = Cursors.Default;
        BackColor = Ui.Ivory; ForeColor = Ui.Espresso;
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // Windows re-shows the caret when the box gets focus or is clicked / navigated; hide it right after.
        if (m.Msg is WM_SETFOCUS or WM_KEYDOWN or WM_KEYUP or WM_MOUSEMOVE or WM_LBUTTONDOWN or WM_LBUTTONUP or WM_LBUTTONDBLCLK
            && IsHandleCreated && Focused)
            HideCaret(Handle);
    }
}

/// <summary>Flat button with rounded corners and hover/press states.</summary>
public class RoundedButton : Button
{
    private bool hover, pressed;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color BaseColor { get; set; } = Ui.Primary;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color HoverColor { get; set; } = Ui.PrimaryHover;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color PressColor { get; set; } = Ui.PrimaryPress;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color TextColor { get; set; } = Color.White;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = Color.Empty;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 8;

    public RoundedButton(string text)
    {
        Text = text; Font = Ui.BodyBold; Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Height = 38; Margin = new Padding(0, 0, 8, 0); Padding = new Padding(14, 0, 14, 0);
        Width = Math.Max(96, TextRenderer.MeasureText(text, Font).Width + 36);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Ui.EffectiveBack(Parent));
        var fill = pressed ? PressColor : hover ? HoverColor : BaseColor;
        if (!Enabled) fill = ControlPaint.Light(fill.A == 0 ? Ui.Background : fill);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Ui.RoundRect(rect, Radius);
        if (fill.A > 0) { using var b = new SolidBrush(fill); g.FillPath(b, path); }
        if (!BorderColor.IsEmpty) { using var p = new Pen(BorderColor); g.DrawPath(p, path); }
        if (Focused && ShowFocusCues) { using var p = new Pen(Ui.Accent, 2f); g.DrawPath(p, path); }
        DrawContent(g);
    }

    /// <summary>Draws the label. Overridden by subclasses for custom content (never mutate Text while painting).</summary>
    protected virtual void DrawContent(Graphics g) =>
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? TextColor : Ui.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
}

/// <summary>Sidebar navigation item with active indicator.</summary>
public sealed class NavButton : RoundedButton
{
    private bool active;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Active { get => active; set { active = value; BaseColor = value ? Ui.SidebarActive : Ui.Sidebar; Invalidate(); } }

    public NavButton(string text) : base(text)
    {
        BaseColor = Ui.Sidebar; HoverColor = Ui.SidebarHover; PressColor = Ui.SidebarActive; TextColor = Color.White;
        Font = Ui.BodyMedium; Height = 38; Radius = 8; Margin = new Padding(0, 1, 0, 1);
    }

    protected override void DrawContent(Graphics g)
    {
        if (active) { using var b = new SolidBrush(Ui.Accent); using var p = Ui.RoundRect(new RectangleF(0, 9, 4, Height - 18), 2); g.FillPath(b, p); }
        var r = new Rectangle(18, 0, Width - 24, Height);
        TextRenderer.DrawText(g, Text, active ? Ui.BodyBold : Font, r, active ? Color.White : ColorTranslator.FromHtml("#E4D6CC"),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>White surface with rounded corners and a hairline border.</summary>
public class CardPanel : Panel
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 12;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; } = Ui.Surface;

    public CardPanel()
    {
        BackColor = Ui.Surface;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Ui.EffectiveBack(Parent));
        using var path = Ui.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Radius);
        using var b = new SolidBrush(Fill); g.FillPath(b, path);
        using var p = new Pen(Ui.Border); g.DrawPath(p, path);
    }
}

/// <summary>Border host for TextBoxes; turns caramel when focused.</summary>
public sealed class InputPanel : Panel
{
    internal bool Focused_;
    public InputPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.IBeam;
    }
    // Report the assigned size so auto-sizing layouts don't grow it to the child's preferred width.
    public override Size GetPreferredSize(Size proposedSize) => Size;
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Ui.EffectiveBack(Parent));
        using var path = Ui.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 7);
        using var b = new SolidBrush(BackColor); g.FillPath(b, path);
        using var p = new Pen(Focused_ ? Ui.Accent : Ui.Border, Focused_ ? 1.6f : 1f); g.DrawPath(p, path);
    }
}
