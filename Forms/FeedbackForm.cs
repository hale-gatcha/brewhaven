using Microsoft.Data.SqlClient;
using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using System.Data;
using System.Drawing;

namespace BrewHaven.Forms;

public sealed class FeedbackForm : Form
{
    private readonly DataGridView grid=Ui.Grid();
    private readonly ComboBox type=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ComboBox status=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ComboBox branch=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ComboBox customer=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly ComboBox assigned=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly TextBox subject=new(){Multiline=true};
    private readonly TextBox message=new(){Multiline=true,ScrollBars=ScrollBars.Vertical};
    private readonly NumericUpDown rating=new(){Minimum=1,Maximum=5,Value=1};
    private readonly TextBox resolution=new(){Multiline=true,ScrollBars=ScrollBars.Vertical};
    private readonly FlowLayoutPanel summary=new(){Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,BackColor=Ui.Background,Padding=new Padding(0,0,0,12)};
    private readonly DateTimePicker from=new(){Value=DateTime.Today.AddMonths(-1)};
    private readonly DateTimePicker to=new(){Value=DateTime.Today};

    public FeedbackForm()
    {
        Text="Feedback / Inquiries / Complaints"; BackColor=Ui.Background; Load+=(_,_)=>FontHelper.ApplyToAll(this);
        type.Items.AddRange(["Feedback","Inquiry","Complaint"]); status.Items.AddRange(["Open","In Progress","Resolved","Closed"]);

        // ---- Entry card ----
        var formCard=Ui.Card(DockStyle.Top,20);
        var fields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=3,BackColor=Ui.Surface,AutoScroll=true};
        for(int i=0;i<3;i++){ fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.33f)); fields.RowStyles.Add(new RowStyle(SizeType.AutoSize)); }
        Control F(string label,Control c,int h=60){var f=Ui.Field(label,c,240,h);f.Anchor=AnchorStyles.Top|AnchorStyles.Left;return f;}
        fields.Controls.Add(F("CUSTOMER (OPTIONAL)",customer),0,0); fields.Controls.Add(F("BRANCH *",branch),1,0); fields.Controls.Add(F("TYPE *",type),2,0);
        fields.Controls.Add(F("STATUS *",status),0,1); fields.Controls.Add(F("ASSIGNED TO",assigned),1,1); fields.Controls.Add(F("RATING (FEEDBACK 1-5)",rating),2,1);
        fields.Controls.Add(F("SUBJECT *",subject),0,2); fields.Controls.Add(F("MESSAGE *",message),1,2); fields.Controls.Add(F("RESOLUTION / RESPONSE",resolution),2,2);
        // Let field blocks stretch to the column width.
        fields.Resize+=(_,_)=>{ foreach(Control block in fields.Controls) if(block is TableLayoutPanel t){ int w=Math.Max(160,fields.ClientSize.Width/3-24); t.ColumnStyles[0].Width=w; foreach(Control c in t.Controls) if(t.GetRow(c)==1) c.Width=w; } };

        var actions=Ui.Toolbar(DockStyle.Bottom); actions.Padding=new Padding(0,6,0,0);
        var add=Ui.PrimaryButton("Add"); add.Click+=Add;
        var upd=Ui.SecondaryButton("Update"); upd.Click+=Update;
        var del=Ui.DangerButton("Delete"); del.Click+=Delete;
        var clr=Ui.GhostButton("Clear"); clr.Click+=Clear;
        actions.Controls.AddRange([add,upd,del,clr]);
        formCard.Controls.Add(fields); formCard.Controls.Add(Ui.SectionTitle("Log an entry")); formCard.Controls.Add(actions);
        formCard.Height=formCard.Padding.Vertical+30+2*72+92+56;

        // ---- Filter bar ----
        var filterCard=Ui.Card(DockStyle.Top,12); filterCard.Height=64;
        var filter=Ui.Toolbar(DockStyle.Top); filter.SizeChanged+=(_,_)=>filterCard.Height=filter.Height+filterCard.Padding.Vertical;
        var ft=new ComboBox{Width=120,DropDownStyle=ComboBoxStyle.DropDownList}; ft.Items.AddRange(["All","Feedback","Inquiry","Complaint"]); ft.SelectedIndex=0;
        var fs=new ComboBox{Width=120,DropDownStyle=ComboBoxStyle.DropDownList}; fs.Items.AddRange(["All","Open","In Progress","Resolved","Closed"]); fs.SelectedIndex=0;
        from.Width=140; to.Width=140;
        Control Box(Control c){ var b=Ui.Boxed(c); b.Margin=new Padding(0,3,20,0); return b; }
        var go=Ui.SecondaryButton("Apply Filter"); go.Click+=(_,_)=>LoadData(ft.Text,fs.Text);
        filter.Controls.AddRange([Ui.InlineLabel("TYPE"),Box(ft),Ui.InlineLabel("STATUS"),Box(fs),Ui.InlineLabel("FROM"),Box(from),Ui.InlineLabel("TO"),Box(to),go]);
        filterCard.Controls.Add(filter);

        Controls.Add(Ui.GridCard(grid)); Controls.Add(Ui.Spacer(DockStyle.Top,12)); Controls.Add(filterCard);
        Controls.Add(Ui.Spacer(DockStyle.Top,16)); Controls.Add(formCard); Controls.Add(summary);

        AutoScroll=true; AutoScrollMinSize=new Size(0,880); // page scrolls on short screens instead of crushing the grid
        FillLookups(); LoadData("All","All"); grid.SelectionChanged+=SelectRow;
    }

    private void FillLookups()
    {
        try { using SqlConnection c=DbHelper.CreateConnection(); c.Open();
            Fill("SELECT BranchId,BranchName FROM Branches WHERE IsActive=1 ORDER BY BranchName",branch,"BranchName","BranchId");
            Fill("SELECT CustomerId,FullName FROM Customers WHERE IsActive=1 ORDER BY FullName",customer,"FullName","CustomerId");
            Fill("SELECT UserId,FullName FROM Users WHERE IsActive=1 ORDER BY FullName",assigned,"FullName","UserId");
        } catch(Exception ex){MessageBox.Show(ex.Message,"Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
    private void Fill(string sql,ComboBox combo,string text,string value)
    { using SqlCommand cmd=new(sql,DbHelper.CreateConnection()); cmd.Connection.Open(); using SqlDataReader r=cmd.ExecuteReader(); var dt=new DataTable(); dt.Load(r); combo.DisplayMember=text;combo.ValueMember=value;combo.DataSource=dt; }

    private void LoadData(string typeFilter="All",string statusFilter="All")
    {
        try { using SqlConnection c=DbHelper.CreateConnection(); using SqlCommand cmd=new("SELECT f.FeedbackId,f.Type,f.Status,b.BranchName,ISNULL(cu.FullName,'Walk-In') Customer,f.Subject,f.Message,f.Rating,f.DateSubmitted,ISNULL(u.FullName,'') AssignedTo,f.ResolutionNotes FROM Feedback f JOIN Branches b ON b.BranchId=f.BranchId LEFT JOIN Customers cu ON cu.CustomerId=f.CustomerId LEFT JOIN Users u ON u.UserId=f.AssignedTo WHERE f.DateSubmitted BETWEEN @From AND @To AND (@Type='All' OR f.Type=@Type) AND (@Status='All' OR f.Status=@Status) ORDER BY f.DateSubmitted DESC",c);
        cmd.Parameters.AddWithValue("@From",from.Value.Date); cmd.Parameters.AddWithValue("@To",to.Value.Date.AddDays(1).AddTicks(-1));cmd.Parameters.AddWithValue("@Type",typeFilter);cmd.Parameters.AddWithValue("@Status",statusFilter);c.Open();using var r=cmd.ExecuteReader();var dt=new DataTable();dt.Load(r);grid.DataSource=dt;LoadSummary(); }
        catch(Exception ex){MessageBox.Show($"Could not load feedback.\n{ex.Message}","Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }

    private void LoadSummary()
    { try { using SqlConnection c=DbHelper.CreateConnection(); using SqlCommand cmd=new("SELECT Type,COUNT(*) N FROM Feedback GROUP BY Type; SELECT Status,COUNT(*) N FROM Feedback GROUP BY Status",c);c.Open();using SqlDataReader r=cmd.ExecuteReader();var counts=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);while(r.Read())counts[$"{r[0]}: "]=Convert.ToInt32(r[1]);r.NextResult();while(r.Read())counts[$"{r[0]}: "]=Convert.ToInt32(r[1]);summary.Controls.Clear();foreach(var kv in counts){summary.Controls.Add(new Label{Text=$"{kv.Key.TrimEnd(' ',':')}   {kv.Value}",AutoSize=true,Font=Ui.BodyBold,ForeColor=Ui.Primary,BackColor=Ui.AccentSoft,Padding=new Padding(12,6,12,6),Margin=new Padding(0,0,8,0)});} }
      catch(Exception ex){MessageBox.Show(ex.Message,"Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);} }


    private void Add(object? s,EventArgs e)
    { if(!ValidateEntry())return; try { using SqlConnection c=DbHelper.CreateConnection(); using SqlCommand cmd=new("INSERT Feedback(CustomerId,BranchId,SubmittedByUserId,Type,Subject,Message,Rating,Status,AssignedTo,ResolutionNotes) VALUES(@CustomerId,@BranchId,@UserId,@Type,@Subject,@Message,@Rating,@Status,@AssignedTo,@Resolution)",c);AddParams(cmd);c.Open();cmd.ExecuteNonQuery();LoadData(); } catch(Exception ex){MessageBox.Show(ex.Message,"Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);} }
    private void Update(object? s,EventArgs e)
    { if(!ValidateEntry()||grid.CurrentRow is null)return; try { using SqlConnection c=DbHelper.CreateConnection(); using SqlCommand cmd=new("UPDATE Feedback SET BranchId=@BranchId,Type=@Type,Subject=@Subject,Message=@Message,Rating=@Rating,Status=@Status,AssignedTo=@AssignedTo,ResolutionNotes=@Resolution,DateResolved=CASE WHEN @Status IN ('Resolved','Closed') THEN COALESCE(DateResolved,SYSUTCDATETIME()) ELSE NULL END WHERE FeedbackId=@Id",c);AddParams(cmd);cmd.Parameters.AddWithValue("@Id",grid.CurrentRow.Cells["FeedbackId"].Value);c.Open();cmd.ExecuteNonQuery();LoadData(); } catch(Exception ex){MessageBox.Show(ex.Message,"Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);} }
    private void Delete(object? s,EventArgs e)
    { if(grid.CurrentRow is null)return;if(MessageBox.Show("Delete this feedback record?","Confirm",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;try{using SqlConnection c=DbHelper.CreateConnection();using SqlCommand cmd=new("DELETE FROM Feedback WHERE FeedbackId=@Id",c);cmd.Parameters.AddWithValue("@Id",grid.CurrentRow.Cells["FeedbackId"].Value);c.Open();cmd.ExecuteNonQuery();LoadData();}catch(Exception ex){MessageBox.Show(ex.Message,"Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);} }
    private void Clear(object? s,EventArgs e){subject.Clear();message.Clear();resolution.Clear();rating.Value=1;}

    private bool ValidateEntry()
    { if(branch.SelectedValue is null||type.SelectedItem is null||status.SelectedItem is null){MessageBox.Show("Branch, type and status are required.","Validation",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;} if(subject.Text.Trim().Length==0||message.Text.Trim().Length==0){MessageBox.Show("Subject and message are required.","Validation",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;} string st=status.Text.Trim();if(type.Text=="Feedback"&&rating.Value<1){MessageBox.Show("Feedback requires a 1-5 rating.","Validation",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}if((st=="Resolved"||st=="Closed")&&(type.Text=="Complaint")&&resolution.Text.Trim().Length==0){MessageBox.Show("Complaints require resolution notes before Resolved or Closed.","Validation",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}return true; }
    private void AddParams(SqlCommand cmd)
    { cmd.Parameters.AddWithValue("@CustomerId",customer.SelectedValue is null?DBNull.Value:customer.SelectedValue);cmd.Parameters.AddWithValue("@BranchId",branch.SelectedValue??DBNull.Value);cmd.Parameters.AddWithValue("@UserId",Session.CurrentUser!.UserId);cmd.Parameters.AddWithValue("@Type",type.Text);cmd.Parameters.AddWithValue("@Subject",subject.Text.Trim());cmd.Parameters.AddWithValue("@Message",message.Text.Trim());cmd.Parameters.AddWithValue("@Rating",type.Text=="Feedback"?(object)rating.Value:DBNull.Value);cmd.Parameters.AddWithValue("@Status",status.Text);cmd.Parameters.AddWithValue("@AssignedTo",assigned.SelectedValue??DBNull.Value);cmd.Parameters.AddWithValue("@Resolution",resolution.Text.Trim().Length>0?(object)resolution.Text.Trim():DBNull.Value); }
    private void SelectRow(object? s,EventArgs e){if(grid.CurrentRow is null)return;var r=grid.CurrentRow;type.Text=r.Cells["Type"].Value?.ToString();status.Text=r.Cells["Status"].Value?.ToString();subject.Text=r.Cells["Subject"].Value?.ToString();message.Text=r.Cells["Message"].Value?.ToString();resolution.Text=r.Cells["ResolutionNotes"].Value?.ToString();if(r.Cells["Rating"].Value is int x)rating.Value=x;}
}
