# Dynamic Island

A Dynamic Island for the desktop and Android phones, built with C# and Avalonia so one codebase runs on
Windows, macOS and Android.
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

## What it does

- **Media**: artwork, title, controls, a progress bar you can drag to seek, long titles that scroll,
  colours taken from the album art. Hover the compact island to see the title.
- **Notifications** from any app (see setup below), with a history list (bell button) and per-app mute.
- **Volume and brightness**: change either and the island becomes a level bar.
- **Battery**: a green flash when you plug in, a warning at 20% and 10%.
- **Microphone / camera indicator**: an orange (mic) or green (camera) bubble while any app uses them.
- **Timer and stopwatch**: presets from 1 minute to 1 hour; the countdown rides along in a side bubble
  while music plays (the "split island").
- Everything is configurable in Settings, and everything stays on your device.

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

## Android

The phone app floats the same island over your other apps (top centre, around the camera).

**One-time setup** (Windows): install the .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`), then run
`.\scripts\setup-android.ps1`. It installs the .NET Android workload, the Android SDK and a JDK under
`%LOCALAPPDATA%\Android`. Add `-Emulator` to also create an Android 15 emulator.

**Run** on a phone (USB debugging on) or a running emulator:

```
dotnet build src/DynamicIsland.Android -t:Run
```

**Install on a phone without a cable**: `dotnet publish src/DynamicIsland.Android -c Release`, copy the
`*-Signed.apk` from `src/DynamicIsland.Android/bin/Release/net10.0-android/publish/` to the phone and open it
(allow installing from unknown sources when asked).

On first launch the app lists the permissions the island needs, each with an **Allow** button:

- **Show around the camera** (recommended): turn on *Dynamic Island* under Accessibility. Android keeps the
  status bar above normal app overlays and gives it every touch there, so only an accessibility overlay can
  sit around the camera and still be tapped. The service reads no screen content. Android 13+ may call it a
  *restricted setting* for apps not installed from a store: App info › ⋮ › **Allow restricted settings**.
- **Display over other apps**: without the option above, the island runs from a foreground service and sits
  just below the status bar.
- **Notification access**: notifications and what's playing (Android only shares media sessions with
  apps that have this).
- **Keep-running notice** (Android 13+): shown while the foreground service hosts the island.
- **Run in the background**: stops battery saving from closing the island.

Tap the island to expand it; long-press it to open settings. Touching anywhere else collapses it.

Android limitations: the mic/camera indicator can't name the app using them, and the island doesn't hide for
full-screen videos yet (Android doesn't tell overlays reliably when the status bar is hidden).

## Layout

| Project | Purpose |
|---|---|
| `src/DynamicIsland.Core` | Settings, media/notification models and the interfaces each OS implements. No UI. |
| `src/DynamicIsland.UI` | The island and settings UI (views, view models, styles), shared by every app. |
| `src/DynamicIsland.App` | Desktop app: island window, settings window, tray icon, and `Platform/<OS>` implementations. |
| `src/DynamicIsland.Android` | Android app: overlay service, settings screen, and Android implementations. Builds with .NET 10; not in the solution so desktop builds don't need the Android workload. |

## Platform status

| Feature | Windows | macOS | Android |
|---|---|---|---|
| Island, animations, settings | ✅ | ✅ (untested until built on a Mac) | ✅ (overlay window) |
| Media info and controls | ✅ | Planned (MediaRemote adapter / AppleScript) | ✅ (media sessions) |
| Notifications | ✅ (after the one-time setup above) | Planned (Accessibility API) | ✅ (notification access) |
| Volume, brightness, battery, mic/camera | ✅ | Planned | ✅ (mic/camera can't name the app) |
| Hide for full-screen apps | ✅ | Planned | Not yet |
| Global shortcut, tray icon | ✅ | Planned | — |
| Start automatically | ✅ (HKCU Run key) | ✅ (LaunchAgent) | ✅ (after the phone restarts) |

## Dev tools

- `--demo` shows a fake player with generated artwork.
- `--snapshot <folder>` renders the island's compact and expanded states to PNGs and exits.
- Set `DYNAMIC_ISLAND_DEBUG=1` to write a troubleshooting log to `debug.log` next to the settings file.
- Crashes are written to `%LocalAppData%\DynamicIsland\crash.log` (macOS: `~/Library/Application Support/DynamicIsland/crash.log`;
  Android: `crash.log` in the app's private files, readable with `adb shell run-as com.dynamicisland.app cat files/crash.log`).
