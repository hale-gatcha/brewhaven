using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Drawing;
using static BrewHaven.DataAccess.DbHelper;

namespace BrewHaven.Forms;

/// <summary>
/// Staff page: browse the menu (category buttons + search, item cards) and build a cart (size, +/- quantity, remove),
/// choose Dine-in (table no.) / Takeout, customer or walk-in guest, and a promo. "Place Order" opens the payment pop-up
/// right away; the order is saved (as Paid) only when the payment succeeds. Unavailable and out-of-stock items can't be ordered.
/// </summary>
public sealed class PlaceOrderForm : Form
{
    private static readonly string[] Categories = ["All", "Coffee", "Non-Coffee", "Tea", "Pastries", "Meals", "Add-ons"];
    private static readonly HashSet<string> SizedCategories = new(StringComparer.OrdinalIgnoreCase) { "Coffee", "Non-Coffee", "Tea" };
    private const decimal LargeUpcharge = 20m;
    private const string MinusColumn = "colMinus", PlusColumn = "colPlus";

    private sealed class CartLine
    {
        public int MenuItemId; public string Name = ""; public string Category = ""; public decimal BasePrice; public string Size = "-"; public int Qty;
        public decimal UnitPrice => BasePrice + (Size == "Large" ? LargeUpcharge : 0m);
        public decimal LineTotal => UnitPrice * Qty;
    }

