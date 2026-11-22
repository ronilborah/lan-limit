using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CampusQuota;

internal record Profile(Guid Id, string Name, string? Username = null);
internal record Credentials(string Username, string Password);
internal record Quota(double RemainingMB, double AllottedMB, DateTimeOffset SampledAt)
{
    public double UsedMB => AllottedMB - RemainingMB;
    public string Title => $"{(RemainingMB / 1000).ToString("F2", CultureInfo.InvariantCulture)} GB left";
    public bool IsStale(bool failed, DateTimeOffset now) => failed || now - SampledAt > TimeSpan.FromSeconds(360)
        || SampledAt.LocalDateTime.Date != now.LocalDateTime.Date;
}
internal static class Portal
{
    public static readonly Uri Url = new("https://172.16.0.30:65040/userportal/webpages/myaccount/index.jsp");
    public const int RefreshSeconds = 300;
    public static bool Allowed(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Url.Scheme && uri.Host == Url.Host && uri.Port == Url.Port;
    public static bool SameAccount(string expected, string actual)
    {
        expected = expected.Trim().ToLowerInvariant(); actual = actual.Trim().ToLowerInvariant();
        if (expected.Length == 0 || actual.Length == 0) return false;
        return expected.Contains('@') && actual.Contains('@') ? expected == actual : expected.Split('@')[0] == actual.Split('@')[0];
    }
    public static double? Megabytes(string text)
    {
        var match = Regex.Match(text.Replace(",", ""), @"^\s*(\d+(?:\.\d+)?)\s*(TB|GB|MB|KB|BYTES|B)?\s*$", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            || !double.IsFinite(number)) return null;
        return number * (match.Groups[2].Value.ToUpperInvariant() switch
        { "TB" => 1_000_000, "GB" => 1000, "KB" => .001, "B" or "BYTES" => .000001, _ => 1 });
    }
    public static Quota? Parse(string[][][] tables, DateTimeOffset now)
    {
        foreach (var rows in tables.OrderBy(t => t.Length))
        {
            if (!rows.Any(row => row.Any(cell => cell.Trim().Equals("remaining", StringComparison.OrdinalIgnoreCase)))) continue;
            foreach (var row in rows)
            {
                if (row.Length < 4 || !row[0].ToLowerInvariant().Replace(" ", "").Contains("cycletotalnetworktraffic")) continue;
                if (Megabytes(row[1]) is double allotted && Megabytes(row[^1]) is double remaining
                    && allotted > 0 && remaining >= 0 && remaining <= allotted) return new(remaining, allotted, now);
            }
        }
        return null;
    }
    public static string LoginScript(Credentials credentials) => """
        (() => {
          const c = CREDENTIALS;
          const u = document.getElementById('username'), p = document.getElementById('password');
          const b = document.querySelector('input[name="loginbutton"]');
          if (!u || !p || !b || b.disabled) return false;
          u.value = c.Username; p.value = c.Password;
          for (const f of [u,p]) {
            f.dispatchEvent(new Event('input', {bubbles:true}));
            f.dispatchEvent(new Event('change', {bubbles:true}));
          }
          b.click(); return true;
        })()
        """.Replace("CREDENTIALS", JsonSerializer.Serialize(credentials));
    public const string SnapshotScript = """
        (() => {
          const tables = Array.from(document.querySelectorAll('table')).map(table =>
            Array.from(table.querySelectorAll('tr')).filter(row => row.closest('table') === table)
              .map(row => Array.from(row.children).filter(cell => /^(TD|TH)$/.test(cell.tagName))
                .map(cell => cell.innerText.trim()))
          ).filter(rows => rows.some(row => row.some(cell => /cycle\s*total\s*network\s*traffic/i.test(cell))));
          const texts = [], walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
          while(walker.nextNode()) {
            const n = walker.currentNode;
            if(n.parentElement && !/^(SCRIPT|STYLE)$/.test(n.parentElement.tagName) && n.textContent.trim()) texts.push(n.textContent.trim());
          }
          const index = texts.findIndex(t => /User portal for/i.test(t));
          const identity = (index < 0 ? '' : texts.slice(index,index+4).join(' ')).match(/User portal for\s+([^\s]+)/i);
          const otp = document.getElementById('twofactorTBody');
          return {Tables:tables, Identity:identity ? identity[1] : '',
            Login:!!document.getElementById('username') && !!document.getElementById('password'),
            Interactive:!!otp && getComputedStyle(otp).display !== 'none',
            Error:(document.getElementById('message') || {}).innerText || ''};
        })()
        """;
}
internal record PageSnapshot(string[][][] Tables, string Identity, bool Login, bool Interactive, string Error);
