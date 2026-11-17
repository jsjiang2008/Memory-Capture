# Verification

Verified on this Windows x64 development computer. Base and startup checks rerun November 17, 2026; previous desktop/UI checks performed November 16, 2026.

- Release build completes with zero warnings and errors.
- 54 automated checks cover every supported input family to PNG/JPG/WebP, including a real libheif HEIC sample; EXIF orientation; metadata removal; alpha handling; unchanged originals; previews and encoding without filesystem writes; corrupt/missing inputs; cancellation; collision-safe exports; failure on unavailable destinations; history limits and clearing; screenshot encoding; and failure without disk spill under memory pressure.
- A filesystem watcher observes the test input/output/cache tree during conversion and preview. This caught native HEIC spooling, which was corrected by reading the existing original file directly.
- Eight desktop integration checks verify real window and full-display capture dimensions and known pixel colors, region cropping, GDI bitmap conversion, bitmap clipboard round-tripping, and PNG clipboard availability.
- Version 1.1.0 adds standalone Print Screen selection, a dedicated selection button, key-up handling, remembered settings, and retry after a registration conflict. Ten native hotkey checks pass: registration, repeated selection, exclusive ownership, retained previous shortcuts on failure, Print Screen switching, and release on exit. Physical PrtSc behavior with the Windows snipping toggle disabled still needs a user check.
- Version 1.2.0 adds Start with Windows and a hidden startup launch. All 54 base checks and 21 startup checks pass. Startup checks use an isolated temporary registry key and verify path quoting, invalid/overlong paths, enable/disable, retained unrelated values, updating an older app location, missing-executable errors, access-denial propagation, and unchanged real startup settings. A native WPF window initializes and registers a global shortcut while hidden, then opens with the shortcut retained. An actual sign-out/reboot remains a manual check.
- A self-contained Windows x64 publish includes .NET 10.0.14, the Windows Desktop runtime, the image library, native HEIC codecs, and the no-disk-cache policy.
- The running UI was inspected for capture and converter layouts. A 248 × 151 region capture appeared in memory; a real HEIC preview loaded at 1,280 × 854.

Not independently verified: a clean machine without installed .NET/HEIC extensions; mixed-DPI multi-monitor selection; protected windows; HDR color accuracy. These remain manual release checks, not claims of passing tests.
