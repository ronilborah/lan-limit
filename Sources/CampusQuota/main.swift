import AppKit

if CommandLine.arguments.contains("--self-test") {
    runSelfTests()
} else {
    let app = NSApplication.shared
    let delegate = AppDelegate()
    app.delegate = delegate
    withExtendedLifetime(delegate) { app.run() }
}
