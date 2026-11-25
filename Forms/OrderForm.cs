using Microsoft.Data.SqlClient;
using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using System.Data;
using System.Drawing;

namespace BrewHaven.Forms;

public sealed class OrderForm : Form
{
    private readonly ComboBox customer=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ComboBox menu=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly NumericUpDown quantity=new(){Minimum=1,Maximum=100,Value=1};
    private readonly ComboBox orderType=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ComboBox promotion=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ComboBox reward=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ComboBox paymentMethod=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly TextBox amountPaid=new();
    private readonly Label subtotal=new(){AutoSize=true,Font=Ui.Body,ForeColor=Ui.Muted};
    private readonly Label discountLabel=new(){AutoSize=true,Font=Ui.Body,ForeColor=Ui.Muted};
    private readonly Label total=new(){AutoSize=true,Font=Ui.Heading,ForeColor=Ui.Text};
    private readonly Label change=new(){AutoSize=true,Font=Ui.BodyBold,ForeColor=Ui.Primary};
    private readonly DataGridView cart=Ui.Grid();
    private readonly DataTable cartTable;

    public OrderForm()
    {
        Text="Place Order";BackColor=Ui.Background; Load+=(_,_)=>Helpers.FontHelper.ApplyToAll(this);
        cartTable=new DataTable();cartTable.Columns.Add("MenuItemId",typeof(int));cartTable.Columns.Add("Item");cartTable.Columns.Add("Quantity",typeof(int));cartTable.Columns.Add("UnitPrice",typeof(decimal));cartTable.Columns.Add("LineTotal",typeof(decimal));cart.DataSource=cartTable;
        cart.DataBindingComplete+=(_,_)=>{ if(cart.Columns.Contains("MenuItemId"))cart.Columns["MenuItemId"]!.Visible=false;
            foreach(var col in new[]{"UnitPrice","LineTotal"}) if(cart.Columns.Contains(col)){cart.Columns[col]!.DefaultCellStyle.Format="C";cart.Columns[col]!.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;}
            if(cart.Columns.Contains("UnitPrice"))cart.Columns["UnitPrice"]!.HeaderText="Unit Price"; if(cart.Columns.Contains("LineTotal"))cart.Columns["LineTotal"]!.HeaderText="Line Total"; };

        // ---- Left: order builder ----
        var left=Ui.Card(DockStyle.Left,20); left.Width=440;
        var stack=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,BackColor=Ui.Surface};
        stack.Controls.Add(new Label{Text="Order details",AutoSize=true,Font=Ui.SubHeading,ForeColor=Ui.Text,Margin=new Padding(0,0,0,12)});
        stack.Controls.Add(Ui.Field("CUSTOMER",customer,340)); stack.Controls.Add(Ui.Field("ORDER TYPE",orderType,340));

        var itemRow=new FlowLayoutPanel{AutoSize=true,WrapContents=false,BackColor=Ui.Surface,Margin=Padding.Empty};
        itemRow.Controls.Add(Ui.Field("MENU ITEM",menu,240)); var q=Ui.Field("QTY",quantity,82); q.Margin=new Padding(0,0,0,14); itemRow.Controls.Add(q);
        stack.Controls.Add(itemRow);
        var itemButtons=Ui.Toolbar(); itemButtons.Margin=new Padding(0,0,0,18);
        var add=Ui.PrimaryButton("+ Add Item");add.Click+=AddItem;
        var remove=Ui.DangerButton("Remove Selected");remove.Click+=(_,_)=>{if(cart.CurrentRow!=null)cartTable.Rows.RemoveAt(cart.CurrentRow.Index);Recalculate();};
        itemButtons.Controls.AddRange([add,remove]); stack.Controls.Add(itemButtons);

