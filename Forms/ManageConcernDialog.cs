using BrewHaven.DataAccess;
using BrewHaven.Models;
using System.Data;
using System.Drawing;

namespace BrewHaven.Forms;

/// <summary>
/// Admin pop-up: manage one concern. Only the management fields can change (Status, Resolution / Response notes,
/// and a new internal note); Type, Category, Customer, Subject, Message and Rating are read-only.
/// A Complaint can't be Resolved / Closed without resolution notes. DialogResult = OK only after the save succeeds.
/// </summary>
public sealed class ManageConcernDialog : PopupDialog
{
    private readonly int feedbackId;
    private readonly FeedbackRepository repository = new();
    private ConcernDetails? record;

    // Read-only original details
    private readonly Label lblTypeBadge = FeedbackDetailsDialog.BadgeLabel();
    private readonly Label lblStatusBadge = FeedbackDetailsDialog.BadgeLabel();
    private readonly TextBox txtCategory = ReadOnlyBox();
    private readonly TextBox txtCustomer = ReadOnlyBox();
    private readonly TextBox txtBranch = ReadOnlyBox();
    private readonly StarRating ratingStars = new();
    private readonly TextBox txtSubject = ReadOnlyBox();
    private readonly TextBox txtMessage = ReadOnlyBox(multiline: true);
    private readonly TextBox txtInternalNotes = ReadOnlyBox(multiline: true);
    private readonly Control ratingField;

    // Editable management fields
    private readonly ComboBox cboStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtResolution = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, MaxLength = 4000 };
    private readonly TextBox txtNewNote = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, MaxLength = 1000 };

    public ManageConcernDialog(int feedbackId) : base("Manage Concern", "Manage concern", "Save Changes", 0, 740)
    {
        this.feedbackId = feedbackId;
        ClientSize = new Size(740, Math.Min(900, Screen.PrimaryScreen?.WorkingArea.Height - 60 ?? 900));
        Fields.AutoScroll = true;
        cboStatus.Items.AddRange(FeedbackRepository.Statuses);

        var typeStatus = new FlowLayoutPanel { Height = 34, Width = Col, BackColor = Ui.Background, WrapContents = false, Margin = Padding.Empty };
        typeStatus.Controls.Add(lblTypeBadge); typeStatus.Controls.Add(lblStatusBadge);

        AddRow(SectionLabel("ORIGINAL CONCERN (READ-ONLY)"));
        AddField("TYPE / CURRENT STATUS", typeStatus, 0);
        AddField("CATEGORY", txtCategory, 1);
        AddField("CUSTOMER", txtCustomer, 0);
        AddField("BRANCH", txtBranch, 1);
        AddField("SUBJECT", txtSubject, 0);
        ratingField = AddField("RATING", ratingStars, 1);
        AddField("MESSAGE", txtMessage, 0, span: true, multilineHeight: 90);

        AddRow(SectionLabel("MANAGEMENT"));
        AddField("STATUS *", cboStatus, 0);
        AddField("RESOLUTION / RESPONSE NOTES", txtResolution, 0, span: true, multilineHeight: 84);
        AddField("INTERNAL NOTES SO FAR", txtInternalNotes, 0, span: true, multilineHeight: 70);
        AddField("ADD AN INTERNAL NOTE (OPTIONAL)", txtNewNote, 0, span: true, multilineHeight: 60);
        AddRow(Note("Complaints need resolution notes before they can be Resolved or Closed. Date Resolved is set automatically. " +
                    "Internal notes are for Admin and Managers only."));

        BtnSave.Click += BtnSave_Click;
        Load += ManageConcernDialog_Load;
    }

    private static TextBox ReadOnlyBox(bool multiline = false) => new ReadOnlyTextBox
    {
        ReadOnly = true, BackColor = Ui.Ivory, ForeColor = Ui.Espresso, TabStop = multiline,
        Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, WordWrap = true
    };

    private static Label SectionLabel(string text) => new()
    {
        Text = text, AutoSize = true, Font = Ui.SmallBold, ForeColor = Ui.Accent, Margin = new Padding(0, 4, 0, 8), UseMnemonic = false
    };

    private void ManageConcernDialog_Load(object? sender, EventArgs e)
    {
        try
        {
            record = repository.GetDetails(feedbackId);
            if (record is null || record.IsArchived)
            {
                MessageBox.Show("This entry no longer exists or was removed.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                CancelAfterLoad();
                return;
            }
            Text = $"Manage Concern - #{record.FeedbackId}";
            LblTitle.Text = $"Manage concern #{record.FeedbackId}";
            FeedbackDetailsDialog.SetBadges(lblTypeBadge, lblStatusBadge, record.Type, record.Status);
            txtCategory.Text = record.Category;
            txtCustomer.Text = record.CustomerName;
            txtBranch.Text = record.BranchName;
            txtSubject.Text = record.Subject;
            ratingField.Visible = record.Type == "Feedback";
            ratingStars.Rating = record.Rating;
            txtMessage.Text = record.Message.ReplaceLineEndings("\r\n");
            txtInternalNotes.Text = (record.InternalNotes ?? "No internal notes yet.").ReplaceLineEndings("\r\n");
            cboStatus.SelectedItem = record.Status;
            txtResolution.Text = (record.ResolutionNotes ?? "").ReplaceLineEndings("\r\n");
            cboStatus.Focus();
        }
        catch (Exception ex) { ShowError("Could not load the concern.", ex); CancelAfterLoad(); }
    }

    private bool ValidateInputs()
    {
        if (cboStatus.SelectedItem is null) return Invalid("Select a status.", cboStatus);
        if (record!.Type == "Complaint" && cboStatus.Text is "Resolved" or "Closed" && txtResolution.Text.Trim().Length == 0)
            return Invalid("Add resolution notes before setting a complaint to Resolved or Closed.", txtResolution);
        if (cboStatus.Text is "Resolved" or "Closed" && txtResolution.Text.Trim().Length == 0 &&
            MessageBox.Show("There are no resolution / response notes. Save anyway?", "No Notes",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        { txtResolution.Focus(); return false; }
        return true;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (record is null || !ValidateInputs()) return;   // dialog stays open
        try
        {
            repository.Manage(feedbackId, cboStatus.Text, txtResolution.Text, txtNewNote.Text);
            DialogResult = DialogResult.OK;
        }
        catch (ArgumentException ex) { MessageBox.Show(ex.Message, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        catch (Exception ex) { ShowError("Could not save the changes.", ex); }
    }
}
