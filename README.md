# Memory Capture

A portable Windows 11 screenshot tool and local image converter. Captures stay in memory until you explicitly save them, preventing random screenshots from cluttering your hard drive. Memory Capture also allows for image type conversion, meaning no more searching up a conversion website on Google and wasting a bunch of your time.

## Run

Extract **MemoryCapture-1.5.0-win-x64.zip** into a folder and open **MemoryCapture.exe**. Keep the executable, DLLs, and `policy.xml` together. The portable package includes .NET and the image codecs; no installer, administrator access, or Microsoft Store HEIC extension is required.

If another copy is running, open it from the system tray. Save or copy anything you need, then right-click the tray icon and choose **Exit and discard captures** before opening the new version. The main window's **Exit** button only hides the app.

## Screenshots

1. Choose **Region**, **Window**, or **Full display**, then click **New capture**. The configurable **Ctrl + Shift + S** shortcut uses the selected mode.
2. Drag a rectangle, click a window, or click a display. **Escape** or right-click cancels.
3. Use **Copy** to paste the image elsewhere, or **Save as…** to create a PNG, JPG, or WebP file.

The history holds up to **20 captures or 256 MB**, evicting the oldest entries at the limit. **Delete** and **Clear all** remove temporary captures. Clicking **Exit** in the main window or closing it with **X** keeps the app running in the tray, preserving temporary captures, the conversion queue, and the capture shortcut. Double-click the tray icon to reopen it. To fully quit and discard the temporary history, right-click the tray icon and choose **Exit and discard captures**. Saved files and copied clipboard images remain independent of the history.

## Using the Print Screen key

Memory Capture supports **PrtSc / Print Screen** as a standalone capture shortcut.

