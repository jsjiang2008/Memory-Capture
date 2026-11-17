using Microsoft.Win32;
using System.Windows.Interop;

namespace MemoryCapture;

public interface IStartupStore
{
    string? ReadCommand();
    void WriteCommand(string command);
    void RemoveCommand();
}

/// <summary>Owns one current-user Run value. Other startup entries are never changed.</summary>
public sealed class WindowsStartupStore(
    string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run",
    string valueName = "MemoryCapture") : IStartupStore
{
    public string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }
    public void WriteCommand(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
        key.SetValue(valueName, command, RegistryValueKind.String);
    }
    public void RemoveCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

public enum StartupRegistration { Off, ThisCopy, OtherCopy }

public sealed class StartupService(IStartupStore store, string executablePath)
{
    public static StartupService ForCurrentApp() => new(new WindowsStartupStore(), Path.Combine(AppContext.BaseDirectory, "MemoryCapture.exe"));

    public static string BuildCommand(string executablePath)
    {
        if (!Path.IsPathFullyQualified(executablePath) || executablePath.IndexOfAny(['"', '\r', '\n']) >= 0 ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an absolute path to the Memory Capture executable.", nameof(executablePath));
        var command = $"\"{Path.GetFullPath(executablePath)}\" --startup";
        if (command.Length > 260)
            throw new InvalidOperationException("The app's folder path is too long for Windows startup. Move it to a shorter path and try again.");
        return command;
    }

    public StartupRegistration GetRegistration()
    {
        string? registered = store.ReadCommand();
        if (string.IsNullOrWhiteSpace(registered)) return StartupRegistration.Off;
        return string.Equals(registered.Trim(), BuildCommand(executablePath), StringComparison.OrdinalIgnoreCase)
            ? StartupRegistration.ThisCopy : StartupRegistration.OtherCopy;
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled) { store.RemoveCommand(); return; }
        string command = BuildCommand(executablePath);
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("The app executable was moved or removed. Open Memory Capture from its new folder and try again.", executablePath);
        store.WriteCommand(command);
    }
}

public static class StartupLaunch
{
    public static bool IsStartupLaunch(IEnumerable<string> arguments) => arguments.Any(arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));

    public static void OpenWindow(Window window, bool startInTray)
    {
        // A hidden HWND still raises SourceInitialized, so the tray and global hotkey
        // work immediately even though the main window has never been displayed.
        if (startInTray) new WindowInteropHelper(window).EnsureHandle();
        else window.Show();
    }
}
