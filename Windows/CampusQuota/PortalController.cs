using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CampusQuota;

internal sealed class PortalController : IDisposable
{
    public Profile Profile { get; private set; }
    public WebView2 View { get; } = new() { Dock = DockStyle.Fill };
    public Quota? Quota { get; private set; }
    public string? ActualUsername { get; private set; }
    public string Message { get; private set; } = "Connecting to campus portal…";
    public bool Failed { get; private set; }
    public bool Busy { get; private set; }
    public int Attempts { get; private set; }
    public int Successes { get; private set; }
    public DateTimeOffset? LastAttempt { get; private set; }
    public event Action? Changed;
    public event Action? ProcessFailed;
    private readonly Settings settings;
    private readonly Form owner;
    private bool disposed, blocked, missingLogin, submitted, reading, loading, certificatePending;
    private int revision;
    private readonly System.Windows.Forms.Timer watchdog = new() { Interval = 5000 };
    public bool Initialized => View.CoreWebView2 != null;

    public PortalController(Profile profile, Settings settings, Form owner)
    {
        Profile = profile; this.settings = settings; this.owner = owner;
        View.CreationProperties = new CoreWebView2CreationProperties
        { UserDataFolder = Path.Combine(Settings.Root, "Browser", profile.Id.ToString()) };
        watchdog.Tick += async (_, _) =>
        {
            if (!Busy || certificatePending) return;
            if (DateTimeOffset.Now - LastAttempt >= TimeSpan.FromSeconds(45))
            {
                revision++; View.CoreWebView2?.Stop();
                Failure(submitted ? "Sign-in timed out. Check the saved login, then retry manually." : "Portal timed out. Connect to campus LAN; retrying at the next update.", submitted);
            }
            else await ReadPageAsync();
        };
    }
    public async Task InitializeAsync()
    {
        await View.EnsureCoreWebView2Async();
        if (disposed) return;
        var core = View.CoreWebView2;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.NavigationStarting += (_, e) =>
        {
            if (!Portal.Allowed(e.Uri)) { e.Cancel = true; return; }
            loading = true;
            if (!Busy) { Busy = true; LastAttempt = DateTimeOffset.Now; }
        };
        core.FrameNavigationStarting += (_, e) => { if (!Portal.Allowed(e.Uri) && e.Uri != "about:blank") e.Cancel = true; };
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (!Portal.Allowed(e.Request.Uri) && !e.Request.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                && !e.Request.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
        };
        core.ServerCertificateErrorDetected += CertificateError;
        core.NavigationCompleted += async (_, e) =>
        {
            loading = false;
            if (disposed) return;
            if (!e.IsSuccess)
            {
                if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    Failure("Cannot reach the portal (" + e.WebErrorStatus + "). Check campus LAN or certificate approval.");
                return;
            }
            await ReadPageAsync();
        };
        core.ProcessFailed += (_, _) =>
        {
            if (disposed) return;
            Pause(); Failure("Portal process stopped. Reconnecting…"); ProcessFailed?.Invoke();
        };
        watchdog.Start();
    }
    public void Refresh(bool manual = false)
    {
        if (disposed || !Initialized || certificatePending || (Busy && DateTimeOffset.Now - LastAttempt < TimeSpan.FromSeconds(45))) return;
        if (manual) { blocked = false; missingLogin = false; }
        if (blocked || missingLogin) return;
        revision++; View.CoreWebView2.Stop();
        Busy = true; Failed = false; submitted = false; loading = true;
        LastAttempt = DateTimeOffset.Now; Attempts++;
        Message = "Updating daily balance…"; Changed?.Invoke();
        View.CoreWebView2.Navigate(Portal.Url.AbsoluteUri);
    }
    public void Pause()
    {
        revision++; Busy = false; submitted = false; loading = false;
        if (!disposed) View.CoreWebView2?.Stop();
    }
    public async Task UpdateLoginAsync(Profile profile)
    {
        Pause(); Profile = profile; blocked = false; missingLogin = false; Quota = null; ActualUsername = null;
        await View.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
        if (!disposed) Refresh(true);
    }
    private void Failure(string text, bool authentication = false, bool clear = false)
    {
        Busy = false; Failed = true; Message = text;
        if (authentication) blocked = true;
        if (clear) { Quota = null; ActualUsername = null; }
        Changed?.Invoke();
    }
    private async Task ReadPageAsync()
    {
        if (disposed || !Busy || loading || reading || certificatePending || !Portal.Allowed(View.Source?.AbsoluteUri ?? "")) return;
        int token = revision; reading = true;
        try
        {
            var json = await View.CoreWebView2.ExecuteScriptAsync(Portal.SnapshotScript);
            if (disposed || token != revision || !Busy || loading) return;
            var page = JsonSerializer.Deserialize<PageSnapshot>(json);
            if (page == null) return;
            var quota = Portal.Parse(page.Tables, DateTimeOffset.Now);
            if (quota != null)
            {
                if (Profile.Username is string expected && !Portal.SameAccount(expected, page.Identity))
                { Failure("Portal account did not match the selected login. Update this account's saved login.", true, true); return; }
                Quota = quota; ActualUsername = page.Identity; Successes++;
                Busy = false; Failed = false; submitted = false; blocked = false; missingLogin = false;
                Message = "Connected · " + quota.Title; Changed?.Invoke(); return;
            }
            if (!page.Login) return;
            Quota = null; ActualUsername = null;
            if (page.Interactive) { Failure("The portal needs an interactive sign-in step. Complete it in this window.", true); return; }
            if (submitted)
            {
                if (!string.IsNullOrWhiteSpace(page.Error)) Failure("Sign-in was rejected. Update the saved password or retry manually.", true);
                return;
            }
            if (blocked || missingLogin) { Busy = false; return; }
            var credentials = CredentialStore.Load(Profile.Id);
            if (credentials == null)
            { missingLogin = true; Failure("Save this account's username and password using Save login…"); return; }
            submitted = true; Message = "Signing in with the saved login…"; Changed?.Invoke();
            var result = await View.CoreWebView2.ExecuteScriptAsync(Portal.LoginScript(credentials));
            if (!disposed && token == revision && result == "false")
                Failure("The portal login form changed. Sign in manually in this window.", true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException)
        { /* Navigation can destroy the script context. The watchdog still enforces the deadline. */ }
        catch (CryptographicException) { Failure("Windows could not unlock the saved login. Save the login again.", true); }
        catch (IOException) { Failure("Cannot read the saved login. Save the login again.", true); }
        finally { reading = false; }
    }
    private void CertificateError(object? sender, CoreWebView2ServerCertificateErrorDetectedEventArgs e)
    {
        e.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
        if (disposed || !Portal.Allowed(e.RequestUri) || certificatePending) return;
        using var deferral = e.GetDeferral();
        certificatePending = true;
        try
        {
            using var cert = X509Certificate2.CreateFromPem(e.ServerCertificate.ToPemEncoding());
            var hash = string.Join(":", SHA256.HashData(cert.RawData).Select(b => b.ToString("X2")));
            if (settings.ApprovedCertificate == hash) { e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow; return; }
            owner.Show(); owner.Activate();
            var answer = MessageBox.Show(owner,
                "Windows cannot verify 172.16.0.30:65040. An impostor could read your login. Verify this certificate with campus IT.\n\nCertificate: "
                + cert.Subject + "\nSHA-256:\n" + hash
                + "\n\nTrust only this exact certificate at this portal inside Campus Quota?", "Approve campus portal certificate?",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes)
            {
                settings.ApprovedCertificate = hash; settings.Save();
                e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
            }
            else Failure("Portal certificate approval was cancelled.");
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        { Failure("Could not verify or save portal certificate approval."); }
        finally { certificatePending = false; LastAttempt = DateTimeOffset.Now; }
    }
    public void Dispose()
    {
        Pause(); disposed = true; watchdog.Stop(); watchdog.Dispose(); View.Dispose();
    }
}
