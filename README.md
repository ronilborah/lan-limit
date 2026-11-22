# Campus Quota

Campus Quota shows how much campus data you have left in the macOS menu bar or Windows system tray. It supports saved accounts and refreshes every five minutes.

## Run after cloning

Connect to the campus LAN, open the cloned folder in Finder or File Explorer, and double-click the file for your computer.

| Computer | Double-click in the repository root |
| --- | --- |
| Mac running macOS 14 or later, Apple silicon or Intel | **Campus Quota.app** |
| Windows 10 or 11 | **Campus Quota.exe** |

Both apps are included in the clone. The Windows executable bundles .NET and uses Microsoft Edge WebView2. If the portal window reports that WebView2 is missing, install the [Microsoft Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) and reopen the app. The Mac app is ad hoc signed and the Windows app is unsigned, so the operating system may prompt on first launch.

For Windows account setup, tray controls and diagnostics, see the [Windows guide](Windows/README.md).

## Using the Mac app

1. Open **Campus Quota.app** on campus LAN. Keep one installed copy and quit older versions before replacing them.
2. If prompted about an untrusted portal certificate, verify its SHA-256 fingerprint with campus IT before approving. Approval is restricted to that exact certificate at this portal inside the app.
3. Click **Save login…**, enter a label, campus username and password, then **Save & sign in**. Existing profiles are preserved; save their passwords once to enable automatic login.
4. Use **Accounts → Add account…** for more logins. Switch accounts from that submenu without re-entering their passwords.
5. Close the window to keep the app running in the menu bar. Click its label for used/remaining data, last/next update, accounts, and manual refresh. Reopen from Finder to bring the window back. **Quit Campus Quota** stops it.

The window's **Usage & accounts** button opens the same menu. To start after logging into macOS, add your installed copy in System Settings → General → Login Items.

## Background monitoring

The native timer refreshes every 300 seconds, whether the portal window is visible or closed. Its common run-loop mode also works while menus are open. A macOS process activity avoids App Nap interfering with monitoring while still allowing the Mac to sleep. Wake and recovery from a network failure request a fresh reading.

WebKit is attached to a retained native window before loading, including startup and account switches. Requests have a timeout and watchdog; WebKit process termination triggers recovery. Overlapping updates and callbacks from paused account sessions are ignored. There is no connection to Activity Monitor, Codex, a browser extension, or screen sharing.

Saved logins recover expired portal sessions automatically. A rejected password stops automatic authentication attempts until you update it or select **Refresh now**, avoiding repeated bad logins. Missing quota data displays `LAN —`, not zero. Network failures retain a `~`-marked last-known value. Samples older than six minutes or from a previous day are stale.

The app cannot refresh while the Mac is asleep, the app is quit, or the portal is unreachable. It does not estimate offline usage.

## Accounts and privacy

Each profile has its own persistent WebKit store and macOS Keychain entry. Passwords are stored in Keychain, with device-local accessibility after the first unlock, and sent through the campus portal's existing login form. Preferences store labels, usernames, profile IDs and the approved certificate fingerprint, never passwords. Removing a profile removes its saved login and local website store.

The selected account identity is checked before accepting its quota. Full usernames in different realms cannot share readings; short campus usernames match the portal identity's local part. The parser uses **Remaining** in **Cycle total network traffic**. Both upload and download count. 10,000 MB displays as 10.00 GB (1 GB = 1,000 MB).

The app has no telemetry or external backend. Top-level navigation outside the configured portal origin is blocked. Certificate verification is normal by default. Explicit approval pins one exact certificate for `172.16.0.30:65040` inside this app; other hosts and macOS/browser trust settings are unaffected. A changed certificate requires new approval.

**Switching selects the portal account being monitored. It does not automate a separate campus gateway / internet access login page.** If gateway login is separate, change that there.

## Build and verify

Install Apple's Command Line Tools, then run:

```sh
zsh build.sh
```

Output: `dist/Campus Quota.app`, for the build machine's architecture. To build for both Apple silicon and Intel:

```sh
CAMPUSQUOTA_ARCHS="arm64 x86_64" zsh build.sh
```

The script builds a temporary bundle, runs 18 checks, verifies its ad hoc signature, then replaces the output by renaming. It never overwrites the executable of a running build in place. This is a locally signed app, not an Apple notarized release.

Checks cover daily versus lifetime usage, the real portal's headers and nested layout, nonbreaking spaces, zero/invalid quotas, account identity isolation, legacy profiles, credential encoding, and refresh/staleness policy. They do not access the real Keychain or a real account. Rerun them with:

```sh
"dist/Campus Quota.app/Contents/MacOS/CampusQuota" --self-test
```

See [TESTING.md](TESTING.md) for native smoke tests.

## Updating the launchers

When publishing a change to the Mac app, build both architectures using the universal build command above, then copy the verified bundle into the repository root.

```sh
ditto "dist/Campus Quota.app" "Campus Quota.app"
```

For Windows, run the following in PowerShell with the .NET 10 SDK and Node.js installed. The root executable targets x86 so Windows can run the same file on Intel, AMD and ARM PCs. The build script also supports native x64 and ARM64 packages.

```powershell
./build-windows.ps1 -Runtime win-x86
Copy-Item 'dist/CampusQuota-win-x86/CampusQuota.exe' 'Campus Quota.exe' -Force
```

Commit the updated launcher alongside its source. GitHub Actions tests the checked-in launchers as well as the source builds.

## Diagnostics and source

**Diagnostics…** shows refresh counts and timing. A local `~/Library/Application Support/CampusQuota/health.json` contains non-secret counters and timestamps for verifying background updates, with no usernames, passwords, cookies or quota figures. It is excluded from Git.

Source files in `Sources/CampusQuota`: `AppDelegate.swift` (UI, accounts and lifecycle), `PortalController.swift` (sessions and recovery), `KeychainStore.swift` (credentials), `Core.swift` (parsing and identity), `SelfTests.swift`, and `main.swift`.