        stack.Controls.Add(new Label{Text="Discounts & payment",AutoSize=true,Font=Ui.SubHeading,ForeColor=Ui.Text,Margin=new Padding(0,4,0,12),UseMnemonic=false});
        stack.Controls.Add(Ui.Field("PROMOTION",promotion,340)); stack.Controls.Add(Ui.Field("REWARD",reward,340));
        var payRow=new FlowLayoutPanel{AutoSize=true,WrapContents=false,BackColor=Ui.Surface,Margin=Padding.Empty};
        payRow.Controls.Add(Ui.Field("PAYMENT METHOD",paymentMethod,161)); var paid=Ui.Field("AMOUNT PAID",amountPaid,161); paid.Margin=new Padding(0,0,0,14); payRow.Controls.Add(paid);
        stack.Controls.Add(payRow);
        left.Controls.Add(stack);

        // ---- Right: cart + totals ----
        var right=Ui.Card(DockStyle.Fill,20);
        var cartTitle=Ui.SectionTitle("Current order");
        var totals=new Panel{Dock=DockStyle.Bottom,Height=190,BackColor=Ui.Surface};
        var line=new Panel{Dock=DockStyle.Top,Height=1,BackColor=Ui.Border};
        var process=Ui.PrimaryButton("Process Payment");process.Dock=DockStyle.Bottom;process.Height=48;process.Click+=ProcessPayment;
        totals.Controls.Add(subtotal); totals.Controls.Add(discountLabel); totals.Controls.Add(total); totals.Controls.Add(change); totals.Controls.Add(line); totals.Controls.Add(process);
        subtotal.Location=new Point(0,14); discountLabel.Location=new Point(0,38); total.Location=new Point(0,64);
        change.Anchor=AnchorStyles.Top|AnchorStyles.Right;
        totals.Resize+=(_,_)=>change.Location=new Point(totals.Width-change.Width,74);
        change.SizeChanged+=(_,_)=>change.Location=new Point(totals.Width-change.Width,74);
        right.Controls.Add(cart); right.Controls.Add(cartTitle); right.Controls.Add(totals);

