using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CampusQuota;

internal static class NativeChecks
{
    // Invoked only with --self-test. All browser data and credentials use a disposable directory.
    public static int Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "CampusQuota-check-" + Guid.NewGuid());
        Settings.TestRoot = directory;
        try
        {
            var settings = Settings.Load(); settings.Save();
            if (Settings.Load().Selected != settings.Selected) throw new Exception("Settings round trip failed.");
            var id = Guid.NewGuid(); var credentials = new Credentials("test-user", "test-only-quote-'\\\"");
            CredentialStore.Save(id, credentials);
            if (CredentialStore.Load(id) != credentials) throw new Exception("DPAPI credential round trip failed.");
            CredentialStore.Remove(id);
            if (CredentialStore.Load(id) != null) throw new Exception("Credential removal failed.");
            using var form = new BrowserCheckForm();
            Application.Run(form);
            if (form.Error != null) throw form.Error;
            return 0;
        }
        catch (Exception e)
        {
            // Avoid modal failure dialogs on CI, but keep a useful report for the job.
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "windows-check-failure.txt"), e.ToString());
            return 1;
        }
        finally
        {
            Settings.TestRoot = null;
            try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    private sealed class BrowserCheckForm : Form
    {
        public Exception? Error { get; private set; }
        private readonly System.Windows.Forms.Timer deadline = new() { Interval = 45000 };
        public BrowserCheckForm()
        {
            Size = new Size(900, 600);
            deadline.Tick += (_, _) => { Error = new TimeoutException("WebView2 native checks timed out."); Close(); };
            Shown += async (_, _) =>
            {
                deadline.Start();
                try { await CheckAsync(); }
                catch (Exception e) { Error = e; }
                finally { deadline.Stop(); Close(); }
            };
        }
        private async Task CheckAsync()
        {
            using var tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Campus Quota native check", Visible = true };
            using var first = new WebView2 { Dock = DockStyle.Fill, CreationProperties = new() { UserDataFolder = Path.Combine(Settings.Root, "one") } };
            using var second = new WebView2 { Dock = DockStyle.Fill, CreationProperties = new() { UserDataFolder = Path.Combine(Settings.Root, "two") } };
            Controls.Add(first); Controls.Add(second);
            await first.EnsureCoreWebView2Async(); await second.EnsureCoreWebView2Async();
            var manager = first.CoreWebView2.CookieManager;
            manager.AddOrUpdateCookie(manager.CreateCookie("campus-quota-test", "one", Portal.Url.Host, "/"));
            var cookies = await second.CoreWebView2.CookieManager.GetCookiesAsync(Portal.Url.AbsoluteUri);
            if (cookies.Any(c => c.Name == "campus-quota-test")) throw new Exception("Account cookie isolation failed.");
            var loaded = new TaskCompletionSource();
            first.CoreWebView2.NavigationCompleted += (_, e) =>
            { if (e.IsSuccess) loaded.TrySetResult(); else loaded.TrySetException(new Exception("Fixture navigation failed.")); };
            first.NavigateToString("""
                <html><body><span>User portal for</span><span>test-user@bits-hyderabad.ac.in</span><span>Policy information</span>
                <table><tr><th>Resource</th><th>Allotted</th><th>Usage</th><th>Remaining</th></tr>
                <tr><td>Cycle total network traffic</td><td>10000 MB</td><td>7000 MB</td><td>3000 MB</td></tr></table>
                <input id="username"><input id="password" type="password"><input name="loginbutton" type="button" onclick="window.submitted=true">
                </body></html>
                """);
            await loaded.Task;
            var snapshot = JsonSerializer.Deserialize<PageSnapshot>(await first.CoreWebView2.ExecuteScriptAsync(Portal.SnapshotScript))!;
            if (snapshot.Identity != "test-user@bits-hyderabad.ac.in" || Portal.Parse(snapshot.Tables, DateTimeOffset.Now)?.Title != "3.00 GB left")
                throw new Exception("WebView2 quota/identity extraction failed.");
            var credentials = new Credentials("test-'\\\"", "test-only-\\\"-'");
            await first.CoreWebView2.ExecuteScriptAsync(Portal.LoginScript(credentials));
            var readback = JsonSerializer.Deserialize<string[]>(await first.CoreWebView2.ExecuteScriptAsync("[document.getElementById('username').value,document.getElementById('password').value]"))!;
            if (readback[0] != credentials.Username || readback[1] != credentials.Password
                || await first.CoreWebView2.ExecuteScriptAsync("window.submitted === true") != "true")
                throw new Exception("WebView2 credential injection failed.");
            await first.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
            if ((await manager.GetCookiesAsync(Portal.Url.AbsoluteUri)).Count != 0) throw new Exception("Cookie removal failed.");
        }
        protected override void Dispose(bool disposing) { if (disposing) deadline.Dispose(); base.Dispose(disposing); }
    }
}
