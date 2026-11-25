using BrewHaven.Helpers;
using System.Drawing;

namespace BrewHaven.Forms;

/// <summary>
/// Base for the Manager / Staff pop-up dialogs: FixedDialog, CenterParent, no Maximize/Minimize, not in the taskbar,
/// a heading, a 2-column field grid, and Save + Cancel buttons (AcceptButton / CancelButton).
/// Subclasses add fields with <see cref="AddField"/>, handle <see cref="BtnSave"/>.Click, validate with
/// <see cref="Invalid"/> (the dialog stays open) and set DialogResult = OK only after the save succeeds.
/// </summary>
public class PopupDialog : Form
{
    protected const int Col = 310;
    protected const int Full = Col * 2 + 18;

    protected readonly Label LblTitle = new() { Dock = DockStyle.Top, Height = 44, Font = Ui.SubHeading, ForeColor = Ui.Text, UseMnemonic = false };
    protected readonly TableLayoutPanel Fields = new() { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Ui.Background };
    protected readonly RoundedButton BtnSave;
    protected readonly RoundedButton BtnCancel;
    private int currentRow = -1;

    /// <param name="saveText">Text of the primary button, or null for a read-only dialog with just a Close button.</param>
    protected PopupDialog(string windowTitle, string heading, string? saveText, int clientHeight, int clientWidth = 716)
    {
        Text = windowTitle; LblTitle.Text = heading;
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        BackColor = Ui.Background; Font = Ui.Body; ClientSize = new Size(clientWidth, clientHeight);
        Padding = new Padding(28, 20, 28, 20);

        Fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Col + 18));
        Fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Col + 18));

        BtnSave = Ui.PrimaryButton(saveText ?? "Save");
        BtnCancel = Ui.GhostButton(saveText is null ? "Close" : "Cancel");
        BtnSave.DialogResult = DialogResult.None;   // only close after a successful save
        BtnCancel.DialogResult = DialogResult.Cancel;
        BtnSave.Margin = new Padding(8, 0, 0, 0); BtnCancel.Margin = new Padding(8, 0, 0, 0);
        BtnSave.Visible = saveText is not null;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, BackColor = Ui.Background, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(BtnSave); buttons.Controls.Add(BtnCancel);

        Controls.Add(Fields); Controls.Add(buttons); Controls.Add(LblTitle);
        AcceptButton = saveText is null ? BtnCancel : BtnSave;
        CancelButton = BtnCancel;

        Load += (_, _) => FontHelper.ApplyToAll(this);
    }

    /// <summary>
    /// Adds a labelled input. Column 0 (or span = true) starts a new row; column 1 goes beside the previous field.
    /// Returns the field block (hide it with Visible = false to collapse the row).
    /// </summary>
    protected Control AddField(string label, Control input, int column, bool span = false, int multilineHeight = 70)
    {
        if (currentRow < 0 || column == 0 || span) currentRow++;
        EnsureRow(currentRow);
        var block = Ui.Field(label, input, span ? Full : Col, multilineHeight);
        Fields.Controls.Add(block, span ? 0 : column, currentRow);
        if (span) Fields.SetColumnSpan(block, 2);
        return block;
    }

    /// <summary>Adds any control (a note, a summary panel) on its own row across both columns.</summary>
    protected void AddRow(Control control)
    {
        currentRow++;
        EnsureRow(currentRow);
        Fields.Controls.Add(control, 0, currentRow);
        Fields.SetColumnSpan(control, 2);
    }

    private void EnsureRow(int row)
    {
        while (Fields.RowStyles.Count <= row) Fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Fields.RowCount = Math.Max(Fields.RowCount, row + 1);
    }

    /// <summary>A small muted note label for use with <see cref="AddRow"/>.</summary>
    protected static Label Note(string text) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(Full, 0), Font = Ui.Small, ForeColor = Ui.Muted,
        Margin = new Padding(0, 0, 0, 10), UseMnemonic = false
    };

    /// <summary>Shows the problem and focuses the field; returns false so callers can "return Invalid(...)". The dialog stays open.</summary>
    protected static bool Invalid(string message, Control focus)
    {
        MessageBox.Show(message, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        focus.Focus();
        return false;
    }

    protected static void ShowError(string what, Exception ex) =>
        MessageBox.Show($"{what}\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

    /// <summary>Closes the dialog as cancelled once it has finished opening (used when loading fails).</summary>
    protected void CancelAfterLoad() => BeginInvoke(() => { DialogResult = DialogResult.Cancel; });

    /// <summary>Restricts a TextBox to digits.</summary>
    protected static void DigitsOnly(TextBox box) =>
        box.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
}
