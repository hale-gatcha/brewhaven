using BrewHaven.Helpers;
using System.Drawing;
using System.Drawing.Printing;
using System.Text;

namespace BrewHaven.Forms;

/// <summary>
/// Export to CSV / Print (with preview) for a page made of several grids: a title, a filter line, summary figures,
/// then one section per grid. Button columns, hidden columns and columns whose Tag is <see cref="SkipTag"/> are left out.
/// </summary>
public sealed class GridReport
{
    /// <summary>Set a column's Tag to this to leave it out of exports (e.g. painted bar columns).</summary>
    public const string SkipTag = "skip-export";

    public string Title { get; init; } = "";
    public string Filters { get; init; } = "";
    public IReadOnlyList<string> SummaryLines { get; init; } = [];
    public IReadOnlyList<(string Title, DataGridView Grid)> Sections { get; init; } = [];

    private static List<DataGridViewColumn> ExportColumns(DataGridView grid) =>
        grid.Columns.Cast<DataGridViewColumn>()
            .Where(c => c.Visible && c is not DataGridViewButtonColumn && !Equals(c.Tag, SkipTag))
            .OrderBy(c => c.DisplayIndex).ToList();

    // ---- CSV -----------------------------------------------------------------------------------------

    public void ExportCsv(IWin32Window owner, string fileName)
    {
        using var dialog = new SaveFileDialog { Filter = "CSV file (*.csv)|*.csv", FileName = fileName };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;
        try
        {
            static string Csv(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
            var csv = new StringBuilder();
            csv.AppendLine(Csv(Title));
            csv.AppendLine(Csv(Filters));
            csv.AppendLine();
            foreach (string line in SummaryLines) csv.AppendLine(Csv(line));
            foreach (var (title, grid) in Sections)
            {
                var columns = ExportColumns(grid);
                csv.AppendLine();
                csv.AppendLine(Csv(title));
                csv.AppendLine(string.Join(",", columns.Select(c => Csv(c.HeaderText))));
                foreach (DataGridViewRow row in grid.Rows)
                    csv.AppendLine(string.Join(",", columns.Select(c => Csv(row.Cells[c.Index].FormattedValue?.ToString()))));
                if (grid.Rows.Count == 0) csv.AppendLine(Csv("No data"));
            }
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));   // BOM so Excel opens it as UTF-8
            MessageBox.Show(owner, "Exported.", "Export to CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Could not export.\n{ex.Message}", "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---- Print ---------------------------------------------------------------------------------------

    private int sectionIndex, rowIndex;
    private bool sectionStarted, firstPage;

    public void Print(IWin32Window owner)
    {
        using var document = new PrintDocument { DocumentName = Title };
        document.DefaultPageSettings.Landscape = true;
        document.DefaultPageSettings.Margins = new Margins(50, 50, 50, 50);
        document.BeginPrint += (_, _) => { sectionIndex = 0; rowIndex = 0; sectionStarted = false; firstPage = true; };
        document.PrintPage += PrintPage;
        using var preview = new PrintPreviewDialog { Document = document, Width = 1100, Height = 800, StartPosition = FormStartPosition.CenterParent };
        try { preview.ShowDialog(owner); }
        catch (Exception ex) { MessageBox.Show(owner, $"Could not print.\n{ex.Message}", "Print Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void PrintPage(object? sender, PrintPageEventArgs e)
    {
        if (e.Graphics is null) return;
        Graphics g = e.Graphics;
        Rectangle area = e.MarginBounds;
        Font titleFont = FontHelper.SemiBold(14f), sectionFont = FontHelper.SemiBold(10.5f), bodyFont = FontHelper.Regular(8f), headFont = FontHelper.SemiBold(8f);
        using var textBrush = new SolidBrush(Ui.Espresso);
        using var headBrush = new SolidBrush(Ui.DarkChocolate);
        using var altBrush = new SolidBrush(Ui.Ivory);
        using var linePen = new Pen(Ui.Border);
        var cellFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center };
        float rowHeight = bodyFont.GetHeight(g) + 10;
        float y = area.Top;

        if (firstPage)
        {
            g.DrawString($"Brew Haven — {Title}", titleFont, textBrush, area.Left, y); y += titleFont.GetHeight(g) + 4;
            g.DrawString(Filters, bodyFont, textBrush, area.Left, y); y += bodyFont.GetHeight(g) + 6;
            string summary = string.Join("     ", SummaryLines);
            SizeF size = g.MeasureString(summary, headFont, area.Width);
            g.DrawString(summary, headFont, textBrush, new RectangleF(area.Left, y, area.Width, size.Height)); y += size.Height + 14;
            firstPage = false;
        }

        while (sectionIndex < Sections.Count)
        {
            var (title, grid) = Sections[sectionIndex];
            var columns = ExportColumns(grid);
            float sectionTitleHeight = sectionFont.GetHeight(g) + 6;

            // Start a section only if its title, header and at least one row fit on this page.
            if (!sectionStarted)
            {
                if (y + sectionTitleHeight + rowHeight * 2 > area.Bottom) { e.HasMorePages = true; return; }
                g.DrawString(title, sectionFont, textBrush, area.Left, y); y += sectionTitleHeight;
                sectionStarted = true;
            }

            float totalWidth = Math.Max(1, columns.Sum(c => c.Width));
            float[] widths = columns.Select(c => c.Width / totalWidth * area.Width).ToArray();

            // Header row (repeated at the top of each page the section continues on)
            g.FillRectangle(headBrush, area.Left, y, area.Width, rowHeight);
            float x = area.Left;
            for (int i = 0; i < columns.Count; i++)
            {
                g.DrawString(columns[i].HeaderText, headFont, Brushes.White, new RectangleF(x + 4, y, widths[i] - 8, rowHeight), cellFormat);
                x += widths[i];
            }
            y += rowHeight;

            if (grid.Rows.Count == 0)
            {
                g.DrawString("No data for these filters.", bodyFont, textBrush, new RectangleF(area.Left + 4, y, area.Width, rowHeight), cellFormat);
                y += rowHeight;
            }
            while (rowIndex < grid.Rows.Count)
            {
                if (y + rowHeight > area.Bottom) { e.HasMorePages = true; return; }
                DataGridViewRow row = grid.Rows[rowIndex];
                if (rowIndex % 2 == 1) g.FillRectangle(altBrush, area.Left, y, area.Width, rowHeight);
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
                cellFormat.Alignment = StringAlignment.Near;
                g.DrawLine(linePen, area.Left, y + rowHeight, area.Right, y + rowHeight);
                y += rowHeight;
                rowIndex++;
            }
            sectionIndex++; rowIndex = 0; sectionStarted = false;
            y += 16;
        }
        e.HasMorePages = false;
    }
}
