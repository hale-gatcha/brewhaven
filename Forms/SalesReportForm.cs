using Microsoft.Data.SqlClient;
using BrewHaven.DataAccess;
using System.Data;
using System.Drawing;
using System.Text;

namespace BrewHaven.Forms;

public sealed class SalesReportForm : Form
{
    private readonly DateTimePicker date=new(){Value=DateTime.Today};
    private readonly ComboBox period=new(){DropDownStyle=ComboBoxStyle.DropDownList};
    private readonly DataGridView grid=Ui.Grid();
    private readonly Label total=new(){AutoSize=true,Font=Ui.SubHeading,ForeColor=Ui.Primary,BackColor=Ui.Surface,Margin=new Padding(24,8,0,0)};

    public SalesReportForm()
    {
        Text="Sales and Reports";BackColor=Ui.Background; Load+=(_,_)=>Helpers.FontHelper.ApplyToAll(this);
        period.Items.AddRange(["Daily","Weekly","Monthly"]);period.SelectedIndex=0;
        var top=Ui.Card(DockStyle.Top,12); top.Height=64;
        var bar=Ui.Toolbar(DockStyle.Top); bar.SizeChanged+=(_,_)=>top.Height=bar.Height+top.Padding.Vertical;
        date.Width=150; period.Width=120;
        Control Box(Control c){ var b=Ui.Boxed(c); b.Margin=new Padding(0,3,20,0); return b; }
        var view=Ui.PrimaryButton("View");view.Click+=(_,_)=>LoadSales();
        var csv=Ui.SecondaryButton("Export CSV");csv.Click+=ExportCsv;
        bar.Controls.AddRange([Ui.InlineLabel("DATE"),Box(date),Ui.InlineLabel("PERIOD"),Box(period),view,csv,total]);
        top.Controls.Add(bar);
        Controls.Add(Ui.GridCard(grid));Controls.Add(Ui.Spacer());Controls.Add(top);
        grid.DataBindingComplete+=(_,_)=>{ if(grid.Columns.Contains("Sales")){grid.Columns["Sales"]!.DefaultCellStyle.Format="C";grid.Columns["Sales"]!.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;}
            if(grid.Columns.Contains("Period"))grid.Columns["Period"]!.DefaultCellStyle.Format="MMM d, yyyy"; };
        LoadSales();
    }

    private void LoadSales()
    { try{using SqlConnection c=DbHelper.CreateConnection();c.Open();string sql=period.Text switch{"Weekly"=>"SELECT CAST(DATEADD(DAY,-(DATEPART(WEEKDAY,OrderDate)-1),CAST(OrderDate AS date)) AS date) Period,COUNT(*) Orders,SUM(Total) Sales FROM Orders WHERE OrderDate>=@From AND OrderDate<@To AND Status='Completed' GROUP BY CAST(DATEADD(DAY,-(DATEPART(WEEKDAY,OrderDate)-1),CAST(OrderDate AS date)) AS date)","Monthly"=>"SELECT CAST(DATEFROMPARTS(YEAR(OrderDate),MONTH(OrderDate),1) AS date) Period,COUNT(*) Orders,SUM(Total) Sales FROM Orders WHERE OrderDate>=@From AND OrderDate<@To AND Status='Completed' GROUP BY YEAR(OrderDate),MONTH(OrderDate)",_=>"SELECT CAST(OrderDate AS date) Period,COUNT(*) Orders,SUM(Total) Sales FROM Orders WHERE OrderDate>=@From AND OrderDate<@To AND Status='Completed' GROUP BY CAST(OrderDate AS date)"};
        DateTime from=period.Text switch{"Weekly"=>date.Value.Date.AddDays(-(int)date.Value.Date.DayOfWeek),"Monthly"=>new DateTime(date.Value.Year,date.Value.Month,1),_=>date.Value.Date};DateTime to=period.Text switch{"Weekly"=>from.AddDays(7),"Monthly"=>from.AddMonths(1),_=>from.AddDays(1)};using SqlCommand cmd=new(sql,c);cmd.Parameters.AddWithValue("@From",from);cmd.Parameters.AddWithValue("@To",to);using SqlDataReader r=cmd.ExecuteReader();var dt=new DataTable();dt.Load(r);grid.DataSource=dt;decimal sum=0m;foreach(DataRow row in dt.Rows)sum+=Convert.ToDecimal(row["Sales"]);total.Text=$"Total Sales: {sum:C}";}catch(Exception ex){MessageBox.Show($"Could not load sales.\n{ex.Message}","Database Error",MessageBoxButtons.OK,MessageBoxIcon.Error);} }

    private void ExportCsv(object? s,EventArgs e)
    { if(grid.DataSource is not DataTable dt)return;using var dialog=new SaveFileDialog{Filter="CSV (*.csv)|*.csv",FileName=$"BrewHaven-{period.Text}-{date.Value:yyyyMMdd}.csv"};if(dialog.ShowDialog()!=DialogResult.OK)return;try{var b=new StringBuilder();b.AppendLine(string.Join(",",dt.Columns.Cast<DataColumn>().Select(c=>c.ColumnName)));foreach(DataRow r in dt.Rows){var values=r.ItemArray.Select(v=>"\""+(v?.ToString()?.Replace("\"","\"\"")??"")+"\"");b.AppendLine(string.Join(",",values));}File.WriteAllText(dialog.FileName,b.ToString(),Encoding.UTF8);MessageBox.Show("CSV exported.","Export",MessageBoxButtons.OK,MessageBoxIcon.Information);}catch(Exception ex){MessageBox.Show(ex.Message,"Export Error",MessageBoxButtons.OK,MessageBoxIcon.Error);} }
}
