using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Windows.Markup;
using MemoryCapture;
using CaptureMode = MemoryCapture.CaptureMode;

internal static class ChooserChecks
{
    public static void Run(Action<bool, string> check)
    {
        // Load the actual application styles without starting the app or registering its hotkey.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var appXaml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppStyles.xaml"));
        var resources = new XElement(wpf + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
            appXaml.Root!.Element(wpf + "Application.Resources")!.Elements());
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
        foreach (var mode in Enum.GetValues<CaptureMode>())
        {
            var result = Open(chooser => Buttons(chooser).Single(button => Equals(button.Tag, mode)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), render: mode == CaptureMode.Region);
            check(result.Accepted == true && result.Mode == mode, mode + $" button returns the requested capture type (accepted={result.Accepted}, mode={result.Mode})");
        }
        foreach (var (key, mode) in new[] { (Key.R, CaptureMode.Region), (Key.W, CaptureMode.Window), (Key.F, CaptureMode.Display) })
        {
            var result = Open(chooser => Press(chooser, key));
            check(result.Accepted == true && result.Mode == mode, key + " selects the requested capture type");
        }
        var cancelled = Open(chooser => Buttons(chooser).Single(button => button.IsCancel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        check(cancelled.Accepted != true && cancelled.Mode is null && !cancelled.MainMenu, "Cancel returns no capture type");
        var escaped = Open(chooser => Press(chooser, Key.Escape));
        check(escaped.Accepted != true && escaped.Mode is null && !escaped.MainMenu, "Escape cancels without selecting a capture");
        var closed = Open(chooser => chooser.Close());
        check(closed.Accepted != true && closed.Mode is null, "closing the chooser cancels capture");
        var reopened = Open(chooser =>
        {
            check(chooser.SelectedMode is null, "reopening does not reuse an earlier accepted choice");
            Press(chooser, Key.Escape);
        });
        check(reopened.Accepted != true && reopened.Mode is null, "reopened chooser can be cancelled");
        var menuButton = Open(chooser => Buttons(chooser).Single(b => b.Name == "MainMenuButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        check(menuButton.MainMenu && menuButton.Accepted != true && menuButton.Mode is null, "chooser Main menu button requests navigation without a capture");
        var menuKey = Open(chooser => Press(chooser, Key.M));
        check(menuKey.MainMenu && menuKey.Accepted != true && menuKey.Mode is null, "M requests the main menu from the chooser");
        FrozenOverlayChecks.Run(check);
        app.Shutdown();
    }

    internal static System.Collections.Generic.IEnumerable<Button> Buttons(CaptureModeWindow chooser) => ButtonsIn((Panel)chooser.Content);

    private static System.Collections.Generic.IEnumerable<Button> ButtonsIn(Panel panel)
    {
        foreach (UIElement child in panel.Children)
        {
            if (child is Button button) yield return button;
            else if (child is Panel nested)
                foreach (var nestedButton in ButtonsIn(nested)) yield return nestedButton;
        }
    }

    private static void Press(CaptureModeWindow chooser, Key key)
    {
        var source = HwndSource.FromHwnd(new WindowInteropHelper(chooser).Handle)!;
        chooser.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    }

    private static (bool? Accepted, CaptureMode? Mode, bool MainMenu) Open(Action<CaptureModeWindow> act, bool render = false)
    {
        var chooser = new CaptureModeWindow();
        Exception? failure = null;
        chooser.ContentRendered += (_, _) => chooser.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                if (render)
                {
                    chooser.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)Math.Ceiling(chooser.ActualWidth), (int)Math.Ceiling(chooser.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(chooser);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    Directory.CreateDirectory("test-results");
                    using var output = File.Create(Path.Combine("test-results", "capture-chooser.png"));
                    encoder.Save(output);
                }
                act(chooser);
            }
            catch (Exception ex) { failure = ex; chooser.Close(); }
        }));
        bool? accepted = chooser.ShowDialog();
        if (failure is not null) throw failure;
        return (accepted, chooser.SelectedMode, chooser.OpenMainMenu);
    }
}
