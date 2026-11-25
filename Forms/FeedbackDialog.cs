using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Drawing;

namespace BrewHaven.Forms;

/// <summary>
/// Pop-up used for both Create (empty, "Save") and Edit (pre-filled, "Update") of a feedback / inquiry / complaint.
/// Returns DialogResult.OK only after the database save succeeds.
/// </summary>
public sealed class FeedbackDialog : Form
{
    private readonly int? feedbackId;
    private int? loadedCustomerId, loadedBranchId;

    private readonly Label lblTitle = new() { Dock = DockStyle.Top, Height = 44, Font = Ui.SubHeading, ForeColor = Ui.Text, UseMnemonic = false };
    private readonly ComboBox cboCustomer = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboBranch = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown numRating = new() { Minimum = 1, Maximum = 5, Value = 5 };
    private readonly ComboBox cboStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtSubject = new() { MaxLength = 150 };
    private readonly TextBox txtMessage = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox txtResolution = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label lblRatingHint = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 0, 0, 6) };
    private readonly RoundedButton btnSave;
    private readonly RoundedButton btnCancel = Ui.GhostButton("Cancel");

    public FeedbackDialog(int? feedbackId = null)
    {
        this.feedbackId = feedbackId;
        bool editing = feedbackId.HasValue;

        Text = editing ? "Edit Feedback" : "Create Feedback";
        lblTitle.Text = editing ? "Edit feedback / inquiry / complaint" : "New feedback / inquiry / complaint";
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        BackColor = Ui.Background; Font = Ui.Body; ClientSize = new Size(700, 640);
        Padding = new Padding(28, 20, 28, 20);

        cboType.Items.AddRange(["Feedback", "Inquiry", "Complaint"]); cboType.SelectedIndex = 0;
        cboStatus.Items.AddRange(["Open", "In Progress", "Resolved", "Closed"]); cboStatus.SelectedIndex = 0;

        // Fields: 2 columns of 310px; Message and Resolution span both columns
        const int col = 310, full = col * 2 + 18;
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, BackColor = Ui.Background };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, col + 18));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, col + 18));
        for (int i = 0; i < 6; i++) fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.Controls.Add(Ui.Field("CUSTOMER (OPTIONAL)", cboCustomer, col), 0, 0);
        fields.Controls.Add(Ui.Field("BRANCH *", cboBranch, col), 1, 0);
        fields.Controls.Add(Ui.Field("TYPE *", cboType, col), 0, 1);
        fields.Controls.Add(Ui.Field("RATING (1-5)", numRating, col), 1, 1);
        fields.Controls.Add(Ui.Field("STATUS *", cboStatus, col), 0, 2);
        fields.Controls.Add(Ui.Field("SUBJECT *", txtSubject, col), 1, 2);
        var messageField = Ui.Field("MESSAGE *", txtMessage, full, 96);
        fields.Controls.Add(messageField, 0, 3); fields.SetColumnSpan(messageField, 2);
        var resolutionField = Ui.Field("RESOLUTION NOTES", txtResolution, full, 84);
        fields.Controls.Add(resolutionField, 0, 4); fields.SetColumnSpan(resolutionField, 2);
        fields.Controls.Add(lblRatingHint, 0, 5); fields.SetColumnSpan(lblRatingHint, 2);
        lblRatingHint.Text = "Rating is required for Feedback. Complaints need resolution notes before they can be Resolved or Closed.";

        // Buttons (right-aligned): Save/Update + Cancel
        btnSave = Ui.PrimaryButton(editing ? "Update" : "Save");
        btnSave.DialogResult = DialogResult.None;   // only close after a successful save
        btnCancel.DialogResult = DialogResult.Cancel;
        btnSave.Margin = new Padding(8, 0, 0, 0); btnCancel.Margin = new Padding(8, 0, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, BackColor = Ui.Background, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(btnSave); buttons.Controls.Add(btnCancel);

        Controls.Add(fields); Controls.Add(buttons); Controls.Add(lblTitle);
        AcceptButton = btnSave; CancelButton = btnCancel;

        cboType.SelectedIndexChanged += (_, _) => numRating.Enabled = cboType.Text == "Feedback";
        btnSave.Click += BtnSave_Click;
        Load += FeedbackDialog_Load;
    }

    private void FeedbackDialog_Load(object? sender, EventArgs e)
    {
        FontHelper.ApplyToAll(this);
        bool ok = (!feedbackId.HasValue || LoadRecord(feedbackId.Value)) && LoadLookups();
        if (!ok) { BeginInvoke(() => { DialogResult = DialogResult.Cancel; }); return; }
        numRating.Enabled = cboType.Text == "Feedback";
        cboCustomer.Focus();
    }

    private bool LoadRecord(int id)
    {
        const string sql = "SELECT CustomerId, BranchId, Type, Subject, Message, Rating, Status, ResolutionNotes FROM Feedback WHERE FeedbackId = @Id";
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            connection.Open();
            using SqlDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                MessageBox.Show("This entry no longer exists. It may have been removed.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            loadedCustomerId = reader.IsDBNull(0) ? null : reader.GetInt32(0);
            loadedBranchId = reader.GetInt32(1);
            cboType.SelectedItem = reader.GetString(2);
            txtSubject.Text = reader.GetString(3);
            txtMessage.Text = reader.GetString(4);
            if (!reader.IsDBNull(5)) numRating.Value = Math.Clamp(reader.GetInt32(5), 1, 5);
            cboStatus.SelectedItem = reader.GetString(6);
            txtResolution.Text = reader.IsDBNull(7) ? "" : reader.GetString(7);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load the entry.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool LoadLookups()
    {
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            connection.Open();

            // Active customers, plus the entry's current customer even if since deactivated.
            var customers = new DataTable();
            using (SqlCommand command = new("SELECT CustomerId, FullName FROM Customers WHERE IsActive = 1 OR CustomerId = @Current ORDER BY FullName", connection))
            {
                command.Parameters.Add("@Current", SqlDbType.Int).Value = (object?)loadedCustomerId ?? DBNull.Value;
                using SqlDataReader reader = command.ExecuteReader();
                customers.Load(reader);
            }
            DataRow walkIn = customers.NewRow(); walkIn["CustomerId"] = 0; walkIn["FullName"] = "(Walk-in / not a member)";
            customers.Rows.InsertAt(walkIn, 0);
            cboCustomer.DisplayMember = "FullName"; cboCustomer.ValueMember = "CustomerId"; cboCustomer.DataSource = customers;

            // Active branches, plus the entry's current branch even if since deactivated.
            var branches = new DataTable();
            using (SqlCommand command = new("SELECT BranchId, BranchName FROM Branches WHERE IsActive = 1 OR BranchId = @Current ORDER BY BranchName", connection))
            {
                command.Parameters.Add("@Current", SqlDbType.Int).Value = (object?)loadedBranchId ?? DBNull.Value;
                using SqlDataReader reader = command.ExecuteReader();
                branches.Load(reader);
            }
            cboBranch.DisplayMember = "BranchName"; cboBranch.ValueMember = "BranchId"; cboBranch.DataSource = branches;

            cboCustomer.SelectedValue = loadedCustomerId ?? 0;
            if (loadedBranchId.HasValue) cboBranch.SelectedValue = loadedBranchId.Value;
            else if (Session.CurrentUser?.BranchId is int myBranch && branches.Select($"BranchId = {myBranch}").Length > 0)
                cboBranch.SelectedValue = myBranch;   // default new entries to the Admin's own branch
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load customers and branches.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    /// <summary>Shows the problem and focuses the field; the dialog stays open.</summary>
    private static bool Invalid(string message, Control focus)
    {
        MessageBox.Show(message, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        focus.Focus();
        return false;
    }

    private bool ValidateInputs()
    {
        if (cboBranch.SelectedValue is null) return Invalid("Select a branch.", cboBranch);
        if (cboType.SelectedItem is null) return Invalid("Select a type.", cboType);
        if (cboStatus.SelectedItem is null) return Invalid("Select a status.", cboStatus);
        if (txtSubject.Text.Trim().Length == 0) return Invalid("Subject is required.", txtSubject);
        if (txtMessage.Text.Trim().Length == 0) return Invalid("Message is required.", txtMessage);
        if (cboType.Text == "Feedback" && (numRating.Value < 1 || numRating.Value > 5))
            return Invalid("Feedback needs a rating from 1 to 5.", numRating);
        if (cboType.Text == "Complaint" && cboStatus.Text is "Resolved" or "Closed" && txtResolution.Text.Trim().Length == 0)
            return Invalid("Add resolution notes before setting a complaint to Resolved or Closed.", txtResolution);
        return true;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (!ValidateInputs()) return;   // dialog stays open

        // DateResolved is stamped the first time an entry becomes Resolved/Closed, and cleared if it is reopened.
        string sql = feedbackId.HasValue
            ? """
              UPDATE Feedback SET CustomerId = @CustomerId, BranchId = @BranchId, Type = @Type, Subject = @Subject, Message = @Message,
                     Rating = @Rating, Status = @Status, ResolutionNotes = @Resolution,
                     DateResolved = CASE WHEN @Status IN ('Resolved', 'Closed') THEN COALESCE(DateResolved, SYSUTCDATETIME()) ELSE NULL END
              WHERE FeedbackId = @Id
              """
            : """
              INSERT INTO Feedback (CustomerId, BranchId, SubmittedByUserId, Type, Subject, Message, Rating, Status, ResolutionNotes, DateResolved)
              VALUES (@CustomerId, @BranchId, @UserId, @Type, @Subject, @Message, @Rating, @Status, @Resolution,
                      CASE WHEN @Status IN ('Resolved', 'Closed') THEN SYSUTCDATETIME() ELSE NULL END)
              """;
        try
        {
            int customerId = Convert.ToInt32(cboCustomer.SelectedValue ?? 0);
            string resolution = txtResolution.Text.Trim();
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@CustomerId", SqlDbType.Int).Value = customerId == 0 ? DBNull.Value : customerId;
            command.Parameters.Add("@BranchId", SqlDbType.Int).Value = Convert.ToInt32(cboBranch.SelectedValue);
            command.Parameters.Add("@Type", SqlDbType.NVarChar, 12).Value = cboType.Text;
            command.Parameters.Add("@Subject", SqlDbType.NVarChar, 150).Value = txtSubject.Text.Trim();
            command.Parameters.Add("@Message", SqlDbType.NVarChar, -1).Value = txtMessage.Text.Trim();
            command.Parameters.Add("@Rating", SqlDbType.Int).Value = cboType.Text == "Feedback" ? (int)numRating.Value : DBNull.Value;
            command.Parameters.Add("@Status", SqlDbType.NVarChar, 15).Value = cboStatus.Text;
            command.Parameters.Add("@Resolution", SqlDbType.NVarChar, -1).Value = resolution.Length > 0 ? resolution : DBNull.Value;
            if (feedbackId.HasValue) command.Parameters.Add("@Id", SqlDbType.Int).Value = feedbackId.Value;
            else command.Parameters.Add("@UserId", SqlDbType.Int).Value = Session.CurrentUser!.UserId;
            connection.Open();
            if (command.ExecuteNonQuery() == 0)
            {
                MessageBox.Show("Nothing was saved: the entry no longer exists.", "Not Saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;   // closes the dialog; the list page reloads
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the entry.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
