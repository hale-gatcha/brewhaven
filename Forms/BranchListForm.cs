using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;

namespace BrewHaven.Forms;

/// <summary>Admin page: branches with Search + Create and per-row Edit / Remove.</summary>
public sealed class BranchListForm : Form
{
    private readonly DataGridView dgvBranches = Ui.Grid();
    private readonly TextBox txtSearch = new() { Width = 300, PlaceholderText = "Search branch, address, manager..." };
    private readonly RoundedButton btnCreate = Ui.PrimaryButton("+  Create");
    private readonly Label lblCount = new() { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(12, 12, 0, 0) };
    private DataTable table = new();

    public BranchListForm()
    {
        Text = "Manage Branches"; BackColor = Ui.Background;

        var toolbarCard = Ui.Card(DockStyle.Top, 12); toolbarCard.Height = 64;
        var toolbar = Ui.Toolbar(DockStyle.Top);
        toolbar.SizeChanged += (_, _) => toolbarCard.Height = toolbar.Height + toolbarCard.Padding.Vertical;
        var searchBox = Ui.Boxed(txtSearch); searchBox.Margin = new Padding(0, 3, 16, 0);
        toolbar.Controls.AddRange([Ui.InlineLabel("SEARCH"), searchBox, btnCreate, lblCount]);
        toolbarCard.Controls.Add(toolbar);

        Ui.AddRowActions(dgvBranches);
        Controls.Add(Ui.GridCard(dgvBranches)); Controls.Add(Ui.Spacer()); Controls.Add(toolbarCard);

        txtSearch.TextChanged += (_, _) => ApplySearch();
        btnCreate.Click += BtnCreate_Click;
        dgvBranches.CellClick += DgvBranches_CellClick;
        dgvBranches.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && !IsActionColumn(e.ColumnIndex)) EditRow(e.RowIndex); };
        dgvBranches.DataBindingComplete += (_, _) => FormatColumns();
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadData(); };
    }

    private void LoadData()
    {
        const string sql = """
            SELECT b.BranchId, b.BranchName, ISNULL(b.Address, '') AS Address, ISNULL(b.ContactNumber, '') AS ContactNumber,
                   ISNULL(m.FullName, '-') AS Manager,
                   (SELECT COUNT(*) FROM Users u WHERE u.BranchId = b.BranchId) AS Users,
                   CASE WHEN b.IsActive = 1 THEN 'Active' ELSE 'Inactive' END AS Status
            FROM Branches b
            LEFT JOIN Users m ON m.UserId = b.ManagerUserId
            ORDER BY b.BranchName
            """;
        try
        {
            DbHelper.EnsureSchema();   // adds Branches.ManagerUserId on older databases
            using SqlConnection connection = DbHelper.CreateConnection();
            using SqlCommand command = new(sql, connection);
            connection.Open();
            using SqlDataReader reader = command.ExecuteReader();
            var data = new DataTable();
            data.Load(reader);
            table = data;
            dgvBranches.DataSource = table;
            ApplySearch();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load branches.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplySearch()
    {
        try { table.DefaultView.RowFilter = Ui.ContainsFilter(txtSearch.Text, "BranchName", "Address", "ContactNumber", "Manager", "Status"); }
        catch (Exception) { table.DefaultView.RowFilter = ""; }
        lblCount.Text = $"{table.DefaultView.Count} of {table.Rows.Count} branches";
    }

    private void FormatColumns()
    {
        var c = dgvBranches.Columns;
        if (c.Contains("BranchId")) c["BranchId"]!.Visible = false;
        (string Name, string Header, float Weight)[] layout =
        [
            ("BranchName", "BRANCH", 120), ("Address", "ADDRESS", 190), ("ContactNumber", "CONTACT", 95),
            ("Manager", "BRANCH MANAGER", 120), ("Users", "USERS", 55), ("Status", "STATUS", 70)
        ];
        foreach (var (name, header, weight) in layout)
            if (c.Contains(name)) { c[name]!.HeaderText = header; c[name]!.FillWeight = weight; }
        if (c.Contains("Users")) c["Users"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
    }

    private bool IsActionColumn(int columnIndex) =>
        columnIndex >= 0 && dgvBranches.Columns[columnIndex].Name is Ui.EditColumn or Ui.RemoveColumn;

    private void DgvBranches_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
        string column = dgvBranches.Columns[e.ColumnIndex].Name;
        if (column == Ui.EditColumn) EditRow(e.RowIndex);
        else if (column == Ui.RemoveColumn) RemoveRow(e.RowIndex);
    }

    private void BtnCreate_Click(object? sender, EventArgs e)
    {
        using var dialog = new BranchDialog();
        if (dialog.ShowDialog(DialogOwner()) == DialogResult.OK) LoadData();
    }

    private void EditRow(int rowIndex)
    {
        if (dgvBranches.Rows[rowIndex].Cells["BranchId"].Value is not int id) return;
        using var dialog = new BranchDialog(id);
        if (dialog.ShowDialog(DialogOwner()) == DialogResult.OK) LoadData();
    }

    private void RemoveRow(int rowIndex)
    {
        DataGridViewRow row = dgvBranches.Rows[rowIndex];
        if (row.Cells["BranchId"].Value is not int id) return;
        string name = row.Cells["BranchName"].Value?.ToString() ?? "this branch";
        try
        {
            using SqlConnection connection = DbHelper.CreateConnection();
            connection.Open();

            // Block the delete when anything still points at this branch.
            using (SqlCommand check = new("""
                SELECT (SELECT COUNT(*) FROM Users WHERE BranchId = @Id),
                       (SELECT COUNT(*) FROM Orders WHERE BranchId = @Id),
                       (SELECT COUNT(*) FROM Inventory WHERE BranchId = @Id),
                       (SELECT COUNT(*) FROM Feedback WHERE BranchId = @Id)
                """, connection))
            {
                check.Parameters.Add("@Id", SqlDbType.Int).Value = id;
                using SqlDataReader reader = check.ExecuteReader();
                reader.Read();
                var linked = new List<string>();
                if (reader.GetInt32(0) > 0) linked.Add($"{reader.GetInt32(0)} user(s)");
                if (reader.GetInt32(1) > 0) linked.Add($"{reader.GetInt32(1)} order(s)");
                if (reader.GetInt32(2) > 0) linked.Add($"{reader.GetInt32(2)} inventory item(s)");
                if (reader.GetInt32(3) > 0) linked.Add($"{reader.GetInt32(3)} feedback entr(ies)");
                if (linked.Count > 0)
                {
                    MessageBox.Show($"\"{name}\" can't be removed because it still has {string.Join(", ", linked)} linked to it.\n\n" +
                                    "Edit the branch and set Status to Inactive instead.",
                        "Cannot Remove Branch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            if (MessageBox.Show($"Remove the branch \"{name}\"?\nThis cannot be undone.", "Confirm Remove",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            using SqlCommand delete = new("DELETE FROM Branches WHERE BranchId = @Id", connection);
            delete.Parameters.Add("@Id", SqlDbType.Int).Value = id;
            delete.ExecuteNonQuery();
            LoadData();
        }
        catch (SqlException ex) when (ex.Number == 547)
        {
            MessageBox.Show($"\"{name}\" is still linked to other records, so it can't be removed. Set its Status to Inactive instead.",
                "Cannot Remove Branch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not remove the branch.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>This page is hosted inside the dashboard, so dialogs are owned by the dashboard window.</summary>
    private IWin32Window DialogOwner() => TopLevelControl ?? (IWin32Window)this;
}
