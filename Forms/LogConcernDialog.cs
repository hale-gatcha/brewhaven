using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using BrewHaven.Models;
using System.Data;

namespace BrewHaven.Forms;

/// <summary>
/// Staff pop-up: log a walk-in customer's feedback / inquiry / complaint. Branch is the Staff user's own (read-only);
/// Status = Open, DateSubmitted = now, LoggedBy = current user and SubmittedVia = Walk-in are set automatically.
/// Validation keeps the dialog open; DialogResult = OK only after the save succeeds.
/// </summary>
public sealed class LogConcernDialog : PopupDialog
{
    private const int WalkInId = 0;

    private readonly ComboBox cboCustomer = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtBranch = new ReadOnlyTextBox { TabStop = false };
    private readonly TextBox txtGuestName = new() { MaxLength = 100, PlaceholderText = "Optional" };
    private readonly TextBox txtGuestContact = new() { MaxLength = 11, PlaceholderText = "Optional, e.g. 09171234567" };
    private readonly ComboBox cboType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboCategory = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown numRating = new() { Minimum = 1, Maximum = 5, Value = 5 };
    private readonly TextBox txtSubject = new() { MaxLength = 150 };
    private readonly TextBox txtMessage = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, MaxLength = 4000 };
    private readonly Control guestNameField, guestContactField, ratingField;
    private readonly FeedbackRepository repository = new();

    public LogConcernDialog() : base("Log Customer Concern", "Log a customer concern", "Save", 640)
    {
        cboType.Items.AddRange(FeedbackRepository.Types); cboType.SelectedIndex = 0;
        cboCategory.Items.AddRange(FeedbackRepository.Categories); cboCategory.SelectedIndex = 0;
        DigitsOnly(txtGuestContact);

        AddField("CUSTOMER", cboCustomer, 0);
        AddField("BRANCH", txtBranch, 1);
        guestNameField = AddField("WALK-IN GUEST NAME", txtGuestName, 0);
        guestContactField = AddField("WALK-IN GUEST CONTACT", txtGuestContact, 1);
        AddField("TYPE *", cboType, 0);
        AddField("CATEGORY *", cboCategory, 1);
        AddField("SUBJECT *", txtSubject, 0);
        ratingField = AddField("RATING (1-5) *", numRating, 1);
        AddField("MESSAGE *", txtMessage, 0, span: true, multilineHeight: 120);
        AddRow(Note($"Message needs at least {FeedbackRepository.MessageMinLength} characters. Rating is required only for Feedback. " +
                    "Status is set to Open; an Admin will follow it up."));

        cboCustomer.SelectedIndexChanged += (_, _) => UpdateGuestFields();
        cboType.SelectedIndexChanged += (_, _) => ratingField.Visible = cboType.Text == "Feedback";
        BtnSave.Click += BtnSave_Click;
        Load += LogConcernDialog_Load;
    }

    private void LogConcernDialog_Load(object? sender, EventArgs e)
    {
        try
        {
            string? branch = repository.GetMyBranchName();
            if (branch is null)
            {
                MessageBox.Show("Your account isn't assigned to a branch, so concerns can't be logged. Ask an Admin to set your branch.",
                    "No Branch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                CancelAfterLoad();
                return;
            }
            txtBranch.Text = branch;

            DataTable customers = repository.GetActiveCustomers();
            DataRow walkIn = customers.NewRow(); walkIn["CustomerId"] = WalkInId; walkIn["Label"] = "Walk-in guest (no customer record)";
            customers.Rows.InsertAt(walkIn, 0);
            cboCustomer.DisplayMember = "Label"; cboCustomer.ValueMember = "CustomerId"; cboCustomer.DataSource = customers;
            cboCustomer.SelectedValue = WalkInId;
            UpdateGuestFields();
            ratingField.Visible = cboType.Text == "Feedback";
            cboCustomer.Focus();
        }
        catch (Exception ex) { ShowError("Could not open the Log Concern form.", ex); CancelAfterLoad(); }
    }

    private bool IsWalkIn => Convert.ToInt32(cboCustomer.SelectedValue ?? WalkInId) == WalkInId;

    private void UpdateGuestFields() => guestNameField.Visible = guestContactField.Visible = IsWalkIn;

    private bool ValidateInputs()
    {
        if (cboType.SelectedItem is null) return Invalid("Select a type.", cboType);
        if (cboCategory.SelectedItem is null) return Invalid("Select a category.", cboCategory);
        if (txtSubject.Text.Trim().Length == 0) return Invalid("Subject is required.", txtSubject);
        if (txtMessage.Text.Trim().Length < FeedbackRepository.MessageMinLength)
            return Invalid($"Message must be at least {FeedbackRepository.MessageMinLength} characters.", txtMessage);
        if (cboType.Text == "Feedback" && numRating.Value is < 1m or > 5m) return Invalid("Feedback needs a rating from 1 to 5.", numRating);
        string contact = txtGuestContact.Text.Trim();
        if (IsWalkIn && contact.Length > 0 && !Validation.IsValidPhMobile(contact))
            return Invalid("Guest contact number must be 11 digits starting with 09 (or leave it blank).", txtGuestContact);
        return true;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (!ValidateInputs()) return;   // dialog stays open
        try
        {
            repository.LogConcern(new NewConcern
            {
                CustomerId = IsWalkIn ? null : Convert.ToInt32(cboCustomer.SelectedValue),
                GuestName = IsWalkIn ? txtGuestName.Text : null,
                GuestContact = IsWalkIn ? txtGuestContact.Text : null,
                Type = cboType.Text,
                Category = cboCategory.Text,
                Subject = txtSubject.Text,
                Message = txtMessage.Text,
                Rating = cboType.Text == "Feedback" ? (int)numRating.Value : null,
            });
            DialogResult = DialogResult.OK;
        }
        catch (ArgumentException ex) { MessageBox.Show(ex.Message, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        catch (Exception ex) { ShowError("Could not log the concern.", ex); }
    }
}
