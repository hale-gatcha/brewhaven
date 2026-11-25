using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Drawing;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.Forms;

/// <summary>
/// Payment pop-up opened straight from "Place Order" with the cart (nothing is saved before this).
/// Shows the order summary, an optional promo and loyalty-points reward, and Cash / Card / E-Wallet details.
/// "Pay" saves everything in ONE transaction: the order (Paid) and its items, stock deduction + stock movements,
/// points redeemed / earned, and the payment. "Cancel" saves nothing.
/// </summary>
public sealed class PaymentDialog : PopupDialog
{
    private readonly OrderDraft draft;

    private readonly Label lblSummary = new() { AutoSize = true, MaximumSize = new Size(Full, 0), Font = Ui.Body, ForeColor = Ui.Text, Margin = new Padding(0, 0, 0, 8), UseMnemonic = false };
    private readonly DataGridView dgvItems = Ui.Grid();
    private readonly ComboBox cboPromo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboReward = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label lblTotals = new() { AutoSize = true, MaximumSize = new Size(Full, 0), Font = Ui.Body, ForeColor = Ui.Text, Margin = new Padding(0, 2, 0, 2), UseMnemonic = false };
    private readonly Label lblDue = new() { AutoSize = true, Font = Ui.Heading, ForeColor = Ui.Primary, Margin = new Padding(0, 0, 0, 12), UseMnemonic = false };
    private readonly ComboBox cboMethod = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown numTendered = new() { Minimum = 0, Maximum = 1_000_000, DecimalPlaces = 2, ThousandsSeparator = true };
    private readonly TextBox txtChange = new() { ReadOnly = true, TabStop = false };
    private readonly ComboBox cboCardType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtCardLast4 = new() { MaxLength = 4, PlaceholderText = "e.g. 4821" };
    private readonly TextBox txtApproval = new() { MaxLength = 50 };
    private readonly ComboBox cboProvider = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox txtWalletRef = new() { MaxLength = 50 };
    private readonly List<Control> cashFields = [], cardFields = [], walletFields = [];
    private Control? rewardField;

    /// <summary>The saved order's number after DialogResult.OK.</summary>
    public int OrderId { get; private set; }
    /// <summary>Change given (cash) after a successful payment.</summary>
    public decimal ChangeGiven { get; private set; }

    public PaymentDialog(OrderDraft draft) : base("Payment", "Payment", "Pay", 0, 740)
    {
        this.draft = draft;
        ClientSize = new Size(740, Math.Min(860, (Screen.PrimaryScreen?.WorkingArea.Height ?? 900) - 60));
        Fields.AutoScroll = ClientSize.Height < 860;   // scroll only when the screen is too short for the whole dialog

        cboMethod.Items.AddRange(["Cash", "Card", "E-Wallet"]); cboMethod.SelectedIndex = 0;
        cboCardType.Items.AddRange(["Visa", "Mastercard", "JCB", "American Express", "BancNet Debit"]); cboCardType.SelectedIndex = 0;
        cboProvider.Items.AddRange(["GCash", "Maya"]); cboProvider.SelectedIndex = 0;
        DigitsOnly(txtCardLast4);

        // Order summary (read-only)
        AddRow(lblSummary);
        var itemsHost = new Panel { Size = new Size(Full, 170), BackColor = Ui.Surface, Margin = new Padding(0, 0, 0, 10) };
        dgvItems.Dock = DockStyle.Fill; itemsHost.Controls.Add(dgvItems);
        AddRow(itemsHost);
        AddField("APPLY PROMO (OPTIONAL)", cboPromo, 0);
        rewardField = AddField("REDEEM LOYALTY POINTS (OPTIONAL)", cboReward, 1);
        AddRow(lblTotals);
        AddRow(lblDue);

        // Payment method and its details
        AddField("PAYMENT METHOD *", cboMethod, 0);
        cashFields.Add(AddField("AMOUNT TENDERED (PHP) *", numTendered, 0));
        cashFields.Add(AddField("CHANGE (PHP)", txtChange, 1));
        cardFields.Add(AddField("CARD TYPE *", cboCardType, 0));
        cardFields.Add(AddField("LAST 4 DIGITS *", txtCardLast4, 1));
        cardFields.Add(AddField("APPROVAL REFERENCE NO. *", txtApproval, 0, span: true));
        walletFields.Add(AddField("PROVIDER *", cboProvider, 0));
        walletFields.Add(AddField("REFERENCE NUMBER *", txtWalletRef, 1));

        dgvItems.DataBindingComplete += (_, _) => FormatItems();
        cboMethod.SelectedIndexChanged += (_, _) => ShowMethodFields();
        cboPromo.SelectedIndexChanged += (_, _) => Recalculate();
        cboReward.SelectedIndexChanged += (_, _) => Recalculate();
        numTendered.ValueChanged += (_, _) => Recalculate();
        numTendered.KeyUp += (_, _) => Recalculate();
        BtnSave.Click += BtnPay_Click;
        Load += (_, _) =>
        {
            if (!LoadOptions()) { CancelAfterLoad(); return; }
            ShowMethodFields();
            numTendered.Value = Math.Min(numTendered.Maximum, Math.Ceiling(AmountDue()));
        };
        Shown += (_, _) => { numTendered.Focus(); numTendered.Select(0, numTendered.Text.Length); };
    }

