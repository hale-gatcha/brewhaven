using System.Data;
using System.Drawing;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.Forms;

/// <summary>
/// Read-only "Feedback Details" pop-up: every field of one feedback / inquiry / complaint, with Type and Status
/// badges and a star rating. "Edit" (Admin / Manager only) closes it with DialogResult.Yes so the list page can
/// open the edit dialog for the same record; "Close" closes it.
/// </summary>
public sealed class FeedbackDetailsDialog : PopupDialog
{
    private static readonly Color SandBeige = ColorTranslator.FromHtml("#D1B5A1");

    private readonly int feedbackId;
    private readonly TextBox txtFeedbackNo = ReadOnlyBox();
    private readonly TextBox txtDateSubmitted = ReadOnlyBox();
    private readonly Label lblTypeBadge = BadgeLabel();
    private readonly Label lblStatusBadge = BadgeLabel();
    private readonly StarRating ratingStars = new();
    private readonly TextBox txtCustomer = ReadOnlyBox();
    private readonly TextBox txtContact = ReadOnlyBox();
    private readonly TextBox txtEmail = ReadOnlyBox();
    private readonly TextBox txtBranch = ReadOnlyBox();
    private readonly TextBox txtLoggedBy = ReadOnlyBox();
    private readonly TextBox txtAssignedTo = ReadOnlyBox();
    private readonly TextBox txtDateResolved = ReadOnlyBox();
    private readonly TextBox txtSubject = ReadOnlyBox();
    private readonly TextBox txtMessage = ReadOnlyBox(multiline: true);
    private readonly TextBox txtResolution = ReadOnlyBox(multiline: true);
    private Control? ratingField;

    /// <param name="canEdit">Shows the Edit button (Admin and Manager).</param>
    public FeedbackDetailsDialog(int feedbackId, bool canEdit)
        : base("Feedback Details", "Feedback Details", canEdit ? "Edit" : null, 0, 740)
    {
        this.feedbackId = feedbackId;
        // Fit smaller screens: the field area scrolls if the window would be taller than the screen.
        ClientSize = new Size(740, Math.Min(850, Screen.PrimaryScreen?.WorkingArea.Height - 60 ?? 850));
        Fields.AutoScroll = true;

        // Header bar: Dark Chocolate with white text.
        LblTitle.BackColor = Ui.DarkChocolate; LblTitle.ForeColor = Color.White;
        LblTitle.Height = 48; LblTitle.TextAlign = ContentAlignment.MiddleLeft; LblTitle.Padding = new Padding(16, 0, 0, 0);
        LblTitle.Margin = new Padding(0, 0, 0, 12);
        Controls.Add(Ui.Spacer(DockStyle.Top, 14)); Controls.SetChildIndex(Controls[^1], 1);   // gap under the header bar

        BtnCancel.Text = "Close";
        AcceptButton = BtnCancel;        // Enter closes; Edit is a deliberate click
        BtnSave.DialogResult = DialogResult.Yes;   // Edit: close and let the list page open the edit dialog

        var typeStatus = new FlowLayoutPanel { Height = 34, Width = Col, BackColor = Ui.Background, WrapContents = false, Margin = Padding.Empty };
        typeStatus.Controls.Add(lblTypeBadge); typeStatus.Controls.Add(lblStatusBadge);

        AddField("FEEDBACK NO.", txtFeedbackNo, 0);
        AddField("DATE SUBMITTED", txtDateSubmitted, 1);
        AddField("TYPE / STATUS", typeStatus, 0);
        ratingField = AddField("RATING", ratingStars, 1);
        AddField("CUSTOMER", txtCustomer, 0);
        AddField("CONTACT NUMBER", txtContact, 1);
        AddField("EMAIL", txtEmail, 0);
        AddField("BRANCH", txtBranch, 1);
        AddField("LOGGED BY", txtLoggedBy, 0);
        AddField("ASSIGNED TO", txtAssignedTo, 1);
        AddField("SUBJECT", txtSubject, 0);
        AddField("DATE RESOLVED", txtDateResolved, 1);
        AddField("MESSAGE", txtMessage, 0, span: true, multilineHeight: 110);
        AddField("RESOLUTION / RESPONSE NOTES", txtResolution, 0, span: true, multilineHeight: 84);

        Load += (_, _) => { if (!LoadRecord()) CancelAfterLoad(); };
        // Start on the Close button with nothing highlighted in the read-only boxes.
        Shown += (_, _) =>
        {
            foreach (var box in new[] { txtMessage, txtResolution }) { box.SelectionStart = 0; box.SelectionLength = 0; }
            BtnCancel.Focus();
        };
    }

