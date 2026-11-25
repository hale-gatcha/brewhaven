using BrewHaven.Helpers;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace BrewHaven.Forms;

public sealed class DashboardForm : Form
{
    private readonly Panel body = new() { Dock = DockStyle.Fill, Padding = new Padding(28, 8, 28, 28), BackColor = Ui.Background };
    private readonly Label pageTitle = new() { AutoSize = true, Font = Ui.Heading, ForeColor = Color.White, BackColor = Ui.Header, Location = new Point(28, 14), UseMnemonic = false };
    private readonly Label pageSubtitle = new() { AutoSize = true, Font = Ui.Body, ForeColor = Ui.OnDarkMuted, BackColor = Ui.Header, Location = new Point(30, 50), UseMnemonic = false };
    private readonly List<(NavButton Button, string Module)> navButtons = [];
    private readonly List<(string Text, string Module)> modules = [];
    // No role has a Home page any more: each sidebar lists only that role's own pages (plus Log out).
    private readonly bool noHomePage = Session.CurrentUser?.RoleName is "Super Admin" or "Admin" or "Manager" or "Staff";

    /// <summary>True when the window closed because the user clicked Log out (not the window's X).</summary>
    public bool LoggedOut { get; private set; }

    public DashboardForm()
    {
        Text = $"Brew Haven - {Session.CurrentUser?.FullName}"; WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(1100, 680); BackColor = Ui.Background; Font = Ui.Body;
        Load += (_, _) => FontHelper.ApplyToAll(this);

        // ---- Sidebar ----
        var sidebar = new Panel { Dock = DockStyle.Left, Width = 256, BackColor = Ui.Sidebar, Padding = new Padding(16, 0, 16, 16) };
        var brand = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Ui.Sidebar };
        brand.Paint += PaintBrand;
        var menuLabel = new Label { Text = "MENU", Dock = DockStyle.Top, Height = 26, Font = Ui.SmallBold, ForeColor = Ui.OnDarkMuted, Padding = new Padding(4, 0, 0, 0) };
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Ui.Sidebar };
        nav.Resize += (_, _) => { foreach (Control c in nav.Controls) c.Width = nav.ClientSize.Width - 2; };

        string role = Session.CurrentUser?.RoleName ?? "";
        // Super Admin sees only these two pages (create / edit / remove through pop-up dialogs).
        if (role is "Super Admin") { Add("Subscription and T&C", "SA_Subscriptions"); Add("Manage Users", "SA_Users"); }
        // Admin: the same Subscription and Users pages as Super Admin (Users limited to Manager / Staff accounts),
        // plus Customer Feedback, Manage Branches and Reports.
        if (role is "Admin")
        {
            Add("Subscription and T&C", "SA_Subscriptions"); Add("Manage Users", "SA_Users");
            Add("Customer Feedback", "AD_Feedback"); Add("Manage Branches", "AD_Branches"); Add("Reports", "AD_Reports");
        }
        // Manager: Customer Feedback is the same page Admin uses; Reports is Admin's page limited to the Manager's branch.
        if (role is "Manager")
        {
            Add("Customer Feedback", "AD_Feedback"); Add("View Customer History", "MG_History"); Add("Redeem Rewards", "MG_Rewards");
            Add("Manage Promotions", "MG_Promotions"); Add("View Sales", "MG_Sales"); Add("Inventory", "MG_Inventory"); Add("Reports", "MG_Reports");
        }
        if (role is "Staff")
        {
            Add("Customer Registration", "ST_Customers"); Add("Browse Menu & Place Order", "ST_Order");
            Add("Record Customer Info", "ST_QuickCustomer");
        }

        // Add any missing columns/tables the pages rely on (once per run; harmless if already done).
        try { DataAccess.DbHelper.EnsureSchema(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not update the database for the latest pages. Some pages may show errors.\n{ex.Message}",
                "Database Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (!noHomePage)
        { var home = new NavButton("Home") { Width = 222 }; home.Click += (_, _) => ShowHome(); nav.Controls.Add(home); navButtons.Add((home, "Home")); }
        foreach (var (text, module) in modules)
        { var b = new NavButton(text) { Width = 222 }; b.Click += (_, _) => OpenModule(module, text); nav.Controls.Add(b); navButtons.Add((b, module)); }

        // User card + logout at the bottom of the sidebar
        var userCard = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Ui.Sidebar };
        userCard.Paint += PaintUserCard;
        var logout = new NavButton("Log out") { Dock = DockStyle.Bottom };
        logout.Click += (_, _) => { LoggedOut = true; Session.CurrentUser = null; Close(); };
        var divider = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Ui.SidebarHover };

        // Docking runs from the last-added control inwards, so add the Fill control first.
        sidebar.Controls.Add(nav); sidebar.Controls.Add(menuLabel); sidebar.Controls.Add(brand);
        sidebar.Controls.Add(divider); sidebar.Controls.Add(userCard); sidebar.Controls.Add(Ui.Spacer(DockStyle.Bottom, 8)); sidebar.Controls.Add(logout);

        // ---- Header ----
        var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Ui.Header };
        var dateLabel = new Label { Text = DateTime.Now.ToString("dddd, MMMM d"), AutoSize = true, Font = Ui.BodyBold, ForeColor = Ui.OnDarkMuted, BackColor = Ui.Header, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        header.Controls.Add(pageTitle); header.Controls.Add(pageSubtitle); header.Controls.Add(dateLabel);
        header.Resize += (_, _) => dateLabel.Location = new Point(header.Width - dateLabel.Width - 30, 30);
        body.Padding = new Padding(28, 20, 28, 28);

        Controls.Add(body); Controls.Add(header); Controls.Add(sidebar);
        // No Home page for Super Admin / Admin: open their first page straight away.
        if (noHomePage && modules.Count > 0) OpenModule(modules[0].Module, modules[0].Text);
        else ShowHome();
    }

    private void Add(string text, string module) => modules.Add((text, module));

    private void SetActive(string module)
    { foreach (var (b, m) in navButtons) b.Active = m == module; }

    private void SetHeader(string title, string subtitle) { pageTitle.Text = title; pageSubtitle.Text = subtitle; }

    private void OpenModule(string module, string title)
    {
        Form? form = module switch
        {
            "Users" => new CrudForm(CrudForm.UserDefinition), "Subscriptions" => new CrudForm(CrudForm.SubscriptionDefinition),
            "Branches" => new CrudForm(CrudForm.BranchDefinition), "Customers" => new CrudForm(CrudForm.CustomerDefinition),
            "Promotions" => new CrudForm(CrudForm.PromotionDefinition), "Rewards" => new CrudForm(CrudForm.RewardDefinition),
            "MenuItems" => new CrudForm(CrudForm.MenuItemDefinition), "Preferences" => new CrudForm(CrudForm.PreferenceDefinition),
            "Inventory" => new CrudForm(CrudForm.InventoryDefinition), "Feedback" => new FeedbackForm(),
            "Orders" => new OrderForm(), "Sales" or "Reports" => new SalesReportForm(),
            "CustomerHistory" => new CustomerHistoryForm(),
            "SA_Subscriptions" => new SubscriptionListForm(), "SA_Users" => new UserListForm(),
            "AD_Feedback" => new FeedbackListForm(), "AD_Branches" => new BranchListForm(), "AD_Reports" => new ReportsForm(),
            "MG_History" => new CustomerHistoryForm(), "MG_Rewards" => new RedeemRewardsForm(), "MG_Promotions" => new PromotionListForm(),
            "MG_Sales" => new SalesViewForm(), "MG_Inventory" => new InventoryListForm(),
            "MG_Reports" => new ReportsForm(Session.CurrentUser?.BranchId ?? 0),
            "ST_Customers" => new CustomerListForm(), "ST_Order" => new PlaceOrderForm(),
            "ST_QuickCustomer" => new QuickCustomerForm(),
            _ => null
        };
        if (form is null) return;
        SetActive(module);
        SetHeader(title, Subtitle(module));
        form.TopLevel = false; form.FormBorderStyle = FormBorderStyle.None; form.Dock = DockStyle.Fill; form.BackColor = Ui.Background;
        ClearBody(); body.Controls.Add(form); form.Show();
    }

    private static string Subtitle(string module) => module switch
    {
        "Users" => "Create accounts and assign roles and branches",
        "Subscriptions" => "Plans, pricing and terms & conditions",
        "Branches" => "Locations and contact details",
        "Customers" => "Walk-in customer records and loyalty points",
        "Promotions" => "Discount campaigns and their schedules",
        "Rewards" => "Loyalty rewards customers can redeem",
        "MenuItems" => "Drinks, food and pricing",
        "Preferences" => "What your regulars usually order",
        "Inventory" => "Stock levels per branch",
        "Feedback" => "Track feedback, inquiries and complaints to resolution",
        "Orders" => "Build an order and take payment",
        "Sales" or "Reports" => "Completed sales by day, week or month",
        "CustomerHistory" => "Past orders for a customer",
        "SA_Subscriptions" => "Create, edit and remove subscription plans and their terms & conditions",
        "SA_Users" => UserDialog.AdminRestricted
            ? "Create, edit and remove Manager and Staff accounts"
            : "Create, edit and remove user accounts, roles and branches",
        "AD_Feedback" => "Feedback, inquiries and complaints, colour-coded by status",
        "AD_Branches" => "Branch locations, contacts and branch managers",
        "AD_Reports" => "Sales, orders, feedback and branch summaries by date range",
        "MG_History" => "A customer's past orders and preferences (double-click an order for details)",
        "MG_Rewards" => "Redeem loyalty points for rewards and points promos",
        "MG_Promotions" => "Create and update promos (set Inactive instead of removing)",
        "MG_Sales" => "Paid sales by day, week or month, top sellers and promo usage",
        "MG_Inventory" => "Stock levels and Stock In / Stock Out (see the Stock Status column)",
        "MG_Reports" => "Sales, orders, feedback and branch summary for your branch",
        "ST_Customers" => "Register walk-in customers (a customer code is generated automatically)",
        "ST_Order" => "Browse the menu, build the cart, then take payment right away",
        "ST_QuickCustomer" => "Quickly record a customer's name, contact number and email",
        _ => ""
    };

    private void ClearBody()
    {
        foreach (Control c in body.Controls.Cast<Control>().ToList()) { body.Controls.Remove(c); c.Dispose(); }
    }

    private void ShowHome()
    {
        SetActive("Home");
        var name = Session.CurrentUser?.FullName ?? "";
        int hour = DateTime.Now.Hour;
        string greet = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
        SetHeader($"{greet}, {name.Split(' ').FirstOrDefault()}", $"Signed in as {Session.CurrentUser?.RoleName}. Pick a module to get started.");
        ClearBody();

        var tiles = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Background, Padding = new Padding(0, 4, 0, 0) };
        foreach (var (text, module) in modules)
        {
            var tile = Ui.Card(DockStyle.None, 18); tile.Size = new Size(260, 120); tile.Margin = new Padding(0, 0, 18, 18); tile.Cursor = Cursors.Hand;
            var t = new Label { Text = text, Dock = DockStyle.Top, Height = 30, Font = Ui.SubHeading, ForeColor = Ui.Text, Cursor = Cursors.Hand, UseMnemonic = false };
            var d = new Label { Text = Subtitle(module), Dock = DockStyle.Fill, Font = Ui.Body, ForeColor = Ui.Muted, Cursor = Cursors.Hand };
            tile.Controls.Add(d); tile.Controls.Add(t);
            EventHandler open = (_, _) => OpenModule(module, text);
            tile.Click += open; t.Click += open; d.Click += open;
            EventHandler enter = (_, _) => { tile.Fill = Ui.AccentSoft; t.BackColor = d.BackColor = Ui.AccentSoft; tile.Invalidate(); };
            EventHandler leave = (_, _) => { if (tile.ClientRectangle.Contains(tile.PointToClient(Cursor.Position))) return; tile.Fill = Ui.Surface; t.BackColor = d.BackColor = Ui.Surface; tile.Invalidate(); };
            foreach (Control c in new Control[] { tile, t, d }) { c.MouseEnter += enter; c.MouseLeave += leave; }
            tiles.Controls.Add(tile);
        }
        body.Controls.Add(tiles);
    }

    private void PaintBrand(object? s, PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Ui.Accent)) using (var p = Ui.RoundRect(new RectangleF(4, 24, 36, 36), 10)) g.FillPath(b, p);
        TextRenderer.DrawText(g, "BH", Ui.BodyBold, new Rectangle(4, 24, 36, 36), Ui.Sidebar, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "Brew Haven", Ui.SubHeading, new Point(50, 22), Color.White);
        TextRenderer.DrawText(g, "Coffee Shop", Ui.Small, new Point(51, 44), Ui.OnDarkMuted);
    }

    private void PaintUserCard(object? s, PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        string name = Session.CurrentUser?.FullName ?? "";
        string initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
        using (var b = new SolidBrush(Ui.SidebarActive)) g.FillEllipse(b, 4, 14, 38, 38);
        TextRenderer.DrawText(g, initials, Ui.SmallBold, new Rectangle(4, 14, 38, 38), Ui.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        var w = ((Control)s!).Width - 60;
        TextRenderer.DrawText(g, name, Ui.BodyBold, new Rectangle(52, 14, w, 20), Color.White, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, Session.CurrentUser?.RoleName ?? "", Ui.Small, new Rectangle(52, 34, w, 18), Ui.OnDarkMuted, TextFormatFlags.EndEllipsis);
    }
}
