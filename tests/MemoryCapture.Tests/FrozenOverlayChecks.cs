using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using MemoryCapture;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

internal static class FrozenOverlayChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var mode in Enum.GetValues<CaptureMode>()) RunCapture(mode, false, check);
        RunCapture(CaptureMode.Region, true, check);
    }

    private static void RunCapture(CaptureMode mode, bool cancel, Action<bool, string> check)
    {
        var screen = Forms.Screen.PrimaryScreen!.Bounds;
        var window = new Drawing.Rectangle(screen.X + screen.Width / 2 - 160, screen.Y + screen.Height / 2 - 120, 320, 240);
        var snapshot = new Drawing.Bitmap(screen.Width, screen.Height);
        using (var graphics = Drawing.Graphics.FromImage(snapshot)) graphics.Clear(Drawing.Color.FromArgb(32, 96, 192));
        using var overlay = new CaptureOverlay(CaptureMode.Region, true, screen, snapshot, new[] { ((nint)42, window) });
        Exception? failure = null;
        bool choseMode = false;
        int ticks = 0;
        using var timer = new Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (++ticks > 100) throw new Exception("Frozen overlay did not finish within five seconds.");
                var chooser = Application.Current.Windows.OfType<CaptureModeWindow>().FirstOrDefault(w => w.IsVisible);
                if (!choseMode && chooser is not null)
                {
                    check(overlay.Visible && new WindowInteropHelper(chooser).Owner == overlay.Handle, "mode chooser is owned by the visible frozen overlay");
                    using var rendered = new Drawing.Bitmap(overlay.Width, overlay.Height);
                    overlay.DrawToBitmap(rendered, overlay.ClientRectangle);
                    var pixel = rendered.GetPixel(20, 20);
                    check(pixel.B > pixel.R && pixel.B < 192, "frozen frame is painted and dimmed underneath the chooser");
                    choseMode = true;
                    var buttons = ((StackPanel)chooser.Content).Children.OfType<Button>();
                    var button = cancel ? buttons.Single(b => b.IsCancel) : buttons.Single(b => Equals(b.Tag, mode));
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else if (choseMode && chooser is null && overlay.Enabled && !cancel)
                {
                    check(overlay.SelectedMode == mode, "overlay uses the selected " + mode + " mode");
                    int x = screen.Width / 2, y = screen.Height / 2;
                    if (mode == CaptureMode.Region)
                    {
                        Mouse(overlay, "OnMouseDown", Forms.MouseButtons.Left, x - 160, y - 120);
                        Mouse(overlay, "OnMouseUp", Forms.MouseButtons.Left, x + 160, y + 120);
                    }
                    else Mouse(overlay, "OnMouseDown", Forms.MouseButtons.Left, x, y);
                    timer.Stop();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
                timer.Stop();
                foreach (var chooser in Application.Current.Windows.OfType<CaptureModeWindow>().ToArray()) chooser.Close();
                overlay.Close();
            }
        };
        overlay.Shown += (_, _) => timer.Start();
        var result = overlay.ShowDialog();
        timer.Stop();
        if (failure is not null) throw failure;
        if (overlay.Error is not null) throw overlay.Error;
        if (cancel)
        {
            check(result != Forms.DialogResult.OK && overlay.Result is null, "cancelling the chooser dismisses the frozen overlay without a capture");
            return;
        }
        check(result == Forms.DialogResult.OK && overlay.Result is not null, mode + " returns a frozen image");
        var image = overlay.Result!;
        check(image.PixelWidth == (mode == CaptureMode.Display ? screen.Width : 320) && image.PixelHeight == (mode == CaptureMode.Display ? screen.Height : 240), mode + " crop has the expected dimensions");
        byte[] pixels = new byte[4];
        image.CopyPixels(new Int32Rect(image.PixelWidth / 2, image.PixelHeight / 2, 1, 1), pixels, 4, 0);
        check(pixels[0] == 192 && pixels[1] == 96 && pixels[2] == 32 && pixels[3] == 255, mode + " retains the original frozen pixels without the chooser or dimming");
    }

    private static void Mouse(CaptureOverlay overlay, string method, Forms.MouseButtons button, int x, int y) =>
        typeof(CaptureOverlay).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, [new Forms.MouseEventArgs(button, 1, x, y, 0)]);
}
