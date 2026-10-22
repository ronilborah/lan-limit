import AppKit
import WebKit
import Network

final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private var status: NSStatusItem!
    private var profiles: [Profile] = []
    private var selected: UUID?
    private var controllers: [UUID: PortalController] = [:]
    private var window: NSWindow?
    private var connectionLabel: NSTextField?
    private var timer: Timer?
    private var activity: NSObjectProtocol?
    private var observers: [NSObjectProtocol] = []
    private let network = NWPathMonitor()
    private var automaticRefreshCount = 0
    private var manualRefreshCount = 0
    private var nextUpdate = Date().addingTimeInterval(refreshInterval)
    private var networkAvailable = false
    private var lastStatus = "Starting"
    private let isDemo = CommandLine.arguments.contains("--demo")

    private var profile: Profile? { profiles.first { $0.id == selected } }
    private var active: PortalController? { selected.flatMap { controllers[$0] } }

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        installAppMenu()
        if let data = UserDefaults.standard.data(forKey: "profiles"), let saved = try? JSONDecoder().decode([Profile].self, from: data) { profiles = saved }
        if profiles.isEmpty { profiles = [Profile(id: UUID(), name: "My campus account", username: nil)] }
        selected = UUID(uuidString: UserDefaults.standard.string(forKey: "selected") ?? "")
        if profile == nil { selected = profiles.first?.id }
        persist()
        status = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        status.button?.font = .monospacedDigitSystemFont(ofSize: 12, weight: .medium)
        // Keep this menu bar utility eligible to service its timer when occluded.
        // This does not stop the Mac from sleeping or require screen sharing.
        activity = ProcessInfo.processInfo.beginActivity(options: [.userInitiatedAllowingIdleSystemSleep], reason: "Monitor campus quota every five minutes")
        observers.append(NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in self?.refresh(automatic: true) })
        observers.append(NotificationCenter.default.addObserver(forName: NSApplication.didBecomeActiveNotification, object: nil, queue: .main) { [weak self] _ in
            guard let self, !self.isDemo, Date() >= self.nextUpdate else { return }
            self.refresh(automatic: true)
        })
        network.pathUpdateHandler = { [weak self] path in
            DispatchQueue.main.async {
                guard let self else { return }
                let becameAvailable = !self.networkAvailable && path.status == .satisfied
                self.networkAvailable = path.status == .satisfied
                if becameAvailable && self.active?.failed == true { self.refresh(automatic: true) }
            }
        }
        network.start(queue: DispatchQueue(label: "in.ronil.campusquota.network"))
        // Attach WebKit to its retained native window before requesting a page.
        // Loading an unattached view can stall when starting in the background.
        _ = makeController()
        showPortal(nil)
        scheduleTimer()
        refresh(automatic: true)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { showPortal(nil); return true }
    func applicationWillTerminate(_ notification: Notification) {
        timer?.invalidate(); network.cancel()
        controllers.values.forEach { $0.stop() }
        for observer in observers { NotificationCenter.default.removeObserver(observer); NSWorkspace.shared.notificationCenter.removeObserver(observer) }
        if let activity { ProcessInfo.processInfo.endActivity(activity) }
    }

    private func persist() {
        UserDefaults.standard.set(try? JSONEncoder().encode(profiles), forKey: "profiles")
        UserDefaults.standard.set(selected?.uuidString, forKey: "selected")
    }

    @discardableResult
    private func makeController() -> PortalController? {
        guard let profile else { return nil }
        if let existing = controllers[profile.id] { return existing }
        let controller = PortalController(profile: profile)
        controller.onChange = { [weak self] in self?.rebuildMenu() }
        controller.presentCertificate = { [weak self] alert, completion in
            guard let self else { completion(.alertFirstButtonReturn); return }
            self.showPortal(nil)
            guard let window = self.window else { completion(.alertFirstButtonReturn); return }
            alert.beginSheetModal(for: window, completionHandler: completion)
        }
        controllers[profile.id] = controller
        return controller
    }

    private func installAppMenu() {
        let main = NSMenu(); let item = NSMenuItem(); let commands = NSMenu()
        addItem(commands, "Open Campus Quota", action: #selector(showPortal(_:)), key: "o")
        addItem(commands, "Refresh now", action: #selector(refreshAction), key: "r")
        commands.addItem(.separator())
        addItem(commands, "Quit Campus Quota", action: #selector(quit), key: "q")
        item.submenu = commands; main.addItem(item); NSApp.mainMenu = main
    }

    private func scheduleTimer() {
        timer?.invalidate()
        nextUpdate = Date().addingTimeInterval(refreshInterval)
        let timer = Timer(timeInterval: refreshInterval, repeats: true) { [weak self] _ in self?.refresh(automatic: true) }
        timer.tolerance = 5
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    private func refresh(automatic: Bool) {
        guard !isDemo, let controller = makeController() else { return }
        if automatic { automaticRefreshCount += 1 } else { manualRefreshCount += 1 }
        controller.refresh(manual: !automatic)
        if automatic { scheduleTimer() }
        rebuildMenu()
    }

    @objc private func refreshAction() { refresh(automatic: false) }

    @discardableResult
    private func addItem(_ menu: NSMenu, _ title: String, action: Selector? = nil, key: String = "") -> NSMenuItem {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
        item.target = self; menu.addItem(item); return item
    }

    private func rebuildMenu() {
        guard status != nil else { return }
        let controller = active
        let quota = controller?.quota
        let stale = quota.map { controller?.failed == true || Date().timeIntervalSince($0.sampledAt) > staleInterval || !Calendar.current.isDateInToday($0.sampledAt) } ?? false
        let menu = NSMenu()
        status.button?.title = quota.map { (stale ? "~" : "") + $0.title } ?? "LAN —"
        status.button?.setAccessibilityLabel("Campus Quota: " + (status.button?.title ?? ""))
        status.button?.toolTip = profile?.name
        addItem(menu, profile?.name ?? "Campus Quota")
        if let username = controller?.actualUsername ?? profile?.username { addItem(menu, username) }
        if let quota {
            addItem(menu, quota.title + (stale ? " (last known)" : ""))
            addItem(menu, String(format: "Used %.2f of %.2f GB", quota.usedMB / 1000, quota.allottedMB / 1000))
            addItem(menu, "Updated " + Self.time(quota.sampledAt))
        }
        addItem(menu, "Auto update: every 5 minutes")
        addItem(menu, "Next update: " + Self.time(nextUpdate))
        addItem(menu, controller?.message ?? "Ready")
        menu.addItem(.separator())
        addItem(menu, "Open portal…", action: #selector(showPortal(_:)))
        addItem(menu, "Refresh now", action: #selector(refreshAction))
        let accounts = NSMenu()
        for account in profiles {
            let item = addItem(accounts, account.name, action: #selector(switchAccount(_:)))
            item.representedObject = account.id.uuidString; item.state = selected == account.id ? .on : .off
        }
        accounts.addItem(.separator())
        addItem(accounts, "Add account…", action: #selector(addAccount))
        let accountItem = addItem(menu, "Accounts"); accountItem.submenu = accounts
        addItem(menu, "Save / update login…", action: #selector(editAccount))
        addItem(menu, "Remove this saved account…", action: #selector(removeAccount))
        menu.addItem(.separator())
        addItem(menu, "Diagnostics…", action: #selector(diagnostics))
        addItem(menu, "Quit Campus Quota", action: #selector(quit))
        status.menu = menu
        connectionLabel?.stringValue = (controller?.message ?? "Ready") + "\nAuto update every 5 minutes · next " + Self.time(nextUpdate)
        lastStatus = controller?.message ?? "Ready"
        writeHealth()
    }

    private static func time(_ date: Date) -> String {
        let formatter = DateFormatter(); formatter.timeStyle = .medium; return formatter.string(from: date)
    }

    @objc private func showPortal(_ sender: Any?) {
        guard let profile, let controller = makeController() else { return }
        let newWindow = window == nil
        let window = self.window ?? NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1120, height: 780), styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.isReleasedWhenClosed = false; window.delegate = self
        window.title = "Campus Quota — " + profile.name
        self.window = window
        let root = NSView(frame: NSRect(x: 0, y: 0, width: 1120, height: 780))
        let label = NSTextField(wrappingLabelWithString: "Connecting…")
        label.font = .systemFont(ofSize: 13); label.translatesAutoresizingMaskIntoConstraints = false
        connectionLabel = label
        let details = NSButton(title: "Usage & accounts", target: self, action: #selector(showDetails(_:)))
        let login = NSButton(title: "Save login…", target: self, action: #selector(editAccount))
        let refresh = NSButton(title: "Refresh", target: self, action: #selector(refreshAction))
        let buttons = NSStackView(views: [details, login, refresh]); buttons.orientation = .horizontal; buttons.spacing = 8; buttons.translatesAutoresizingMaskIntoConstraints = false
        let web = controller.view; web.translatesAutoresizingMaskIntoConstraints = false
        root.addSubview(label); root.addSubview(buttons); root.addSubview(web)
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16), label.topAnchor.constraint(equalTo: root.topAnchor, constant: 12),
            label.trailingAnchor.constraint(equalTo: buttons.leadingAnchor, constant: -12), label.heightAnchor.constraint(greaterThanOrEqualToConstant: 42),
            buttons.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16), buttons.topAnchor.constraint(equalTo: root.topAnchor, constant: 12),
            web.topAnchor.constraint(equalTo: label.bottomAnchor, constant: 12), web.leadingAnchor.constraint(equalTo: root.leadingAnchor),
            web.trailingAnchor.constraint(equalTo: root.trailingAnchor), web.bottomAnchor.constraint(equalTo: root.bottomAnchor)
        ])
        window.contentView = root; window.minSize = NSSize(width: 820, height: 520)
        if newWindow { window.center() }
        NSApp.activate(ignoringOtherApps: true); window.makeKeyAndOrderFront(nil)
        rebuildMenu()
    }

    @objc private func showDetails(_ sender: NSButton) {
        rebuildMenu()
        status.menu?.popUp(positioning: nil, at: NSPoint(x: 0, y: sender.bounds.height), in: sender)
    }

    @objc private func switchAccount(_ sender: NSMenuItem) {
        guard let idString = sender.representedObject as? String, let id = UUID(uuidString: idString), profiles.contains(where: { $0.id == id }) else { return }
        if id != selected { active?.pause() }
        let wasVisible = window?.isVisible == true
        selected = id; persist(); _ = makeController()
        showPortal(nil)
        if !wasVisible { window?.orderOut(nil) }
        refresh(automatic: true)
    }

    @objc private func addAccount() { accountEditor(existing: nil) }
    @objc private func editAccount() { accountEditor(existing: profile) }

    private func accountEditor(existing: Profile?) {
        let alert = NSAlert()
        alert.messageText = existing == nil ? "Add campus account" : "Save account login"
        alert.informativeText = "Your password stays in macOS Keychain. Campus Quota signs in automatically when this account's portal session expires."
        let label = NSTextField(string: existing?.name ?? "")
        let username = NSTextField(string: existing == nil ? "" : existing?.username ?? active?.actualUsername ?? "")
        let password = NSSecureTextField(string: "")
        label.placeholderString = "Account label"; username.placeholderString = "Campus username"; password.placeholderString = "Password"
        label.setAccessibilityLabel("Account label"); username.setAccessibilityLabel("Campus username"); password.setAccessibilityLabel("Password")
        let controls = NSStackView(views: [NSTextField(labelWithString: "Label"), label, NSTextField(labelWithString: "Username"), username, NSTextField(labelWithString: "Password"), password])
        controls.orientation = .vertical; controls.alignment = .leading; controls.spacing = 5
        controls.frame = NSRect(x: 0, y: 0, width: 350, height: 176)
        for field in [label, username, password] { field.widthAnchor.constraint(equalToConstant: 350).isActive = true }
        alert.accessoryView = controls
        alert.addButton(withTitle: "Save & sign in"); alert.addButton(withTitle: "Cancel")
        NSApp.activate(ignoringOtherApps: true)
        guard alert.runModal() == .alertFirstButtonReturn else { password.stringValue = ""; return }
        let user = username.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        let name = label.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !user.isEmpty, !password.stringValue.isEmpty else { showError("Enter a username and password to save this account."); return }
        let account = Profile(id: existing?.id ?? UUID(), name: name.isEmpty ? user : name, username: user)
        do {
            try KeychainStore.save(Credentials(username: user, password: password.stringValue), for: account.id)
            password.stringValue = ""
            if let index = profiles.firstIndex(where: { $0.id == account.id }) { profiles[index] = account } else { profiles.append(account) }
            selected = account.id; persist()
            let controller = makeController()
            showPortal(nil)
            controller?.invalidateSavedLogin(username: user)
        } catch { password.stringValue = ""; showError(error.localizedDescription) }
    }

    @objc private func removeAccount() {
        guard let profile else { return }
        let alert = NSAlert(); alert.messageText = "Remove \(profile.name)?"
        alert.informativeText = "Removes this app's saved login and portal cookies. Your campus account is unaffected."
        alert.addButton(withTitle: "Remove"); alert.addButton(withTitle: "Cancel")
        NSApp.activate(ignoringOtherApps: true)
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        do { try KeychainStore.remove(profile.id) } catch { showError(error.localizedDescription); return }
        controllers.removeValue(forKey: profile.id)?.stop()
        WKWebsiteDataStore.remove(forIdentifier: profile.id) { _ in }
        profiles.removeAll { $0.id == profile.id }
        if profiles.isEmpty { profiles = [Profile(id: UUID(), name: "My campus account", username: nil)] }
        selected = profiles.first?.id; persist(); _ = makeController(); showPortal(nil); refresh(automatic: true)
    }

    private func showError(_ text: String) {
        let alert = NSAlert(); alert.messageText = "Campus Quota"; alert.informativeText = text
        NSApp.activate(ignoringOtherApps: true); alert.runModal()
    }

    @objc private func diagnostics() {
        let controller = active
        let alert = NSAlert(); alert.messageText = "Campus Quota diagnostics"
        alert.informativeText = "Version 1.1.0\nRuns as a standalone macOS app\nRefresh interval: 300 seconds\nAutomatic refresh triggers: \(automaticRefreshCount)\nManual refresh triggers: \(manualRefreshCount)\nSuccessful readings for this account: \(controller?.successfulRefreshes ?? 0)\nNext automatic refresh: \(Self.time(nextUpdate))\nStatus: \(lastStatus)\n\nClosing this window, Activity Monitor, or screen sharing does not quit Campus Quota. Use Quit Campus Quota to stop it. Sleeping Macs refresh after waking."
        NSApp.activate(ignoringOtherApps: true); alert.runModal()
    }

    private func writeHealth() {
        // No usernames, passwords, cookies or quota figures in the health file.
        let data: [String: Any] = ["pid": ProcessInfo.processInfo.processIdentifier, "version": "1.1.0", "intervalSeconds": refreshInterval,
            "automaticTriggers": automaticRefreshCount, "manualTriggers": manualRefreshCount,
            "successfulReadings": active?.successfulRefreshes ?? 0, "attempts": active?.attempts ?? 0,
            "lastAttempt": active?.lastAttempt?.timeIntervalSince1970 ?? 0,
            "lastSuccess": active?.quota?.sampledAt.timeIntervalSince1970 ?? 0,
            "nextAutomaticUpdate": nextUpdate.timeIntervalSince1970, "busy": active?.busy ?? false]
        guard let bytes = try? JSONSerialization.data(withJSONObject: data, options: [.sortedKeys]),
              let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else { return }
        let folder = base.appendingPathComponent("CampusQuota", isDirectory: true)
        do { try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true); try bytes.write(to: folder.appendingPathComponent("health.json"), options: .atomic) } catch { /* Diagnostics must never interrupt monitoring. */ }
    }

    @objc private func quit() { NSApp.terminate(nil) }
}
