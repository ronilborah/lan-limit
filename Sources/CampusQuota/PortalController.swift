import AppKit
import WebKit
import CryptoKit
import Security

final class PortalController: NSObject, WKNavigationDelegate {
    let profileID: UUID
    var expectedUsername: String?
    let view: WKWebView
    var onChange: (() -> Void)?
    var presentCertificate: ((NSAlert, @escaping (NSApplication.ModalResponse) -> Void) -> Void)?
    private(set) var quota: Quota?
    private(set) var actualUsername: String?
    private(set) var message = "Connecting to campus portal…"
    private(set) var failed = false
    private(set) var attempts = 0
    private(set) var successfulRefreshes = 0
    private(set) var lastAttempt: Date?
    private(set) var busy = false
    private var revision = 0
    private var loginSubmitted = false
    private var authenticationBlocked = false
    private var needsSavedLogin = false
    private var certificatePending = false
    private var watchdog: Timer?
    private var observer: NSKeyValueObservation?

    init(profile: Profile) {
        profileID = profile.id
        expectedUsername = profile.username
        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = WKWebsiteDataStore(forIdentifier: profile.id)
        view = WKWebView(frame: NSRect(x: 0, y: 0, width: 1120, height: 700), configuration: configuration)
        super.init()
        view.navigationDelegate = self
        view.allowsBackForwardNavigationGestures = true
        // Some portal screens update through AJAX after the main navigation finishes.
        observer = view.observe(\.isLoading, options: [.new]) { [weak self] view, _ in
            if !view.isLoading { DispatchQueue.main.async { self?.readPage() } }
        }
        watchdog = Timer(timeInterval: 5, repeats: true) { [weak self] _ in self?.checkProgress() }
        RunLoop.main.add(watchdog!, forMode: .common)
    }

    deinit { watchdog?.invalidate() }

    func refresh(manual: Bool = false) {
        if certificatePending { return }
        if busy, let start = lastAttempt, Date().timeIntervalSince(start) < requestDeadline { return }
        if manual { authenticationBlocked = false }
        guard !authenticationBlocked else { return }
        if needsSavedLogin && !manual { return }
        revision += 1
        view.stopLoading()
        busy = true; failed = false; loginSubmitted = false
        lastAttempt = Date(); attempts += 1
        message = "Updating daily balance…"; onChange?()
        view.load(URLRequest(url: portalURL, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 25))
    }

    func invalidateSavedLogin(username: String) {
        expectedUsername = username
        authenticationBlocked = false
        needsSavedLogin = false
        // Cookies may belong to a different username after editing a profile.
        quota = nil; actualUsername = nil
        busy = false; revision += 1; view.stopLoading()
        let store = view.configuration.websiteDataStore
        store.removeData(ofTypes: WKWebsiteDataStore.allWebsiteDataTypes(), modifiedSince: .distantPast) { [weak self] in
            self?.refresh(manual: true)
        }
    }

    func stop() {
        revision += 1; busy = false; view.stopLoading()
        watchdog?.invalidate(); observer?.invalidate()
        onChange = nil; presentCertificate = nil
    }

    func pause() {
        revision += 1; busy = false; loginSubmitted = false; view.stopLoading()
    }

    private func finishFailure(_ text: String, authentication: Bool = false, clearQuota: Bool = false) {
        busy = false; failed = true; message = text
        if authentication { authenticationBlocked = true }
        if clearQuota { quota = nil; actualUsername = nil }
        onChange?()
    }

    private func checkProgress() {
        guard busy, !certificatePending, let start = lastAttempt else { return }
        if Date().timeIntervalSince(start) >= requestDeadline {
            revision += 1; view.stopLoading()
            finishFailure(loginSubmitted ? "Sign-in timed out. Check the saved login, then retry manually." : "Portal timed out. Will retry at the next update. Connect to campus LAN.", authentication: loginSubmitted)
        } else if !view.isLoading { readPage() }
    }

    private static let snapshotJS = """
    (() => {
      const tables = \(QuotaParser.extractionJS);
      // Join text nodes explicitly. WebKit can concatenate adjacent portal blocks
      // as "user@domainPolicy information" in body.innerText.
      const texts = [];
      const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
      while (walker.nextNode()) {
        const node = walker.currentNode;
        if (!/^(SCRIPT|STYLE)$/.test(node.parentElement.tagName) && node.textContent.trim()) texts.push(node.textContent.trim());
      }
      const headerIndex = texts.findIndex(text => /User portal for/i.test(text));
      const header = headerIndex < 0 ? '' : texts.slice(headerIndex, headerIndex + 4).join(' ');
      const identity = header.match(/User portal for\\s+([^\\s]+)/i);
      const normal = document.getElementById('normalTBody');
      const otp = document.getElementById('twofactorTBody');
      return {tables, identity: identity ? identity[1] : '',
        login: !!document.getElementById('username') && !!document.getElementById('password'),
        interactive: !!otp && getComputedStyle(otp).display !== 'none',
        error: (document.getElementById('message') || {}).innerText || ''};
    })()
    """

