using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Drawing;

namespace BrewHaven.Forms;

/// <summary>
/// Pop-up used for both Create (empty, "Save") and Edit (pre-filled, "Update") of a branch.
/// Returns DialogResult.OK only after the database save succeeds.
/// </summary>
public sealed class BranchDialog : Form
{
    private readonly int? branchId;
    private int? loadedManagerId;

    private readonly Label lblTitle = new() { Dock = DockStyle.Top, Height = 44, Font = Ui.SubHeading, ForeColor = Ui.Text, UseMnemonic = false };
    private readonly TextBox txtBranchName = new() { MaxLength = 100 };
    private readonly TextBox txtAddress = new() { MaxLength = 255 };
    private readonly TextBox txtContact = new() { MaxLength = 11, PlaceholderText = "09XXXXXXXXX" };
    private readonly ComboBox cboManager = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly RoundedButton btnSave;
    private readonly RoundedButton btnCancel = Ui.GhostButton("Cancel");

    public BranchDialog(int? branchId = null)
    {
        this.branchId = branchId;
        bool editing = branchId.HasValue;

        Text = editing ? "Edit Branch" : "Create Branch";
        lblTitle.Text = editing ? "Edit branch" : "New branch";
        FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        BackColor = Ui.Background; Font = Ui.Body; ClientSize = new Size(700, 400);
        Padding = new Padding(28, 20, 28, 20);

        cboStatus.Items.AddRange(["Active", "Inactive"]); cboStatus.SelectedIndex = 0;
        txtContact.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };

