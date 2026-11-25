using Microsoft.Data.SqlClient;
using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using System.Data;
using System.Drawing;

namespace BrewHaven.Forms;

public sealed class CrudForm : Form
{
    private readonly Definition definition;
    private readonly DataGridView grid=Ui.Grid();
    private readonly FlowLayoutPanel fields=new() { Dock=DockStyle.Fill, BackColor=Ui.Surface, AutoScroll=true, WrapContents=true };
    private readonly Dictionary<string,Control> controls=new();

    public sealed class Definition
    {
        public string Title=""; public string SelectSql=""; public string InsertSql=""; public string UpdateSql=""; public string DeleteSql="";
        public string IdColumn=""; public List<Field> Fields=[];
    }
    public sealed class Field { public string Name=""; public string Label=""; public bool Required; public bool Numeric; public bool Email; public bool Contact; public bool Password; }

    public CrudForm(Definition def)
    {
        definition=def; Text=def.Title; BackColor=Ui.Background; Load+=(_,_)=>FontHelper.ApplyToAll(this);

        // Entry card: section title, fields, action toolbar
        var formCard=Ui.Card(DockStyle.Top,20);
        var header=Ui.SectionTitle("Details");
        foreach(var f in def.Fields)
        {
            bool multi=f.Name is "Notes" or "TermsAndConditions" or "Description";
            Control c;
            if(f.Name is "RoleId" or "BranchId" or "CustomerId" or "PreferredMenuItemId") c=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList };
            else c=new TextBox { Multiline=multi, ScrollBars=multi ? ScrollBars.Vertical : ScrollBars.None, PasswordChar=f.Password ? '•' : '\0' };
            controls[f.Name]=c;
            fields.Controls.Add(Ui.Field(f.Required ? f.Label+" *" : f.Label, c, multi ? 320 : 230, 64));
        }
        var toolbar=Ui.Toolbar(DockStyle.Bottom); toolbar.Padding=new Padding(0,6,0,0);
        var add=Ui.PrimaryButton("Add"); add.Click+=AddClick;
        var update=Ui.SecondaryButton("Update"); update.Click+=UpdateClick;
        var delete=Ui.DangerButton("Delete"); delete.Click+=DeleteClick;
        var clear=Ui.GhostButton("Clear"); clear.Click+=ClearClick;
        var search=Ui.GhostButton("Search"); search.Click+=SearchClick;
        toolbar.Controls.AddRange([add,update,delete,clear,search]);
        formCard.Controls.Add(fields); formCard.Controls.Add(header); formCard.Controls.Add(toolbar);

        Controls.Add(Ui.GridCard(grid)); Controls.Add(Ui.Spacer()); Controls.Add(formCard);

        // Size the entry card to its fields; the page scrolls if the grid would get too short.
        AutoScroll=true;
        void Fit()
        {
            int inner=Math.Max(200,formCard.ClientSize.Width-formCard.Padding.Horizontal);
            int need=fields.GetPreferredSize(new Size(inner,0)).Height;
            formCard.Height=formCard.Padding.Vertical+header.Height+need+toolbar.Height+4;
            AutoScrollMinSize=new Size(0,formCard.Height+16+320);
        }
        Resize+=(_,_)=>Fit(); Load+=(_,_)=>Fit();

