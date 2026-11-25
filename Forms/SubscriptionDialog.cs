using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Drawing;

namespace BrewHaven.Forms;

/// <summary>
/// Pop-up used for both Create (empty, "Save") and Edit (pre-filled, "Update") of a subscription plan.
/// Returns DialogResult.OK only after the database save succeeds.
/// </summary>
public sealed class SubscriptionDialog : Form
{
    private readonly int? subscriptionId;

    private readonly Label lblTitle = new() { Dock = DockStyle.Top, Height = 44, Font = Ui.SubHeading, ForeColor = Ui.Text, UseMnemonic = false };
    private readonly TextBox txtName = new() { MaxLength = 100 };
    private readonly NumericUpDown numPrice = new() { Minimum = 0, Maximum = 9_999_999, DecimalPlaces = 2, ThousandsSeparator = true, Increment = 50 };
    private readonly ComboBox cboStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker dtpStart = new();
    private readonly DateTimePicker dtpEnd = new();
    private readonly TextBox txtTerms = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical };
    private readonly RoundedButton btnSave;
    private readonly RoundedButton btnCancel = Ui.GhostButton("Cancel");

    public SubscriptionDialog(int? subscriptionId = null)
    {
        this.subscriptionId = subscriptionId;
        bool editing = subscriptionId.HasValue;

        Text = editing ? "Edit Subscription" : "Create Subscription";
        lblTitle.Text = editing ? "Edit subscription plan" : "New subscription plan";
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        BackColor = Ui.Background; Font = Ui.Body; ClientSize = new Size(700, 560);
        Padding = new Padding(28, 20, 28, 20);

        cboStatus.Items.AddRange(["Active", "Inactive"]); cboStatus.SelectedIndex = 0;
        dtpStart.Value = DateTime.Today; dtpEnd.Value = DateTime.Today.AddYears(1);

        // Fields: 2 columns of 310px
        const int col = 310, full = col * 2 + 18;
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, BackColor = Ui.Background };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, col + 18));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, col + 18));
        for (int i = 0; i < 4; i++) fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var nameField = Ui.Field("PLAN NAME *", txtName, full);
        fields.Controls.Add(nameField, 0, 0); fields.SetColumnSpan(nameField, 2);
        fields.Controls.Add(Ui.Field("MONTHLY PRICE (PHP) *", numPrice, col), 0, 1);
        fields.Controls.Add(Ui.Field("STATUS *", cboStatus, col), 1, 1);
        fields.Controls.Add(Ui.Field("STARTS *", dtpStart, col), 0, 2);
        fields.Controls.Add(Ui.Field("ENDS *", dtpEnd, col), 1, 2);
        var termsField = Ui.Field("TERMS & CONDITIONS *", txtTerms, full, 170);
        fields.Controls.Add(termsField, 0, 3); fields.SetColumnSpan(termsField, 2);

        // Buttons (right-aligned): Save/Update + Cancel
        btnSave = Ui.PrimaryButton(editing ? "Update" : "Save");
        btnSave.DialogResult = DialogResult.None;   // only close after a successful save
        btnCancel.DialogResult = DialogResult.Cancel;
        btnSave.Margin = new Padding(8, 0, 0, 0); btnCancel.Margin = new Padding(8, 0, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, BackColor = Ui.Background, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(btnSave); buttons.Controls.Add(btnCancel);

        Controls.Add(fields); Controls.Add(buttons); Controls.Add(lblTitle);
        AcceptButton = btnSave; CancelButton = btnCancel;

        btnSave.Click += BtnSave_Click;
        Load += SubscriptionDialog_Load;
    }

    private void SubscriptionDialog_Load(object? sender, EventArgs e)
    {
        FontHelper.ApplyToAll(this);
        if (subscriptionId.HasValue && !LoadRecord(subscriptionId.Value))
            BeginInvoke(() => { DialogResult = DialogResult.Cancel; });
        txtName.Focus();
    }

    private bool LoadRecord(int id)
    {
        const string sql = "SELECT Name, TermsAndConditions, MonthlyPrice, StartsAt, EndsAt, IsActive FROM Subscriptions WHERE SubscriptionId = @Id";
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            connection.Open();
            using SqlDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                MessageBox.Show("This subscription no longer exists. It may have been removed.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            txtName.Text = reader.GetString(0);
            txtTerms.Text = reader.GetString(1);
            numPrice.Value = Math.Clamp(reader.GetDecimal(2), numPrice.Minimum, numPrice.Maximum);
            dtpStart.Value = reader.GetDateTime(3);
            dtpEnd.Value = reader.GetDateTime(4);
            cboStatus.SelectedItem = reader.GetBoolean(5) ? "Active" : "Inactive";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load the subscription.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        string name = txtName.Text.Trim();
        if (name.Length == 0) return Invalid("Plan name is required.", txtName);
        if (txtTerms.Text.Trim().Length == 0) return Invalid("Terms & Conditions are required.", txtTerms);
        if (cboStatus.SelectedItem is null) return Invalid("Select a status.", cboStatus);
        if (dtpEnd.Value.Date < dtpStart.Value.Date) return Invalid("The end date must be on or after the start date.", dtpEnd);
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new("SELECT COUNT(*) FROM Subscriptions WHERE Name = @Name AND SubscriptionId <> @Id", connection);
            command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = name;
            command.Parameters.Add("@Id", SqlDbType.Int).Value = subscriptionId ?? 0;
            connection.Open();
            if (Convert.ToInt32(command.ExecuteScalar()) > 0) return Invalid($"A plan named \"{name}\" already exists.", txtName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not check for duplicates.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        return true;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (!ValidateInputs()) return;   // dialog stays open

        string sql = subscriptionId.HasValue
            ? """
              UPDATE Subscriptions SET Name = @Name, TermsAndConditions = @Terms, MonthlyPrice = @Price,
                     StartsAt = @StartsAt, EndsAt = @EndsAt, IsActive = @IsActive
              WHERE SubscriptionId = @Id
              """
            : """
              INSERT INTO Subscriptions (Name, TermsAndConditions, MonthlyPrice, StartsAt, EndsAt, IsActive)
              VALUES (@Name, @Terms, @Price, @StartsAt, @EndsAt, @IsActive)
              """;
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = txtName.Text.Trim();
            command.Parameters.Add("@Terms", SqlDbType.NVarChar, -1).Value = txtTerms.Text.Trim();
            var price = command.Parameters.Add("@Price", SqlDbType.Decimal); price.Precision = 12; price.Scale = 2; price.Value = numPrice.Value;
            command.Parameters.Add("@StartsAt", SqlDbType.Date).Value = dtpStart.Value.Date;
            command.Parameters.Add("@EndsAt", SqlDbType.Date).Value = dtpEnd.Value.Date;
            command.Parameters.Add("@IsActive", SqlDbType.Bit).Value = cboStatus.Text == "Active";
            if (subscriptionId.HasValue) command.Parameters.Add("@Id", SqlDbType.Int).Value = subscriptionId.Value;
            connection.Open();
            if (command.ExecuteNonQuery() == 0)
            {
                MessageBox.Show("Nothing was saved: the subscription no longer exists.", "Not Saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;   // closes the dialog; the list page reloads
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the subscription.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
