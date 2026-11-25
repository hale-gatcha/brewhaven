using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Drawing;

namespace BrewHaven.Forms;

/// <summary>
/// Pop-up used for both Create (empty, "Save") and Edit (pre-filled, "Update") of a user account.
/// Returns DialogResult.OK only after the database save succeeds.
/// </summary>
public sealed class UserDialog : Form
{
    private const int MinPasswordLength = 6;

    /// <summary>
    /// True for the Admin role: they may only see, create, edit and remove Manager and Staff accounts.
    /// Super Admin is unrestricted.
    /// </summary>
    public static bool AdminRestricted => Session.CurrentUser?.RoleName == "Admin";

    private readonly int? userId;
    private readonly bool editingSelf;
    private int? loadedRoleId, loadedBranchId;

    private readonly Label lblTitle = new() { Dock = DockStyle.Top, Height = 44, Font = Ui.SubHeading, ForeColor = Ui.Text, UseMnemonic = false };
    private readonly TextBox txtFullName = new() { MaxLength = 100 };
    private readonly TextBox txtUsername = new() { MaxLength = 50 };
    private readonly TextBox txtEmail = new() { MaxLength = 254 };
    private readonly TextBox txtContact = new() { MaxLength = 11, PlaceholderText = "09XXXXXXXXX" };
    private readonly ComboBox cboRole = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboBranch = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtPassword = new() { MaxLength = 128, PasswordChar = '•' };
    private readonly ComboBox cboStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label lblPasswordHint = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 0, 0, 8) };
    private readonly RoundedButton btnSave;
    private readonly RoundedButton btnCancel = Ui.GhostButton("Cancel");

    public UserDialog(int? userId = null)
    {
        this.userId = userId;
        bool editing = userId.HasValue;
        editingSelf = editing && userId == Session.CurrentUser?.UserId;

        Text = editing ? "Edit User" : "Add User";
        lblTitle.Text = editing ? "Edit user account" : "New user account";
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        BackColor = Ui.Background; Font = Ui.Body; ClientSize = new Size(700, 450);
        Padding = new Padding(28, 20, 28, 20);

        cboStatus.Items.AddRange(["Active", "Inactive"]); cboStatus.SelectedIndex = 0;
        lblPasswordHint.Text = editing
            ? "Leave the password blank to keep the current password."
            : $"Password must be at least {MinPasswordLength} characters.";
        txtContact.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };

        // Fields: 2 columns of 310px
        const int col = 310;
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, BackColor = Ui.Background };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, col + 18));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, col + 18));
        for (int i = 0; i < 5; i++) fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.Controls.Add(Ui.Field("FULL NAME *", txtFullName, col), 0, 0);
        fields.Controls.Add(Ui.Field("USERNAME *", txtUsername, col), 1, 0);
        fields.Controls.Add(Ui.Field("EMAIL *", txtEmail, col), 0, 1);
        fields.Controls.Add(Ui.Field("CONTACT NUMBER *", txtContact, col), 1, 1);
        fields.Controls.Add(Ui.Field("ROLE *", cboRole, col), 0, 2);
        fields.Controls.Add(Ui.Field("BRANCH", cboBranch, col), 1, 2);
        fields.Controls.Add(Ui.Field(editing ? "NEW PASSWORD" : "PASSWORD *", txtPassword, col), 0, 3);
        fields.Controls.Add(Ui.Field("STATUS *", cboStatus, col), 1, 3);
        fields.Controls.Add(lblPasswordHint, 0, 4); fields.SetColumnSpan(lblPasswordHint, 2);

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
        Load += UserDialog_Load;
    }

    private void UserDialog_Load(object? sender, EventArgs e)
    {
        FontHelper.ApplyToAll(this);
        try { DbHelper.EnsureSchema(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not prepare the Users table.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            BeginInvoke(() => { DialogResult = DialogResult.Cancel; });
            return;
        }
        bool ok = (!userId.HasValue || LoadRecord(userId.Value)) && LoadLookups();
        if (!ok) { BeginInvoke(() => { DialogResult = DialogResult.Cancel; }); return; }
        txtFullName.Focus();
    }

    private bool LoadRecord(int id)
    {
        const string sql = """
            SELECT u.FullName, u.Username, u.Email, u.ContactNumber, u.RoleId, u.BranchId, u.IsActive, r.RoleName
            FROM Users u INNER JOIN Roles r ON r.RoleId = u.RoleId
            WHERE u.UserId = @Id
            """;
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            connection.Open();
            using SqlDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                MessageBox.Show("This user no longer exists. It may have been removed.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            if (AdminRestricted && reader.GetString(7) is not ("Manager" or "Staff"))
            {
                MessageBox.Show("You can only edit Manager and Staff accounts.", "Not Allowed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            txtFullName.Text = reader.GetString(0);
            txtUsername.Text = reader.GetString(1);
            txtEmail.Text = reader.GetString(2);
            txtContact.Text = reader.IsDBNull(3) ? "" : reader.GetString(3);
            loadedRoleId = reader.GetInt32(4);
            loadedBranchId = reader.IsDBNull(5) ? null : reader.GetInt32(5);
            cboStatus.SelectedItem = reader.GetBoolean(6) ? "Active" : "Inactive";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load the user.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool LoadLookups()
    {
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            connection.Open();

            var roles = new DataTable();
            // Admin can only assign Manager or Staff; Super Admin can assign any role.
            using (SqlCommand command = new("SELECT RoleId, RoleName FROM Roles WHERE @ManagedOnly = 0 OR RoleName IN ('Manager', 'Staff') ORDER BY RoleId", connection))
            {
                command.Parameters.Add("@ManagedOnly", SqlDbType.Bit).Value = AdminRestricted;
                using SqlDataReader reader = command.ExecuteReader();
                roles.Load(reader);
            }
            cboRole.DisplayMember = "RoleName"; cboRole.ValueMember = "RoleId"; cboRole.DataSource = roles;

            // Active branches, plus the user's current branch even if it has since been deactivated.
            var branches = new DataTable();
            using (SqlCommand command = new("SELECT BranchId, BranchName FROM Branches WHERE IsActive = 1 OR BranchId = @Current ORDER BY BranchName", connection))
            {
                command.Parameters.Add("@Current", SqlDbType.Int).Value = (object?)loadedBranchId ?? DBNull.Value;
                using SqlDataReader reader = command.ExecuteReader();
                branches.Load(reader);
            }
            DataRow none = branches.NewRow(); none["BranchId"] = 0; none["BranchName"] = "(No branch)";
            branches.Rows.InsertAt(none, 0);
            cboBranch.DisplayMember = "BranchName"; cboBranch.ValueMember = "BranchId"; cboBranch.DataSource = branches;

            if (loadedRoleId.HasValue) cboRole.SelectedValue = loadedRoleId.Value;
            else if (roles.Rows.Count > 0) cboRole.SelectedIndex = roles.Rows.Count - 1; // lowest-privilege role (Staff) for new users
            cboBranch.SelectedValue = loadedBranchId ?? 0;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load roles and branches.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        string fullName = txtFullName.Text.Trim(), username = txtUsername.Text.Trim(), email = txtEmail.Text.Trim();
        string contact = txtContact.Text.Trim(), password = txtPassword.Text;

        if (fullName.Length == 0) return Invalid("Full name is required.", txtFullName);
        if (username.Length == 0) return Invalid("Username is required.", txtUsername);
        if (!Validation.IsValidUsername(username))
            return Invalid("Username must be 3-50 characters: letters, numbers, dot, dash or underscore (no spaces).", txtUsername);
        if (email.Length == 0) return Invalid("Email is required.", txtEmail);
        if (!Validation.IsValidEmail(email)) return Invalid("Enter a valid email address, e.g. name@example.com.", txtEmail);
        if (contact.Length == 0) return Invalid("Contact number is required.", txtContact);
        if (!Validation.IsValidPhMobile(contact)) return Invalid("Contact number must be an 11-digit PH mobile number starting with 09 (e.g. 09171234567).", txtContact);
        if (cboRole.SelectedValue is null) return Invalid("Select a role.", cboRole);
        if (cboStatus.SelectedItem is null) return Invalid("Select a status.", cboStatus);
        if (!userId.HasValue && password.Length == 0) return Invalid("Password is required for a new user.", txtPassword);
        if (password.Length > 0 && password.Length < MinPasswordLength)
            return Invalid($"Password must be at least {MinPasswordLength} characters.", txtPassword);
        if (editingSelf && cboStatus.Text != "Active") return Invalid("You can't deactivate the account you are signed in with.", cboStatus);
        if (editingSelf && cboRole.Text != Session.CurrentUser?.RoleName)
            return Invalid("You can't change the role of the account you are signed in with.", cboRole);

        // No duplicate username or email (another user's).
        const string sql = """
            SELECT COUNT(CASE WHEN Username = @Username THEN 1 END),
                   COUNT(CASE WHEN Email = @Email THEN 1 END)
            FROM Users WHERE UserId <> @Id
            """;
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@Username", SqlDbType.NVarChar, 50).Value = username;
            command.Parameters.Add("@Email", SqlDbType.NVarChar, 254).Value = email;
            command.Parameters.Add("@Id", SqlDbType.Int).Value = userId ?? 0;
            connection.Open();
            using SqlDataReader reader = command.ExecuteReader();
            reader.Read();
            if (reader.GetInt32(0) > 0) return Invalid($"The username \"{username}\" is already taken.", txtUsername);
            if (reader.GetInt32(1) > 0) return Invalid($"The email \"{email}\" is already used by another account.", txtEmail);
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

        bool changePassword = txtPassword.Text.Length > 0;
        string sql = userId.HasValue
            ? $"""
               UPDATE Users SET FullName = @FullName, Username = @Username, Email = @Email, ContactNumber = @Contact,
                      RoleId = @RoleId, BranchId = @BranchId, IsActive = @IsActive{(changePassword ? ", PasswordHash = @PasswordHash" : "")}
               WHERE UserId = @Id
                 AND (@ManagedOnly = 0 OR RoleId IN (SELECT RoleId FROM Roles WHERE RoleName IN ('Manager', 'Staff')))
               """
            : """
              INSERT INTO Users (FullName, Username, Email, ContactNumber, RoleId, BranchId, IsActive, PasswordHash)
              VALUES (@FullName, @Username, @Email, @Contact, @RoleId, @BranchId, @IsActive, @PasswordHash)
              """;
        try
        {
            int branchId = Convert.ToInt32(cboBranch.SelectedValue ?? 0);
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@FullName", SqlDbType.NVarChar, 100).Value = txtFullName.Text.Trim();
            command.Parameters.Add("@Username", SqlDbType.NVarChar, 50).Value = txtUsername.Text.Trim();
            command.Parameters.Add("@Email", SqlDbType.NVarChar, 254).Value = txtEmail.Text.Trim();
            command.Parameters.Add("@Contact", SqlDbType.NVarChar, 11).Value = txtContact.Text.Trim();
            command.Parameters.Add("@RoleId", SqlDbType.Int).Value = Convert.ToInt32(cboRole.SelectedValue);
            command.Parameters.Add("@BranchId", SqlDbType.Int).Value = branchId == 0 ? DBNull.Value : branchId;
            command.Parameters.Add("@IsActive", SqlDbType.Bit).Value = cboStatus.Text == "Active";
            if (changePassword || !userId.HasValue)
                command.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, 512).Value = PasswordHasher.Hash(txtPassword.Text);
            if (userId.HasValue)
            {
                command.Parameters.Add("@Id", SqlDbType.Int).Value = userId.Value;
                command.Parameters.Add("@ManagedOnly", SqlDbType.Bit).Value = AdminRestricted;
            }
            connection.Open();
            if (command.ExecuteNonQuery() == 0)
            {
                MessageBox.Show("Nothing was saved: the user no longer exists.", "Not Saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (editingSelf && Session.CurrentUser is { } me)
            { me.FullName = txtFullName.Text.Trim(); me.Username = txtUsername.Text.Trim(); me.BranchId = branchId == 0 ? null : branchId; }

            DialogResult = DialogResult.OK;   // closes the dialog; the list page reloads
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            MessageBox.Show("That username or email is already used by another account.", "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the user.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