1. Open **Windows Settings**, search for **Print Screen**, and turn off **Use the Print Screen key to open screen capture/snipping**. The wording may vary by Windows version. See [Microsoft's keyboard shortcut instructions](https://support.microsoft.com/en-gb/accessibility/windows/keyboard-shortcuts-in-windows).
2. In Memory Capture, click the shortcut button at the top (initially **Ctrl + Shift + S**), then click **Use Print Screen**. You can also press PrtSc in the shortcut dialog and click **Use shortcut**.
3. Keep Memory Capture running in the system tray. Press **PrtSc** to freeze the screen and open the capture chooser over that frozen image. Select **Region (drag)**, **Window**, or **Full screen**, then drag an area or click a window/display. You can also press **R**, **W**, or **F** in the chooser; **Escape** cancels. Nothing is automatically copied or saved; use **Copy** or **Save** when ready.

Before completing a Print Screen capture, click **Change mode (Backspace)** on the frozen screen or press **Backspace** to reopen the chooser. You can also press **R**, **W**, or **F** to switch directly to Region, Window, or Full screen. Switching clears any unfinished drag and keeps the same frozen image. **Escape** still cancels the entire capture.

All three Print Screen modes use the same frozen frame, so content does not advance while you choose. The chooser and selection overlay are excluded from the resulting screenshot. **Window** crops the selected window's visible screen area; overlapping windows remain visible in that crop. For a live capture of the window itself, select **Window** in the main app and use **New capture**.

Your shortcut choice is remembered across launches. Print Screen asks for the capture type each time. **New capture**, the tray capture command, and other configured shortcuts continue to use the mode selected in the main window. Cancelling the chooser or selection while the app is in the tray leaves it there. Closing the app window or clicking its **Exit** button keeps the shortcut active in the tray; **Exit and discard captures** in the tray menu stops it. On some laptops, you may need **Fn + PrtSc** to send Print Screen.

If Print Screen is unavailable, disable the Windows setting and any competing screenshot app's Print Screen shortcut, then select **Use Print Screen** again. A failed change leaves the previous shortcut active. If Windows still opens its own capture tool, recheck the setting and restart Memory Capture. The app does not change Windows settings for you.

To switch back, click the shortcut button, press **Ctrl + Shift + S**, and click **Use shortcut**. Existing installations retain their shortcut until you choose a new one.

## Start with Windows

Check **Start with Windows** in the top-right corner of the app. At your next Windows sign-in, Memory Capture starts quietly in the system tray with your saved capture shortcut active. Double-click the tray icon to open the window. Uncheck the option to stop future automatic launches; the current session stays open.

The option is off by default, applies only to your Windows account, and needs no administrator access. Keep the portable app in a permanent folder before enabling it. After moving the app or upgrading into a new folder, open the new copy and check the option there to update the startup location. The main window, screenshot chooser, and capture shortcut dialog show the running app version in their title bars.

If you previously added a shortcut through `shell:startup`, remove that manual shortcut to avoid duplicate launches. If Windows Settings > Apps > Startup has disabled Memory Capture, re-enable it there too. Windows can delay startup apps after sign-in.

## Convert images

Add or drop HEIC/HEIF, PNG, JPG/JPEG, WebP, BMP, or TIFF files. Select a file to preview it, choose **PNG**, **JPG**, or **WebP**, and adjust JPG/WebP quality if needed.

- **Copy preview** copies the converted image at its original resolution. The on-screen preview is reduced for display.
- **Save selected…** saves one file; **Save all…** processes the queue into a selected folder.
- Existing names receive numbered suffixes. Originals are never overwritten.
- A failed item does not stop the rest of the batch. Cancel stops before the next operation; a native decode/encode already running must finish first. Files saved before cancellation remain.

Dimensions and orientation are preserved, colors are converted to sRGB, and descriptive metadata is removed. PNG/WebP preserve transparency; JPG uses a white background. HEIC uses the primary image and TIFF uses the first page. Output is 8-bit SDR. Input limits are **100 MB per file**, **50 megapixels**, and **32,768 pixels per side**, with a **512 MB native pixel-cache limit**. Other application/codec allocations are additional; this is not a total-process RAM cap.

## Storage behavior

The app does not save captures, preview thumbnails, conversion queues, or temporary image files. ImageMagick disk and mapped caches are disabled. HEIC is read directly from its existing original because passing it to the native decoder as a stream or byte array would create a temporary copy. All screenshot encoding and conversion output stays in memory until Save.

Your chosen shortcut is persisted at `%LOCALAPPDATA%\MemoryCapture\settings.json`. Enabling **Start with Windows** also creates the `MemoryCapture` value under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`; turning it off removes that value. Neither setting contains image data. Processing is local; the app contains no upload or telemetry feature. Windows controls paging, crash dumps, clipboard history/synchronization, and file-picker history. Memory-only operation is not a guarantee that Windows never writes RAM or copied data to disk.

## Build and test

Requires Windows and the .NET 10 SDK. From PowerShell in this folder:

```powershell
.\build.ps1 -Test -Package
```

The versioned ZIP and unpacked portable app are created in `dist` (for example, `MemoryCapture-1.5.0-win-x64`). Build caches stay in `.build` and `.packages`. The image library is pinned to Magick.NET-Q8-x64 15.1.0.

`-HotkeyTest` checks actual Windows shortcut registration, conflicts, switching, and cleanup using hidden test windows. It briefly claims Print Screen if available, then releases it without changing Windows settings.

`-ChooserTest` checks chooser buttons, keyboard selection, cancellation, reopening, and the frozen-screen flow for all three modes, including reopening the chooser, repeated mode switches, and switching during a drag. It temporarily displays a generated image as the frozen overlay and verifies the captured pixels and dimensions; it does not read or save your real desktop.

`-StartupTest` checks startup registration in an isolated temporary registry key, path validation, enable/disable, upgrades, and hidden-window shortcut registration. It does not enable startup for your account. It briefly opens a test window.

`-DesktopTest` additionally opens a temporary colored test window to verify actual Windows Graphics Capture and clipboard round-tripping. This replaces the clipboard with a small generated test image.

The test runner generates small fixtures in `test-results/fixtures`. For real HEIC coverage, place a `.heic` there before running tests. The tested sample is the [libheif example](https://github.com/strukturag/libheif/blob/master/examples/example.heic), SHA-256 `7F8B363E4936C0666A25F64F3A92FDA10BD8E5453BE4592530B65A55DD98F3F2`. The public sample is used only for development and is not bundled with the app.

## Architecture

WPF provides the main interface. A physical-pixel WinForms overlay handles multi-monitor selection; GDI freezes the desktop for regions/displays and every Print Screen mode; Windows Graphics Capture handles direct application-window capture. `CaptureHistory` owns temporary screenshots. `ImagingService` reads originals and returns memory buffers. `ClipboardService` publishes bitmap and PNG clipboard formats. `ExportService` is the only image-file writer, uses exclusive create-new semantics, and removes incomplete output on cancellation/write failure.

## Current limits

Windows x64 only. There is no annotation, scrolling capture, video recording, or HDR fidelity guarantee. Minimized windows must be restored; protected content may be blank or unavailable. Mixed-DPI and negative-coordinate handling is implemented in physical pixels, but still needs hands-on validation on a multi-monitor setup. A clean Windows machine without .NET or HEIC extensions was not available for independent testing; the self-contained package includes both dependencies.

See `VERIFICATION.md` for the checks performed and `THIRD-PARTY-NOTICES.md` for dependencies.