        const int col = 310, full = col * 2 + 18;
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, BackColor = Ui.Background };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, col + 18));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, col + 18));
        for (int i = 0; i < 4; i++) fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.Controls.Add(Ui.Field("BRANCH NAME *", txtBranchName, col), 0, 0);
        fields.Controls.Add(Ui.Field("CONTACT NUMBER", txtContact, col), 1, 0);
        var addressField = Ui.Field("ADDRESS", txtAddress, full);
        fields.Controls.Add(addressField, 0, 1); fields.SetColumnSpan(addressField, 2);
        fields.Controls.Add(Ui.Field("BRANCH MANAGER", cboManager, col), 0, 2);
        fields.Controls.Add(Ui.Field("STATUS *", cboStatus, col), 1, 2);

        btnSave = Ui.PrimaryButton(editing ? "Update" : "Save");
        btnSave.DialogResult = DialogResult.None;   // only close after a successful save
        btnCancel.DialogResult = DialogResult.Cancel;
        btnSave.Margin = new Padding(8, 0, 0, 0); btnCancel.Margin = new Padding(8, 0, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, BackColor = Ui.Background, Padding = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(btnSave); buttons.Controls.Add(btnCancel);

        Controls.Add(fields); Controls.Add(buttons); Controls.Add(lblTitle);
        AcceptButton = btnSave; CancelButton = btnCancel;

        btnSave.Click += BtnSave_Click;
        Load += BranchDialog_Load;
    }

    private void BranchDialog_Load(object? sender, EventArgs e)
    {
        FontHelper.ApplyToAll(this);
        try { DbHelper.EnsureSchema(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not prepare the Branches table.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            BeginInvoke(() => { DialogResult = DialogResult.Cancel; });
            return;
        }
        bool ok = (!branchId.HasValue || LoadRecord(branchId.Value)) && LoadManagers();
        if (!ok) { BeginInvoke(() => { DialogResult = DialogResult.Cancel; }); return; }
        txtBranchName.Focus();
    }

    private bool LoadRecord(int id)
    {
        const string sql = "SELECT BranchName, Address, ContactNumber, ManagerUserId, IsActive FROM Branches WHERE BranchId = @Id";
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            connection.Open();
            using SqlDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                MessageBox.Show("This branch no longer exists. It may have been removed.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            txtBranchName.Text = reader.GetString(0);
            txtAddress.Text = reader.IsDBNull(1) ? "" : reader.GetString(1);
            txtContact.Text = reader.IsDBNull(2) ? "" : reader.GetString(2);
            loadedManagerId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            cboStatus.SelectedItem = reader.GetBoolean(4) ? "Active" : "Inactive";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load the branch.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool LoadManagers()
    {
        // Active users with the Manager role, plus the branch's current manager even if since deactivated.
        const string sql = """
            SELECT u.UserId, u.FullName + ' (' + u.Username + ')' AS DisplayName
            FROM Users u INNER JOIN Roles r ON r.RoleId = u.RoleId
            WHERE (r.RoleName = 'Manager' AND u.IsActive = 1) OR u.UserId = @Current
            ORDER BY u.FullName
            """;
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@Current", SqlDbType.Int).Value = (object?)loadedManagerId ?? DBNull.Value;
            connection.Open();
            var managers = new DataTable();
            using (SqlDataReader reader = command.ExecuteReader()) managers.Load(reader);
            DataRow none = managers.NewRow(); none["UserId"] = 0; none["DisplayName"] = "(No manager assigned)";
            managers.Rows.InsertAt(none, 0);
            cboManager.DisplayMember = "DisplayName"; cboManager.ValueMember = "UserId"; cboManager.DataSource = managers;
            cboManager.SelectedValue = loadedManagerId ?? 0;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load managers.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        string name = txtBranchName.Text.Trim(), contact = txtContact.Text.Trim();
        if (name.Length == 0) return Invalid("Branch name is required.", txtBranchName);
        if (contact.Length > 0 && !Validation.IsValidPhMobile(contact))
            return Invalid("Contact number must be an 11-digit PH number starting with 09 (e.g. 09171234567), or left blank.", txtContact);
        if (cboStatus.SelectedItem is null) return Invalid("Select a status.", cboStatus);
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new("SELECT COUNT(*) FROM Branches WHERE BranchName = @Name AND BranchId <> @Id", connection);
            command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = name;
            command.Parameters.Add("@Id", SqlDbType.Int).Value = branchId ?? 0;
            connection.Open();
            if (Convert.ToInt32(command.ExecuteScalar()) > 0) return Invalid($"A branch named \"{name}\" already exists.", txtBranchName);
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

        string sql = branchId.HasValue
            ? """
              UPDATE Branches SET BranchName = @Name, Address = @Address, ContactNumber = @Contact,
                     ManagerUserId = @ManagerId, IsActive = @IsActive
              WHERE BranchId = @Id
              """
            : """
              INSERT INTO Branches (BranchName, Address, ContactNumber, ManagerUserId, IsActive)
              VALUES (@Name, @Address, @Contact, @ManagerId, @IsActive)
              """;
        try
        {
            int managerId = Convert.ToInt32(cboManager.SelectedValue ?? 0);
            string address = txtAddress.Text.Trim(), contact = txtContact.Text.Trim();
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@Name", SqlDbType.NVarChar, 100).Value = txtBranchName.Text.Trim();
            command.Parameters.Add("@Address", SqlDbType.NVarChar, 255).Value = address.Length > 0 ? address : DBNull.Value;
            command.Parameters.Add("@Contact", SqlDbType.NVarChar, 11).Value = contact.Length > 0 ? contact : DBNull.Value;
            command.Parameters.Add("@ManagerId", SqlDbType.Int).Value = managerId == 0 ? DBNull.Value : managerId;
            command.Parameters.Add("@IsActive", SqlDbType.Bit).Value = cboStatus.Text == "Active";
            if (branchId.HasValue) command.Parameters.Add("@Id", SqlDbType.Int).Value = branchId.Value;
            connection.Open();
            if (command.ExecuteNonQuery() == 0)
            {
                MessageBox.Show("Nothing was saved: the branch no longer exists.", "Not Saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;   // closes the dialog; the list page reloads
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            MessageBox.Show("A branch with that name already exists.", "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save the branch.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
