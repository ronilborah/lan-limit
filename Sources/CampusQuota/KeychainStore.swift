import Foundation
import Security

struct Credentials: Codable {
    let username: String
    let password: String
}

enum KeychainStore {
    private static let service = "in.ronil.campusquota.saved-logins"

    private static func query(_ id: UUID) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword,
         kSecAttrService as String: service,
         kSecAttrAccount as String: id.uuidString]
    }

    static func save(_ credentials: Credentials, for id: UUID) throws {
        let data = try JSONEncoder().encode(credentials)
        let update = SecItemUpdate(query(id) as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        if update == errSecSuccess { return }
        guard update == errSecItemNotFound else { throw failure(update) }
        var item = query(id)
        item[kSecValueData as String] = data
        item[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        let result = SecItemAdd(item as CFDictionary, nil)
        guard result == errSecSuccess else { throw failure(result) }
    }

    static func load(_ id: UUID) throws -> Credentials? {
        var item = query(id)
        item[kSecReturnData as String] = true
        item[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let code = SecItemCopyMatching(item as CFDictionary, &result)
        if code == errSecItemNotFound { return nil }
        guard code == errSecSuccess, let data = result as? Data else { throw failure(code) }
        return try JSONDecoder().decode(Credentials.self, from: data)
    }

    static func remove(_ id: UUID) throws {
        let code = SecItemDelete(query(id) as CFDictionary)
        guard code == errSecSuccess || code == errSecItemNotFound else { throw failure(code) }
    }

    private static func failure(_ code: OSStatus) -> NSError {
        NSError(domain: NSOSStatusErrorDomain, code: Int(code), userInfo: [NSLocalizedDescriptionKey:
            "Keychain could not access the saved login (\(code)). Unlock your login Keychain and try again."])
    }
}

enum LoginScript {
    static func make(_ credentials: Credentials) throws -> String {
        // JSON is JavaScript data, never a string interpolated into executable code.
        let data = try JSONEncoder().encode(credentials)
        guard let json = String(data: data, encoding: .utf8) else { throw CocoaError(.fileReadCorruptFile) }
        return """
        (() => {
          const credentials = \(json);
          const username = document.getElementById('username');
          const password = document.getElementById('password');
          const button = document.querySelector('input[name="loginbutton"]');
          if (!username || !password || !button || button.disabled) return false;
          username.value = credentials.username; password.value = credentials.password;
          for (const field of [username, password]) {
            field.dispatchEvent(new Event('input', {bubbles: true}));
            field.dispatchEvent(new Event('change', {bubbles: true}));
          }
          button.click(); return true;
        })()
        """
    }
}
