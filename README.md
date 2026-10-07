# Dynamic Island

A Dynamic Island for the desktop, built with C# and Avalonia so one codebase runs on Windows and macOS.
See [DYNAMIC_ISLAND_REQUIREMENTS.md](DYNAMIC_ISLAND_REQUIREMENTS.md) for the requirements.

## Run

Requires the .NET 9 SDK (or newer).

```
dotnet run --project src/DynamicIsland.App            # real media from your player
dotnet run --project src/DynamicIsland.App -- --demo  # a fake track, for UI work
```

In VS Code, press **F5** (install the recommended extensions when prompted).

- Click the island to expand it, or press **Ctrl + Alt + I** from anywhere (Windows).
- **Esc** collapses it. Tab moves between the controls.
- Settings and Exit are in the system tray icon menu.

## Notifications (Windows, one-time setup)

Windows only lets apps with a *package identity* read other apps' notifications. To give the
locally built app one:

1. Turn on **Developer Mode**: Settings › System › For developers.
2. Build, then run `.\scripts\register-package.ps1` (or the VS Code task *register package*).
3. Start the app and choose **Allow** when Windows asks about notification access.

Notifications from Teams, Outlook, WhatsApp, browsers and other apps then appear in the island.
Click a notification to open its app; use the buttons to mute that app or dismiss it.
For the **new Teams**, keep Teams › Settings › Notifications › *Notification style* set to **Windows**.

Re-run the script if you move the build folder or switch to a Release build
(`-Configuration Release`, or `-ExePath <folder>`). `-Unregister` removes it.

## Layout

| Project | Purpose |
|---|---|
| `src/DynamicIsland.Core` | Settings, media/notification models and the interfaces each OS implements. No UI. |
| `src/DynamicIsland.App` | Avalonia UI (island + settings), tray icon, and `Platform/<OS>` implementations. |

## Platform status

| Feature | Windows | macOS |
|---|---|---|
| Island, animations, settings, tray | ✅ | ✅ (untested until built on a Mac) |
| Media info and controls | ✅ | Planned (MediaRemote adapter / AppleScript) |
| Notifications | ✅ (after the one-time setup above) | Planned (Accessibility API) |
| Hide for full-screen apps, global shortcut | ✅ | Planned |
| Start at sign-in | ✅ (HKCU Run key) | ✅ (LaunchAgent) |

## Dev tools

- `--demo` shows a fake player with generated artwork.
- `--snapshot <folder>` renders the island's compact and expanded states to PNGs and exits.
- Crashes are written to `%LocalAppData%\DynamicIsland\crash.log` (macOS: `~/Library/Application Support/DynamicIsland/crash.log`).
