using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Interop;

namespace MemoryCapture;

public sealed record Shortcut(uint Modifiers = 6, uint VirtualKey = 0x53)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((Modifiers & 4) != 0) parts.Add("Shift");
        if ((Modifiers & 1) != 0) parts.Add("Alt");
        parts.Add(KeyInterop.KeyFromVirtualKey((int)VirtualKey).ToString());
        return string.Join(" + ", parts);
    }
    public bool IsValid => (Modifiers & ~7u) == 0 && (Modifiers & 3) != 0 &&
        (VirtualKey is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 or >= 0x70 and <= 0x7A);
}

public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Action _action;
    private int _currentId;
    private int _nextId = 100;
    public HotkeyService(Window window, Action action)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)!;
        _action = action;
        _source.AddHook(Hook);
    }
    public bool Register(Shortcut shortcut)
    {
        if (!shortcut.IsValid) return false;
        int id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, shortcut.Modifiers | 0x4000, shortcut.VirtualKey)) return false;
        if (_currentId != 0) UnregisterHotKey(_source.Handle, _currentId);
        _currentId = id;
        return true;
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == _currentId) { handled = true; _action(); }
        return 0;
    }
    public void Dispose()
    {
        if (_currentId != 0) UnregisterHotKey(_source.Handle, _currentId);
        _source.RemoveHook(Hook);
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}

public static class Preferences
{
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoryCapture", "settings.json");
    public static Shortcut Load()
    {
        try { var shortcut = JsonSerializer.Deserialize<Shortcut>(File.ReadAllText(SettingsPath)); return shortcut is { IsValid: true } ? shortcut : new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public static void Save(Shortcut shortcut)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(shortcut));
    }
}

internal sealed class ShortcutWindow : Window
{
    private readonly TextBlock _display;
    public Shortcut Chosen { get; private set; }
    public ShortcutWindow(Shortcut current)
    {
        Chosen = current;
        Title = "Capture shortcut"; Width = 450; Height = 245; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Press your new shortcut", FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Use Ctrl or Alt with a letter, number, or F1–F11.", Margin = new Thickness(0, 8, 0, 16), Foreground = Brushes.SlateGray });
        _display = new TextBlock { Text = current.ToString(), FontSize = 20, Foreground = Brushes.Indigo, Margin = new Thickness(0, 0, 0, 20) };
        panel.Children.Add(_display);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var save = new Button { Content = "Use shortcut", IsDefault = true };
        save.Click += (_, _) => { DialogResult = true; };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        buttons.Children.Add(save); buttons.Children.Add(cancel); panel.Children.Add(buttons); Content = panel;
        PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Escape or Key.Enter or Key.Tab) return;
            uint modifiers = 0;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 2;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 1;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 4;
            var next = new Shortcut(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) && next.IsValid) { Chosen = next; _display.Text = next.ToString(); }
            e.Handled = true;
        };
    }
}
