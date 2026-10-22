import AppKit
import WebKit
import CryptoKit
import Security

let portalURL = URL(string: "https://172.16.0.30:65040/userportal/webpages/myaccount/index.jsp")!

struct Profile: Codable, Equatable {
    let id: UUID
    var name: String
    var username: String?
}
let refreshInterval: TimeInterval = 300
let staleInterval: TimeInterval = 360
let requestDeadline: TimeInterval = 45

func sameAccount(_ expected: String, _ actual: String) -> Bool {
    func canonical(_ s: String) -> String { s.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() }
    let wanted = canonical(expected), received = canonical(actual)
    guard !wanted.isEmpty, !received.isEmpty else { return false }
    if wanted.contains("@"), received.contains("@") { return wanted == received }
    return wanted.components(separatedBy: "@")[0] == received.components(separatedBy: "@")[0]
}

struct Quota {
    let remainingMB: Double
    let allottedMB: Double
    let sampledAt: Date
    var usedMB: Double { allottedMB - remainingMB }
    var title: String { String(format: "%.2f GB left", remainingMB / 1000) }
}

enum QuotaParser {
    static func megabytes(_ text: String) -> Double? {
        let normalized = text.replacingOccurrences(of: ",", with: "").uppercased()
        guard let range = normalized.range(of: #"^\s*\d+(?:\.\d+)?\s*(?:TB|GB|MB|KB|BYTES|B)?\s*$"#, options: .regularExpression) else { return nil }
        let token = String(normalized[range])
        guard let numberRange = token.range(of: #"\d+(?:\.\d+)?"#, options: .regularExpression),
              let value = Double(token[numberRange]) else { return nil }
        if token.contains("TB") { return value * 1_000_000 }
        if token.contains("GB") { return value * 1000 }
        if token.contains("KB") { return value / 1000 }
        if token.contains("MB") { return value }
        if token.contains("B") { return value / 1_000_000 }
        return value // This portal sometimes puts MB on a separate line.
    }
    static func parse(_ tables: [[[String]]], now: Date = Date()) -> Quota? {
        for rows in tables.sorted(by: { $0.count < $1.count }) {
            // Require both daily-cycle row wording and an explicit Remaining header.
            guard rows.contains(where: { $0.contains(where: { $0.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() == "remaining" }) }) else { continue }
            for row in rows {
                guard let first = row.first, first.lowercased().replacingOccurrences(of: " ", with: "").contains("cycletotalnetworktraffic"), row.count >= 4,
                      let quota = megabytes(row[1]), let remaining = megabytes(row.last!),
                      quota > 0, remaining >= 0, remaining <= quota else { continue }
                // Refuse malformed rows rather than displaying an invented balance.
                return Quota(remainingMB: remaining, allottedMB: quota, sampledAt: now)
            }
        }
        return nil
    }
    static let extractionJS = #"""
    (() => Array.from(document.querySelectorAll('table')).map(table =>
      Array.from(table.querySelectorAll('tr')).filter(row => row.closest('table') === table)
        .map(row => Array.from(row.children).filter(cell => /^(TD|TH)$/.test(cell.tagName))
          .map(cell => cell.innerText.trim()))
    ).filter(rows => rows.some(row => row.some(cell => /cycle\s*total\s*network\s*traffic/i.test(cell)))))()
    """#
}
