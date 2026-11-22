using CampusQuota;
using System.Text.Json;

int count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; }
var now = DateTimeOffset.Now;
string[] headers = ["Resource", "Allotted", "Up to last session", "Current session", "Total", "Remaining"];
string[] Row(string label = "Cycle total network traffic", string allotted = "10000 MB", string remaining = "2919.06\nMB") => [label, allotted, "0 MB", "7080.94 MB", "7080.94 MB", remaining];
Quota? Parse(string[] row) => Portal.Parse([[headers, row]], now);
var q = Parse(Row());
Check(q?.Title == "2.92 GB left", "daily remaining");
Check(Math.Abs((q?.UsedMB ?? 0) - 7080.94) < .001, "upload plus download");
Check(Parse(Row(label: "Total network traffic")) == null, "reject lifetime total");
Check(Parse(Row(remaining: "NA")) == null, "reject missing");
Check(Parse(Row(remaining: "12000 MB")) == null, "reject above allocation");
Check(Portal.Megabytes("10,000 MB") == 10000, "thousands");
Check(Portal.Megabytes("2.5 GB") == 2500, "decimal GB");
Check(Portal.Parse([[["Cycle total network traffic in outer layout table"]], [["Resource", "Allotted", "Usage", "Remaining"], Row(allotted: "10000\u00a0MB", remaining: "2597.37\u00a0MB")]], now)?.Title == "2.60 GB left", "nested layout and nonbreaking spaces");
Check(Portal.Megabytes("-10 MB") == null, "negative");
Check(Portal.Megabytes("Error 10 MB") == null, "mixed text");
Check(Parse(Row(remaining: "0 MB"))?.Title == "0.00 GB left", "exhausted zero");
Check(Portal.SameAccount("test-user", "TEST-USER@bits-hyderabad.ac.in"), "short username");
Check(!Portal.SameAccount("test-user", "other-user@bits-hyderabad.ac.in"), "wrong identity");
Check(!Portal.SameAccount("", ""), "missing identity");
Check(!Portal.SameAccount("test-user@one.example", "test-user@other.example"), "distinct realms");
Check(JsonSerializer.Deserialize<Profile>("{\"Id\":\"00000000-0000-0000-0000-000000000001\",\"Name\":\"Existing profile\"}")?.Username == null, "old profile");
var credentials = new Credentials("a'b\"\\\n", "test-only-\\-\"-\n");
Check(JsonSerializer.Deserialize<Credentials>(JsonSerializer.Serialize(credentials)) == credentials, "credential round trip");
Check(Portal.RefreshSeconds == 300 && !q!.IsStale(false, now.AddSeconds(300)), "five-minute fresh");
Check(q!.IsStale(true, now), "network failure stale");
Check(q.IsStale(false, now.AddSeconds(361)), "six-minute stale");
var midnight = new DateTimeOffset(2026, 10, 22, 0, 0, 1, now.Offset);
Check(new Quota(1, 2, midnight.AddSeconds(-2)).IsStale(false, midnight), "previous day stale");
Check(Portal.Allowed(Portal.Url.AbsoluteUri) && !Portal.Allowed("http://172.16.0.30:65040/") && !Portal.Allowed("https://evil.example/"), "origin restriction");
Check(Parse(Row(allotted: "0 MB", remaining: "0 MB")) == null, "reject invalid allocation");
Check(Portal.Parse([[Row()]], now) == null, "require remaining header");
Check(Portal.Megabytes(new string('9', 400) + " MB") == null, "reject nonfinite");
// Run the exact shipping JavaScript against synthetic DOM fixtures using Node.
if (args.Length == 1)
{
    Directory.CreateDirectory(args[0]);
    File.WriteAllText(Path.Combine(args[0], "snapshot.js"), Portal.SnapshotScript);
    File.WriteAllText(Path.Combine(args[0], "login.js"), Portal.LoginScript(credentials));
    File.WriteAllText(Path.Combine(args[0], "credentials.json"), JsonSerializer.Serialize(credentials));
}
Console.WriteLine($"{count} Windows core checks passed.");