    private bool LoadOptions()
    {
        try
        {
            int items = draft.Lines.Sum(l => l.Qty);
            lblSummary.Text =
                $"{(draft.OrderType == "Dine-In" ? $"Dine-in · Table {draft.TableNumber}" : "Takeout")}  ·  {items} item(s)  ·  {draft.BranchName}\n" +
                $"Customer: {draft.CustomerName}{(draft.CustomerId is null ? "" : $"  ·  {draft.CustomerPoints:N0} points")}";

            var table = new DataTable();
            table.Columns.Add("Item"); table.Columns.Add("Size"); table.Columns.Add("Qty", typeof(int));
            table.Columns.Add("Price", typeof(decimal)); table.Columns.Add("Total", typeof(decimal));
            foreach (OrderDraftLine l in draft.Lines) table.Rows.Add(l.Name, l.Size ?? "-", l.Qty, l.UnitPrice, l.LineTotal);
            dgvItems.DataSource = table;

            // Promos: active, in date, this branch (or all), no points needed.
            DataTable promos = Query("""
                SELECT PromotionId, PromotionName + ' (' + CASE WHEN DiscountType = 'Fixed' THEN 'PHP ' + CONVERT(nvarchar(20), DiscountValue)
                       ELSE CONVERT(nvarchar(20), CAST(DiscountValue AS decimal(5,2))) + '%' END + ' off)' AS Name, DiscountType, DiscountValue
                FROM Promotions
                WHERE IsActive = 1 AND CAST(GETDATE() AS date) BETWEEN StartDate AND EndDate
                  AND (BranchId IS NULL OR BranchId = @BranchId) AND ISNULL(PointsRequired, 0) = 0
                ORDER BY PromotionName
                """, P("@BranchId", SqlDbType.Int, draft.BranchId));
            AddNoneRow(promos, "PromotionId", "Name", "No promo");
            cboPromo.DisplayMember = "Name"; cboPromo.ValueMember = "PromotionId"; cboPromo.DataSource = promos;
            cboPromo.SelectedValue = draft.PromotionId ?? 0;

            // Rewards the customer can afford (members only).
            DataTable rewards = Query("""
                SELECT RewardId, RewardName + ' (' + CAST(PointsRequired AS nvarchar(10)) + ' pts = PHP ' + CONVERT(nvarchar(20), DiscountValue) + ' off)' AS Name,
                       PointsRequired, DiscountValue
                FROM Rewards WHERE IsActive = 1 AND PointsRequired <= @Points ORDER BY PointsRequired
                """, P("@Points", SqlDbType.Int, draft.CustomerId is null ? -1 : draft.CustomerPoints));
            AddNoneRow(rewards, "RewardId", "Name", draft.CustomerId is null ? "Walk-in guest: no points" : "Don't redeem points");
            cboReward.DisplayMember = "Name"; cboReward.ValueMember = "RewardId"; cboReward.DataSource = rewards;
            rewardField!.Enabled = draft.CustomerId is not null && rewards.Rows.Count > 1;

            Recalculate();
            return true;
        }
        catch (Exception ex) { ShowError("Could not open the payment.", ex); return false; }
    }

