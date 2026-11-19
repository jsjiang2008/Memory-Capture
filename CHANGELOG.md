# Changelog

## 1.4.1 — 2026-11-18

- The main window's **Exit** button now hides the app in the system tray, like the window's **X** button.
- Temporary captures, the conversion queue, and the capture shortcut stay available while the window is hidden. Double-click the tray icon to reopen it.
- **Exit and discard captures** in the tray menu still fully quits the app and clears temporary captures.
- Updated the Exit tooltip, tray notification, and README to explain the two behaviors.

To switch versions, save any captures you need, right-click the tray icon, and choose **Exit and discard captures** before opening 1.4.1. Re-enable **Start with Windows** in the new copy if you use it.

## 1.4.0 — 2026-11-18

- Print Screen freezes the desktop before showing the mode chooser. The chooser appears over the frozen screen.
- Region, Window, and Full screen selections keep the original frozen pixels while you choose. The chooser and dimming are excluded from captured images.
- Window selection from Print Screen crops the window's visible area in the frozen frame. Direct Window capture from the main app continues to use Windows Graphics Capture.
- Cancelling the chooser dismisses the frozen overlay without creating a capture.
- Added integration checks for frozen pixels, all three modes, capture dimensions, and cancellation.

Save any captures you need and exit the older app before opening 1.4.0. If you use **Start with Windows**, enable it in the new copy to update the startup location.

## 1.3.1 — 2026-11-18

- Show the app name and version in the main window, screenshot chooser, and capture shortcut dialog title bars (for example, **Memory Capture 1.3.1**).
- Read all three titles from the built app version so they stay consistent with future updates.

Save any captures you need and exit the older app before opening 1.3.1. If you use **Start with Windows**, enable it in the new copy to update the startup location.

## 1.3.0 — 2026-11-17

- Print Screen now opens a capture chooser with **Region (drag)**, **Window**, and **Full screen** options before taking a screenshot.
- Added **R**, **W**, and **F** keys for choosing a mode, plus **Escape** to cancel.
- Cancelling a Print Screen capture from the tray keeps the main window hidden. Repeated shortcut presses do not open additional choosers during capture.
- The chooser closes before the screen is captured, and images remain in memory until copied or saved.

Save any captures you need and exit the older app before opening 1.3.0. If you use **Start with Windows**, enable it in the new copy to update the startup location.

## 1.2.0 — 2026-11-17

- Added an optional **Start with Windows** checkbox. Automatic launches open quietly in the system tray with the saved capture shortcut active.
- Turning the option off removes the app's startup entry. Enabling it from a new folder updates the registered app location.
- Added the version number to the window title so installed copies are easier to distinguish.
- Added startup registration and hidden-window integration checks, plus README setup instructions.

### Updating to 1.2.0

Save or copy any captures you need, choose **Exit** in the old app, then open the new version. Enable **Start with Windows** in the top-right corner to start automatically at sign-in. The option is off by default. If you move the app to another folder later, enable it again from that copy to update the startup location.

### Verification

- Release build completed with zero warnings and errors.
- All 54 existing automated checks and 21 new startup checks passed.
- An actual Windows sign-out or restart has not been tested.

## 1.1.0 — 2026-11-16

- Added standalone Print Screen capture, a **Use Print Screen** button, and support for keyboards that report Print Screen on key-up.
- Remembered the selected shortcut and retained the previous working shortcut when a new one is unavailable.
- Documented how to disable Windows' built-in Print Screen capture shortcut.

## 1.0.0 — 2026-11-16

- Initial portable Windows x64 release with region, window, and display screenshots held in memory until copied or saved.
- Added local HEIC/HEIF, PNG, JPEG, WebP, BMP, and TIFF conversion to PNG, JPG, and WebP.
- Included temporary capture history, batch conversion, collision-safe saving, and a system tray menu.
