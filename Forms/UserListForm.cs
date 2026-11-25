using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;

namespace BrewHaven.Forms;

/// <summary>Super Admin page: list of user accounts with Search, Create, and per-row Edit / Remove.</summary>
public sealed class UserListForm : Form
{
    private readonly DataGridView dgvUsers = Ui.Grid();
    private readonly TextBox txtSearch = new() { Width = 300, PlaceholderText = "Search name, username, email, role..." };
    private readonly RoundedButton btnCreate = Ui.PrimaryButton("+  Add User");
    private readonly Label lblCount = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(12, 12, 0, 0) };
    private DataTable table = new();

    public UserListForm()
    {
        Text = "Manage Users"; BackColor = Ui.Background;

        // Toolbar: Search + Create (no input fields on the page itself)
        var toolbarCard = Ui.Card(DockStyle.Top, 12); toolbarCard.Height = 64;
        var toolbar = Ui.Toolbar(DockStyle.Top);
        toolbar.SizeChanged += (_, _) => toolbarCard.Height = toolbar.Height + toolbarCard.Padding.Vertical;
        var searchBox = Ui.Boxed(txtSearch); searchBox.Margin = new Padding(0, 3, 16, 0);
        toolbar.Controls.AddRange([Ui.InlineLabel("SEARCH"), searchBox, btnCreate, lblCount]);
        toolbarCard.Controls.Add(toolbar);

        Ui.AddRowActions(dgvUsers);
        Controls.Add(Ui.GridCard(dgvUsers)); Controls.Add(Ui.Spacer()); Controls.Add(toolbarCard);

        txtSearch.TextChanged += (_, _) => ApplySearch();
        btnCreate.Click += BtnCreate_Click;
        dgvUsers.CellClick += DgvUsers_CellClick;
        dgvUsers.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && !IsActionColumn(e.ColumnIndex)) EditRow(e.RowIndex); };
        dgvUsers.DataBindingComplete += (_, _) => FormatColumns();
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadData(); };
    }

    private void LoadData()
    {
        const string sql = """
            SELECT u.UserId, u.FullName, u.Username, u.Email, u.ContactNumber, r.RoleName AS Role,
                   ISNULL(b.BranchName, '-') AS Branch,
                   CASE WHEN u.IsActive = 1 THEN 'Active' ELSE 'Inactive' END AS Status
            FROM Users u
            INNER JOIN Roles r ON r.RoleId = u.RoleId
            LEFT JOIN Branches b ON b.BranchId = u.BranchId
            WHERE @ManagedOnly = 0 OR r.RoleName IN ('Manager', 'Staff')
            ORDER BY u.FullName
            """;
        try
        {
            DbHelper.EnsureSchema();
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new(sql, connection);
            command.Parameters.Add("@ManagedOnly", SqlDbType.Bit).Value = UserDialog.AdminRestricted;
            connection.Open();
            using SqlDataReader reader = command.ExecuteReader();
            var data = new DataTable();
            data.Load(reader);
            table = data;
            dgvUsers.DataSource = table;
            ApplySearch();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load users.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplySearch()
    {
        try { table.DefaultView.RowFilter = Ui.ContainsFilter(txtSearch.Text, "FullName", "Username", "Email", "ContactNumber", "Role", "Branch", "Status"); }
        catch (Exception) { table.DefaultView.RowFilter = ""; }
        lblCount.Text = $"{table.DefaultView.Count} of {table.Rows.Count} users";
    }

    private void FormatColumns()
    {
        var c = dgvUsers.Columns;
        if (c.Contains("UserId")) c["UserId"]!.Visible = false;
        (string Name, string Header, float Weight)[] layout =
        [
            ("FullName", "FULL NAME", 130), ("Username", "USERNAME", 95), ("Email", "EMAIL", 150),
            ("ContactNumber", "CONTACT", 95), ("Role", "ROLE", 85), ("Branch", "BRANCH", 100), ("Status", "STATUS", 70)
        ];
        foreach (var (name, header, weight) in layout)
            if (c.Contains(name)) { c[name]!.HeaderText = header; c[name]!.FillWeight = weight; }
    }

    private bool IsActionColumn(int columnIndex) =>
        columnIndex >= 0 && dgvUsers.Columns[columnIndex].Name is Ui.EditColumn or Ui.RemoveColumn;

    private void DgvUsers_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        string column = dgvUsers.Columns[e.ColumnIndex].Name;
        if (column == Ui.EditColumn) EditRow(e.RowIndex);
        else if (column == Ui.RemoveColumn) RemoveRow(e.RowIndex);
    }

    private void BtnCreate_Click(object? sender, EventArgs e)
    {
        using var dialog = new UserDialog();
        if (dialog.ShowDialog(DialogOwner()) == DialogResult.OK) LoadData();
    }

    private void EditRow(int rowIndex)
    {
        if (dgvUsers.Rows[rowIndex].Cells["UserId"].Value is not int id) return;
        using var dialog = new UserDialog(id);
        if (dialog.ShowDialog(DialogOwner()) == DialogResult.OK)
        {
            LoadData();
            TopLevelControl?.Refresh(); // the sidebar user card shows the signed-in user's name
        }
    }

    private void RemoveRow(int rowIndex)
    {
        DataGridViewRow row = dgvUsers.Rows[rowIndex];
        if (row.Cells["UserId"].Value is not int id) return;
        if (id == Session.CurrentUser?.UserId)
        {
            MessageBox.Show("You can't remove the account you are signed in with.", "Not Allowed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        string name = row.Cells["FullName"].Value?.ToString() ?? "this user";
        string username = row.Cells["Username"].Value?.ToString() ?? "";
        if (MessageBox.Show($"Remove the user \"{name}\" ({username})?\nThis cannot be undone.", "Confirm Remove",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            // An Admin may only remove Manager / Staff accounts; the WHERE clause enforces it in the database too.
            using SqlCommand command = new("""
                DELETE FROM Users
                WHERE UserId = @Id
                  AND (@ManagedOnly = 0 OR RoleId IN (SELECT RoleId FROM Roles WHERE RoleName IN ('Manager', 'Staff')))
                """, connection);
            command.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            command.Parameters.Add("@ManagedOnly", SqlDbType.Bit).Value = UserDialog.AdminRestricted;
            connection.Open();
            if (command.ExecuteNonQuery() == 0)
                MessageBox.Show("This user could not be removed. You can only remove Manager and Staff accounts.", "Not Removed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadData();
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            MessageBox.Show("This user has orders or feedback linked to their account, or manages a branch, so it can't be removed.\n" +
                            "Edit the user and set Status to Inactive instead. They will no longer be able to log in.",
                "Cannot Remove", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not remove the user.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>This page is hosted inside the dashboard, so dialogs are owned by the dashboard window.</summary>
    private IWin32Window DialogOwner() => TopLevelControl ?? (IWin32Window)this;
}
