# Changelog

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
