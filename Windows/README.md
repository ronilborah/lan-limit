# Campus Quota for Windows

Windows 10/11 tray companion to the macOS app. Same campus portal and daily quota, saved accounts, automatic sign-in and five-minute background refreshes. This is a native Windows Forms app using Microsoft Edge WebView2, with no external backend.

## Run

After cloning, open the repository folder in File Explorer and double-click **Campus Quota.exe**. If you downloaded an architecture-specific build artifact instead, extract the ZIP into a permanent folder and double-click **CampusQuota.exe**. The .NET runtime is bundled. Microsoft Edge **WebView2 Evergreen Runtime** must be installed; download it from https://developer.microsoft.com/microsoft-edge/webview2/ if the app reports it is missing.

The launcher in the clone works across architectures. For an architecture-specific ZIP, choose `win-x64` for Intel/AMD or `win-arm64` for Windows on ARM. The executable is not Authenticode signed.

1. Connect to campus LAN and open the app.
2. If the portal certificate needs approval, verify the displayed SHA-256 fingerprint with campus IT. Approval is scoped to that certificate and portal inside this app; Windows trust settings stay unchanged.
3. Click **Save login…**, enter a label, username and password, then **Save & sign in**.
4. Right-click the tray icon for full balance, used/allotted data, timestamps, accounts, refresh, diagnostics and quit. Double-click to reopen the portal. **Usage & accounts** opens the same menu.
5. Closing the window keeps monitoring running. Choose **Quit Campus Quota** to stop it. Launching the executable again opens the existing instance.

The tray icon shows a compact remaining-GB number (prefixed with `~` for stale data); hover for the full balance and account name. Windows cannot show the same wide menu-bar text as macOS. You may need to move the icon out of the Windows tray overflow area.

For login startup, press Win+R, enter `shell:startup`, and put a shortcut to the executable you normally launch there.

## Behavior and privacy

Updates run every 300 seconds with the window closed. Wake/network recovery requests an update. A rejected password pauses automatic sign-in until manual refresh or login update. Missing readings show `LAN —`; failures preserve a marked last-known reading. Readings older than six minutes or from a previous day are stale. Sleeping or disconnected computers cannot obtain live readings.

Each account gets a separate persistent browser data directory. Saved credentials are encrypted using Windows DPAPI for the current user on this computer, rather than macOS Keychain. Preferences contain labels, usernames, profile IDs and the approved fingerprint, never plaintext passwords. Removing an account clears its WebView2 browsing data and encrypted credential. Empty browser infrastructure directories may remain.

Data lives in `%LOCALAPPDATA%\CampusQuota`. `health.json` contains only timing/counters, no usernames, cookies, passwords or quota figures. Mac accounts and credentials are not automatically imported. The portal identity must match the selected username before quota is accepted. Only the daily **Cycle total network traffic → Remaining** is read; 1 GB = 1000 MB. Gateway/internet login remains separate if the campus uses a separate page.

## Build

Install .NET 10 SDK and Node.js 22 or newer, then run from PowerShell:

```powershell
./build-windows.ps1
# For Windows on ARM:
./build-windows.ps1 -Runtime win-arm64
```

The ZIP is written to `dist/CampusQuota-<runtime>.zip`. Use `-Runtime win-x86` to rebuild the executable distributed in the repository root. The build checks the quota parser, account matching, credential encoding, staleness/origin policy and the actual injected JavaScript before publishing. GitHub Actions runs Windows-native DPAPI and WebView2 checks for the shipped executable and source builds. The portal itself is not contacted by offline checks.

## Live verification on Windows

Use authorized test accounts on campus LAN. Verify save/sign-in, daily remaining value, switch between distinct accounts, quit/reopen persistence, expired-session recovery and rejected-password pause. Close the window for more than five minutes and confirm health counters/timestamps advance. Test disconnect/reconnect and sleep/wake. Cancel certificate approval and verify the portal stays blocked. Remove only test profiles and confirm their credential files and browser data are cleared. Automated checks do not establish real-campus authentication or live background behavior.
