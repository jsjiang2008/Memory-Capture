using System.Windows.Input;

namespace MemoryCapture;

public sealed class CaptureModeWindow : Window
{
    public CaptureMode? SelectedMode { get; private set; }

    public CaptureModeWindow()
    {
        Title = $"{App.DisplayName} - Choose a screenshot type";
        Width = 510;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        Topmost = true;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "What would you like to capture?", FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Screen frozen. You can change modes before capturing.", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 8, 0, 18) });
        AddMode(panel, CaptureMode.Region, "Region (drag)", "Drag a rectangle around an area.", "R");
        AddMode(panel, CaptureMode.Window, "Window", "Click an application window.", "W");
        AddMode(panel, CaptureMode.Display, "Full screen", "Click the display you want to capture.", "F");
        var cancel = new Button { Content = "Cancel (Esc)", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        panel.Children.Add(cancel);
        Content = panel;
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.None) return;
            CaptureMode? mode = e.Key switch { Key.R => CaptureMode.Region, Key.W => CaptureMode.Window, Key.F => CaptureMode.Display, _ => null };
            if (mode is { } selected) { e.Handled = true; Choose(selected); }
            else if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; }
        };
    }

    private void AddMode(StackPanel panel, CaptureMode mode, string title, string description, string key)
    {
        var label = new StackPanel { Width = 388 };
        label.Children.Add(new TextBlock { Text = title + "   (" + key + ")", FontWeight = FontWeights.SemiBold, FontSize = 16 });
        label.Children.Add(new TextBlock { Text = description, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 5, 0, 0) });
        var button = new Button { Content = label, Tag = mode, Margin = new Thickness(0, 0, 0, 8), IsDefault = mode == CaptureMode.Region };
        button.Click += (_, _) => Choose(mode);
        panel.Children.Add(button);
        if (mode == CaptureMode.Region) Loaded += (_, _) => button.Focus();
    }

    private void Choose(CaptureMode mode)
    {
        SelectedMode = mode;
        DialogResult = true;
    }
}