    // Left: menu
    private readonly FlowLayoutPanel pnlCategories = new() { Dock = DockStyle.Top, AutoSize = true, BackColor = Ui.Surface, Padding = new Padding(0, 0, 0, 8) };
    private readonly TextBox txtSearch = new() { Width = 260, PlaceholderText = "Search the menu..." };
    private readonly FlowLayoutPanel pnlMenu = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Surface, Padding = new Padding(0, 4, 0, 0) };
    private readonly List<RoundedButton> categoryButtons = [];
    private string category = "All";

    // Right: order
    private readonly ComboBox cboBranch = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox cboCustomer = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly RoundedButton btnNewCustomer = Ui.SecondaryButton("+ New");
    private readonly RadioButton rdoDineIn = new() { Text = "Dine-in", AutoSize = true, Checked = true, Margin = new Padding(0, 6, 14, 0) };
    private readonly RadioButton rdoTakeout = new() { Text = "Takeout", AutoSize = true, Margin = new Padding(0, 6, 18, 0) };
    private readonly NumericUpDown numTable = new() { Minimum = 1, Maximum = 99, Value = 1, Width = 60 };
    private readonly ComboBox cboPromo = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DataGridView dgvCart = Ui.Grid();
    private readonly Label lblSubtotal = new() { AutoSize = true, Font = Ui.Body, ForeColor = Ui.Muted };
    private readonly Label lblDiscount = new() { AutoSize = true, Font = Ui.Body, ForeColor = Ui.Muted };
    private readonly Label lblTotal = new() { AutoSize = true, Font = Ui.Heading, ForeColor = Ui.Text };
    private readonly RoundedButton btnPlaceOrder = Ui.PrimaryButton("Place Order");
    private readonly RoundedButton btnClearCart = Ui.GhostButton("Clear");

    private DataTable menu = new();
    private readonly List<CartLine> cart = [];
    private bool loading;

    public PlaceOrderForm()
    {
        Text = "Browse Menu and Place Order"; BackColor = Ui.Background;

        // ---- Left: categories, search, menu cards ----
        var menuCard = Ui.Card(DockStyle.Fill, 16);
        foreach (string c in Categories)
        {
            var b = Ui.SecondaryButton(c); b.Margin = new Padding(0, 0, 6, 6); b.Height = 34;
            b.Click += (_, _) => { category = c; StyleCategoryButtons(); RenderMenu(); };
            categoryButtons.Add(b); pnlCategories.Controls.Add(b);
        }
        var searchBar = Ui.Toolbar(DockStyle.Top); searchBar.Padding = new Padding(0, 0, 0, 6);
        searchBar.Controls.AddRange([Ui.InlineLabel("SEARCH"), Ui.ToolbarBox(txtSearch),
            new Label { Text = "Click an item to add it to the cart.", AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 12, 0, 0) }]);
        menuCard.Controls.Add(pnlMenu); menuCard.Controls.Add(searchBar); menuCard.Controls.Add(pnlCategories);

        // ---- Right: order options, cart, totals ----
        var orderCard = Ui.Card(DockStyle.Right, 16); orderCard.Width = 480;
        var options = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Ui.Surface };
        options.Controls.Add(Ui.Field("BRANCH", cboBranch, 440));
        var customerRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Surface, Margin = Padding.Empty };
        customerRow.Controls.Add(Ui.Field("CUSTOMER", cboCustomer, 330));
        btnNewCustomer.Margin = new Padding(0, 22, 0, 0); btnNewCustomer.Width = 92; customerRow.Controls.Add(btnNewCustomer);
        options.Controls.Add(customerRow);
        var typeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Ui.Surface, Margin = new Padding(0, 0, 0, 12) };
        var tableLabel = Ui.InlineLabel("TABLE NO."); tableLabel.Margin = new Padding(0, 10, 8, 0);
        typeRow.Controls.AddRange([Ui.InlineLabel("ORDER TYPE"), rdoDineIn, rdoTakeout, tableLabel, Ui.Boxed(numTable)]);
        options.Controls.Add(typeRow);
        options.Controls.Add(Ui.Field("PROMO", cboPromo, 440));

        var totals = new Panel { Dock = DockStyle.Bottom, Height = 150, BackColor = Ui.Surface };
        lblSubtotal.Location = new Point(0, 10); lblDiscount.Location = new Point(0, 32); lblTotal.Location = new Point(0, 56);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Surface, Padding = new Padding(0, 6, 0, 0) };
        btnPlaceOrder.Width = 200; btnPlaceOrder.Height = 44; btnPlaceOrder.Margin = new Padding(8, 0, 0, 0);
        buttons.Controls.Add(btnPlaceOrder); buttons.Controls.Add(btnClearCart);
        totals.Controls.Add(lblSubtotal); totals.Controls.Add(lblDiscount); totals.Controls.Add(lblTotal); totals.Controls.Add(buttons);

        dgvCart.Dock = DockStyle.Fill;
        var cartTitle = Ui.SectionTitle("Cart  (click a drink's size to switch Regular / Large)");
        orderCard.Controls.Add(dgvCart); orderCard.Controls.Add(cartTitle); orderCard.Controls.Add(options); orderCard.Controls.Add(totals);

        Controls.Add(menuCard); Controls.Add(Ui.Spacer(DockStyle.Right, 16)); Controls.Add(orderCard);

        Ui.AddRowButtons(dgvCart,
            new Ui.RowButton(MinusColumn, "−", Ui.RowButtonStyle.Secondary, 42),
            new Ui.RowButton(PlusColumn, "+", Ui.RowButtonStyle.Secondary, 42),
            new Ui.RowButton(Ui.RemoveColumn, "Remove", Ui.RowButtonStyle.Danger, 84));

        txtSearch.TextChanged += (_, _) => RenderMenu();
        cboBranch.SelectedIndexChanged += (_, _) => { if (!loading) { LoadMenu(); LoadPromos(); } };
        cboPromo.SelectedIndexChanged += (_, _) => RenderCart();
        rdoDineIn.CheckedChanged += (_, _) => numTable.Enabled = rdoDineIn.Checked;
        btnNewCustomer.Click += (_, _) => RegisterCustomer();
        btnPlaceOrder.Click += (_, _) => PlaceOrder();
        btnClearCart.Click += (_, _) =>
        {
            if (cart.Count > 0 && MessageBox.Show("Clear the cart?", "Clear", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            { cart.Clear(); RenderCart(); }
        };
        dgvCart.CellClick += DgvCart_CellClick;
        dgvCart.DataBindingComplete += (_, _) => FormatCart();
        Load += (_, _) =>
        {
            FontHelper.ApplyToAll(this);
            StyleCategoryButtons();
            try { EnsureSchema(); } catch (Exception ex) { MessageBox.Show($"Could not prepare the database.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            LoadBranches(); LoadCustomers(); LoadMenu(); LoadPromos(); RenderCart();
        };
    }

    private int BranchId => Convert.ToInt32(cboBranch.SelectedValue ?? 0);

    private void StyleCategoryButtons()
    {
        foreach (RoundedButton b in categoryButtons)
        {
            bool active = b.Text == category;
            b.BaseColor = active ? Ui.Primary : Ui.Surface; b.HoverColor = active ? Ui.PrimaryHover : Ui.AccentSoft;
            b.TextColor = active ? Color.White : Ui.Primary; b.BorderColor = active ? Color.Empty : Ui.Border;
            b.Invalidate();
        }
    }

    // ---- Loading ----------------------------------------------------------------------------------

    private void LoadBranches()
    {
        loading = true;
        try
        {
            DataTable branches = Query("SELECT BranchId, BranchName FROM Branches WHERE IsActive = 1 ORDER BY BranchName");
            cboBranch.DisplayMember = "BranchName"; cboBranch.ValueMember = "BranchId"; cboBranch.DataSource = branches;
            if (Session.CurrentUser?.BranchId is int mine && branches.Select($"BranchId = {mine}").Length > 0)
            {
                cboBranch.SelectedValue = mine;
                cboBranch.Enabled = false;   // staff assigned to a branch always order for that branch
            }
        }
        catch (Exception ex) { MessageBox.Show($"Could not load branches.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { loading = false; }
    }

    private void LoadCustomers(int? select = null)
    {
        try
        {
            DataTable customers = Query("""
                SELECT CustomerId, FullName + ' · ' + ISNULL(CustomerCode, '') + ' · ' + ISNULL(ContactNumber, '') AS Display
                FROM Customers WHERE IsActive = 1 ORDER BY FullName
                """);
            DataRow guest = customers.NewRow(); guest["CustomerId"] = 0; guest["Display"] = "Walk-in guest";
            customers.Rows.InsertAt(guest, 0);
            cboCustomer.DisplayMember = "Display"; cboCustomer.ValueMember = "CustomerId"; cboCustomer.DataSource = customers;
            cboCustomer.SelectedValue = select ?? 0;
        }
        catch (Exception ex) { MessageBox.Show($"Could not load customers.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LoadMenu()
    {
        try
        {
            // Stock is NULL for menu items that aren't linked to this branch's inventory (not stock-tracked).
            menu = Query("""
                SELECT m.MenuItemId, m.ItemName, m.Category, ISNULL(m.Description, '') AS Description, m.Price, m.IsAvailable, inv.Stock
                FROM MenuItems m
                OUTER APPLY (SELECT SUM(i.Quantity) AS Stock FROM Inventory i WHERE i.MenuItemId = m.MenuItemId AND i.BranchId = @BranchId) inv
                ORDER BY m.Category, m.ItemName
                """, P("@BranchId", SqlDbType.Int, BranchId));
            RenderMenu();
        }
        catch (Exception ex) { MessageBox.Show($"Could not load the menu.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void LoadPromos()
    {
        try
        {
            DataTable promos = Query("""
                SELECT PromotionId, PromotionName + ' (' + CASE WHEN DiscountType = 'Fixed' THEN 'PHP ' + CONVERT(nvarchar(20), DiscountValue)
                       ELSE CONVERT(nvarchar(20), CAST(DiscountValue AS decimal(5,2))) + '%' END + ' off)' AS Name, DiscountType, DiscountValue
                FROM Promotions
                WHERE IsActive = 1 AND CAST(GETDATE() AS date) BETWEEN StartDate AND EndDate
                  AND (BranchId IS NULL OR BranchId = @BranchId) AND ISNULL(PointsRequired, 0) = 0
                ORDER BY PromotionName
                """, P("@BranchId", SqlDbType.Int, BranchId));
            foreach (DataColumn column in promos.Columns) column.AllowDBNull = true;   // DiscountType is NOT NULL in the database; the "No promo" row leaves it empty
            DataRow none = promos.NewRow(); none["PromotionId"] = 0; none["Name"] = "No promo";
            promos.Rows.InsertAt(none, 0);
            cboPromo.DisplayMember = "Name"; cboPromo.ValueMember = "PromotionId"; cboPromo.DataSource = promos;
        }
        catch (Exception ex) { MessageBox.Show($"Could not load promos.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    // ---- Menu cards ---------------------------------------------------------------------------------

    private void RenderMenu()
    {
        string term = txtSearch.Text.Trim();
        pnlMenu.SuspendLayout();
        foreach (Control old in pnlMenu.Controls.Cast<Control>().ToList()) { pnlMenu.Controls.Remove(old); old.Dispose(); }
        int shown = 0;
        foreach (DataRow r in menu.Rows)
        {
            string cat = (string)r["Category"], name = (string)r["ItemName"], description = (string)r["Description"];
            if (category != "All" && !string.Equals(cat, category, StringComparison.OrdinalIgnoreCase)) continue;
            if (term.Length > 0 && !name.Contains(term, StringComparison.OrdinalIgnoreCase) && !description.Contains(term, StringComparison.OrdinalIgnoreCase)) continue;
            var card = new MenuCard((int)r["MenuItemId"], name, cat, (decimal)r["Price"], description, (bool)r["IsAvailable"], r["Stock"] as int?);
            card.Click += (_, _) => AddToCart(card);
            pnlMenu.Controls.Add(card);
            shown++;
        }
        if (shown == 0)
            pnlMenu.Controls.Add(new Label { Text = menu.Rows.Count == 0 ? "The menu is empty. Ask a manager to add menu items." : "No items match.", AutoSize = true, Font = Ui.Body, ForeColor = Ui.Muted, Margin = new Padding(4, 8, 0, 0) });
        pnlMenu.ResumeLayout(true);
    }

    private int? StockFor(int menuItemId) =>
        menu.Rows.Cast<DataRow>().Where(r => (int)r["MenuItemId"] == menuItemId).Select(r => r["Stock"] as int?).FirstOrDefault();

    private int QtyInCart(int menuItemId) => cart.Where(l => l.MenuItemId == menuItemId).Sum(l => l.Qty);

    private bool HasStockFor(int menuItemId, string name, int extra)
    {
        if (StockFor(menuItemId) is int stock && QtyInCart(menuItemId) + extra > stock)
        {
            MessageBox.Show(stock <= 0 ? $"{name} is out of stock." : $"Only {stock} {name} left in stock.", "Not Enough Stock", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        return true;
    }

    private void AddToCart(MenuCard card)
    {
        if (!card.CanOrder)
        {
            MessageBox.Show($"{card.ItemName} is {card.StatusText.ToLowerInvariant()} and can't be ordered right now.", "Not Available", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!HasStockFor(card.MenuItemId, card.ItemName, 1)) return;
        string size = SizedCategories.Contains(card.Category) ? "Regular" : "-";
        CartLine? line = cart.FirstOrDefault(l => l.MenuItemId == card.MenuItemId && l.Size == size);
        if (line is null) cart.Add(new CartLine { MenuItemId = card.MenuItemId, Name = card.ItemName, Category = card.Category, BasePrice = card.Price, Size = size, Qty = 1 });
        else line.Qty++;
        RenderCart();
    }

    // ---- Cart ---------------------------------------------------------------------------------------

    private decimal Subtotal() => cart.Sum(l => l.LineTotal);

    private decimal Discount() =>
        cboPromo.SelectedItem is DataRowView p && Convert.ToInt32(p["PromotionId"]) != 0
            ? OrderRepository.PromoDiscount((string)p["DiscountType"], Convert.ToDecimal(p["DiscountValue"]), Subtotal()) : 0m;

    private void RenderCart()
    {
        var table = new DataTable();
        table.Columns.Add("Line", typeof(int)); table.Columns.Add("Item"); table.Columns.Add("Size");
        table.Columns.Add("Qty", typeof(int)); table.Columns.Add("Price", typeof(decimal)); table.Columns.Add("Total", typeof(decimal));
        for (int i = 0; i < cart.Count; i++)
            table.Rows.Add(i, cart[i].Name, cart[i].Size, cart[i].Qty, cart[i].UnitPrice, cart[i].LineTotal);
        dgvCart.DataSource = table;

        decimal subtotal = Subtotal(), discount = Discount();
        lblSubtotal.Text = $"Subtotal:  PHP {subtotal:N2}   ({cart.Sum(l => l.Qty)} item(s))";
        lblDiscount.Text = $"Discount:  PHP {discount:N2}";
        lblTotal.Text = $"Total:  PHP {Math.Max(0m, subtotal - discount):N2}";
        btnPlaceOrder.Enabled = cart.Count > 0;
    }

    private void FormatCart()
    {
        var c = dgvCart.Columns;
        if (c.Contains("Line")) c["Line"]!.Visible = false;
        foreach (string money in new[] { "Price", "Total" })
            if (c.Contains(money)) { c[money]!.DefaultCellStyle.Format = "N2"; c[money]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight; }
        if (c.Contains("Qty")) { c["Qty"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter; c["Qty"]!.FillWeight = 40; }
        if (c.Contains("Size")) c["Size"]!.FillWeight = 65;
        if (c.Contains("Item")) c["Item"]!.FillWeight = 150;
    }

    private void DgvCart_CellClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || dgvCart.Rows[e.RowIndex].Cells["Line"].Value is not int index || index >= cart.Count) return;
        CartLine line = cart[index];
        switch (dgvCart.Columns[e.ColumnIndex].Name)
        {
            case PlusColumn:
                if (!HasStockFor(line.MenuItemId, line.Name, 1)) return;
                line.Qty++;
                break;
            case MinusColumn:
                if (--line.Qty <= 0) cart.RemoveAt(index);
                break;
            case Ui.RemoveColumn:
                cart.RemoveAt(index);
                break;
            case "Size":
                if (line.Size == "-") return;   // not a drink
                string newSize = line.Size == "Regular" ? "Large" : "Regular";
                CartLine? same = cart.FirstOrDefault(l => l != line && l.MenuItemId == line.MenuItemId && l.Size == newSize);
                if (same is not null) { same.Qty += line.Qty; cart.Remove(line); }   // merge with the existing line of that size
                else line.Size = newSize;
                break;
            default:
                return;
        }
        RenderCart();
    }

    // ---- Customer / order ---------------------------------------------------------------------------

    private void RegisterCustomer()
    {
        using var dialog = new CustomerDialog();
        if (dialog.ShowDialog(Ui.DialogOwner(this)) == DialogResult.OK) LoadCustomers(dialog.SavedCustomerId);
    }


    /// <summary>
    /// Place Order: validate, then go straight to the payment pop-up. Nothing is saved here; the payment pop-up saves
    /// the order, payment, stock and points together. Cancel leaves the cart untouched.
    /// </summary>
    private void PlaceOrder()
    {
        // a) Validate
        if (cart.Count == 0) { MessageBox.Show("Add at least one item to the cart.", "Place Order", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (BranchId == 0) { MessageBox.Show("Select a branch.", "Place Order", MessageBoxButtons.OK, MessageBoxIcon.Warning); cboBranch.Focus(); return; }
        if (!rdoDineIn.Checked && !rdoTakeout.Checked) { MessageBox.Show("Choose Dine-in or Takeout.", "Place Order", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (rdoDineIn.Checked && numTable.Value < 1) { MessageBox.Show("Enter the table number for dine-in.", "Place Order", MessageBoxButtons.OK, MessageBoxIcon.Warning); numTable.Focus(); return; }
        if (cboCustomer.SelectedValue is null) { MessageBox.Show("Select a customer or \"Walk-in guest\".", "Place Order", MessageBoxButtons.OK, MessageBoxIcon.Warning); cboCustomer.Focus(); return; }

        int customerId = Convert.ToInt32(cboCustomer.SelectedValue);
        int promoId = cboPromo.SelectedValue is null ? 0 : Convert.ToInt32(cboPromo.SelectedValue);
        var lines = cart.Select(l => new OrderDraftLine(l.MenuItemId, l.Name, l.Size == "-" ? null : l.Size, l.Qty, l.UnitPrice)).ToList();

        try
        {
            // Unavailable / out-of-stock check against the database (nothing is saved).
            OrderRepository.CheckAvailability(BranchId, lines);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Can't Place Order", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadMenu();   // refresh the cards so the item shows as unavailable / out of stock
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not check the items.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        int points = 0;
        string customerName = "Walk-in guest";
        if (customerId != 0)
        {
            try
            {
                CustomerRecord? c = CustomerRepository.Load(customerId);
                if (c is null) { MessageBox.Show("That customer no longer exists. Choose another.", "Place Order", MessageBoxButtons.OK, MessageBoxIcon.Warning); LoadCustomers(); return; }
                points = c.LoyaltyPoints; customerName = $"{c.FullName} ({c.CustomerCode})";
            }
            catch (Exception ex) { MessageBox.Show($"Could not load the customer.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        }

        var draft = new OrderDraft
        {
            BranchId = BranchId, BranchName = cboBranch.Text,
            CustomerId = customerId == 0 ? null : customerId, CustomerName = customerName, CustomerPoints = points,
            OrderType = rdoDineIn.Checked ? "Dine-In" : "Takeout", TableNumber = rdoDineIn.Checked ? (int)numTable.Value : null,
            PromotionId = promoId == 0 ? null : promoId, Lines = lines
        };

        // b) Payment pop-up right away. Cancel = nothing saved, cart kept as it is.
        int orderId; decimal change;
        using (var payment = new PaymentDialog(draft))
        {
            if (payment.ShowDialog(Ui.DialogOwner(this)) != DialogResult.OK) return;
            orderId = payment.OrderId; change = payment.ChangeGiven;
        }

        // c) Paid: receipt, then reset the page for the next customer.
        MessageBox.Show($"Payment complete. Order #{orderId} is paid." + (change > 0 ? $"\n\nChange: PHP {change:N2}" : ""),
            "Payment Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        ReceiptPrinter.ShowReceipt(Ui.DialogOwner(this), orderId);

        cart.Clear();
        rdoDineIn.Checked = true; numTable.Value = 1;
        LoadCustomers();            // points changed
        cboPromo.SelectedIndex = 0;
        RenderCart();
        LoadMenu();                 // stock changed
    }
}
