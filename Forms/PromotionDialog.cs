using System.Data;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.Forms;

/// <summary>Pop-up for creating ("Save") and editing ("Update") a promotion.</summary>
public sealed class PromotionDialog : PopupDialog
{
    private readonly int? promotionId;
    private int? loadedBranchId;

    private readonly TextBox txtName = new() { MaxLength = 100 };
    private readonly ComboBox cboStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtDescription = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, MaxLength = 500 };
    private readonly ComboBox cboDiscountType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown numDiscountValue = new() { Minimum = 0, Maximum = 100_000, DecimalPlaces = 2, ThousandsSeparator = true, Value = 10 };
    private readonly NumericUpDown numPoints = new() { Minimum = 0, Maximum = 1_000_000, ThousandsSeparator = true };
    private readonly ComboBox cboBranch = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker dtpStart = new();
    private readonly DateTimePicker dtpEnd = new();

    public PromotionDialog(int? promotionId = null)
        : base(promotionId is null ? "Create Promotion" : "Edit Promotion", promotionId is null ? "New promotion" : "Edit promotion",
               promotionId is null ? "Save" : "Update", 560)
    {
        this.promotionId = promotionId;
        cboStatus.Items.AddRange(["Active", "Inactive"]); cboStatus.SelectedIndex = 0;
        cboDiscountType.Items.AddRange(["Percentage", "Fixed Amount"]); cboDiscountType.SelectedIndex = 0;
        dtpStart.Value = DateTime.Today; dtpEnd.Value = DateTime.Today.AddMonths(1);

        AddField("PROMO NAME *", txtName, 0);
        AddField("STATUS *", cboStatus, 1);
        AddField("DISCOUNT TYPE *", cboDiscountType, 0);
        AddField("DISCOUNT VALUE * (% OR PHP)", numDiscountValue, 1);
        AddField("POINTS REQUIRED (0 = NONE)", numPoints, 0);
        AddField("APPLICABLE BRANCH *", cboBranch, 1);
        AddField("START DATE *", dtpStart, 0);
        AddField("END DATE *", dtpEnd, 1);
        AddField("DESCRIPTION", txtDescription, 0, span: true, multilineHeight: 80);
        AddRow(Note("Promos with 0 points are applied to orders at Place Order / Payment. Promos that cost points are redeemed with loyalty points."));

        BtnSave.Click += BtnSave_Click;
        Load += (_, _) =>
        {
            bool ok = (promotionId is null || LoadRecord(promotionId.Value)) && LoadBranches();
            if (!ok) CancelAfterLoad(); else txtName.Focus();
        };
    }

    private bool LoadRecord(int id)
    {
        try
        {
            DataTable t = Query("""
                SELECT PromotionName, Description, DiscountType, ISNULL(DiscountValue, DiscountPercent), ISNULL(PointsRequired, 0),
                       StartDate, EndDate, BranchId, IsActive
                FROM Promotions WHERE PromotionId = @Id
                """, P("@Id", SqlDbType.Int, id));
            if (t.Rows.Count == 0)
            {
                MessageBox.Show("This promotion no longer exists.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            DataRow r = t.Rows[0];
            txtName.Text = (string)r[0];
            txtDescription.Text = r[1] as string ?? "";
            cboDiscountType.SelectedIndex = (string)r[2] == "Fixed" ? 1 : 0;
            numDiscountValue.Value = Math.Clamp((decimal)r[3], numDiscountValue.Minimum, numDiscountValue.Maximum);
            numPoints.Value = Math.Clamp((int)r[4], 0, (int)numPoints.Maximum);
            dtpStart.Value = (DateTime)r[5]; dtpEnd.Value = (DateTime)r[6];
            loadedBranchId = r[7] as int?;
            cboStatus.SelectedIndex = (bool)r[8] ? 0 : 1;
            return true;
        }
        catch (Exception ex) { ShowError("Could not load the promotion.", ex); return false; }
    }

    private bool LoadBranches()
    {
        try
        {
            DataTable branches = Query("SELECT BranchId, BranchName FROM Branches WHERE IsActive = 1 OR BranchId = @Current ORDER BY BranchName",
                P("@Current", SqlDbType.Int, loadedBranchId));
            DataRow all = branches.NewRow(); all["BranchId"] = 0; all["BranchName"] = "All branches";
            branches.Rows.InsertAt(all, 0);
            cboBranch.DisplayMember = "BranchName"; cboBranch.ValueMember = "BranchId"; cboBranch.DataSource = branches;
            cboBranch.SelectedValue = loadedBranchId ?? 0;
            return true;
        }
        catch (Exception ex) { ShowError("Could not load branches.", ex); return false; }
    }

    private bool ValidateInputs()
    {
        string name = txtName.Text.Trim();
        bool percentage = cboDiscountType.SelectedIndex == 0;
        if (name.Length == 0) return Invalid("Promo name is required.", txtName);
        if (numDiscountValue.Value <= 0) return Invalid("Discount value must be more than 0.", numDiscountValue);
        if (percentage && numDiscountValue.Value > 100) return Invalid("A percentage discount can't be more than 100%.", numDiscountValue);
        if (dtpEnd.Value.Date < dtpStart.Value.Date) return Invalid("End date must be on or after the start date.", dtpEnd);
        if (cboBranch.SelectedValue is null) return Invalid("Select the applicable branch.", cboBranch);
        try
        {
            int duplicates = Convert.ToInt32(Scalar("SELECT COUNT(*) FROM Promotions WHERE PromotionName = @Name AND PromotionId <> @Id",
                P("@Name", SqlDbType.NVarChar, name, 100), P("@Id", SqlDbType.Int, promotionId ?? 0)));
            if (duplicates > 0) return Invalid($"A promotion named \"{name}\" already exists.", txtName);
        }
        catch (Exception ex) { ShowError("Could not check for duplicates.", ex); return false; }
        return true;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (!ValidateInputs()) return;   // dialog stays open
        bool percentage = cboDiscountType.SelectedIndex == 0;
        int branchId = Convert.ToInt32(cboBranch.SelectedValue);
        string description = txtDescription.Text.Trim();
        // DiscountPercent is kept filled for older screens: the % for percentage promos, 0 for fixed-amount ones.
        string sql = promotionId is null
            ? """
              INSERT INTO Promotions (PromotionName, Description, DiscountPercent, DiscountType, DiscountValue, PointsRequired, StartDate, EndDate, BranchId, IsActive)
              VALUES (@Name, @Description, @Percent, @Type, @Value, @Points, @Start, @End, @BranchId, @IsActive)
              """
            : """
              UPDATE Promotions SET PromotionName = @Name, Description = @Description, DiscountPercent = @Percent, DiscountType = @Type,
                     DiscountValue = @Value, PointsRequired = @Points, StartDate = @Start, EndDate = @End, BranchId = @BranchId, IsActive = @IsActive
              WHERE PromotionId = @Id
              """;
        try
        {
            int rows = Execute(sql,
                P("@Name", SqlDbType.NVarChar, txtName.Text.Trim(), 100),
                P("@Description", SqlDbType.NVarChar, description.Length > 0 ? description : null, 500),
                P("@Percent", SqlDbType.Decimal, percentage ? numDiscountValue.Value : 0m),
                P("@Type", SqlDbType.NVarChar, percentage ? "Percentage" : "Fixed", 10),
                P("@Value", SqlDbType.Decimal, numDiscountValue.Value),
                P("@Points", SqlDbType.Int, numPoints.Value > 0 ? (int)numPoints.Value : null),
                P("@Start", SqlDbType.Date, dtpStart.Value.Date),
                P("@End", SqlDbType.Date, dtpEnd.Value.Date),
                P("@BranchId", SqlDbType.Int, branchId == 0 ? null : branchId),
                P("@IsActive", SqlDbType.Bit, cboStatus.SelectedIndex == 0),
                P("@Id", SqlDbType.Int, promotionId ?? 0));
            if (rows == 0) { MessageBox.Show("Nothing was saved: the promotion no longer exists.", "Not Saved", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) { ShowError("Could not save the promotion.", ex); }
    }
}