    private void FormatItems()
    {
        foreach (string money in new[] { "Price", "Total" })
            if (dgvItems.Columns.Contains(money))
            {
                dgvItems.Columns[money]!.DefaultCellStyle.Format = "N2";
                dgvItems.Columns[money]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
        if (dgvItems.Columns.Contains("Qty")) dgvItems.Columns["Qty"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        if (dgvItems.Columns.Contains("Item")) dgvItems.Columns["Item"]!.FillWeight = 200;
    }

    private static void AddNoneRow(DataTable table, string idColumn, string nameColumn, string text)
    {
        // The other columns (DiscountType, PointsRequired, ...) come from NOT NULL database columns; the "none" row leaves them empty.
        foreach (DataColumn column in table.Columns) column.AllowDBNull = true;
        DataRow none = table.NewRow(); none[idColumn] = 0; none[nameColumn] = text;
        table.Rows.InsertAt(none, 0);
    }

    private static DataRowView? Chosen(ComboBox combo) =>
        combo.SelectedItem is DataRowView r && Convert.ToInt32(r[0]) != 0 ? r : null;

    private decimal PromoDiscount() =>
        Chosen(cboPromo) is { } p ? OrderRepository.PromoDiscount((string)p["DiscountType"], Convert.ToDecimal(p["DiscountValue"]), draft.Subtotal) : 0m;

    private decimal RewardDiscount() => Chosen(cboReward) is { } r ? Convert.ToDecimal(r["DiscountValue"]) : 0m;

    private decimal Discount() => Math.Min(draft.Subtotal, PromoDiscount() + RewardDiscount());

    private decimal AmountDue() => draft.Subtotal - Discount();

    private void Recalculate()
    {
        decimal due = AmountDue();
        lblTotals.Text = $"Subtotal:  PHP {draft.Subtotal:N2}     Discount:  PHP {Discount():N2}";
        lblDue.Text = $"Total:  PHP {due:N2}";
        decimal tendered = numTendered.Value;
        txtChange.Text = tendered >= due ? (tendered - due).ToString("N2") : $"short by {due - tendered:N2}";
    }

    private void ShowMethodFields()
    {
        string method = cboMethod.Text;
        foreach (Control c in cashFields) c.Visible = method == "Cash";
        foreach (Control c in cardFields) c.Visible = method == "Card";
        foreach (Control c in walletFields) c.Visible = method == "E-Wallet";
    }

    private bool ValidateInputs()
    {
        decimal due = AmountDue();
        switch (cboMethod.Text)
        {
            case "Cash":
                if (numTendered.Value < due) return Invalid($"Amount tendered must be at least PHP {due:N2}.", numTendered);
                break;
            case "Card":
                if (cboCardType.SelectedItem is null) return Invalid("Select the card type.", cboCardType);
                if (txtCardLast4.Text.Length != 4 || !txtCardLast4.Text.All(char.IsDigit)) return Invalid("Enter the last 4 digits of the card.", txtCardLast4);
                if (txtApproval.Text.Trim().Length < 4) return Invalid("Enter the approval reference number from the card terminal.", txtApproval);
                break;
            case "E-Wallet":
                if (cboProvider.SelectedItem is null) return Invalid("Select GCash or Maya.", cboProvider);
                string reference = txtWalletRef.Text.Trim();
                if (reference.Length < 6 || !reference.All(char.IsLetterOrDigit))
                    return Invalid("Enter the e-wallet reference number (letters and digits, at least 6).", txtWalletRef);
                break;
            default:
                return Invalid("Select a payment method.", cboMethod);
        }
        return true;
    }

    private void BtnPay_Click(object? sender, EventArgs e)
    {
        if (!ValidateInputs()) return;   // dialog stays open, nothing saved
        string method = cboMethod.Text;
        int userId = Session.CurrentUser?.UserId ?? 0;
        try
        {
            using SqlConnection connection = CreateConnection();
            connection.Open();
            using SqlTransaction tx = connection.BeginTransaction();

            // 1. Items still available and in stock (re-checked inside the transaction).
            OrderRepository.CheckAvailability(tx, draft.BranchId, draft.Lines);

            // 2. Promo (re-checked: active, in date, this branch, no points needed).
            decimal subtotal = draft.Subtotal, discount = 0m;
            int? promotionId = null;
            if (Chosen(cboPromo) is { } promoRow)
            {
                int promoId = Convert.ToInt32(promoRow["PromotionId"]);
                DataTable promo = Query(tx, """
                    SELECT DiscountType, DiscountValue FROM Promotions
                    WHERE PromotionId = @Id AND IsActive = 1 AND CAST(GETDATE() AS date) BETWEEN StartDate AND EndDate
                      AND (BranchId IS NULL OR BranchId = @BranchId) AND ISNULL(PointsRequired, 0) = 0
                    """, P("@Id", SqlDbType.Int, promoId), P("@BranchId", SqlDbType.Int, draft.BranchId));
                if (promo.Rows.Count == 0) throw new InvalidOperationException("The selected promo is no longer active or has expired. Choose another.");
                discount += OrderRepository.PromoDiscount((string)promo.Rows[0][0], (decimal)promo.Rows[0][1], subtotal);
                promotionId = promoId;
            }

            // 3. Reward (re-checked: active; points deducted only if the customer still has enough).
            int? rewardId = null; int rewardPoints = 0;
            if (draft.CustomerId is int cid && Chosen(cboReward) is { } rewardRow)
            {
                rewardId = Convert.ToInt32(rewardRow["RewardId"]);
                DataTable reward = Query(tx, "SELECT PointsRequired, DiscountValue FROM Rewards WHERE RewardId = @Id AND IsActive = 1", P("@Id", SqlDbType.Int, rewardId));
                if (reward.Rows.Count == 0) throw new InvalidOperationException("The selected reward is no longer available.");
                rewardPoints = (int)reward.Rows[0][0];
                if (Execute(tx, "UPDATE Customers SET LoyaltyPoints = LoyaltyPoints - @Pts WHERE CustomerId = @Id AND LoyaltyPoints >= @Pts",
                        P("@Pts", SqlDbType.Int, rewardPoints), P("@Id", SqlDbType.Int, cid)) == 0)
                    throw new InvalidOperationException("The customer doesn't have enough points for that reward.");
                discount += (decimal)reward.Rows[0][1];
            }

            discount = Math.Min(discount, subtotal);
            decimal total = subtotal - discount;
            decimal paid = method == "Cash" ? numTendered.Value : total;
            if (paid < total) throw new InvalidOperationException($"Amount tendered must be at least PHP {total:N2}.");
            decimal change = paid - total;
            int earned = draft.CustomerId is null ? 0 : OrderRepository.PointsFor(total);

            // 4. The order (Paid) and its items.
            int orderId = Convert.ToInt32(Scalar(tx, """
                INSERT INTO Orders (BranchId, CustomerId, StaffUserId, OrderType, Status, Subtotal, Discount, Total, PointsEarned, TableNumber, PromotionId, OrderDate)
                OUTPUT INSERTED.OrderId
                VALUES (@BranchId, @CustomerId, @UserId, @Type, 'Paid', @Subtotal, @Discount, @Total, @Earned, @Table, @PromoId, SYSDATETIME())
                """,
                P("@BranchId", SqlDbType.Int, draft.BranchId), P("@CustomerId", SqlDbType.Int, draft.CustomerId),
                P("@UserId", SqlDbType.Int, userId), P("@Type", SqlDbType.NVarChar, draft.OrderType, 10),
                P("@Subtotal", SqlDbType.Decimal, subtotal), P("@Discount", SqlDbType.Decimal, discount), P("@Total", SqlDbType.Decimal, total),
                P("@Earned", SqlDbType.Int, earned), P("@Table", SqlDbType.Int, draft.TableNumber), P("@PromoId", SqlDbType.Int, promotionId)));
            foreach (OrderDraftLine line in draft.Lines)
                Execute(tx, "INSERT INTO OrderItems (OrderId, MenuItemId, Quantity, UnitPrice, Size) VALUES (@OrderId, @MenuItemId, @Qty, @Price, @Size)",
                    P("@OrderId", SqlDbType.Int, orderId), P("@MenuItemId", SqlDbType.Int, line.MenuItemId), P("@Qty", SqlDbType.Int, line.Qty),
                    P("@Price", SqlDbType.Decimal, line.UnitPrice), P("@Size", SqlDbType.NVarChar, line.Size, 10));

            // 5. Stock out for the items sold (+ StockMovements rows).
            OrderRepository.DeductStock(tx, orderId, draft.BranchId, userId);

            // 6. Points: record the redemption, add the points earned.
            if (draft.CustomerId is int customer)
            {
                if (rewardId is not null)
                    Execute(tx, """
                        INSERT INTO RewardRedemptions (CustomerId, RewardId, PointsRedeemed, OrderId, RedemptionCode, ProcessedByUserId)
                        VALUES (@CustomerId, @RewardId, @Pts, @OrderId, @Code, @UserId)
                        """, P("@CustomerId", SqlDbType.Int, customer), P("@RewardId", SqlDbType.Int, rewardId), P("@Pts", SqlDbType.Int, rewardPoints),
                        P("@OrderId", SqlDbType.Int, orderId), P("@Code", SqlDbType.NVarChar, OrderRepository.NewCode("RD"), 20), P("@UserId", SqlDbType.Int, userId));
                if (earned > 0)
                    Execute(tx, "UPDATE Customers SET LoyaltyPoints = LoyaltyPoints + @Pts WHERE CustomerId = @Id",
                        P("@Pts", SqlDbType.Int, earned), P("@Id", SqlDbType.Int, customer));
            }

            // 7. The payment.
            Execute(tx, """
                INSERT INTO Payments (OrderId, Method, AmountPaid, ChangeAmount, CardType, CardLast4, Provider, ReferenceNumber, ReceivedByUserId, PaidAt)
                VALUES (@OrderId, @Method, @Paid, @Change, @CardType, @Last4, @Provider, @Ref, @UserId, SYSDATETIME())
                """,
                P("@OrderId", SqlDbType.Int, orderId), P("@Method", SqlDbType.NVarChar, method, 10),
                P("@Paid", SqlDbType.Decimal, paid), P("@Change", SqlDbType.Decimal, change),
                P("@CardType", SqlDbType.NVarChar, method == "Card" ? cboCardType.Text : null, 20),
                P("@Last4", SqlDbType.Char, method == "Card" ? txtCardLast4.Text : null, 4),
                P("@Provider", SqlDbType.NVarChar, method == "E-Wallet" ? cboProvider.Text : null, 20),
                P("@Ref", SqlDbType.NVarChar, method switch { "Card" => txtApproval.Text.Trim(), "E-Wallet" => txtWalletRef.Text.Trim(), _ => null }, 50),
                P("@UserId", SqlDbType.Int, userId));

            tx.Commit();
            OrderId = orderId;
            ChangeGiven = change;
            DialogResult = DialogResult.OK;
        }
        catch (InvalidOperationException ex)
        {
            // A business rule failed (stock, promo, points): nothing was saved; the dialog stays open.
            MessageBox.Show(ex.Message + "\n\nNothing was saved.", "Payment Not Completed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The payment couldn't be completed, so nothing was saved and nothing was charged. Please try again.\n\nDetails: {ex.Message}",
                "Payment Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