    private func readPage() {
        guard !view.isLoading, !certificatePending, busy else { return }
        let token = revision
        view.evaluateJavaScript(Self.snapshotJS) { [weak self] result, error in
            guard let self, self.revision == token, self.busy, !self.view.isLoading else { return }
            guard error == nil, let page = result as? [String: Any] else { return }
            if let tables = page["tables"] as? [[[String]]], let value = QuotaParser.parse(tables) {
                let identity = page["identity"] as? String ?? ""
                if let expected = self.expectedUsername, !sameAccount(expected, identity) {
                    self.finishFailure("Portal account did not match the selected login. Update this account's saved login.", authentication: true, clearQuota: true)
                    return
                }
                self.actualUsername = identity.isEmpty ? nil : identity
                self.quota = value; self.successfulRefreshes += 1
                self.busy = false; self.failed = false; self.loginSubmitted = false
                self.authenticationBlocked = false
                self.needsSavedLogin = false
                self.message = "Connected · " + value.title
                self.onChange?(); return
            }
            guard page["login"] as? Bool == true else { return }
            self.quota = nil; self.actualUsername = nil
            if page["interactive"] as? Bool == true {
                self.finishFailure("The portal needs an interactive sign-in step. Complete it in this window.", authentication: true)
                return
            }
            if self.loginSubmitted {
                let errorMessage = (page["error"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
                if !errorMessage.isEmpty {
                    self.finishFailure("Sign-in was rejected. Update the saved password or retry manually.", authentication: true)
                }
                return
            }
            do {
                guard let credentials = try KeychainStore.load(self.profileID) else {
                    self.needsSavedLogin = true
                    self.finishFailure("Save this account's username and password using Usage & accounts → Save / update login…")
                    return
                }
                self.loginSubmitted = true
                self.message = "Signing in with the saved login…"; self.onChange?()
                let script = try LoginScript.make(credentials)
                self.view.evaluateJavaScript(script) { [weak self] result, error in
                    guard let self, self.revision == token else { return }
                    // A successful submit can destroy the old JS context. Navigation
                    // callbacks, rather than the script return value, determine success.
                    if result as? Bool == false && error == nil {
                        self.finishFailure("The portal login form changed. Sign in manually in this window.", authentication: true)
                    }
                }
            } catch { self.finishFailure(error.localizedDescription, authentication: true) }
        }
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        // Handle sign-ins completed manually after an earlier missing-login message.
        if !busy { busy = true; lastAttempt = Date() }
        readPage()
    }

    func webViewWebContentProcessDidTerminate(_ webView: WKWebView) {
        busy = false
        message = "Portal process restarted. Reconnecting…"; onChange?()
        refresh()
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) { navigationFailed(error) }
    func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { navigationFailed(error) }
    private func navigationFailed(_ error: Error) {
        let ns = error as NSError
        if ns.code == NSURLErrorCancelled { return }
        if [-1200, -1201, -1202, -1203, -1204].contains(ns.code) {
            finishFailure("Portal certificate not approved. Click Refresh to review it.")
        } else { finishFailure("Cannot reach the portal (\(ns.code)). Check campus LAN; retrying at the next update.") }
    }

    func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction, decisionHandler: @escaping (WKNavigationActionPolicy) -> Void) {
        guard let url = navigationAction.request.url else { decisionHandler(.cancel); return }
        let allowed = url.scheme == portalURL.scheme && url.host == portalURL.host && url.port == portalURL.port
        decisionHandler(allowed || url.absoluteString == "about:blank" ? .allow : .cancel)
    }

    func webView(_ webView: WKWebView, didReceive challenge: URLAuthenticationChallenge, completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        let space = challenge.protectionSpace
        guard space.authenticationMethod == NSURLAuthenticationMethodServerTrust,
              space.host == portalURL.host, space.port == portalURL.port,
              let trust = space.serverTrust else { completionHandler(.performDefaultHandling, nil); return }
        if SecTrustEvaluateWithError(trust, nil) { completionHandler(.performDefaultHandling, nil); return }
        guard let chain = SecTrustCopyCertificateChain(trust) as? [SecCertificate], let leaf = chain.first else {
            completionHandler(.cancelAuthenticationChallenge, nil); return
        }
        let fingerprint = SHA256.hash(data: SecCertificateCopyData(leaf) as Data).map { String(format: "%02X", $0) }.joined(separator: ":")
        let key = "approvedCertificate.172.16.0.30.65040"
        if UserDefaults.standard.string(forKey: key) == fingerprint {
            completionHandler(.useCredential, URLCredential(trust: trust)); return
        }
        guard !certificatePending, let presentCertificate else { completionHandler(.cancelAuthenticationChallenge, nil); return }
        certificatePending = true
        let alert = NSAlert(); alert.alertStyle = .warning
        alert.messageText = "Approve the campus portal certificate?"
        alert.informativeText = "macOS cannot verify 172.16.0.30:65040. An impostor could read your login. Verify this certificate with campus IT.\n\nCertificate: \(SecCertificateCopySubjectSummary(leaf) as String? ?? "Unknown")\nSHA-256:\n\(fingerprint)\n\nApproval is only for this exact certificate at this portal inside Campus Quota. A changed certificate requires approval again."
        alert.addButton(withTitle: "Cancel"); alert.addButton(withTitle: "Trust this certificate")
        presentCertificate(alert) { [weak self] response in
            self?.certificatePending = false
            self?.lastAttempt = Date() // Time spent reading the sheet is not a network timeout.
            if response == .alertSecondButtonReturn {
                UserDefaults.standard.set(fingerprint, forKey: key)
                completionHandler(.useCredential, URLCredential(trust: trust))
            } else {
                self?.finishFailure("Portal certificate approval was cancelled.")
                completionHandler(.cancelAuthenticationChallenge, nil)
            }
        }
    }
}
