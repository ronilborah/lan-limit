import Foundation
func runSelfTests() {
    let headers = ["Resource", "Allotted", "Up to last session", "Current session", "Total", "Remaining"]
    let row = ["Cycle total network traffic", "10000 MB", "0 MB", "7080.94 MB", "7080.94 MB", "2919.06\nMB"]
    func require(_ condition: Bool, _ message: String) { if !condition { fputs("FAIL: \(message)\n", stderr); exit(1) } }
    let quota = QuotaParser.parse([[headers, row]])
    require(quota?.title == "2.92 GB left", "Screenshot balance")
    require(abs((quota?.usedMB ?? 0) - 7080.94) < 0.001, "Uploads plus downloads")
    require(QuotaParser.parse([[headers, ["Total network traffic", "10000 MB", "0 MB", "0 MB", "0 MB", "10000 MB"]]]) == nil, "Reject lifetime total")
    require(QuotaParser.parse([[headers, ["Cycle total network traffic", "10000 MB", "0 MB", "0 MB", "0 MB", "NA"]]]) == nil, "Reject missing balance")
    require(QuotaParser.parse([[headers, ["Cycle total network traffic", "10000 MB", "0 MB", "0 MB", "0 MB", "12000 MB"]]]) == nil, "Reject invalid balance")
    require(QuotaParser.megabytes("10,000 MB") == 10000, "Thousands separator")
    require(QuotaParser.megabytes("2.5 GB") == 2500, "GB conversion")
    let liveHeaders = ["Resource", "Allotted", "Usage", "Remaining"]
    let liveRows = [liveHeaders, ["Up to last session", "Current session", "Total"], ["Cycle total network traffic", "10000\u{00a0}MB", "0\u{00a0}MB", "7402.63\u{00a0}MB", "7402.63\u{00a0}MB", "2597.37\u{00a0}MB"]]
    require(QuotaParser.parse([[["Cycle total network traffic in outer layout table"]], liveRows])?.title == "2.60 GB left", "Live portal headers, nested layout and nonbreaking spaces")
    require(QuotaParser.megabytes("-10 MB") == nil, "Reject negative balance instead of turning it positive")
    require(QuotaParser.megabytes("Error 10 MB") == nil, "Reject mixed error text")
    require(QuotaParser.parse([[liveHeaders, ["Cycle total network traffic", "10000 MB", "0 MB", "10000 MB", "10000 MB", "0 MB"]]])?.title == "0.00 GB left", "Exhausted quota is a valid zero")
    require(sameAccount("test-user", "TEST-USER@bits-hyderabad.ac.in"), "Short username matches canonical portal identity")
    require(!sameAccount("test-user", "other-user@bits-hyderabad.ac.in"), "Refuse a different account's quota")
    require(!sameAccount("", ""), "Missing identity cannot verify an account")
    require(!sameAccount("test-user@one.example", "test-user@other.example"), "Full usernames in different realms must not share a balance")
    let oldProfile = Data("{\"id\":\"00000000-0000-0000-0000-000000000001\",\"name\":\"Existing profile\"}".utf8)
    require((try? JSONDecoder().decode(Profile.self, from: oldProfile))?.username == nil, "Migrate profiles created before saved-login support")
    let sampleCredentials = Credentials(username: "a'b\\\"\\n", password: "test-only-\\\\-\\\"-\\n")
    let encoded = try! JSONEncoder().encode(sampleCredentials)
    require((try? JSONDecoder().decode(Credentials.self, from: encoded))?.password == sampleCredentials.password, "Credentials with quotes and backslashes round trip")
    require(refreshInterval == 300 && staleInterval > refreshInterval, "A fresh five-minute sample must not become stale before the next refresh")
    print("18 checks passed: quota parsing, identity isolation, profile migration, credential encoding and refresh policy")
}
