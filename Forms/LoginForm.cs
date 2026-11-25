using BrewHaven.DataAccess;
using BrewHaven.Helpers;
using System.Drawing;

namespace BrewHaven.Forms;

public sealed class LoginForm : Form
{
    private readonly ComboBox shop = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox username = new() { Dock = DockStyle.Fill };
    private readonly TextBox password = new() { Dock = DockStyle.Fill, PasswordChar = '•' };

    public LoginForm()
    {
        Text = "Brew Haven - Log in"; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(860, 520); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
        BackColor = Ui.Surface; Font = Ui.Body;
        Load += (_, _) => FontHelper.ApplyToAll(this);

        // Brand panel (left)
        var brand = new BrandPanel { Dock = DockStyle.Left, Width = 360 };

        // Form panel (right)
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Surface, Padding = new Padding(64, 0, 64, 0) };
        var stack = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Ui.Surface };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        stack.Controls.Add(new Label { Text = "Welcome back", AutoSize = true, Font = Ui.Heading, ForeColor = Ui.Text, Margin = new Padding(0, 0, 0, 2) });
        stack.Controls.Add(new Label { Text = "Log in to your Brew Haven account", AutoSize = true, Font = Ui.Body, ForeColor = Ui.Muted, Margin = new Padding(0, 0, 0, 26) });
        foreach (var (label, box) in new (string, Control)[] { ("SHOP", shop), ("USERNAME", username), ("PASSWORD", password) })
        { var f = Ui.Field(label, box, 340); f.Margin = new Padding(0, 0, 0, 14); stack.Controls.Add(f); }

        var login = Ui.PrimaryButton("Log in"); login.Width = 340; login.Height = 42; login.Margin = new Padding(0, 10, 0, 8); login.Click += LoginClick;
        var cancel = Ui.GhostButton("Exit"); cancel.Width = 340; cancel.Margin = Padding.Empty; cancel.Click += (_,_) => Close();
        stack.Controls.Add(login); stack.Controls.Add(cancel);
        right.Controls.Add(stack);
        right.Resize += (_,_) => stack.Location = new Point(right.Padding.Left, Math.Max(0, (right.ClientSize.Height - stack.Height) / 2));

        Controls.Add(right); Controls.Add(brand);
        AcceptButton = login;
        Shown += (_,_) => { stack.Location = new Point(right.Padding.Left, Math.Max(0, (right.ClientSize.Height - stack.Height) / 2)); LoadShops(); username.Focus(); };
    }

    /// <summary>Fills the Shop dropdown from MasterCRM.</summary>
    private void LoadShops()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            var tenants = new TenantRepository().GetActiveTenants();
            shop.Items.Clear();
            foreach (var t in tenants) shop.Items.Add(t);
            if (shop.Items.Count > 0) shop.SelectedIndex = 0;
            else MessageBox.Show("No active shops are set up in MasterCRM.", "No shops", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Cannot connect to MasterCRM to load the shop list.\n{ex.Message}", "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { Cursor = Cursors.Default; }
    }

    /// <summary>Dark espresso panel with the brand mark and tagline.</summary>
    private sealed class BrandPanel : Panel
    {
        private static readonly Image? Logo = LoadLogo();

        private static Image? LoadLogo()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "logo.jpeg");
            if (!File.Exists(path)) return null;
            // Load via a memory copy so the file isn't locked while the app runs.
            using var ms = new MemoryStream(File.ReadAllBytes(path));
            using var src = Image.FromStream(ms);
            return new Bitmap(src);
        }

        /// <summary>Square region around the round emblem (logo.jpeg is 1701x1632 with a white border).</summary>
        private static Rectangle LogoCrop(Image img)
        {
            int cx = img.Width * 855 / 1701, cy = img.Height * 790 / 1632, r = img.Width * 640 / 1701;
            return new Rectangle(cx - r, cy - r, r * 2, r * 2);
        }

        public BrandPanel() { DoubleBuffered = true; ResizeRedraw = true; BackColor = Ui.Sidebar; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // Soft decorative circles
            using (var b = new SolidBrush(Color.FromArgb(28, Ui.Accent))) { g.FillEllipse(b, Width - 220, -90, 320, 320); g.FillEllipse(b, -120, Height - 200, 300, 300); }
            // Logo: circular badge cropped from logo.jpeg
            const int size = 170;
            var dest = new Rectangle((Width - size) / 2, 36, size, size);
            if (Logo is not null)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                using var clip = new System.Drawing.Drawing2D.GraphicsPath();
                clip.AddEllipse(dest);
                var state = g.Save();
                g.SetClip(clip);
                g.DrawImage(Logo, dest, LogoCrop(Logo), GraphicsUnit.Pixel);
                g.Restore(state);
            }
            var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, "Brew Haven", Ui.Display, new Rectangle(0, dest.Bottom + 18, Width, 50), Color.White, center);
            TextRenderer.DrawText(g, "COFFEE SHOP", Ui.SmallBold, new Rectangle(0, dest.Bottom + 72, Width, 20), Ui.Accent, center);
            var r = new Rectangle(46, Height - 120, Width - 92, 80);
            TextRenderer.DrawText(g, "Orders, customers, rewards and reports all in one place.", Ui.Body, r, Ui.OnDarkMuted,
                TextFormatFlags.WordBreak | TextFormatFlags.Left);
        }
    }

    private void LoginClick(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(username.Text) || string.IsNullOrWhiteSpace(password.Text))
        { MessageBox.Show("Username and password are required.","Validation",MessageBoxButtons.OK,MessageBoxIcon.Warning); return; }
        if (shop.SelectedItem is not Tenant tenant)
        {
            if (shop.Items.Count == 0) LoadShops();   // retry if MasterCRM was unreachable at startup
            MessageBox.Show("Please select a shop.","Validation",MessageBoxButtons.OK,MessageBoxIcon.Warning); return;
        }
        try
        {
            DbHelper.UseTenant(tenant);
            var repo = new UserRepository();
            string? hash = repo.GetPasswordHash(username.Text);
            if (hash is null || !PasswordHasher.Verify(password.Text, hash))
            { MessageBox.Show("Invalid username or password.","Login failed",MessageBoxButtons.OK,MessageBoxIcon.Warning); return; }
            var account = repo.Authenticate(username.Text);
            if (account is null) throw new InvalidOperationException("Unable to load the authenticated account.");
            Session.CurrentUser = account;
            Hide();
            var dashboard = new DashboardForm();
            dashboard.FormClosed += (_,_) =>
            {
                if (dashboard.LoggedOut)
                {
                    // Back to a fresh login screen; the username stays filled in for convenience.
                    password.Clear();
                    Show(); Activate();
                    password.Focus();
                }
                else Close(); // dashboard closed with the window's X: exit the app
            };
            dashboard.Show();
        }
        catch (Exception ex) { MessageBox.Show($"Cannot connect to the database or log in.\n{ex.Message}","Database / Login Error",MessageBoxButtons.OK,MessageBoxIcon.Error); }
    }
}
