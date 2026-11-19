using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace MemoryCapture;

public enum CaptureMode { Region, Window, Display }
public sealed record CaptureResult(BitmapSource Image, CaptureMode Mode);

public static class CaptureService
{
    public static Drawing.Bitmap CaptureDesktopSnapshot(Drawing.Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || (long)bounds.Width * bounds.Height * 4 > 256L * 1024 * 1024)
            throw new InvalidOperationException("The combined displays exceed the capture memory limit.");
        var bitmap = new Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Location, Drawing.Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }
    public static async Task<CaptureResult?> CaptureAsync(CaptureMode mode, bool chooseMode = false)
    {
        using var picker = new CaptureOverlay(mode, chooseMode);
        var result = picker.ShowDialog();
        if (picker.Error is { } error) throw new InvalidOperationException("Could not choose a capture mode.", error);
        if (result != Forms.DialogResult.OK) return null;
        // Print Screen uses the original frozen pixels in every mode. Direct window
        // capture retains Windows Graphics Capture for unobscured window contents.
        if (picker.SelectedMode == CaptureMode.Window && !chooseMode)
        {
            await Task.Delay(120);
            return new(await WindowCapture.CaptureAsync(picker.SelectedWindow), picker.SelectedMode);
        }
        return picker.Result is { } image ? new(image, picker.SelectedMode) : null;
    }

    public static BitmapSource FromDrawingBitmap(Drawing.Bitmap bitmap)
    {
        var area = new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(area, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
            for (int y = 0; y < bitmap.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width * 4, bitmap.Width * 4);
            // GDI screen pixels have an undefined alpha channel.
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            var image = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null, pixels, bitmap.Width * 4);
            image.Freeze();
            return image;
        }
        finally { bitmap.UnlockBits(data); }
    }
}

internal sealed class CaptureOverlay : Forms.Form
{
    private CaptureMode _mode;
    private readonly bool _chooseMode;
    private bool _awaitingMode;
    private CaptureModeWindow? _chooser;
    private readonly Forms.Button? _changeModeButton;
    public CaptureMode SelectedMode => _mode;
    public Exception? Error { get; private set; }
    private readonly Drawing.Rectangle _desktop;
    private readonly Drawing.Bitmap _snapshot;
    private readonly List<(nint Handle, Drawing.Rectangle Bounds)> _windows = [];
    private Drawing.Rectangle _selection;
    private Drawing.Point _anchor;
    private bool _dragging;
    public BitmapSource? Result { get; private set; }
    public nint SelectedWindow { get; private set; }

    public CaptureOverlay(CaptureMode mode, bool chooseMode = false)
        : this(mode, chooseMode, Forms.SystemInformation.VirtualScreen, null, null) { }

