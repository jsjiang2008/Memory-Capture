using System.Runtime.InteropServices;

namespace MemoryCapture;

public static class ClipboardService
{
    public static async Task CopyAsync(BitmapSource bitmap)
    {
        // Bitmap gives broad paste support; PNG preserves transparency for apps that support it.
        var data = new DataObject();
        data.SetImage(bitmap);
        using var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(png);
        png.Position = 0;
        data.SetData("PNG", png);
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetDataObject(data, true); return; }
            catch (COMException) when (attempt < 4) { await Task.Delay(80); }
        }
    }
}