        LoadData(); FillLookups();
        grid.SelectionChanged+=GridSelectionChanged;
    }

    private void FillLookups()
    {
        if(controls.ContainsKey("RoleId")) FillCombo("RoleId","SELECT RoleId,RoleName FROM Roles ORDER BY RoleName","RoleName","RoleId");
        if(controls.ContainsKey("BranchId")) FillCombo("BranchId","SELECT BranchId,BranchName FROM Branches WHERE IsActive=1 ORDER BY BranchName","BranchName","BranchId");
        if(controls.ContainsKey("CustomerId") && controls["CustomerId"] is ComboBox cb) FillCombo("CustomerId","SELECT CustomerId,FullName FROM Customers WHERE IsActive=1 ORDER BY FullName","FullName","CustomerId");
        if(controls.ContainsKey("PreferredMenuItemId")) FillCombo("PreferredMenuItemId","SELECT MenuItemId,ItemName FROM MenuItems WHERE IsAvailable=1 ORDER BY ItemName","ItemName","MenuItemId");
    }
    private void FillCombo(string name,string sql,string text,string value)
    { try { using SqlConnection c=DbHelper.CreateConnection(); using SqlCommand cmd=new(sql,c); c.Open(); using SqlDataReader r=cmd.ExecuteReader(); var dt=new DataTable(); dt.Load(r); var combo=(ComboBox)controls[name]; combo.DisplayMember=text; combo.ValueMember=value; combo.DataSource=dt; } catch(Exception ex){MessageBox.Show(ex.Message,"Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);} }

    private void LoadData(string search="")
    {
        try { using SqlConnection c=DbHelper.CreateConnection(); using SqlCommand cmd=new(definition.SelectSql,c); if(search.Length>0) { cmd.Parameters.AddWithValue("@Search","%"+search+"%"); } c.Open(); using SqlDataReader r=cmd.ExecuteReader(); var dt=new DataTable(); dt.Load(r); grid.DataSource=dt; }
        catch(Exception ex){MessageBox.Show($"Could not load {definition.Title}.\n{ex.Message}","Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }

    private bool ValidateEntry()
    {
        foreach(var f in definition.Fields)
        {
            string val=GetValue(f.Name);
            if(f.Required && string.IsNullOrWhiteSpace(val)){MessageBox.Show($"{f.Label} is required.","Validation",MessageBoxButtons.OK,MessageBoxIcon.Warning); return false;}
            if(f.Email && val.Length>0 && !Validation.IsValidEmail(val)){MessageBox.Show("Enter a valid email address.","Validation",MessageBoxButtons.OK,MessageBoxIcon.Warning); return false;}
            if(f.Contact && val.Length>0 && val.Length!=11 || f.Contact && val.Length>0 && val.Any(ch=>!char.IsDigit(ch))){MessageBox.Show("Contact number must contain exactly 11 digits.","Validation",MessageBoxButtons.OK,MessageBoxIcon.Warning); return false;}
            if(f.Numeric && val.Length>0 && !decimal.TryParse(val,out decimal n)){MessageBox.Show($"{f.Label} must be numeric.","Validation",MessageBoxButtons.OK,MessageBoxIcon.Warning); return false;}
        }
        return true;
    }

    private string GetValue(string name)
    { var c=controls[name]; if(c is ComboBox cb) return cb.SelectedValue?.ToString()??""; return c.Text.Trim(); }

    private void AddClick(object? s,EventArgs e)
    { if(!ValidateEntry())return; try { using SqlConnection c=DbHelper.CreateConnection(); using SqlCommand cmd=new(definition.InsertSql,c); AddParameters(cmd); c.Open(); cmd.ExecuteNonQuery(); LoadData(); Clear(null,EventArgs.Empty); } catch(Exception ex){MessageBox.Show($"Could not add record.\n{ex.Message}","Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);} }
    private void UpdateClick(object? s,EventArgs e)
    { if(grid.CurrentRow?.Cells[definition.IdColumn].Value is null)return; if(!ValidateEntry())return; try { using SqlConnection c=DbHelper.CreateConnection(); using SqlCommand cmd=new(definition.UpdateSql,c); AddParameters(cmd); cmd.Parameters.AddWithValue("@Id",grid.CurrentRow.Cells[definition.IdColumn].Value); c.Open(); cmd.ExecuteNonQuery(); LoadData(); } catch(Exception ex){MessageBox.Show($"Could not update record.\n{ex.Message}","Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);} }
    private void DeleteClick(object? s,EventArgs e)
    { if(grid.CurrentRow?.Cells[definition.IdColumn].Value is null)return; if(MessageBox.Show("Delete this record?","Confirm",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return; try { using SqlConnection c=DbHelper.CreateConnection(); using SqlCommand cmd=new(definition.DeleteSql,c); cmd.Parameters.AddWithValue("@Id",grid.CurrentRow.Cells[definition.IdColumn].Value); c.Open(); cmd.ExecuteNonQuery(); LoadData(); Clear(null,EventArgs.Empty); } catch(Exception ex){MessageBox.Show($"Could not delete record.\n{ex.Message}","Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);} }
    private void Clear(object? s,EventArgs e)
    { foreach(var c in controls.Values){if(c is TextBox t)t.Clear(); else if(c is ComboBox cb)cb.SelectedIndex=cb.Items.Count>0?0:-1;} }
    private void ClearClick(object? s,EventArgs e)=>Clear(s,e);
    private void SearchClick(object? s,EventArgs e)
    { string term=controls.Values.OfType<TextBox>().FirstOrDefault(t=>t.Text.Length>0)?.Text??""; LoadData(term); }

    private void AddParameters(SqlCommand cmd)
    { foreach(var f in definition.Fields){ string val=GetValue(f.Name); object value=val.Length>0?val:DBNull.Value; if(f.Password) value=PasswordHasher.Hash(val); if(f.Numeric && val.Length>0) value=decimal.TryParse(val,out decimal d)?d:DBNull.Value; cmd.Parameters.AddWithValue("@"+f.Name,value); } }

    private void GridSelectionChanged(object? s,EventArgs e)
    { if(grid.CurrentRow is null)return; foreach(var f in definition.Fields) if(grid.Columns.Contains(f.Name)&&grid.CurrentRow.Cells[f.Name].Value is { } value) { if(controls[f.Name] is ComboBox cb)cb.SelectedValue=value; else controls[f.Name].Text=value is DBNull?"":value.ToString(); } }

    public static readonly Definition UserDefinition=new(){Title="Manage Users",IdColumn="UserId",
        SelectSql="SELECT u.UserId,u.Username,u.FullName,u.Email,r.RoleName,b.BranchName,u.IsActive FROM Users u JOIN Roles r ON r.RoleId=u.RoleId LEFT JOIN Branches b ON b.BranchId=u.BranchId",
        InsertSql="INSERT Users(RoleId,BranchId,Username,FullName,Email,PasswordHash) VALUES(@RoleId,NULLIF(@BranchId,0),@Username,@FullName,@Email,@PasswordHash)",
        UpdateSql="UPDATE Users SET RoleId=@RoleId,BranchId=NULLIF(@BranchId,0),FullName=@FullName,Email=@Email WHERE UserId=@Id",
        DeleteSql="DELETE FROM Users WHERE UserId=@Id",
        Fields=[new Field{Name="RoleId",Label="Role",Required=true},new Field{Name="BranchId",Label="Branch",Required=false},new Field{Name="Username",Label="Username",Required=true},new Field{Name="FullName",Label="Full Name",Required=true},new Field{Name="Email",Label="Email",Required=true,Email=true},new Field{Name="PasswordHash",Label="Password",Required=true,Password=true}]};

    public static readonly Definition SubscriptionDefinition=new(){Title="Subscriptions and Terms",IdColumn="SubscriptionId", SelectSql="SELECT * FROM Subscriptions",
        InsertSql="INSERT Subscriptions(Name,TermsAndConditions,MonthlyPrice,StartsAt,EndsAt,IsActive) VALUES(@Name,@TermsAndConditions,@MonthlyPrice,@StartsAt,@EndsAt,@IsActive)",
        UpdateSql="UPDATE Subscriptions SET Name=@Name,TermsAndConditions=@TermsAndConditions,MonthlyPrice=@MonthlyPrice,StartsAt=@StartsAt,EndsAt=@EndsAt,IsActive=@IsActive WHERE SubscriptionId=@Id",DeleteSql="DELETE FROM Subscriptions WHERE SubscriptionId=@Id",
        Fields=[new Field{Name="Name",Label="Name",Required=true},new Field{Name="TermsAndConditions",Label="Terms & Conditions",Required=true},new Field{Name="MonthlyPrice",Label="Monthly Price",Required=true,Numeric=true},new Field{Name="StartsAt",Label="Starts (yyyy-mm-dd)",Required=true},new Field{Name="EndsAt",Label="Ends (yyyy-mm-dd)",Required=true},new Field{Name="IsActive",Label="Active (1/0)",Required=true,Numeric=true}]};

    public static readonly Definition BranchDefinition=new(){Title="Manage Branches",IdColumn="BranchId",SelectSql="SELECT * FROM Branches",
        InsertSql="INSERT Branches(BranchName,Address,ContactNumber,IsActive) VALUES(@BranchName,@Address,@ContactNumber,@IsActive)",UpdateSql="UPDATE Branches SET BranchName=@BranchName,Address=@Address,ContactNumber=@ContactNumber,IsActive=@IsActive WHERE BranchId=@Id",DeleteSql="DELETE FROM Branches WHERE BranchId=@Id",
        Fields=[new Field{Name="BranchName",Label="Branch Name",Required=true},new Field{Name="Address",Label="Address",Required=true},new Field{Name="ContactNumber",Label="Contact (11 digits)",Contact=true},new Field{Name="IsActive",Label="Active (1/0)",Required=true,Numeric=true}]};

    public static readonly Definition CustomerDefinition=new(){Title="Walk-In Customers",IdColumn="CustomerId",SelectSql="SELECT * FROM Customers",
        InsertSql="INSERT Customers(FullName,ContactNumber,Email,LoyaltyPoints,IsActive) VALUES(@FullName,@ContactNumber,@Email,@LoyaltyPoints,@IsActive)",UpdateSql="UPDATE Customers SET FullName=@FullName,ContactNumber=@ContactNumber,Email=@Email,LoyaltyPoints=@LoyaltyPoints,IsActive=@IsActive WHERE CustomerId=@Id",DeleteSql="DELETE FROM Customers WHERE CustomerId=@Id",
        Fields=[new Field{Name="FullName",Label="Full Name",Required=true},new Field{Name="ContactNumber",Label="Contact (11 digits)",Required=true,Contact=true},new Field{Name="Email",Label="Email",Email=true},new Field{Name="LoyaltyPoints",Label="Loyalty Points",Numeric=true},new Field{Name="IsActive",Label="Active (1/0)",Numeric=true}]};

    public static readonly Definition PromotionDefinition=new(){Title="Promotions",IdColumn="PromotionId",SelectSql="SELECT * FROM Promotions",
        InsertSql="INSERT Promotions(PromotionName,Description,DiscountPercent,StartDate,EndDate,IsActive) VALUES(@PromotionName,@Description,@DiscountPercent,@StartDate,@EndDate,@IsActive)",UpdateSql="UPDATE Promotions SET PromotionName=@PromotionName,Description=@Description,DiscountPercent=@DiscountPercent,StartDate=@StartDate,EndDate=@EndDate,IsActive=@IsActive WHERE PromotionId=@Id",DeleteSql="DELETE FROM Promotions WHERE PromotionId=@Id",
        Fields=[new Field{Name="PromotionName",Label="Promotion Name",Required=true},new Field{Name="Description",Label="Description"},new Field{Name="DiscountPercent",Label="Discount %",Required=true,Numeric=true},new Field{Name="StartDate",Label="Start (yyyy-mm-dd)",Required=true},new Field{Name="EndDate",Label="End (yyyy-mm-dd)",Required=true},new Field{Name="IsActive",Label="Active (1/0)",Numeric=true}]};

    public static readonly Definition RewardDefinition=new(){Title="Rewards and Loyalty Points",IdColumn="RewardId",SelectSql="SELECT * FROM Rewards",
        InsertSql="INSERT Rewards(RewardName,PointsRequired,DiscountValue,IsActive) VALUES(@RewardName,@PointsRequired,@DiscountValue,@IsActive)",UpdateSql="UPDATE Rewards SET RewardName=@RewardName,PointsRequired=@PointsRequired,DiscountValue=@DiscountValue,IsActive=@IsActive WHERE RewardId=@Id",DeleteSql="DELETE FROM Rewards WHERE RewardId=@Id",
        Fields=[new Field{Name="RewardName",Label="Reward Name",Required=true},new Field{Name="PointsRequired",Label="Points Required",Required=true,Numeric=true},new Field{Name="DiscountValue",Label="Discount Value",Required=true,Numeric=true},new Field{Name="IsActive",Label="Active (1/0)",Numeric=true}]};

    public static readonly Definition MenuItemDefinition=new(){Title="Menu Items",IdColumn="MenuItemId",SelectSql="SELECT * FROM MenuItems",
        InsertSql="INSERT MenuItems(ItemName,Category,Description,Price,IsAvailable) VALUES(@ItemName,@Category,@Description,@Price,@IsAvailable)",UpdateSql="UPDATE MenuItems SET ItemName=@ItemName,Category=@Category,Description=@Description,Price=@Price,IsAvailable=@IsAvailable WHERE MenuItemId=@Id",DeleteSql="DELETE FROM MenuItems WHERE MenuItemId=@Id",
        Fields=[new Field{Name="ItemName",Label="Item Name",Required=true},new Field{Name="Category",Label="Category",Required=true},new Field{Name="Description",Label="Description"},new Field{Name="Price",Label="Price",Required=true,Numeric=true},new Field{Name="IsAvailable",Label="Available (1/0)",Numeric=true}]};

    public static readonly Definition PreferenceDefinition=new(){Title="Customer Preferences",IdColumn="PreferenceId",SelectSql="SELECT p.PreferenceId,c.FullName Customer,m.ItemName PreferredItem,p.PreferredOrderType,p.Notes FROM CustomerPreferences p JOIN Customers c ON c.CustomerId=p.CustomerId LEFT JOIN MenuItems m ON m.MenuItemId=p.PreferredMenuItemId",
        InsertSql="INSERT CustomerPreferences(CustomerId,PreferredMenuItemId,PreferredOrderType,Notes) VALUES(@CustomerId,NULLIF(@PreferredMenuItemId,0),@PreferredOrderType,@Notes)",UpdateSql="UPDATE CustomerPreferences SET CustomerId=@CustomerId,PreferredMenuItemId=NULLIF(@PreferredMenuItemId,0),PreferredOrderType=@PreferredOrderType,Notes=@Notes WHERE PreferenceId=@Id",DeleteSql="DELETE FROM CustomerPreferences WHERE PreferenceId=@Id",
        Fields=[new Field{Name="CustomerId",Label="Customer",Required=true},new Field{Name="PreferredMenuItemId",Label="Preferred Menu Item"},new Field{Name="PreferredOrderType",Label="Dine-In / Takeout",Required=true},new Field{Name="Notes",Label="Notes"}]};

    public static readonly Definition InventoryDefinition=new(){Title="Inventory",IdColumn="InventoryId",SelectSql="SELECT i.InventoryId,b.BranchName,i.ItemName,i.Category,i.Quantity,i.ReorderLevel,i.UpdatedAt FROM Inventory i JOIN Branches b ON b.BranchId=i.BranchId",
        InsertSql="INSERT Inventory(BranchId,ItemName,Category,Quantity,ReorderLevel) VALUES(@BranchId,@ItemName,@Category,@Quantity,@ReorderLevel)",UpdateSql="UPDATE Inventory SET BranchId=@BranchId,ItemName=@ItemName,Category=@Category,Quantity=@Quantity,ReorderLevel=@ReorderLevel,UpdatedAt=SYSUTCDATETIME() WHERE InventoryId=@Id",DeleteSql="DELETE FROM Inventory WHERE InventoryId=@Id",
        Fields=[new Field{Name="BranchId",Label="Branch",Required=true},new Field{Name="ItemName",Label="Item Name",Required=true},new Field{Name="Category",Label="Category",Required=true},new Field{Name="Quantity",Label="Stock Quantity",Required=true,Numeric=true},new Field{Name="ReorderLevel",Label="Low-Stock Level",Required=true,Numeric=true}]};
}