        Controls.Add(right); Controls.Add(Ui.Spacer(DockStyle.Left,16)); Controls.Add(left);
        orderType.Items.AddRange(["Dine-In","Takeout"]);orderType.SelectedIndex=0;paymentMethod.Items.AddRange(["Cash","Card","E-Wallet"]);paymentMethod.SelectedIndex=0;
        LoadLookups();Recalculate();
    }

    private void LoadLookups()
    { try { using SqlConnection c=DbHelper.CreateConnection();c.Open();LoadCombo(c,"SELECT CustomerId,FullName FROM Customers WHERE IsActive=1 ORDER BY FullName",customer,"FullName","CustomerId");LoadCombo(c,"SELECT MenuItemId,ItemName FROM MenuItems WHERE IsAvailable=1 ORDER BY ItemName",menu,"ItemName","MenuItemId");LoadCombo(c,"SELECT PromotionId,PromotionName FROM Promotions WHERE IsActive=1 AND CAST(SYSUTCDATETIME() AS date) BETWEEN StartDate AND EndDate",promotion,"PromotionName","PromotionId");LoadCombo(c,"SELECT RewardId,RewardName FROM Rewards WHERE IsActive=1",reward,"RewardName","RewardId"); }catch(Exception ex){MessageBox.Show(ex.Message,"Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);} }
    private void LoadCombo(SqlConnection c,string sql,ComboBox combo,string text,string value){using SqlCommand cmd=new(sql,c);using SqlDataReader r=cmd.ExecuteReader();var dt=new DataTable();dt.Load(r);combo.DisplayMember=text;combo.ValueMember=value;combo.DataSource=dt;}

    private void AddItem(object? s,EventArgs e)
    { if(menu.SelectedValue is null){MessageBox.Show("Select a menu item.","Validation",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}try{using SqlConnection c=DbHelper.CreateConnection();using SqlCommand cmd=new("SELECT Price FROM MenuItems WHERE MenuItemId=@Id",c);cmd.Parameters.AddWithValue("@Id",menu.SelectedValue);c.Open();object p=cmd.ExecuteScalar()??0m;decimal price=Convert.ToDecimal(p);cartTable.Rows.Add(Convert.ToInt32(menu.SelectedValue),menu.Text,(int)quantity.Value,price,(int)quantity.Value*price);Recalculate();}catch(Exception ex){MessageBox.Show(ex.Message,"Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);} }

    private decimal Subtotal()=>cartTable.Rows.Cast<DataRow>().Sum(r=>Convert.ToDecimal(r["LineTotal"]));
    private decimal Discount()
    { decimal sub=Subtotal(); if(promotion.SelectedValue is null)return 0m;try{using SqlConnection c=DbHelper.CreateConnection();using SqlCommand cmd=new("SELECT DiscountPercent FROM Promotions WHERE PromotionId=@Id",c);cmd.Parameters.AddWithValue("@Id",promotion.SelectedValue);c.Open();decimal percent=Convert.ToDecimal(cmd.ExecuteScalar()??0);return decimal.Round(sub*percent/100m,2);}catch{return 0m;} }
    private decimal RewardDiscount(){if(reward.SelectedValue is null||customer.SelectedValue is null)return 0m;try{using SqlConnection c=DbHelper.CreateConnection();using SqlCommand cmd=new("SELECT r.DiscountValue FROM Rewards r JOIN Customers cu ON cu.CustomerId=@CustomerId AND cu.LoyaltyPoints>=r.PointsRequired WHERE r.RewardId=@RewardId",c);cmd.Parameters.AddWithValue("@CustomerId",customer.SelectedValue);cmd.Parameters.AddWithValue("@RewardId",reward.SelectedValue);c.Open();object x=cmd.ExecuteScalar();return x is null?0m:Convert.ToDecimal(x);}catch{return 0m;} }
    private void Recalculate(){decimal sub=Subtotal(),dis=Discount()+RewardDiscount(),tot=Math.Max(0m,sub-dis);subtotal.Text=$"Subtotal: {sub:C}";discountLabel.Text=$"Discount: {dis:C}";total.Text=$"Total: {tot:C}";if(decimal.TryParse(amountPaid.Text,out decimal paid))change.Text=$"Change: {Math.Max(0m,paid-tot):C}";else change.Text="Change: --";}

    private void ProcessPayment(object? s,EventArgs e)
    { Recalculate();if(cartTable.Rows.Count==0){MessageBox.Show("Add at least one menu item.","Validation",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}decimal sub=Subtotal(),dis=Discount()+RewardDiscount(),tot=Math.Max(0m,sub-dis);if(!decimal.TryParse(amountPaid.Text,out decimal paid)||paid<0){MessageBox.Show("Enter a valid amount paid.","Validation",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}if(paymentMethod.Text=="Cash"&&paid<tot){MessageBox.Show("Cash amount is less than the total.","Payment",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}if(paymentMethod.Text!="Cash")paid=tot;
      SqlTransaction? tx=null;try{using SqlConnection c=DbHelper.CreateConnection();c.Open();tx=c.BeginTransaction();int branchId=Session.CurrentUser?.BranchId??GetFirstBranch(tx);int orderId;
        using(SqlCommand cmd=new("INSERT Orders(BranchId,CustomerId,StaffUserId,OrderType,Status,Subtotal,Discount,Total,PointsEarned) OUTPUT INSERTED.OrderId VALUES(@BranchId,@CustomerId,@UserId,@Type,'Completed',@Subtotal,@Discount,@Total,@Points)",c,tx)){cmd.Parameters.AddWithValue("@BranchId",branchId);cmd.Parameters.AddWithValue("@CustomerId",customer.SelectedValue??DBNull.Value);cmd.Parameters.AddWithValue("@UserId",Session.CurrentUser!.UserId);cmd.Parameters.AddWithValue("@Type",orderType.Text);cmd.Parameters.AddWithValue("@Subtotal",sub);cmd.Parameters.AddWithValue("@Discount",dis);cmd.Parameters.AddWithValue("@Total",tot);int points=(int)Math.Floor(tot/10m);cmd.Parameters.AddWithValue("@Points",points);orderId=Convert.ToInt32(cmd.ExecuteScalar());}
        foreach(DataRow r in cartTable.Rows){using SqlCommand cmd=new("INSERT OrderItems(OrderId,MenuItemId,Quantity,UnitPrice) VALUES(@OrderId,@MenuItemId,@Quantity,@UnitPrice)",c,tx);cmd.Parameters.AddWithValue("@OrderId",orderId);cmd.Parameters.AddWithValue("@MenuItemId",r["MenuItemId"]);cmd.Parameters.AddWithValue("@Quantity",r["Quantity"]);cmd.Parameters.AddWithValue("@UnitPrice",r["UnitPrice"]);cmd.ExecuteNonQuery();using SqlCommand inv=new("UPDATE Inventory SET Quantity=CASE WHEN Quantity-@Qty>=0 THEN Quantity-@Qty ELSE Quantity END,UpdatedAt=SYSUTCDATETIME() WHERE BranchId=@BranchId AND MenuItemId=@MenuItemId",c,tx);inv.Parameters.AddWithValue("@BranchId",branchId);inv.Parameters.AddWithValue("@MenuItemId",r["MenuItemId"]);inv.Parameters.AddWithValue("@Qty",r["Quantity"]);inv.ExecuteNonQuery();}
        using(SqlCommand cmd=new("INSERT Payments(OrderId,Method,AmountPaid,ChangeAmount) VALUES(@OrderId,@Method,@Paid,@Change)",c,tx)){cmd.Parameters.AddWithValue("@OrderId",orderId);cmd.Parameters.AddWithValue("@Method",paymentMethod.Text);cmd.Parameters.AddWithValue("@Paid",paid);cmd.Parameters.AddWithValue("@Change",Math.Max(0m,paid-tot));cmd.ExecuteNonQuery();}
        if(customer.SelectedValue!=null){using SqlCommand cmd=new("UPDATE Customers SET LoyaltyPoints=LoyaltyPoints+@Points WHERE CustomerId=@CustomerId",c,tx);cmd.Parameters.AddWithValue("@Points",(int)Math.Floor(tot/10m));cmd.Parameters.AddWithValue("@CustomerId",customer.SelectedValue);cmd.ExecuteNonQuery();if(reward.SelectedValue!=null){using SqlCommand rr=new("INSERT RewardRedemptions(CustomerId,RewardId,PointsRedeemed,OrderId) SELECT @CustomerId,@RewardId,PointsRequired,@OrderId FROM Rewards WHERE RewardId=@RewardId",c,tx);rr.Parameters.AddWithValue("@CustomerId",customer.SelectedValue);rr.Parameters.AddWithValue("@RewardId",reward.SelectedValue);rr.Parameters.AddWithValue("@OrderId",orderId);rr.ExecuteNonQuery();using SqlCommand dp=new("UPDATE Customers SET LoyaltyPoints=CASE WHEN LoyaltyPoints-@Pts>=0 THEN LoyaltyPoints-@Pts ELSE LoyaltyPoints END WHERE CustomerId=@CustomerId",c,tx);dp.Parameters.AddWithValue("@Pts",GetRequiredPoints(c,tx,Convert.ToInt32(reward.SelectedValue)));dp.Parameters.AddWithValue("@CustomerId",customer.SelectedValue);dp.ExecuteNonQuery();}}tx.Commit();tx=null;
        decimal changeAmt=Math.Max(0m,paid-tot);MessageBox.Show($"Payment processed successfully.\nOrder #{orderId}\nTotal: {tot:C}\nChange: {changeAmt:C}","Receipt",MessageBoxButtons.OK,MessageBoxIcon.Information);cartTable.Clear();amountPaid.Clear();Recalculate();}
      catch(Exception ex){try{tx?.Rollback();}catch{}MessageBox.Show($"Payment failed. The transaction was cancelled.\n{ex.Message}","Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);} }
    private static int GetFirstBranch(SqlTransaction tx){using SqlCommand cmd=new("SELECT TOP 1 BranchId FROM Branches WHERE IsActive=1",tx.Connection,tx);return Convert.ToInt32(cmd.ExecuteScalar());}
    private static int GetRequiredPoints(SqlConnection c,SqlTransaction tx,int rewardId){using SqlCommand cmd=new("SELECT PointsRequired FROM Rewards WHERE RewardId=@Id",c,tx);cmd.Parameters.AddWithValue("@Id",rewardId);return Convert.ToInt32(cmd.ExecuteScalar());}
}
