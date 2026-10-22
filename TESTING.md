# Native smoke tests

Run on campus LAN using accounts you have permission to access. Never commit credentials, Keychain exports, cookies, screenshots of account data, or real-account fixtures.

1. **Fresh start:** Launch normally from Finder. Native status and account controls should appear before the page loads. Cancel on a certificate approval prompt must keep the connection blocked.
2. **Saved login:** Save a campus username/password once. Verify automatic sign-in, a correct portal username without trailing heading text, and a menu balance matching the daily total row.
3. **Switching:** Add another saved profile, switch to the first and back, without typing into the portal forms. Check profile and identity. Two profiles using one authorized account test the switching mechanism; different real accounts are required to verify distinct balances independently.
4. **Session expiry:** Use the portal Logout link on a saved profile. The app should sign back in from Keychain and recover the live balance. A rejected login should pause automatic authentication until manual retry or update; do not repeatedly test wrong passwords against a real account.
5. **Restart:** Quit and reopen the identical signed bundle. Profiles, selection and certificate approval should persist, and live balance should recover without re-entering saved logins. macOS may request Keychain access after an ad hoc binary is replaced with a differently signed build; that OS prompt is separate from portal login.
6. **Background:** Record Diagnostics counters or the health file, close the portal window, and leave Activity Monitor/screen sharing closed. After 300 seconds plus timer tolerance and load time, automatic triggers and the successful-reading timestamp should advance without a manual refresh. Reopen through Finder to inspect the balance.
7. **Offline / wake:** Disconnect from campus LAN and refresh once. Existing data should be marked last-known. Reconnect and check recovery. Sleep and wake the Mac and check a fresh attempt; no update is expected during sleep.
8. **Cleanup:** Remove only profiles created for testing. Existing user profiles should remain intact.

Offline checks run with `zsh build.sh`; passing them alone does not establish that live authentication or background monitoring works.
