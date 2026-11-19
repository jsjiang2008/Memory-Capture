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
        RunSwitching(false, check);
        RunSwitching(true, check);
        RunCapture(CaptureMode.Region, false, check, mainMenu: true);
        foreach (var mode in Enum.GetValues<CaptureMode>())
        {
            RunMainMenu(mode, true, false, check);
            RunMainMenu(mode, false, true, check);
        }
        RunMainMenu(CaptureMode.Region, true, true, check);
        RunMainMenu(CaptureMode.Display, false, false, check);
    }

    private static void RunCapture(CaptureMode mode, bool cancel, Action<bool, string> check, bool mainMenu = false)
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
                    var buttons = ChooserChecks.Buttons(chooser);
                    var button = mainMenu ? buttons.Single(b => b.Name == "MainMenuButton") : cancel ? buttons.Single(b => b.IsCancel) : buttons.Single(b => Equals(b.Tag, mode));
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
        if (mainMenu)
        {
            check(overlay.OpenMainMenu && result != Forms.DialogResult.OK && overlay.Result is null, "chooser Main menu request reaches the overlay without creating an image");
            return;
        }
        if (cancel)
        {
            check(result != Forms.DialogResult.OK && overlay.Result is null && !overlay.OpenMainMenu, "cancelling the chooser dismisses the frozen overlay without a capture");
            return;
        }
        check(result == Forms.DialogResult.OK && overlay.Result is not null, mode + " returns a frozen image");
        var image = overlay.Result!;
        check(image.PixelWidth == (mode == CaptureMode.Display ? screen.Width : 320) && image.PixelHeight == (mode == CaptureMode.Display ? screen.Height : 240), mode + " crop has the expected dimensions");
        byte[] pixels = new byte[4];
        image.CopyPixels(new Int32Rect(image.PixelWidth / 2, image.PixelHeight / 2, 1, 1), pixels, 4, 0);
        check(pixels[0] == 192 && pixels[1] == 96 && pixels[2] == 32 && pixels[3] == 255, mode + " retains the original frozen pixels without the chooser or dimming");
    }

    private static void RunSwitching(bool cancel, Action<bool, string> check)
    {
        var screen = Forms.Screen.PrimaryScreen!.Bounds;
        var snapshot = new Drawing.Bitmap(screen.Width, screen.Height);
        using (var graphics = Drawing.Graphics.FromImage(snapshot)) graphics.Clear(Drawing.Color.FromArgb(32, 96, 192));
        var window = new Drawing.Rectangle(screen.X + screen.Width / 2 - 160, screen.Y + screen.Height / 2 - 120, 320, 240);
        using var overlay = new CaptureOverlay(CaptureMode.Region, true, screen, snapshot, new[] { ((nint)42, window) });
        Exception? failure = null;
        int stage = 0, ticks = 0;
        using var timer = new Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (++ticks > 100) throw new Exception("Mode switching did not finish within five seconds.");
                var choosers = Application.Current.Windows.OfType<CaptureModeWindow>().Where(w => w.IsVisible).ToArray();
                var chooser = choosers.FirstOrDefault();
                if (chooser is not null)
                {
                    if (stage == 0) { stage = 1; Choose(chooser, CaptureMode.Region); }
                    else if (stage == 2)
                    {
                        check(choosers.Length == 1 && overlay.Visible && overlay.Result is null, "repeated Backspace opens one chooser without completing a capture");
                        stage = 3;
                        Choose(chooser, CaptureMode.Window);
                    }
                    else if (stage == 4)
                    {
                        check(overlay.Visible && new WindowInteropHelper(chooser).Owner == overlay.Handle, "Change mode button reopens the chooser over the same overlay");
                        stage = 5;
                        if (cancel) ChooserChecks.Buttons(chooser).Single(b => b.IsCancel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        else Choose(chooser, CaptureMode.Display);
                    }
                    return;
                }
                if (!overlay.Enabled) return;
                int x = screen.Width / 2, y = screen.Height / 2;
                if (stage == 1)
                {
                    var button = overlay.Controls.Find("ChangeModeButton", false).OfType<Forms.Button>().Single();
                    check(button.Visible, "Change mode is available after the initial choice");
                    Mouse(overlay, "OnMouseDown", Forms.MouseButtons.Left, x - 50, y - 50);
                    Mouse(overlay, "OnMouseMove", Forms.MouseButtons.Left, x, y);
                    Key(overlay, Forms.Keys.W);
                    Mouse(overlay, "OnMouseUp", Forms.MouseButtons.Left, x, y);
                    check(overlay.SelectedMode == CaptureMode.Window && overlay.Result is null && !overlay.Capture, "switching during a drag clears it and releasing the mouse creates no capture");
                    Key(overlay, Forms.Keys.F);
                    check(overlay.SelectedMode == CaptureMode.Display && overlay.Result is null, "F switches directly to Full screen without capturing");
                    Key(overlay, Forms.Keys.R);
                    check(overlay.SelectedMode == CaptureMode.Region && overlay.Result is null, "R switches directly to Region without capturing");
                    stage = 2;
                    Key(overlay, Forms.Keys.Back);
                    Key(overlay, Forms.Keys.Back);
                }
                else if (stage == 3)
                {
                    check(overlay.SelectedMode == CaptureMode.Window && overlay.Result is null, "reopened chooser changes the selection to Window");
                    var button = overlay.Controls.Find("ChangeModeButton", false).OfType<Forms.Button>().Single();
                    if (!cancel)
                    {
                        using var rendered = new Drawing.Bitmap(overlay.Width, overlay.Height);
                        overlay.DrawToBitmap(rendered, overlay.ClientRectangle);
                        var bounds = Drawing.Rectangle.Intersect(new Drawing.Rectangle(button.Left - 12, button.Top - 62, 520, 108), overlay.ClientRectangle);
                        using var hint = rendered.Clone(bounds, Drawing.Imaging.PixelFormat.Format32bppArgb);
                        System.IO.Directory.CreateDirectory("test-results");
                        hint.Save(System.IO.Path.Combine("test-results", "mode-switch-controls.png"), Drawing.Imaging.ImageFormat.Png);
                    }
                    stage = 4;
                    button.PerformClick();
                    check(overlay.Result is null && overlay.Visible, "clicking Change mode does not capture the screen");
                }
                else if (stage == 5 && !cancel)
                {
                    check(overlay.SelectedMode == CaptureMode.Display, "a second mode change selects Full screen");
                    Mouse(overlay, "OnMouseDown", Forms.MouseButtons.Left, x, y);
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
            check(result != Forms.DialogResult.OK && overlay.Result is null, "cancelling a reopened chooser closes the overlay without a capture");
            return;
        }
        check(result == Forms.DialogResult.OK && overlay.Result is not null, "capture succeeds after repeated mode changes");
        var image = overlay.Result!;
        check(image.PixelWidth == screen.Width && image.PixelHeight == screen.Height, "final dimensions match the last selected mode");
        byte[] pixels = new byte[4];
        image.CopyPixels(new Int32Rect(40, 100, 1, 1), pixels, 4, 0);
        check(pixels[0] == 192 && pixels[1] == 96 && pixels[2] == 32, "mode changes preserve the original frozen frame and exclude the new button");
    }

    private static void RunMainMenu(CaptureMode mode, bool chooseMode, bool keyboard, Action<bool, string> check)
    {
        var screen = Forms.Screen.PrimaryScreen!.Bounds;
        var snapshot = new Drawing.Bitmap(screen.Width, screen.Height);
        using (var graphics = Drawing.Graphics.FromImage(snapshot)) graphics.Clear(Drawing.Color.FromArgb(32, 96, 192));
        using var overlay = new CaptureOverlay(mode, chooseMode, screen, snapshot, Array.Empty<(nint, Drawing.Rectangle)>());
        Exception? failure = null;
        bool ready = !chooseMode;
        int ticks = 0;
        using var timer = new Forms.Timer { Interval = 50 };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (++ticks > 100) throw new Exception("Main menu navigation did not finish within five seconds.");
                var chooser = Application.Current.Windows.OfType<CaptureModeWindow>().FirstOrDefault(w => w.IsVisible);
                if (!ready && chooser is not null) { ready = true; Choose(chooser, mode); return; }
                if (!ready || chooser is not null || !overlay.Enabled) return;
                var button = overlay.Controls.Find("MainMenuButton", false).OfType<Forms.Button>().Single();
                check(button.Visible, "Main menu is visible in " + mode + (chooseMode ? " from Print Screen" : " from direct capture"));
                if (keyboard)
                {
                    if (mode == CaptureMode.Region)
                    {
                        Mouse(overlay, "OnMouseDown", Forms.MouseButtons.Left, 200, 200);
                        Mouse(overlay, "OnMouseMove", Forms.MouseButtons.Left, 250, 250);
                    }
                    Key(overlay, Forms.Keys.M);
                }
                else button.PerformClick();
                timer.Stop();
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
        check(overlay.OpenMainMenu && overlay.Result is null && result != Forms.DialogResult.OK && !overlay.Capture,
            (keyboard ? "M" : "Main menu button") + " exits " + mode + " selection without taking a screenshot");
    }

    private static void Choose(CaptureModeWindow chooser, CaptureMode mode) =>
        ChooserChecks.Buttons(chooser).Single(b => Equals(b.Tag, mode)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void Key(CaptureOverlay overlay, Forms.Keys key) =>
        typeof(CaptureOverlay).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, [new Forms.KeyEventArgs(key)]);

    private static void Mouse(CaptureOverlay overlay, string method, Forms.MouseButtons button, int x, int y) =>
        typeof(CaptureOverlay).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, [new Forms.MouseEventArgs(button, 1, x, y, 0)]);
}
