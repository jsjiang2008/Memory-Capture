# Memory Capture

A portable Windows 11 screenshot tool and local image converter. Captures stay in memory until you explicitly save them.

## Run

Extract **MemoryCapture-1.1.0-win-x64.zip** into a folder and open **MemoryCapture.exe**. Keep the executable, DLLs, and `policy.xml` together. The portable package includes .NET and the image codecs; no installer, administrator access, or Microsoft Store HEIC extension is required.

If another copy is running, open it from the system tray. Save or copy anything you need before choosing **Exit**, then open the new version.

## Screenshots

1. Choose **Region**, **Window**, or **Full display**, then click **New capture**. The configurable **Ctrl + Shift + S** shortcut uses the selected mode.
2. Drag a rectangle, click a window, or click a display. **Escape** or right-click cancels.
3. Use **Copy** to paste the image elsewhere, or **Save as…** to create a PNG, JPG, or WebP file.

The history holds up to **20 captures or 256 MB**, evicting the oldest entries at the limit. **Delete** and **Clear all** remove temporary captures. Closing the window keeps the app running in the tray; **Exit** discards the history. Saved files and copied clipboard images remain independent of the history.

## Using the Print Screen key

Memory Capture supports **PrtSc / Print Screen** as a standalone capture shortcut.

1. Open **Windows Settings**, search for **Print Screen**, and turn off **Use the Print Screen key to open screen capture/snipping**. The wording may vary by Windows version. See [Microsoft's keyboard shortcut instructions](https://support.microsoft.com/en-gb/accessibility/windows/keyboard-shortcuts-in-windows).
2. In Memory Capture, click the shortcut button at the top (initially **Ctrl + Shift + S**), then click **Use Print Screen**. You can also press PrtSc in the shortcut dialog and click **Use shortcut**.
3. Keep Memory Capture running in the system tray. Press **PrtSc** to activate the selected region, window, or full-display capture mode. Nothing is automatically copied or saved; use **Copy** or **Save** when ready.

Your choice is remembered across launches. Closing the app window keeps its shortcut active in the tray; choosing **Exit** stops it. On some laptops, you may need **Fn + PrtSc** to send Print Screen.

If Print Screen is unavailable, disable the Windows setting and any competing screenshot app's Print Screen shortcut, then select **Use Print Screen** again. A failed change leaves the previous shortcut active. If Windows still opens its own capture tool, recheck the setting and restart Memory Capture. The app does not change Windows settings for you.

To switch back, click the shortcut button, press **Ctrl + Shift + S**, and click **Use shortcut**. Existing installations retain their shortcut until you choose a new one.

## Convert images

Add or drop HEIC/HEIF, PNG, JPG/JPEG, WebP, BMP, or TIFF files. Select a file to preview it, choose **PNG**, **JPG**, or **WebP**, and adjust JPG/WebP quality if needed.

- **Copy preview** copies the converted image at its original resolution. The on-screen preview is reduced for display.
- **Save selected…** saves one file; **Save all…** processes the queue into a selected folder.
- Existing names receive numbered suffixes. Originals are never overwritten.
- A failed item does not stop the rest of the batch. Cancel stops before the next operation; a native decode/encode already running must finish first. Files saved before cancellation remain.

Dimensions and orientation are preserved, colors are converted to sRGB, and descriptive metadata is removed. PNG/WebP preserve transparency; JPG uses a white background. HEIC uses the primary image and TIFF uses the first page. Output is 8-bit SDR. Input limits are **100 MB per file**, **50 megapixels**, and **32,768 pixels per side**, with a **512 MB native pixel-cache limit**. Other application/codec allocations are additional; this is not a total-process RAM cap.

## Storage behavior

The app does not save captures, preview thumbnails, conversion queues, or temporary image files. ImageMagick disk and mapped caches are disabled. HEIC is read directly from its existing original because passing it to the native decoder as a stream or byte array would create a temporary copy. All screenshot encoding and conversion output stays in memory until Save.

Only your chosen shortcut is persisted, at `%LOCALAPPDATA%\MemoryCapture\settings.json`. Processing is local; the app contains no upload or telemetry feature. Windows controls paging, crash dumps, clipboard history/synchronization, and file-picker history. Memory-only operation is not a guarantee that Windows never writes RAM or copied data to disk.

## Build and test

Requires Windows and the .NET 10 SDK. From PowerShell in this folder:

```powershell
.\build.ps1 -Test -Package
```

The versioned ZIP and unpacked portable app are created in `dist` (for example, `MemoryCapture-1.1.0-win-x64`). Build caches stay in `.build` and `.packages`. The image library is pinned to Magick.NET-Q8-x64 15.1.0.

`-HotkeyTest` checks actual Windows shortcut registration, conflicts, switching, and cleanup using hidden test windows. It briefly claims Print Screen if available, then releases it without changing Windows settings.

`-DesktopTest` additionally opens a temporary colored test window to verify actual Windows Graphics Capture and clipboard round-tripping. This replaces the clipboard with a small generated test image.

The test runner generates small fixtures in `test-results/fixtures`. For real HEIC coverage, place a `.heic` there before running tests. The tested sample is the [libheif example](https://github.com/strukturag/libheif/blob/master/examples/example.heic), SHA-256 `7F8B363E4936C0666A25F64F3A92FDA10BD8E5453BE4592530B65A55DD98F3F2`. The public sample is used only for development and is not bundled with the app.

## Architecture

WPF provides the main interface. A physical-pixel WinForms overlay handles multi-monitor selection; GDI captures regions/displays and Windows Graphics Capture captures application windows. `CaptureHistory` owns temporary screenshots. `ImagingService` reads originals and returns memory buffers. `ClipboardService` publishes bitmap and PNG clipboard formats. `ExportService` is the only image-file writer, uses exclusive create-new semantics, and removes incomplete output on cancellation/write failure.

## Current limits

Windows x64 only. There is no annotation, scrolling capture, video recording, automatic startup, or HDR fidelity guarantee. Minimized windows must be restored; protected content may be blank or unavailable. Mixed-DPI and negative-coordinate handling is implemented in physical pixels, but still needs hands-on validation on a multi-monitor setup. A clean Windows machine without .NET or HEIC extensions was not available for independent testing; the self-contained package includes both dependencies.

See `VERIFICATION.md` for the checks performed and `THIRD-PARTY-NOTICES.md` for dependencies.
