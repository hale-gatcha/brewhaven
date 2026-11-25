using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace BrewHaven.Helpers;

/// <summary>
/// Loads Poppins (Regular, Medium, SemiBold, Bold) from the Fonts folder next to the .exe, so the app
/// looks the same on PCs that don't have Poppins installed. Falls back to Segoe UI if loading fails.
/// </summary>
public static class FontHelper
{
    public const string FallbackFamily = "Segoe UI";

    private static readonly string[] FontFiles =
        ["Poppins-Regular.ttf", "Poppins-Medium.ttf", "Poppins-SemiBold.ttf", "Poppins-Bold.ttf"];

    // Kept alive for the whole app: fonts created from a PrivateFontCollection break if it is disposed.
    private static readonly PrivateFontCollection Collection = new();
    private static readonly FontFamily? RegularFamily, MediumFamily, SemiBoldFamily;
    private static readonly Dictionary<(string Family, float Size, FontStyle Style), Font> Cache = new();

    /// <summary>True when Poppins was loaded; false when the app is using the Segoe UI fallback.</summary>
    public static bool IsPoppinsLoaded => RegularFamily is not null;

    private const uint FR_PRIVATE = 0x10;
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int AddFontResourceEx(string name, uint flags, IntPtr reserved);

    static FontHelper()
    {
        try
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "Fonts");
            foreach (string file in FontFiles)
            {
                string path = Path.Combine(folder, file);
                if (!File.Exists(path)) continue;
                Collection.AddFontFile(path);           // for GDI+ (Font objects, Graphics.DrawString)
                AddFontResourceEx(path, FR_PRIVATE, IntPtr.Zero); // for GDI (TextBox, ComboBox, TextRenderer)
            }
            foreach (FontFamily family in Collection.Families)
            {
                switch (family.Name)
                {
                    case "Poppins": RegularFamily = family; break;
                    case "Poppins Medium": MediumFamily = family; break;
                    case "Poppins SemiBold": SemiBoldFamily = family; break;
                }
            }
        }
        catch
        {
            RegularFamily = MediumFamily = SemiBoldFamily = null; // use the Segoe UI fallback
        }
    }

    public static Font Regular(float size) => Get(RegularFamily, FallbackFamily, size, FontStyle.Regular);
    public static Font Medium(float size) => Get(MediumFamily ?? RegularFamily, FallbackFamily, size, FontStyle.Regular);
    public static Font SemiBold(float size) => Get(SemiBoldFamily, "Segoe UI Semibold", size, FontStyle.Regular);
    public static Font Bold(float size) => Get(RegularFamily, FallbackFamily, size, FontStyle.Bold);
    /// <summary>Segoe UI. Used only for DateTimePicker: the native date picker draws the day digits wrong with Poppins.</summary>
    public static Font SystemUi(float size) => Get(null, FallbackFamily, size, FontStyle.Regular);

    private static Font Get(FontFamily? family, string fallback, float size, FontStyle style)
    {
        string name = family?.Name ?? fallback;
        var key = (name, size, style);
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out Font? cached)) return cached;
            Font font;
            try { font = family is not null ? new Font(family, size, style, GraphicsUnit.Point) : new Font(fallback, size, style, GraphicsUnit.Point); }
            catch (ArgumentException) { font = new Font(FallbackFamily, size, style, GraphicsUnit.Point); }
            Cache[key] = font;
            return font;
        }
    }

    /// <summary>Returns the Poppins equivalent of a font, keeping its size and weight.</summary>
    public static Font ToPoppins(Font? font)
    {
        if (font is null) return Regular(9.5f);
        if (font.Name.StartsWith("Poppins", StringComparison.OrdinalIgnoreCase)) return font;
        float size = font.SizeInPoints;
        bool semi = font.Name.Contains("Semibold", StringComparison.OrdinalIgnoreCase);
        bool bold = font.Bold;
        FontStyle extra = font.Style & (FontStyle.Italic | FontStyle.Underline | FontStyle.Strikeout);
        Font baseFont = semi ? SemiBold(size) : bold ? Bold(size) : Regular(size);
        return extra == FontStyle.Regular ? baseFont : Get(baseFont.FontFamily, FallbackFamily, size, baseFont.Style | extra);
    }

    /// <summary>
    /// Recursively switches every control under <paramref name="root"/> (panels, tabs, grids, tool strips)
    /// to Poppins. Call it from each form's and dialog's Load event.
    /// </summary>
    public static void ApplyToAll(Control root)
    {
        root.SuspendLayout();
        try { Apply(root); }
        finally { root.ResumeLayout(true); }
    }

    private static void Apply(Control control)
    {
        if (control is DateTimePicker)
        {
            // Exception: the Windows date picker garbles the day digits (e.g. "Nov ?4") with private fonts.
            control.Font = SystemUi(Math.Max(9.75f, control.Font.SizeInPoints));
            return;
        }
        control.Font = ToPoppins(control.Font);

        switch (control)
        {
            case DataGridView grid:
                grid.Font = ToPoppins(grid.Font);
                grid.DefaultCellStyle.Font = ToPoppins(grid.DefaultCellStyle.Font ?? grid.Font);
                grid.ColumnHeadersDefaultCellStyle.Font = ToPoppins(grid.ColumnHeadersDefaultCellStyle.Font ?? SemiBold(grid.Font.SizeInPoints));
                grid.AlternatingRowsDefaultCellStyle.Font = ToPoppins(grid.AlternatingRowsDefaultCellStyle.Font ?? grid.DefaultCellStyle.Font);
                grid.RowsDefaultCellStyle.Font = ToPoppins(grid.RowsDefaultCellStyle.Font ?? grid.DefaultCellStyle.Font);
                foreach (DataGridViewColumn column in grid.Columns)
                    if (column.DefaultCellStyle.Font is not null) column.DefaultCellStyle.Font = ToPoppins(column.DefaultCellStyle.Font);
                // Every form calls ApplyToAll in its Load event, so this is where each grid's style is (re)applied.
                Forms.GridStyler.Apply(grid);
                break;
            case ToolStrip strip:
                foreach (ToolStripItem item in strip.Items) ApplyToItem(item);
                break;
        }

        if (control.ContextMenuStrip is not null) Apply(control.ContextMenuStrip);
        foreach (Control child in control.Controls) Apply(child);
    }

    private static void ApplyToItem(ToolStripItem item)
    {
        item.Font = ToPoppins(item.Font);
        if (item is ToolStripDropDownItem drop)
            foreach (ToolStripItem child in drop.DropDownItems) ApplyToItem(child);
    }
}
