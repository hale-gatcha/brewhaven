using BrewHaven.Helpers;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace BrewHaven.Forms;

/// <summary>
/// The one place grid styling lives. Every DataGridView gets:
/// plain Ivory rows with Espresso text (no alternating colours, no status colours, no hover),
/// ONE highlight for the clicked row (light brown Sand Beige #D1B5A1 with Espresso text), no dotted focus box,
/// a Dark Chocolate header that never changes colour, Cream row lines, Poppins fonts,
/// and nothing selected right after data loads.
/// Applied automatically by Ui.Grid() and by FontHelper.ApplyToAll() (run in every form's Load). Safe to call repeatedly.
/// </summary>
public static class GridStyler
{
    public static readonly Color RowBack = Ui.Ivory;                                    // #F3ECE7
    public static readonly Color RowText = Ui.Espresso;                                 // #381C08
    public static readonly Color SelectedBack = ColorTranslator.FromHtml("#D1B5A1");    // Sand Beige: the ONLY row highlight
    public static readonly Color SelectedText = Ui.Espresso;                            // #381C08 dark text on the light brown
    public static readonly Color LineColor = Ui.Cream;                                  // #E8DAD0 row lines (don't read as a highlight)
    public static readonly Color HeaderBack = Ui.DarkChocolate;                         // #46220A header, never changes
    public static readonly Color HeaderText = Color.White;

    // Grids whose events are already hooked (so calling Apply again doesn't hook them twice).
    private static readonly ConditionalWeakTable<DataGridView, object> Hooked = new();

    public static void Apply(DataGridView dgv)
    {
        // Behaviour
        dgv.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgv.MultiSelect = false;
        dgv.ReadOnly = true;
        dgv.AllowUserToAddRows = false;
        dgv.AllowUserToDeleteRows = false;
        dgv.AllowUserToResizeRows = false;
        dgv.RowHeadersVisible = false;
        dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        // Look
        dgv.EnableHeadersVisualStyles = false;
        dgv.BorderStyle = BorderStyle.None;
        dgv.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        dgv.GridColor = LineColor;
        dgv.BackgroundColor = RowBack;
        dgv.StandardTab = true;

        // Rows: one plain style for every row, the same single highlight everywhere.
        Font body = FontHelper.ToPoppins(dgv.DefaultCellStyle.Font ?? Ui.Body);
        StyleRows(dgv.DefaultCellStyle, body);
        StyleRows(dgv.RowsDefaultCellStyle, body);
        StyleRows(dgv.AlternatingRowsDefaultCellStyle, body);   // identical to the default: no alternating colour
        dgv.RowTemplate.DefaultCellStyle.SelectionBackColor = SelectedBack;
        dgv.RowTemplate.DefaultCellStyle.SelectionForeColor = SelectedText;

        // Header: Dark Chocolate, white Poppins SemiBold, never highlighted.
        dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        var header = dgv.ColumnHeadersDefaultCellStyle;
        header.BackColor = HeaderBack; header.ForeColor = HeaderText;
        header.SelectionBackColor = HeaderBack; header.SelectionForeColor = HeaderText;
        header.Font = FontHelper.SemiBold(Math.Max(8.25f, header.Font?.SizeInPoints ?? 8.25f));

        // Columns and rows that already exist: drop any colour of their own.
        foreach (DataGridViewColumn column in dgv.Columns) ClearColors(column.DefaultCellStyle);
        foreach (DataGridViewRow row in dgv.Rows) ClearColors(row.DefaultCellStyle);

        if (!Hooked.TryGetValue(dgv, out _))
        {
            Hooked.Add(dgv, new object());
            dgv.CellPainting += PaintWithoutFocusBox;
            dgv.DataBindingComplete += (_, _) => ClearAfterLoad(dgv);
            // A grid filled before it's on screen re-selects its first row when it first appears, so clear again then.
            dgv.VisibleChanged += (_, _) =>
            {
                if (dgv.Visible && dgv.IsHandleCreated) dgv.BeginInvoke(() => ClearAfterLoad(dgv));
            };
            dgv.HandleCreated += (_, _) => dgv.BeginInvoke(() => ClearAfterLoad(dgv));
            dgv.ColumnAdded += (_, e) => ClearColors(e.Column.DefaultCellStyle);
        }
    }

    /// <summary>Applies <see cref="Apply"/> to every DataGridView under a form or panel.</summary>
    public static void ApplyToAll(Control root)
    {
        if (root is DataGridView dgv) Apply(dgv);
        foreach (Control child in root.Controls) ApplyToAll(child);
    }

    private static void StyleRows(DataGridViewCellStyle style, Font font)
    {
        style.BackColor = RowBack; style.ForeColor = RowText;
        style.SelectionBackColor = SelectedBack; style.SelectionForeColor = SelectedText;
        style.Font = font;
    }

    /// <summary>Column / row styles inherit the grid's colours instead of setting their own (formats and alignment are kept).</summary>
    private static void ClearColors(DataGridViewCellStyle style)
    {
        style.BackColor = Color.Empty; style.ForeColor = Color.Empty;
        style.SelectionBackColor = Color.Empty; style.SelectionForeColor = Color.Empty;
    }

    /// <summary>No row is highlighted until the user clicks one.</summary>
    private static void ClearAfterLoad(DataGridView dgv)
    {
        try { dgv.CurrentCell = null; } catch (InvalidOperationException) { /* grid busy (e.g. mid-edit): leave current cell */ }
        dgv.ClearSelection();
    }

    /// <summary>Paints cells normally but without the dotted focus rectangle on the current cell.</summary>
    private static void PaintWithoutFocusBox(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView dgv || e.Handled || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (dgv.Columns[e.ColumnIndex].Tag is Ui.RowButton) return;   // Edit / Remove pills are drawn by Ui
        e.Paint(e.ClipBounds, e.PaintParts & ~DataGridViewPaintParts.Focus);
        e.Handled = true;
    }
}
