using BrewHaven.DataAccess;
using BrewHaven.Models;
using System.Drawing;

namespace BrewHaven.Forms;

/// <summary>
/// Read-only "Concern Details" pop-up: every field of one feedback / inquiry / complaint, with Type and Status badges
/// and a star rating. Loaded through FeedbackRepository, so each role only sees what it may see
/// (Staff: what they logged; Manager: their branch; Admin: everything).
/// "Manage" (Admin only) closes it with DialogResult.Yes so the list page can open ManageConcernDialog.
/// Internal notes are shown to Admin and Manager, never to Staff.
/// </summary>
public sealed class FeedbackDetailsDialog : PopupDialog
{
    private static readonly Color SandBeige = ColorTranslator.FromHtml("#D1B5A1");

    private readonly int feedbackId;
    private readonly bool showInternalNotes;
    private readonly TextBox txtFeedbackNo = ReadOnlyBox();
    private readonly TextBox txtDateSubmitted = ReadOnlyBox();
    private readonly Label lblTypeBadge = BadgeLabel();
    private readonly Label lblStatusBadge = BadgeLabel();
    private readonly TextBox txtCategory = ReadOnlyBox();
    private readonly StarRating ratingStars = new();
    private readonly TextBox txtCustomer = ReadOnlyBox();
    private readonly TextBox txtContact = ReadOnlyBox();
    private readonly TextBox txtEmail = ReadOnlyBox();
    private readonly TextBox txtBranch = ReadOnlyBox();
    private readonly TextBox txtLoggedBy = ReadOnlyBox();
    private readonly TextBox txtSubject = ReadOnlyBox();
    private readonly TextBox txtDateResolved = ReadOnlyBox();
    private readonly TextBox txtUpdatedBy = ReadOnlyBox();
    private readonly TextBox txtUpdatedAt = ReadOnlyBox();
    private readonly TextBox txtMessage = ReadOnlyBox(multiline: true);
    private readonly TextBox txtResolution = ReadOnlyBox(multiline: true);
    private readonly TextBox txtInternalNotes = ReadOnlyBox(multiline: true);
    private readonly Control ratingField, internalNotesField;

    /// <param name="canManage">Shows the Manage button (Admin only; the repository enforces it anyway).</param>
    public FeedbackDetailsDialog(int feedbackId, bool canManage)
        : base("Concern Details", "Concern Details", canManage && FeedbackRepository.CurrentRoleIs("Admin") ? "Manage" : null, 0, 740)
    {
        this.feedbackId = feedbackId;
        showInternalNotes = !FeedbackRepository.CurrentRoleIs("Staff");
        // Fit smaller screens: the field area scrolls if the window would be taller than the screen.
        ClientSize = new Size(740, Math.Min(900, Screen.PrimaryScreen?.WorkingArea.Height - 60 ?? 900));
        Fields.AutoScroll = true;

        // Header bar: Dark Chocolate with white text.
        LblTitle.BackColor = Ui.DarkChocolate; LblTitle.ForeColor = Color.White;
        LblTitle.Height = 48; LblTitle.TextAlign = ContentAlignment.MiddleLeft; LblTitle.Padding = new Padding(16, 0, 0, 0);
        Controls.Add(Ui.Spacer(DockStyle.Top, 14)); Controls.SetChildIndex(Controls[^1], 1);   // gap under the header bar

        BtnCancel.Text = "Close";
        AcceptButton = BtnCancel;                    // Enter closes; Manage is a deliberate click
        BtnSave.DialogResult = DialogResult.Yes;     // Manage: close and let the list page open ManageConcernDialog

        var typeStatus = new FlowLayoutPanel { Height = 34, Width = Col, BackColor = Ui.Background, WrapContents = false, Margin = Padding.Empty };
        typeStatus.Controls.Add(lblTypeBadge); typeStatus.Controls.Add(lblStatusBadge);

        AddField("CONCERN NO.", txtFeedbackNo, 0);
        AddField("DATE SUBMITTED", txtDateSubmitted, 1);
        AddField("TYPE / STATUS", typeStatus, 0);
        AddField("CATEGORY", txtCategory, 1);
        AddField("CUSTOMER", txtCustomer, 0);
        ratingField = AddField("RATING", ratingStars, 1);
        AddField("CONTACT NUMBER", txtContact, 0);
        AddField("EMAIL", txtEmail, 1);
        AddField("BRANCH", txtBranch, 0);
        AddField("LOGGED BY", txtLoggedBy, 1);
        AddField("SUBJECT", txtSubject, 0, span: true);
        AddField("MESSAGE", txtMessage, 0, span: true, multilineHeight: 110);
        AddField("RESOLUTION / RESPONSE NOTES", txtResolution, 0, span: true, multilineHeight: 80);
        AddField("DATE RESOLVED", txtDateResolved, 0);
        AddField("LAST UPDATED BY", txtUpdatedBy, 1);
        AddField("LAST UPDATED", txtUpdatedAt, 0);
        internalNotesField = AddField("INTERNAL NOTES (STAFF CAN'T SEE THESE)", txtInternalNotes, 0, span: true, multilineHeight: 80);
        internalNotesField.Visible = showInternalNotes;

        Load += (_, _) => { if (!LoadRecord()) CancelAfterLoad(); };
        // Start on the Close button with nothing highlighted in the read-only boxes.
        Shown += (_, _) =>
        {
            foreach (var box in new[] { txtMessage, txtResolution, txtInternalNotes }) { box.SelectionStart = 0; box.SelectionLength = 0; }
            BtnCancel.Focus();
        };
    }

