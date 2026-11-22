namespace CampusQuota;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) { Environment.ExitCode = NativeChecks.Run(); return; }
        // One instance per signed-in Windows session. Reopening raises the retained window.
        using var mutex = new Mutex(true, @"Local\CampusQuota-v2", out bool first);
        using var reopen = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\CampusQuota-v2-Reopen");
        if (!first) { reopen.Set(); return; }
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "Campus Quota", MessageBoxButtons.OK, MessageBoxIcon.Error);
        try { Application.Run(new MainForm(Settings.Load(), reopen)); }
        catch (Exception e) { MessageBox.Show("Campus Quota could not start.\n\n" + e.Message, "Campus Quota", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