    private static TextBox ReadOnlyBox(bool multiline = false) => new()
    {
        ReadOnly = true, BackColor = Ui.Ivory, ForeColor = Ui.Espresso, TabStop = multiline,
        Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, WordWrap = true
    };

    private static Label BadgeLabel() => new PillLabel
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

    private static void SetBadge(Label badge, string text, Color back, Color fore)
    {
        badge.Text = text.ToUpperInvariant(); badge.BackColor = back; badge.ForeColor = fore;
    }

    private bool LoadRecord()
    {
        const string sql = """
            SELECT f.FeedbackId, f.Type, f.Status, f.Subject, f.Message, f.Rating, f.DateSubmitted, f.ResolutionNotes, f.DateResolved,
                   c.FullName AS CustomerName, c.ContactNumber AS CustomerContact, c.Email AS CustomerEmail,
                   b.BranchName, s.FullName AS LoggedBy, a.FullName AS AssignedTo
            FROM Feedback f
            INNER JOIN Branches b ON b.BranchId = f.BranchId
            LEFT JOIN Customers c ON c.CustomerId = f.CustomerId
            LEFT JOIN Users s ON s.UserId = f.SubmittedByUserId
            LEFT JOIN Users a ON a.UserId = f.AssignedTo
            WHERE f.FeedbackId = @Id
            """;
        try
        {
            DataTable t = Query(sql, P("@Id", SqlDbType.Int, feedbackId));
            if (t.Rows.Count == 0)
            {
                MessageBox.Show("This entry no longer exists. It may have been removed.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            DataRow r = t.Rows[0];
            string type = (string)r["Type"], status = (string)r["Status"];

            txtFeedbackNo.Text = $"#{r["FeedbackId"]}";
            txtDateSubmitted.Text = ((DateTime)r["DateSubmitted"]).ToString("MMM d, yyyy h:mm tt");
            SetBadge(lblTypeBadge, type, type switch { "Complaint" => Ui.Danger, "Inquiry" => Ui.CocoaBrown, _ => Ui.CoffeeBrown }, Color.White);
            SetBadge(lblStatusBadge, status, status switch
            {
                "Open" => Ui.WarmSand,
                "In Progress" => SandBeige,
                "Resolved" => Ui.Cream,
                _ => Ui.Border   // Closed
            }, Ui.Espresso);

            // Rating only applies to the Feedback type.
            ratingField!.Visible = type == "Feedback";
            ratingStars.Rating = r["Rating"] as int?;

            if (r["CustomerName"] is string customer)
            {
                txtCustomer.Text = customer;
                txtContact.Text = r["CustomerContact"] as string ?? "-";
                txtEmail.Text = r["CustomerEmail"] as string ?? "-";
            }
            else { txtCustomer.Text = "Walk-in guest"; txtContact.Text = "-"; txtEmail.Text = "-"; }

            txtBranch.Text = (string)r["BranchName"];
            txtLoggedBy.Text = r["LoggedBy"] as string ?? "-";
            txtAssignedTo.Text = r["AssignedTo"] as string ?? "Not assigned";
            txtSubject.Text = (string)r["Subject"];
            txtDateResolved.Text = r["DateResolved"] is DateTime resolved ? resolved.ToString("MMM d, yyyy h:mm tt") : "";
            // Windows TextBoxes need \r\n for line breaks.
            txtMessage.Text = ((string)r["Message"]).ReplaceLineEndings("\r\n");
            txtResolution.Text = (r["ResolutionNotes"] as string ?? "No resolution notes yet.").ReplaceLineEndings("\r\n");
            Text = $"Feedback Details - #{feedbackId}";
            return true;
        }
        catch (Exception ex) { ShowError("Could not load the feedback details.", ex); return false; }
    }
}
