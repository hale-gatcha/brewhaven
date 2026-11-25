using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.Forms;

/// <summary>
/// Staff page: quick entry of a customer's name, contact and email (Save / Clear), plus the recently recorded
/// customers with Edit / Remove. If the contact or email already exists, offers to update that customer instead.
/// </summary>
public sealed class QuickCustomerForm : Form
{
    private readonly TextBox txtName = new() { Width = 240, MaxLength = 100 };
    private readonly TextBox txtContact = new() { Width = 170, MaxLength = 11, PlaceholderText = "09XXXXXXXXX" };
    private readonly TextBox txtEmail = new() { Width = 240, MaxLength = 254, PlaceholderText = "optional" };
    private readonly RoundedButton btnSave = Ui.PrimaryButton("Save");
    private readonly RoundedButton btnClear = Ui.GhostButton("Clear");
    private readonly DataGridView dgvRecent = Ui.Grid();

    public QuickCustomerForm()
    {
        Text = "Record Customer Information"; BackColor = Ui.Background;
        txtContact.KeyPress += (_, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };

        // Quick-entry card: three fields in a row + Save / Clear
        var entryCard = Ui.Card(DockStyle.Top, 16); entryCard.Height = 130;
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Ui.Surface, WrapContents = true };
        row.Controls.Add(Ui.Field("NAME *", txtName, 240));
        row.Controls.Add(Ui.Field("CONTACT NUMBER *", txtContact, 170));
        row.Controls.Add(Ui.Field("EMAIL", txtEmail, 240));
        var buttons = new FlowLayoutPanel { AutoSize = true, BackColor = Ui.Surface, Margin = new Padding(0, 22, 0, 0) };
        buttons.Controls.Add(btnSave); buttons.Controls.Add(btnClear);
        row.Controls.Add(buttons);
        entryCard.Controls.Add(row); entryCard.Controls.Add(Ui.SectionTitle("Record customer information"));

        var recentCard = Ui.Card(DockStyle.Fill, 12);
        dgvRecent.Dock = DockStyle.Fill;
        recentCard.Controls.Add(dgvRecent); recentCard.Controls.Add(Ui.SectionTitle("Recently recorded customers"));

        Ui.AddRowActions(dgvRecent);
        Controls.Add(recentCard); Controls.Add(Ui.Spacer()); Controls.Add(entryCard);

        btnSave.Click += (_, _) => Save();
        btnClear.Click += (_, _) => ClearFields();
        foreach (var box in new[] { txtName, txtContact, txtEmail })
            box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Save(); } };
        dgvRecent.CellClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || dgvRecent.Rows[e.RowIndex].Cells["CustomerId"].Value is not int id) return;
            string column = dgvRecent.Columns[e.ColumnIndex].Name;
            if (column == Ui.EditColumn) Edit(id);
            else if (column == Ui.RemoveColumn) Remove(e.RowIndex, id);
        };
        dgvRecent.DataBindingComplete += (_, _) =>
        {
            if (dgvRecent.Columns.Contains("CustomerId")) dgvRecent.Columns["CustomerId"]!.Visible = false;
            if (dgvRecent.Columns.Contains("Recorded")) dgvRecent.Columns["Recorded"]!.DefaultCellStyle.Format = "MMM d, yyyy h:mm tt";
            foreach (DataGridViewColumn col in dgvRecent.Columns) if (col.Tag is null) col.HeaderText = col.HeaderText.ToUpperInvariant();
        };
        Load += (_, _) => { FontHelper.ApplyToAll(this); LoadRecent(); txtName.Focus(); };
    }

    private void LoadRecent()
    {
        try
        {
            EnsureSchema();
            dgvRecent.DataSource = Query("""
                SELECT TOP 50 CustomerId, ISNULL(CustomerCode, '') AS Code, FullName AS Name, ISNULL(ContactNumber, '') AS Contact,
                       ISNULL(Email, '') AS Email, CreatedAt AS Recorded
                FROM Customers WHERE IsActive = 1 ORDER BY CreatedAt DESC, CustomerId DESC
                """);
        }
        catch (Exception ex) { MessageBox.Show($"Could not load customers.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ClearFields()
    {
        txtName.Clear(); txtContact.Clear(); txtEmail.Clear(); txtName.Focus();
    }

    private void Save()
    {
        string name = txtName.Text.Trim(), contact = txtContact.Text.Trim(), email = txtEmail.Text.Trim();
        if (CustomerDialog.ValidateBasic(name, contact, email) is string problem)
        {
            MessageBox.Show(problem, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            (problem.StartsWith("Full") ? txtName : problem.Contains("email") ? txtEmail : txtContact).Focus();
            return;
        }
        try
        {
            var conflicts = CustomerRepository.FindConflicts(contact, email.Length > 0 ? email : null, 0);
            if (conflicts.Count > 1)
            {
                MessageBox.Show($"This contact number and email belong to two different customers ({string.Join(" and ", conflicts.Select(c => $"{c.FullName} ({c.CustomerCode})"))}). " +
                                "Check the details, or edit those records from the list.", "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (conflicts.Count == 1)
            {
                var existing = conflicts[0];
                string matched = existing.SameContact && existing.SameEmail ? "contact number and email" : existing.SameContact ? "contact number" : "email";
                if (MessageBox.Show($"A customer with this {matched} already exists: {existing.FullName} ({existing.CustomerCode}).\n\n" +
                                    "Update that customer's name, contact number and email with what you entered?",
                        "Customer Already Exists", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                CustomerRepository.UpdateBasic(existing.CustomerId, name, contact, email.Length > 0 ? email : null);
                MessageBox.Show($"{name} ({existing.CustomerCode}) was updated.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                int id = CustomerRepository.Insert(new CustomerRecord { FullName = name, ContactNumber = contact, Email = email.Length > 0 ? email : null });
                string code = CustomerRepository.Load(id)?.CustomerCode ?? "";
                MessageBox.Show($"{name} was recorded as {code}.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            ClearFields();
            LoadRecent();
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            MessageBox.Show("That contact number or email is already used by another customer.", "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) { MessageBox.Show($"Could not save the customer.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void Edit(int id)
    {
        using var dialog = new CustomerDialog(id);
        if (dialog.ShowDialog(Ui.DialogOwner(this)) == DialogResult.OK) LoadRecent();
    }

    private void Remove(int rowIndex, int id)
    {
        string name = dgvRecent.Rows[rowIndex].Cells["Name"].Value?.ToString() ?? "this customer";
        if (MessageBox.Show($"Remove the customer \"{name}\"?\nThis cannot be undone.", "Confirm Remove",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        try { CustomerRepository.Delete(id); LoadRecent(); }
        catch (SqlException ex) when (ex.Number == 547)
        {
            MessageBox.Show($"\"{name}\" has orders, feedback or redemptions on record, so they can't be removed.", "Cannot Remove", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) { MessageBox.Show($"Could not remove the customer.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