    // Tests can supply a known frozen frame without reading or saving the desktop.
    internal CaptureOverlay(CaptureMode mode, bool chooseMode, Drawing.Rectangle desktop,
        Drawing.Bitmap? snapshot, IEnumerable<(nint Handle, Drawing.Rectangle Bounds)>? windows)
    {
        _mode = mode;
        _chooseMode = _awaitingMode = chooseMode;
        _desktop = desktop;
        if ((long)_desktop.Width * _desktop.Height * 4 > 256L * 1024 * 1024)
            throw new InvalidOperationException("The combined displays exceed the capture memory limit.");
        if (windows is not null) _windows.AddRange(windows);
        else if (mode == CaptureMode.Window || chooseMode)
        {
            Native.EnumWindows((handle, _) =>
            {
                if (!Native.IsWindowVisible(handle) || Native.IsIconic(handle) || Native.GetWindowTextLength(handle) == 0) return true;
                Native.DwmGetWindowAttribute(handle, 14, out int cloaked, sizeof(int));
                if (cloaked != 0) return true;
                Native.GetWindowThreadProcessId(handle, out uint process);
                if (process == Environment.ProcessId) return true;
                if (Native.DwmGetWindowAttribute(handle, 9, out Native.Rect rect, Marshal.SizeOf<Native.Rect>()) != 0)
                    Native.GetWindowRect(handle, out rect);
                var bounds = Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (bounds.Width > 0 && bounds.Height > 0) _windows.Add((handle, bounds));
                return true;
            }, 0);
        }
        _snapshot = snapshot ?? CaptureService.CaptureDesktopSnapshot(_desktop);
        FormBorderStyle = Forms.FormBorderStyle.None;
        StartPosition = Forms.FormStartPosition.Manual;
        AutoScaleMode = Forms.AutoScaleMode.None;
        Bounds = _desktop;
        TopMost = true;
        ShowInTaskbar = false;
        DoubleBuffered = true;
        KeyPreview = true;
        Cursor = chooseMode ? Forms.Cursors.Default : Forms.Cursors.Cross;
        Text = App.DisplayName + " - Capture selection";
        if (chooseMode)
        {
            _changeModeButton = new Forms.Button
            {
                Name = "ChangeModeButton", Text = "Change mode (Backspace)",
                AccessibleName = "Change capture mode", Size = new Drawing.Size(230, 34),
                BackColor = Drawing.Color.FromArgb(54, 69, 99), ForeColor = Drawing.Color.White,
                FlatStyle = Forms.FlatStyle.Flat, Cursor = Forms.Cursors.Hand, Visible = false,
                Font = new Drawing.Font("Segoe UI", 10), UseVisualStyleBackColor = false
            };
            _changeModeButton.FlatAppearance.BorderColor = Drawing.Color.FromArgb(129, 140, 248);
            _changeModeButton.Click += (_, _) => RequestModeChange();
            Controls.Add(_changeModeButton);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Set physical bounds after the HWND is created, avoiding WPF device-independent units.
        Native.SetWindowPos(Handle, new nint(-1), _desktop.X, _desktop.Y, _desktop.Width, _desktop.Height, 0x0040);
        Activate();
        UpdateSelection(Pointer);
        if (_chooseMode)
        {
            // Paint the frozen desktop first; the owned chooser stays over this frame.
            Refresh();
            BeginInvoke(new Action(ChooseMode));
        }
    }

    private void ChooseMode()
    {
        if (IsDisposed || !Visible || _chooser is not null) return;
        try
        {
            _chooser = new CaptureModeWindow();
            new WindowInteropHelper(_chooser).Owner = Handle;
            if (_chooser.ShowDialog() != true || _chooser.SelectedMode is not { } selected)
            { DialogResult = Forms.DialogResult.Cancel; return; }
            _awaitingMode = false;
            Activate();
            SetMode(selected);
        }
        catch (Exception ex) { Error = ex; DialogResult = Forms.DialogResult.Cancel; }
        finally { _chooser = null; }
    }

    private void ResetSelection()
    {
        _dragging = false;
        Capture = false;
        _selection = Drawing.Rectangle.Empty;
        SelectedWindow = 0;
    }

    private void SetMode(CaptureMode mode)
    {
        ResetSelection();
        _mode = mode;
        Cursor = Forms.Cursors.Cross;
        UpdateSelection(Pointer);
    }

    private void RequestModeChange()
    {
        if (!_chooseMode || _awaitingMode || _chooser is not null) return;
        ResetSelection();
        _awaitingMode = true;
        Cursor = Forms.Cursors.Default;
        UpdateModeButton();
        Refresh();
        // Queue after the current mouse/key event so it cannot also select a capture.
        BeginInvoke(new Action(ChooseMode));
    }

    private void UpdateModeButton()
    {
        if (_changeModeButton is null) return;
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds;
        _changeModeButton.Location = new Drawing.Point(screen.Left - _desktop.Left + 36, screen.Top - _desktop.Top + 86);
        _changeModeButton.Visible = !_awaitingMode && !_dragging;
    }

    protected override void OnFormClosing(Forms.FormClosingEventArgs e)
    {
        _chooser?.Close();
        base.OnFormClosing(e);
    }

    private Drawing.Point Pointer => new(Forms.Cursor.Position.X - _desktop.X, Forms.Cursor.Position.Y - _desktop.Y);
    protected override void OnMouseDown(Forms.MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == Forms.MouseButtons.Right) { DialogResult = Forms.DialogResult.Cancel; return; }
        if (_awaitingMode || e.Button != Forms.MouseButtons.Left) return;
        UpdateSelection(e.Location);
        if (_mode == CaptureMode.Region) { _anchor = e.Location; _dragging = true; Capture = true; UpdateModeButton(); }
        else Finish();
    }
    protected override void OnMouseMove(Forms.MouseEventArgs e) { base.OnMouseMove(e); UpdateSelection(e.Location); }
    private void UpdateSelection(Drawing.Point position)
    {
        UpdateModeButton();
        if (_awaitingMode) { Invalidate(); return; }
        var screenPosition = new Drawing.Point(position.X + _desktop.X, position.Y + _desktop.Y);
        if (_mode == CaptureMode.Region && _dragging)
            _selection = Drawing.Rectangle.FromLTRB(Math.Min(_anchor.X, position.X), Math.Min(_anchor.Y, position.Y), Math.Max(_anchor.X, position.X), Math.Max(_anchor.Y, position.Y));
        else if (_mode == CaptureMode.Display)
        {
            _selection = Forms.Screen.FromPoint(screenPosition).Bounds;
            _selection.Offset(-_desktop.X, -_desktop.Y);
        }
        else if (_mode == CaptureMode.Window)
        {
            var item = _windows.FirstOrDefault(x => x.Bounds.Contains(screenPosition));
            SelectedWindow = item.Handle;
            _selection = item.Bounds;
            _selection.Offset(-_desktop.X, -_desktop.Y);
        }
        _selection.Intersect(new Drawing.Rectangle(Drawing.Point.Empty, _desktop.Size));
        Invalidate();
    }
    protected override void OnMouseUp(Forms.MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_mode == CaptureMode.Region && _dragging && e.Button == Forms.MouseButtons.Left)
        { UpdateSelection(e.Location); _dragging = false; Capture = false; UpdateModeButton(); Finish(); }
    }
    private void Finish()
    {
        if (_selection.Width < 2 || _selection.Height < 2 || (_mode == CaptureMode.Window && SelectedWindow == 0)) return;
        if (_mode != CaptureMode.Window || _chooseMode)
        {
            using var cropped = _snapshot.Clone(_selection, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            Result = CaptureService.FromDrawingBitmap(cropped);
        }
        DialogResult = Forms.DialogResult.OK;
    }
    protected override void OnKeyDown(Forms.KeyEventArgs e)
    {
        if (e.KeyCode == Forms.Keys.Escape)
        {
            e.Handled = e.SuppressKeyPress = true;
            DialogResult = Forms.DialogResult.Cancel;
        }
        else if (_chooseMode && !_awaitingMode && e.Modifiers == Forms.Keys.None)
        {
            CaptureMode? mode = e.KeyCode switch
            {
                Forms.Keys.R => CaptureMode.Region, Forms.Keys.W => CaptureMode.Window,
                Forms.Keys.F => CaptureMode.Display, _ => null
            };
            if (mode is { } selected)
            {
                e.Handled = e.SuppressKeyPress = true;
                SetMode(selected);
            }
            else if (e.KeyCode == Forms.Keys.Back)
            {
                e.Handled = e.SuppressKeyPress = true;
                RequestModeChange();
            }
        }
        base.OnKeyDown(e);
    }
    protected override void OnPaint(Forms.PaintEventArgs e)
    {
        e.Graphics.DrawImageUnscaled(_snapshot, 0, 0);
        using var shade = new Drawing.SolidBrush(Drawing.Color.FromArgb(140, 13, 20, 36));
        e.Graphics.FillRectangle(shade, ClientRectangle);
        if (_selection.Width > 0 && _selection.Height > 0)
        {
            e.Graphics.DrawImage(_snapshot, _selection, _selection, Drawing.GraphicsUnit.Pixel);
            using var pen = new Drawing.Pen(Drawing.Color.FromArgb(129, 140, 248), 2);
            e.Graphics.DrawRectangle(pen, _selection);
        }
        if (_awaitingMode) return;
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds;
        int left = screen.Left - _desktop.Left + 24, top = screen.Top - _desktop.Top + 24;
        var hint = _mode switch { CaptureMode.Region => "Drag to capture a region", CaptureMode.Window => "Click a window to capture", _ => "Click a display to capture" };
        using var font = new Drawing.Font("Segoe UI", 12);
        using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(245, 23, 36, 58));
        e.Graphics.FillRectangle(background, left, top, _chooseMode ? 520 : 440, _chooseMode ? 108 : 52);
        e.Graphics.DrawString(hint + "   |   Esc to cancel", font, Drawing.Brushes.White, left + 16, top + 12);
        if (_chooseMode)
        {
            using var shortcutFont = new Drawing.Font("Segoe UI", 10);
            e.Graphics.DrawString("R: Region    W: Window    F: Full screen", shortcutFont, Drawing.Brushes.White, left + 16, top + 38);
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) { _snapshot.Dispose(); _changeModeButton?.Font.Dispose(); } base.Dispose(disposing); }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    internal delegate bool EnumProc(nint hwnd, nint parameter);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint parameter);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect rect, int size);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
}
