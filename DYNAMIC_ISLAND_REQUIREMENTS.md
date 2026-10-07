# Windows Dynamic Island - Requirements

## Purpose

Create a small, unobtrusive Windows desktop companion inspired by the Dynamic Island concept. It should surface useful information and quick actions in a compact area near the top of the screen, expanding when there is something to show or when the user interacts with it.

## Product Goals

- Feel quick and responsive during everyday use.
- Use very little memory and processor time while idle.
- Keep the desktop usable and avoid covering important content unnecessarily.
- Present information clearly in both compact and expanded states.
- Let users control which information and alerts appear.

## Core Functionality

### 1. Compact and Expanded States

- Show a small, pill-shaped island near the top center of the selected display by default.
- Keep the island compact when there is no active content.
- Expand smoothly when the user selects it or when an enabled event needs attention.
- Allow the user to collapse or dismiss expanded content.
- Keep the island open while the user is interacting with its controls.
- Collapse after a short period without interaction when appropriate; do not collapse while an action is in progress.
- Provide a clear visual indication when new information is available.

### 2. Notifications

- Show supported Windows notifications in the island when notification display is enabled.
- Display the app name, notification title, and a short message preview.
- Let the user open the related app or dismiss the notification when those actions are available.
- Allow notification previews to be hidden for privacy.
- Let the user enable or disable notification display.

### 3. Media Controls

- Show the currently active media item when playback information is available, including its title and artwork when provided.
- Provide play or pause, previous, and next controls when supported by the active media session.
- Make media controls available from the expanded island without requiring the user to open another window.
- Hide the media section when there is no active media session, unless the user chooses otherwise.

### 4. App Access and Settings

- Provide a system-tray entry so the user can open settings, show or hide the island, and exit the app.
- Include an option to start the app automatically when the user signs in; this must be user-controlled.
- Allow the user to choose the display and adjust the island's screen position.
- Allow the user to configure which supported notifications and features are shown.
- Provide a way to restore default settings.

## Interaction and Presentation Requirements

- Support mouse interaction for opening, closing, and using island controls.
- Use clear hover, pressed, and disabled states for interactive controls.
- Keep text readable and prevent content from being clipped in compact or expanded states.
- Make animations smooth and brief; avoid distracting movement or flashing.
- Avoid intercepting clicks outside the visible island area.
- Provide keyboard access to the main actions and a predictable way to close expanded content.

## Quality Requirements

- Start quickly and remain responsive while running in the background.
- Keep memory and processor use low, especially when idle.
- Avoid frequent unnecessary background checks and animations when there is nothing to display.
- Work across supported display sizes, scaling settings, and multi-monitor setups.
- Avoid obstructing full-screen content where practical.
- Respect Windows notification permissions and the user's notification preferences.
- Keep notification and media information on the device; do not collect or transmit it.

## Initial Release Scope

The initial release should include:

- A compact island with collapsed and expanded states.
- Mouse and keyboard interaction for opening and closing it.
- Basic settings and system-tray access.
- Notification display with user controls and privacy options.
- Active media information and supported playback controls.
- Low idle resource use and support for multiple displays.

## Possible Later Features

These are optional ideas and are not required for the initial release:

- Time, date, battery, volume, or other system-status indicators.
- Additional quick actions chosen by the user.
- More appearance and animation preferences.
- User-configurable rules for when specific content expands the island.