    private static TextBox ReadOnlyBox(bool multiline = false) => new ReadOnlyTextBox
    {
        ReadOnly = true, BackColor = Ui.Ivory, ForeColor = Ui.Espresso, TabStop = multiline,
        Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, WordWrap = true
    };

    internal static Label BadgeLabel() => new PillLabel
    {
        AutoSize = true, Font = Ui.SmallBold, Padding = new Padding(12, 6, 12, 6), Margin = new Padding(0, 2, 8, 0), UseMnemonic = false
    };

    /// <summary>Rounded badge with a slightly darker outline, so light colours (Cream) still stand out on the Cream dialog.</summary>
    private sealed class PillLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Ui.EffectiveBack(Parent));
            using var path = Ui.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f);
            using var fill = new SolidBrush(BackColor); g.FillPath(fill, path);
            using var pen = new Pen(ControlPaint.Dark(BackColor, 0.08f)); g.DrawPath(pen, path);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Colours the Type and Status badges the same way everywhere.</summary>
    internal static void SetBadges(Label typeBadge, Label statusBadge, string type, string status)
    {
        typeBadge.Text = type.ToUpperInvariant(); typeBadge.ForeColor = Color.White;
        typeBadge.BackColor = type switch { "Complaint" => Ui.Danger, "Inquiry" => Ui.CocoaBrown, _ => Ui.CoffeeBrown };
        statusBadge.Text = status.ToUpperInvariant(); statusBadge.ForeColor = Ui.Espresso;
        statusBadge.BackColor = status switch { "Open" => Ui.WarmSand, "In Progress" => SandBeige, "Resolved" => Ui.Cream, _ => Ui.Border };
    }

    private static string Stamp(DateTime? value) => value is DateTime d ? d.ToString("MMM d, yyyy h:mm tt") : "-";

    private bool LoadRecord()
    {
        try
        {
            ConcernDetails? r = new FeedbackRepository().GetDetails(feedbackId);
            if (r is null)
            {
                MessageBox.Show("This entry no longer exists, was removed, or isn't available to your account.", "Not Found",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            txtFeedbackNo.Text = r.IsArchived ? $"#{r.FeedbackId}  (archived)" : $"#{r.FeedbackId}";
            txtDateSubmitted.Text = $"{Stamp(r.DateSubmitted)}  ·  {r.SubmittedVia}";
            SetBadges(lblTypeBadge, lblStatusBadge, r.Type, r.Status);
            txtCategory.Text = r.Category;
            ratingField.Visible = r.Type == "Feedback";   // rating only applies to Feedback
            ratingStars.Rating = r.Rating;
            txtCustomer.Text = r.CustomerName;
            txtContact.Text = r.CustomerContact ?? "-";
            txtEmail.Text = r.CustomerEmail ?? "-";
            txtBranch.Text = r.BranchName;
            txtLoggedBy.Text = r.LoggedBy;
            txtSubject.Text = r.Subject;
            // Windows TextBoxes need \r\n for line breaks.
            txtMessage.Text = r.Message.ReplaceLineEndings("\r\n");
            txtResolution.Text = (r.ResolutionNotes ?? "No resolution notes yet.").ReplaceLineEndings("\r\n");
            txtDateResolved.Text = Stamp(r.DateResolved);
            txtUpdatedBy.Text = r.UpdatedBy ?? "-";
            txtUpdatedAt.Text = Stamp(r.UpdatedAt);
            txtInternalNotes.Text = (r.InternalNotes ?? "No internal notes.").ReplaceLineEndings("\r\n");
            BtnSave.Visible = BtnSave.Visible && !r.IsArchived;   // archived entries can't be managed
            Text = $"Concern Details - #{feedbackId}";
            return true;
        }
        catch (Exception ex) { ShowError("Could not load the concern details.", ex); return false; }
    }
}
