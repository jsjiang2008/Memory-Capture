using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using MemoryCapture;

internal static class StartupChecks
{
    public static void Run(Action<bool, string> check)
    {
        string path = @"Software\MemoryCapture\Tests\" + Guid.NewGuid().ToString("N");
        var store = new WindowsStartupStore(path);
        var service = new StartupService(store, Environment.ProcessPath!);
        string? originalStartup = new WindowsStartupStore().ReadCommand();
        try
        {
            check(service.GetRegistration() == StartupRegistration.Off, "startup defaults off without creating a registry key");
            using (var absent = Registry.CurrentUser.OpenSubKey(path))
                check(absent is null, "reading startup does not write settings");
            string command = StartupService.BuildCommand(@"C:\My Apps\MemoryCapture.exe");
            check(command == "\"C:\\My Apps\\MemoryCapture.exe\" --startup", "startup quotes executable paths with spaces");
            foreach (string invalid in new[] { "MemoryCapture.exe", @"C:\Apps\app.dll", "C:\\bad\"path\\app.exe", "C:\\" + new string('x', 260) + "\\app.exe" })
            {
                bool rejected = false;
                try { StartupService.BuildCommand(invalid); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { rejected = true; }
                check(rejected, "invalid or overlong startup path rejected");
            }
            service.SetEnabled(true);
            check(service.GetRegistration() == StartupRegistration.ThisCopy, "enable persists the current executable and startup argument");
            check(new StartupService(new WindowsStartupStore(path), Environment.ProcessPath!).GetRegistration() == StartupRegistration.ThisCopy, "startup setting survives service recreation");
            using (var key = Registry.CurrentUser.OpenSubKey(path, writable: true))
            {
                check(key!.GetValueKind("MemoryCapture") == RegistryValueKind.String, "startup command stored as a literal string");
                key.SetValue("UnrelatedApp", "preserve this");
            }
            store.WriteCommand(command);
            check(service.GetRegistration() == StartupRegistration.OtherCopy, "moved or older app registration is detected");
            bool missingRejected = false;
            try { new StartupService(store, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe")).SetEnabled(true); }
            catch (FileNotFoundException) { missingRejected = true; }
            check(missingRejected && store.ReadCommand() == command, "missing executable cannot overwrite a working startup entry");
            service.SetEnabled(true);
            check(service.GetRegistration() == StartupRegistration.ThisCopy, "enabling a new copy replaces the older startup path");
            service.SetEnabled(false);
            service.SetEnabled(false);
            check(service.GetRegistration() == StartupRegistration.Off, "disable removes startup and is safe to repeat");
            using (var key = Registry.CurrentUser.OpenSubKey(path))
                check((string?)key!.GetValue("UnrelatedApp") == "preserve this", "disable preserves unrelated registry values");
            check(StartupLaunch.IsStartupLaunch(["--STARTUP"]) && !StartupLaunch.IsStartupLaunch([]) && !StartupLaunch.IsStartupLaunch(["--startup-other"]), "only the startup argument selects tray launch");
            var blocked = new StartupService(new DeniedStore(), Environment.ProcessPath!);
            bool denied = false;
            try { blocked.SetEnabled(true); } catch (UnauthorizedAccessException) { denied = true; }
            check(denied, "registration failure reaches the UI error handler");
            check(new WindowsStartupStore().ReadCommand() == originalStartup, "tests leave the real Windows startup setting unchanged");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }

        var window = new Window { Width = 300, Height = 160, ShowInTaskbar = false, ShowActivated = false, Title = "Memory Capture startup verification" };
        HotkeyService? hotkey = null;
        bool initialized = false;
        window.SourceInitialized += (_, _) =>
        {
            initialized = true;
            hotkey = new HotkeyService(window, () => { });
            check(hotkey.Register(new Shortcut(7, 0x77)), "global shortcut registers during hidden startup");
        };
        try
        {
            StartupLaunch.OpenWindow(window, startInTray: true);
            check(initialized && new WindowInteropHelper(window).Handle != 0 && !window.IsVisible, "startup initializes the window without displaying it");
            StartupLaunch.OpenWindow(window, startInTray: false);
            check(window.IsVisible && hotkey?.RegisteredShortcut is not null, "opening the hidden window retains the active shortcut");
        }
        finally { hotkey?.Dispose(); window.Close(); }
    }

    private sealed class DeniedStore : IStartupStore
    {
        public string? ReadCommand() => null;
        public void WriteCommand(string command) => throw new UnauthorizedAccessException("Test access denial");
        public void RemoveCommand() => throw new UnauthorizedAccessException("Test access denial");
    }
}
