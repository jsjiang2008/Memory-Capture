using System;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MemoryCapture;

internal static class ShortcutChecks
{
    public static void Validation(Action<bool, string> check)
    {
        check(new Shortcut().IsValid, "existing default shortcut remains valid");
        check(Shortcut.PrintScreen.IsValid && Shortcut.PrintScreen.IsPrintScreen, "standalone Print Screen is valid");
        check(Shortcut.PrintScreen.ToString() == "Print Screen", "Print Screen has a readable label");
        check(KeyInterop.VirtualKeyFromKey(Key.PrintScreen) == Shortcut.PrintScreen.VirtualKey, "keyboard Print Screen maps to the registered Windows key");
        check(!new Shortcut(0, 0x53).IsValid && !new Shortcut(0, 0x70).IsValid, "ordinary bare keys are still rejected");
        check(!new Shortcut(6, 0x7B).IsValid && !new Shortcut(8, 0x2C).IsValid, "reserved F12 and Windows-key shortcuts are rejected");
        check(!new Shortcut(2, 0x2C).IsValid && !new Shortcut(4, 0x2C).IsValid, "only the standalone Print Screen shortcut is added");
        var restored = JsonSerializer.Deserialize<Shortcut>(JsonSerializer.Serialize(Shortcut.PrintScreen));
        check(restored == Shortcut.PrintScreen && restored.IsValid, "Print Screen preference survives JSON round trip");
        var existing = JsonSerializer.Deserialize<Shortcut>("{\"Modifiers\":6,\"VirtualKey\":83,\"IsValid\":true}");
        check(existing == new Shortcut() && existing.IsValid, "existing settings remain compatible");
    }

    public static void Desktop(Action<bool, string> check)
    {
        // Hidden HWNDs exercise real Windows registration without typing into any app,
        // capturing the screen, changing settings, or touching the clipboard.
        var owner = new Window();
        var competitor = new Window();
        var ownerHandle = new WindowInteropHelper(owner).EnsureHandle();
        var otherHandle = new WindowInteropHelper(competitor).EnsureHandle();
        var original = new Shortcut(7, 0x79); // Ctrl+Alt+Shift+F10
        var occupied = new Shortcut(7, 0x78); // Ctrl+Alt+Shift+F9
        using var service = new HotkeyService(owner, () => { });
        try
        {
            check(service.Register(original), "register a global shortcut with Windows");
            check(service.Register(original), "selecting the same registered shortcut succeeds");
            check(!RegisterHotKey(otherHandle, 900, original.Modifiers, original.VirtualKey), "another window cannot take the active shortcut");
            check(RegisterHotKey(otherHandle, 901, occupied.Modifiers, occupied.VirtualKey), "reserve a competing shortcut for conflict test");
            check(!service.Register(occupied) && service.RegisteredShortcut == original, "failed shortcut change retains the previous registration");
            check(!service.Register(new Shortcut(0, 0x53)) && service.RegisteredShortcut == original, "invalid shortcut leaves the active registration intact");
            bool printScreen = service.Register(Shortcut.PrintScreen);
            if (printScreen)
            {
                check(service.RegisteredShortcut == Shortcut.PrintScreen, "standalone Print Screen registers with Windows");
                check(!RegisterHotKey(otherHandle, 902, 0, 0x2C), "Print Screen is exclusively owned by the app");
                check(RegisterHotKey(otherHandle, 903, original.Modifiers, original.VirtualKey), "successful switch releases the previous shortcut");
                service.Dispose();
                check(RegisterHotKey(otherHandle, 904, 0, 0x2C), "exiting releases Print Screen for other applications");
            }
            else
            {
                check(service.RegisteredShortcut == original, "Print Screen conflict leaves the previous shortcut active");
                Console.WriteLine("MANUAL CHECK: Windows or another app owns Print Screen; disable its capture shortcut before retrying.");
            }
        }
        finally
        {
            for (int id = 900; id <= 904; id++) UnregisterHotKey(otherHandle, id);
            service.Dispose();
            owner.Close(); competitor.Close();
        }
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
