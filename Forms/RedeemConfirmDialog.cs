using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Drawing;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.Forms;

/// <summary>
/// Confirmation pop-up for redeeming a reward or points promo: shows the points to deduct and the remaining balance.
/// "Redeem" re-checks everything in a transaction, deducts the points, saves a RewardRedemptions row and
/// exposes the generated <see cref="RedemptionCode"/>.
/// </summary>
public sealed class RedeemConfirmDialog : PopupDialog
{
    private readonly int customerId;
    private readonly string kind;   // "Reward" or "Promo"
    private readonly int itemId;
    private readonly string itemName;
    private readonly int points;

    public string RedemptionCode { get; private set; } = "";
    public int RemainingPoints { get; private set; }

    public RedeemConfirmDialog(int customerId, string customerName, int balance, string kind, int itemId, string itemName, string benefit, int points)
        : base("Confirm Redemption", "Redeem loyalty points", "Redeem", 330)
    {
        this.customerId = customerId; this.kind = kind; this.itemId = itemId; this.itemName = itemName; this.points = points;
        int remaining = balance - points;
        var summary = new Label
        {
            AutoSize = true, MaximumSize = new Size(Full, 0), Font = Ui.Body, ForeColor = Ui.Text, Margin = new Padding(0, 0, 0, 12), UseMnemonic = false,
            Text = $"Customer:  {customerName}\n" +
                   $"{kind}:  {itemName}  ({benefit})\n\n" +
                   $"Current balance:  {balance:N0} points\n" +
                   $"Points to deduct:  {points:N0} points\n" +
                   $"Remaining balance:  {remaining:N0} points"
        };
        AddRow(summary);
        if (remaining < 0)
        {
            AddRow(Note($"Not enough points: {customerName} needs {-remaining:N0} more."));
            BtnSave.Enabled = false;
        }
        BtnSave.Click += BtnRedeem_Click;
    }

    private void BtnRedeem_Click(object? sender, EventArgs e)
    {
        try
        {
            using SqlConnection connection = CreateConnection();
            connection.Open();
            using SqlTransaction tx = connection.BeginTransaction();

            // Re-check the reward / promo inside the transaction (it may have changed since the list was loaded).
            if (kind == "Promo")
            {
                DataTable promo = Query(tx, """
                    SELECT IsActive, StartDate, EndDate, ISNULL(PointsRequired, 0) FROM Promotions WHERE PromotionId = @Id
                    """, P("@Id", SqlDbType.Int, itemId));
                if (promo.Rows.Count == 0) { Fail("This promo no longer exists."); return; }
                DataRow p = promo.Rows[0];
                if (!(bool)p[0]) { Fail("This promo is inactive."); return; }
                if (DateTime.Today < (DateTime)p[1]) { Fail($"This promo starts on {(DateTime)p[1]:MMM d, yyyy}."); return; }
                if (DateTime.Today > (DateTime)p[2]) { Fail($"This promo expired on {(DateTime)p[2]:MMM d, yyyy}."); return; }
                if ((int)p[3] != points) { Fail("The points required for this promo changed. Reload the list and try again."); return; }
            }
            else
            {
                DataTable reward = Query(tx, "SELECT IsActive, PointsRequired FROM Rewards WHERE RewardId = @Id", P("@Id", SqlDbType.Int, itemId));
                if (reward.Rows.Count == 0) { Fail("This reward no longer exists."); return; }
                if (!(bool)reward.Rows[0][0]) { Fail("This reward is inactive."); return; }
                if ((int)reward.Rows[0][1] != points) { Fail("The points required for this reward changed. Reload the list and try again."); return; }
            }

            // Deduct only if the customer still has enough points.
            int updated = Execute(tx, """
                UPDATE Customers SET LoyaltyPoints = LoyaltyPoints - @Points WHERE CustomerId = @Id AND LoyaltyPoints >= @Points
                """, P("@Points", SqlDbType.Int, points), P("@Id", SqlDbType.Int, customerId));
            if (updated == 0) { Fail("The customer doesn't have enough points for this."); return; }

            RedemptionCode = OrderRepository.NewCode("RD");
            Execute(tx, """
                INSERT INTO RewardRedemptions (CustomerId, RewardId, PromotionId, PointsRedeemed, RedemptionCode, ProcessedByUserId)
                VALUES (@CustomerId, @RewardId, @PromotionId, @Points, @Code, @UserId)
                """,
                P("@CustomerId", SqlDbType.Int, customerId),
                P("@RewardId", SqlDbType.Int, kind == "Reward" ? itemId : null),
                P("@PromotionId", SqlDbType.Int, kind == "Promo" ? itemId : null),
                P("@Points", SqlDbType.Int, points),
                P("@Code", SqlDbType.NVarChar, RedemptionCode, 20),
                P("@UserId", SqlDbType.Int, Session.CurrentUser?.UserId));
            RemainingPoints = Convert.ToInt32(Scalar(tx, "SELECT LoyaltyPoints FROM Customers WHERE CustomerId = @Id", P("@Id", SqlDbType.Int, customerId)));
            tx.Commit();
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            ShowError("Could not complete the redemption.", ex);
        }

        void Fail(string message) => MessageBox.Show(message, "Cannot Redeem", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
