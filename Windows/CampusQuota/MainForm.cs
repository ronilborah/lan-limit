using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CampusQuota;
internal sealed class MainForm : Form
{
    private readonly Settings settings;
    private readonly EventWaitHandle reopen;
    private readonly NotifyIcon tray = new() { Visible = true, Text = "Campus Quota — LAN unavailable", Icon = SystemIcons.Application };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(620, 0), Padding = new Padding(8) };
    private readonly Panel browserPanel = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer pulse = new() { Interval = 1000 };
    private PortalController? active;
    private ContextMenuStrip menu = new();
    private DateTimeOffset nextUpdate = DateTimeOffset.Now.AddSeconds(Portal.RefreshSeconds);
    private int automatic, manual, generation;
    private bool quitting, changing, editing;
    private Icon? quotaIcon;
    private string? iconLabel;
    private string? lastHealth;
    private Profile Selected => settings.Profiles.First(p => p.Id == settings.Selected);

    public MainForm(Settings settings, EventWaitHandle reopen)
    {
        this.settings = settings; this.reopen = reopen;
        Text = "Campus Quota"; Size = new Size(1140, 820); MinimumSize = new Size(850, 550); StartPosition = FormStartPosition.CenterScreen;
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6) };
        toolbar.Controls.Add(status);
        AddButton(toolbar, "Usage & accounts", () => { Rebuild(); menu.Show(Cursor.Position); });
        AddButton(toolbar, "Save login…", () => EditAccount(false));
        AddButton(toolbar, "Refresh", () => RefreshPortal(false));
        Controls.Add(browserPanel); Controls.Add(toolbar);
        tray.DoubleClick += (_, _) => ShowPortal();
        FormClosing += (_, e) => { if (!quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
        Shown += async (_, _) => await SwitchAsync();
        pulse.Tick += (_, _) =>
        {
            if (reopen.WaitOne(0)) ShowPortal();
            if (DateTimeOffset.Now >= nextUpdate) RefreshPortal(true);
            Rebuild();
        };
        NetworkChange.NetworkAvailabilityChanged += NetworkChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        pulse.Start(); Rebuild();
    }
    private static void AddButton(FlowLayoutPanel panel, string label, Action action)
    {
        var b = new Button { Text = label, AutoSize = true, Padding = new Padding(5), Margin = new Padding(4) };
        b.Click += (_, _) => action(); panel.Controls.Add(b);
    }
    private void Post(Action action)
    { if (!IsDisposed && IsHandleCreated) { try { BeginInvoke(action); } catch (InvalidOperationException) { } } }
    private void NetworkChanged(object? sender, NetworkAvailabilityEventArgs e)
    { if (e.IsAvailable) Post(() => { if (active?.Failed == true) RefreshPortal(true); }); }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Resume) Post(() => RefreshPortal(true)); }
    private void ShowPortal() { Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); }
    private async Task SwitchAsync()
    {
        int token = ++generation; changing = true;
        active?.Dispose(); active = null; browserPanel.Controls.Clear();
        Text = "Campus Quota — " + Selected.Name;
        var controller = new PortalController(Selected, settings, this);
        active = controller;
        controller.Changed += Rebuild;
        controller.ProcessFailed += () => Post(async () => { if (active == controller && !changing) await SwitchAsync(); });
        browserPanel.Controls.Add(controller.View);
        Rebuild();
        try
        {
            await controller.InitializeAsync();
            if (quitting || token != generation) return;
            changing = false; RefreshPortal(true);
        }
        catch (Exception e)
        {
            if (quitting || token != generation) return;
            changing = false;
            status.Text = "Cannot start the portal. Install Microsoft Edge WebView2 Runtime, then reopen Campus Quota.";
            MessageBox.Show(this, status.Text + "\n\n" + e.Message, "Campus Quota", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private void RefreshPortal(bool auto)
    {
        if (auto) { nextUpdate = DateTimeOffset.Now.AddSeconds(Portal.RefreshSeconds); automatic++; } else manual++;
        if (!changing)
        {
            if (active?.Initialized == true) active.Refresh(!auto);
            else if (!auto) _ = SwitchAsync();
        }
        Rebuild();
    }
    private ToolStripMenuItem Item(string title, Action? action = null)
    {
        var item = new ToolStripMenuItem(title) { Enabled = action != null };
        if (action != null) item.Click += (_, _) => action();
        return item;
    }
    private void Rebuild()
    {
        if (quitting || IsDisposed) return;
        var q = active?.Quota;
        bool stale = q?.IsStale(active?.Failed ?? false, DateTimeOffset.Now) ?? false;
        string title = q == null ? "LAN —" : (stale ? "~" : "") + q.Title;
        string tooltip = "Campus Quota · " + title + " · " + Selected.Name;
        tray.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        SetTrayLabel(q == null ? "—" : (stale ? "~" : "") + (q.RemainingMB / 1000).ToString("0.#", CultureInfo.InvariantCulture));
        // Do not replace/dispose a menu while the user is interacting with it.
        if (menu.Visible) return;
        var replacement = new ContextMenuStrip();
        replacement.Items.Add(Item(Selected.Name));
        var username = active?.ActualUsername ?? Selected.Username;
        if (!string.IsNullOrEmpty(username)) replacement.Items.Add(Item(username));
        if (q != null)
        {
            replacement.Items.Add(Item(q.Title + (stale ? " (last known)" : "")));
            replacement.Items.Add(Item($"Used {q.UsedMB / 1000:F2} of {q.AllottedMB / 1000:F2} GB"));
            replacement.Items.Add(Item("Updated " + q.SampledAt.LocalDateTime.ToLongTimeString()));
        }
        replacement.Items.Add(Item("Auto update: every 5 minutes"));
        replacement.Items.Add(Item("Next update: " + nextUpdate.LocalDateTime.ToLongTimeString()));
        replacement.Items.Add(Item(active?.Message ?? "Starting…"));
        replacement.Items.Add(new ToolStripSeparator());
        replacement.Items.Add(Item("Open portal…", ShowPortal));
        replacement.Items.Add(Item("Refresh now", () => RefreshPortal(false)));
        var accounts = new ToolStripMenuItem("Accounts");
        foreach (var profile in settings.Profiles)
        {
            var item = Item(profile.Name, async () =>
            {
                if (changing || editing) return;
                settings.Selected = profile.Id; settings.Save(); await SwitchAsync();
            });
            item.Checked = profile.Id == settings.Selected; accounts.DropDownItems.Add(item);
        }
        accounts.DropDownItems.Add(new ToolStripSeparator());
        accounts.DropDownItems.Add(Item("Add account…", () => EditAccount(true)));
        replacement.Items.Add(accounts);
        replacement.Items.Add(Item("Save / update login…", () => EditAccount(false)));
        replacement.Items.Add(Item("Remove this saved account…", async () => await RemoveAsync()));
        replacement.Items.Add(new ToolStripSeparator());
        replacement.Items.Add(Item("Diagnostics…", () => MessageBox.Show(this,
            $"Version 1.1.0 · standalone Windows app\nRefresh interval: 300 seconds\nAutomatic triggers: {automatic}\nManual triggers: {manual}\nSuccessful readings this session: {active?.Successes ?? 0}\nNext update: {nextUpdate.LocalDateTime:T}\nStatus: {active?.Message}\n\nClosing the window keeps monitoring in the tray. Sleeping computers refresh after waking.", "Campus Quota diagnostics")));
        replacement.Items.Add(Item("Quit Campus Quota", () => { quitting = true; Close(); }));
        tray.ContextMenuStrip = replacement;
        var previous = menu; menu = replacement; previous.Dispose();
        status.Text = (active?.Message ?? "Starting…") + "\nAuto update every 5 minutes · next " + nextUpdate.LocalDateTime.ToLongTimeString();
        try
        {
            var health = new { pid = Environment.ProcessId, version = "1.1.0", intervalSeconds = Portal.RefreshSeconds,
                automaticTriggers = automatic, manualTriggers = manual, successfulReadings = active?.Successes ?? 0,
                attempts = active?.Attempts ?? 0, lastAttempt = active?.LastAttempt?.ToUnixTimeSeconds() ?? 0,
                lastSuccess = q?.SampledAt.ToUnixTimeSeconds() ?? 0, nextAutomaticUpdate = nextUpdate.ToUnixTimeSeconds(), busy = active?.Busy ?? false };
            var json = System.Text.Json.JsonSerializer.Serialize(health);
            if (json != lastHealth) { Settings.WriteJson("health.json", health); lastHealth = json; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    private void SetTrayLabel(string label)
    {
        if (iconLabel == label) return;
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(25, 45, 65));
        using var font = new Font("Segoe UI", label.Length > 3 ? 9 : 12, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(label, font, Brushes.White, new RectangleF(0, 0, 32, 32), format);
        IntPtr handle = bitmap.GetHicon();
        Icon icon;
        try { using var temporary = Icon.FromHandle(handle); icon = (Icon)temporary.Clone(); }
        finally { DestroyIcon(handle); }
        tray.Icon = icon; quotaIcon?.Dispose(); quotaIcon = icon; iconLabel = label;
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    private async void EditAccount(bool add)
    {
        if (changing || editing) return;
        var existing = add ? null : Selected;
        using var editor = new AccountEditor(existing, add ? null : active?.ActualUsername);
        ShowPortal();
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        editing = true;
        var profile = new Profile(existing?.Id ?? Guid.NewGuid(), editor.AccountName, editor.Username);
        try
        {
            CredentialStore.Save(profile.Id, new(editor.Username, editor.Password)); editor.ClearPassword();
            int index = settings.Profiles.FindIndex(p => p.Id == profile.Id);
            if (index >= 0) settings.Profiles[index] = profile; else settings.Profiles.Add(profile);
            settings.Selected = profile.Id; settings.Save();
            // Pause any old account before attaching a new account's browser.
            if (active?.Profile.Id != profile.Id) await SwitchAsync();
            if (active != null && active.Initialized)
            {
                changing = true;
                try { await active.UpdateLoginAsync(profile); }
                finally { changing = false; }
            }
            Text = "Campus Quota — " + profile.Name; Rebuild();
        }
        catch (Exception e) { MessageBox.Show(this, "Could not save the login.\n\n" + e.Message, "Campus Quota"); }
        finally { editor.ClearPassword(); editing = false; }
    }
    private async Task RemoveAsync()
    {
        if (changing || editing || active?.Initialized != true) return;
        if (MessageBox.Show(this, "Remove this app's saved login and portal cookies for " + Selected.Name + "? Your campus account is unaffected.",
                "Remove account?", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        changing = true;
        try
        {
            active.Pause();
            await active.View.CoreWebView2.Profile.ClearBrowsingDataAsync(Microsoft.Web.WebView2.Core.CoreWebView2BrowsingDataKinds.AllProfile);
            CredentialStore.Remove(Selected.Id);
            settings.Profiles.RemoveAll(p => p.Id == settings.Selected);
            settings.EnsureProfile(); settings.Save(); await SwitchAsync();
        }
        catch (Exception e) { MessageBox.Show(this, "Could not remove all account data.\n\n" + e.Message, "Campus Quota"); }
        finally { changing = false; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            quitting = true; generation++;
            NetworkChange.NetworkAvailabilityChanged -= NetworkChanged; SystemEvents.PowerModeChanged -= PowerChanged;
            pulse.Dispose(); active?.Dispose(); tray.Visible = false; tray.Dispose(); menu.Dispose(); quotaIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
internal sealed class AccountEditor : Form
{
    private readonly TextBox label = new() { Width = 340 };
    private readonly TextBox username = new() { Width = 340 };
    private readonly TextBox password = new() { Width = 340, UseSystemPasswordChar = true };
    public string AccountName => string.IsNullOrWhiteSpace(label.Text) ? Username : label.Text.Trim();
    public string Username => username.Text.Trim();
    public string Password => password.Text;
    public void ClearPassword() => password.Clear();
    public AccountEditor(Profile? profile, string? actual)
    {
        Text = profile == null ? "Add campus account" : "Save account login";
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        label.Text = profile?.Name ?? ""; username.Text = profile?.Username ?? actual ?? "";
        label.AccessibleName = "Account label"; username.AccessibleName = "Campus username"; password.AccessibleName = "Password";
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(16), WrapContents = false };
        panel.Controls.Add(new Label { Text = "Your password is protected by Windows for this user on this computer.", AutoSize = true, MaximumSize = new Size(340, 0) });
        foreach (var pair in new[] { ("Label", label), ("Username", username), ("Password", password) })
        { panel.Controls.Add(new Label { Text = pair.Item1, AutoSize = true }); panel.Controls.Add(pair.Item2); }
        var save = new Button { Text = "Save & sign in", AutoSize = true };
        save.Click += (_, _) =>
        {
            if (Username.Length == 0 || Password.Length == 0) { MessageBox.Show(this, "Enter a username and password."); return; }
            DialogResult = DialogResult.OK;
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        panel.Controls.Add(save); panel.Controls.Add(cancel); Controls.Add(panel); AcceptButton = save; CancelButton = cancel;
    }
}
