using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;

namespace BrewHaven.Forms;

/// <summary>
/// Pop-up for registering ("Register") and editing ("Update") a walk-in customer. New customers get an
/// auto-generated code (BH-00001) and 0 loyalty points. Contact number and email must not belong to another customer.
/// </summary>
public sealed class CustomerDialog : PopupDialog
{
    private readonly int? customerId;
    private readonly TextBox txtFullName = new() { MaxLength = 100 };
    private readonly TextBox txtContact = new() { MaxLength = 11, PlaceholderText = "09XXXXXXXXX" };
    private readonly TextBox txtEmail = new() { MaxLength = 254, PlaceholderText = "optional" };
    private readonly DateTimePicker dtpBirthdate = new() { ShowCheckBox = true, Checked = false };
    private readonly TextBox txtNotes = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, MaxLength = 500 };
    private readonly Label lblCode = Note("");

    /// <summary>The new or edited customer's id after DialogResult.OK.</summary>
    public int SavedCustomerId { get; private set; }

    public CustomerDialog(int? customerId = null)
        : base(customerId is null ? "Register Customer" : "Edit Customer", customerId is null ? "Register walk-in customer" : "Edit customer",
               customerId is null ? "Register" : "Update", 450)
    {
        this.customerId = customerId;
        DigitsOnly(txtContact);
        dtpBirthdate.MaxDate = DateTime.Today;
        dtpBirthdate.Value = DateTime.Today.AddYears(-25);

        AddRow(lblCode);
        AddField("FULL NAME *", txtFullName, 0);
        AddField("CONTACT NUMBER *", txtContact, 1);
        AddField("EMAIL", txtEmail, 0);
        AddField("BIRTHDATE (TICK TO SET)", dtpBirthdate, 1);
        AddField("PREFERENCES / NOTES", txtNotes, 0, span: true, multilineHeight: 90);
        lblCode.Text = customerId is null ? "A customer code is generated automatically. Loyalty points start at 0." : "";

        BtnSave.Click += BtnSave_Click;
        Load += (_, _) =>
        {
            if (customerId is int id && !LoadRecord(id)) { CancelAfterLoad(); return; }
            txtFullName.Focus();
        };
    }

    private bool LoadRecord(int id)
    {
        try
        {
            CustomerRecord? c = CustomerRepository.Load(id);
            if (c is null)
            {
                MessageBox.Show("This customer no longer exists.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            lblCode.Text = $"Customer code: {c.CustomerCode}   ·   Loyalty points: {c.LoyaltyPoints:N0}";
            txtFullName.Text = c.FullName; txtContact.Text = c.ContactNumber; txtEmail.Text = c.Email ?? "";
            if (c.Birthdate is DateTime b && b <= dtpBirthdate.MaxDate) { dtpBirthdate.Value = b; dtpBirthdate.Checked = true; }
            txtNotes.Text = c.Notes ?? "";
            return true;
        }
        catch (Exception ex) { ShowError("Could not load the customer.", ex); return false; }
    }

    /// <summary>Shared rules for Customer Registration and Record Customer Information. Returns an error message, or null when valid.</summary>
    public static string? ValidateBasic(string name, string contact, string email)
    {
        if (name.Length == 0) return "Full name is required.";
        if (contact.Length == 0) return "Contact number is required.";
        if (!Validation.IsValidPhMobile(contact)) return "Contact number must be an 11-digit PH mobile number starting with 09 (e.g. 09171234567).";
        if (email.Length > 0 && !Validation.IsValidEmail(email)) return "Enter a valid email address (e.g. name@example.com), or leave it blank.";
        return null;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        string name = txtFullName.Text.Trim(), contact = txtContact.Text.Trim(), email = txtEmail.Text.Trim();
        if (ValidateBasic(name, contact, email) is string problem)
        {
            Invalid(problem, problem.StartsWith("Full") ? txtFullName : problem.Contains("email") ? txtEmail : txtContact);
            return;
        }
        if (dtpBirthdate.Checked && dtpBirthdate.Value.Date > DateTime.Today) { Invalid("Birthdate can't be in the future.", dtpBirthdate); return; }

        try
        {
            var conflicts = CustomerRepository.FindConflicts(contact, email.Length > 0 ? email : null, customerId ?? 0);
            if (conflicts.FirstOrDefault(c => c.SameContact) is { } sameContact)
            { Invalid($"Contact number {contact} already belongs to {sameContact.FullName} ({sameContact.CustomerCode}).", txtContact); return; }
            if (conflicts.FirstOrDefault(c => c.SameEmail) is { } sameEmail)
            { Invalid($"Email {email} already belongs to {sameEmail.FullName} ({sameEmail.CustomerCode}).", txtEmail); return; }

            var record = new CustomerRecord
            {
                CustomerId = customerId ?? 0, FullName = name, ContactNumber = contact, Email = email.Length > 0 ? email : null,
                Birthdate = dtpBirthdate.Checked ? dtpBirthdate.Value.Date : null, Notes = txtNotes.Text.Trim()
            };
            if (customerId is null) SavedCustomerId = CustomerRepository.Insert(record);
            else
            {
                if (CustomerRepository.Update(record) == 0)
                { MessageBox.Show("Nothing was saved: the customer no longer exists.", "Not Saved", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                SavedCustomerId = customerId.Value;
            }
            DialogResult = DialogResult.OK;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            MessageBox.Show("That contact number or email is already used by another customer.", "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) { ShowError("Could not save the customer.", ex); }
    }
}
