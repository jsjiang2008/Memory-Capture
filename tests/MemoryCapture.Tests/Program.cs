using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;
using MemoryCapture;

internal static class Program
{
    private static int _passed;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAIL: " + name); _passed++; Console.WriteLine("PASS: " + name); }
    private static void Throws(Action action, string name)
    { try { action(); } catch { Check(true, name); return; } throw new Exception("FAIL: " + name); }
    private static MagickImage Solid(byte alpha = 255)
    {
        var pixels = new byte[32 * 24 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 32; pixels[i + 1] = 96; pixels[i + 2] = 192; pixels[i + 3] = alpha; }
        var image = new MagickImage();
        image.ReadPixels(pixels, new PixelReadSettings(32, 24, StorageType.Char, PixelMapping.RGBA));
        return image;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--chooser-tests")) { ChooserChecks.Run(Check); Console.WriteLine($"{_passed} chooser checks passed."); return 0; }
            if (args.Contains("--startup-tests")) { StartupChecks.Run(Check); Console.WriteLine($"{_passed} startup checks passed."); return 0; }
            if (args.Contains("--desktop")) return DesktopChecks();
            if (args.Contains("--hotkeys")) { ShortcutChecks.Desktop(Check); return 0; }
            string root = Path.GetFullPath(args.FirstOrDefault() ?? "test-results");
            Directory.CreateDirectory(root);
            string fixtures = Path.Combine(root, "fixtures");
            string output = Path.Combine(root, "output-" + Guid.NewGuid().ToString("N"));
            string cache = Path.Combine(root, "codec-temp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixtures); Directory.CreateDirectory(output); Directory.CreateDirectory(cache);
            ImagingService.Initialize();
            MagickNET.SetTempDirectory(cache);
            var service = new ImagingService();
            var png = Path.Combine(fixtures, "transparent.png");
            using (var image = Solid(128)) image.Write(png, MagickFormat.Png);
            foreach (var format in new[] { MagickFormat.Jpeg, MagickFormat.WebP, MagickFormat.Bmp, MagickFormat.Tiff })
            {
                using var image = Solid();
                image.Write(Path.Combine(fixtures, "sample." + format.ToString().ToLowerInvariant()), format);
            }
            var rotated = Path.Combine(fixtures, "rotated.jpg");
            using (var image = Solid())
            {
                var exif = new ExifProfile(); exif.SetValue(ExifTag.Orientation, (ushort)6); exif.SetValue(ExifTag.Artist, "Remove this metadata");
                image.SetProfile(exif); image.Orientation = Orientation.RightTop; image.Write(rotated, MagickFormat.Jpeg);
            }
            var corrupt = Path.Combine(fixtures, "broken.png"); File.WriteAllText(corrupt, "not an image");
            var before = SHA256.HashData(File.ReadAllBytes(png));
            var knownFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray();
            var writes = new ConcurrentQueue<string>();
            using var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite };
            watcher.Created += (_, e) => writes.Enqueue(e.FullPath);
            watcher.Changed += (_, e) => writes.Enqueue(e.FullPath);
            watcher.EnableRaisingEvents = true;
            foreach (string file in Directory.GetFiles(fixtures).Where(x => ImagingService.Supports(x) && x != corrupt))
            {
                foreach (var format in Enum.GetValues<OutputFormat>())
                {
                    var bytes = service.EncodeFile(file, new(format), default);
                    using var decoded = new MagickImage(bytes);
                    Check(decoded.Width > 0 && decoded.Height > 0, Path.GetFileName(file) + " → " + format);
                }
            }
            var preview = service.Preview(png, new(OutputFormat.PNG), default);
            Check(preview.Image.PixelWidth == 32 && preview.Image.IsFrozen, "preview stays in memory and is safe across threads");
            using (var image = new MagickImage(service.EncodeFile(png, new(OutputFormat.PNG), default)))
                Check(image.HasAlpha, "PNG retains transparency");
            using (var image = new MagickImage(service.EncodeFile(png, new(OutputFormat.WebP), default)))
                Check(image.HasAlpha, "WebP retains transparency");
            using (var image = new MagickImage(service.EncodeFile(png, new(OutputFormat.JPG), default)))
                Check(!image.HasAlpha, "JPG removes transparency");
            using (var image = new MagickImage(service.EncodeFile(rotated, new(OutputFormat.PNG), default)))
            {
                Check(image.Width == 24 && image.Height == 32, "EXIF rotation applied");
                Check(image.GetExifProfile() is null, "descriptive EXIF metadata removed");
            }
            Check(knownFiles.SequenceEqual(Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x)), "preview and encoding create no image or temporary files");
            Thread.Sleep(100);
            watcher.EnableRaisingEvents = false;
            Check(writes.IsEmpty, "filesystem watcher detects no writes during conversions and previews: " + string.Join(", ", writes.Distinct()));
            Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(png))), "source image unchanged");
            Throws(() => service.EncodeFile(corrupt, new(OutputFormat.PNG), default), "corrupt image fails cleanly");
            Throws(() => service.EncodeFile("missing.heic", new(OutputFormat.PNG), default), "missing source fails cleanly");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                Throws(() => service.EncodeFile(png, new(OutputFormat.PNG), cancelled.Token), "cancelled conversion does not start");
                Throws(() => ExportService.SaveUnique(Path.Combine(output, "cancelled.png"), [1, 2, 3], cancelled.Token), "cancelled export does not start");
                Check(!File.Exists(Path.Combine(output, "cancelled.png")), "cancellation leaves no output");
            }
            var encoded = service.EncodeFile(png, new(OutputFormat.PNG), default);
            var first = ExportService.SaveUnique(Path.Combine(output, "saved.png"), encoded);
            var second = ExportService.SaveUnique(first, encoded);
            Check(first != second && Path.GetFileName(second) == "saved (1).png", "existing filename receives a suffix");
            Check(File.ReadAllBytes(first).SequenceEqual(encoded), "saved image bytes are valid and not overwritten");
            Throws(() => ExportService.SaveUnique(Path.Combine(output, "missing", "image.png"), encoded), "unavailable destination reports failure");
            var small = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[64], 16); small.Freeze();
            var history = new CaptureHistory(2, 128);
            var one = new CaptureEntry(small, "Region", DateTime.Now);
            history.Add(one); history.Add(new(small, "Window", DateTime.Now));
            Check(history.Add(new(small, "Display", DateTime.Now)) == 1 && !history.Items.Contains(one), "history evicts oldest at count/memory limit");
            Check(history.ByteSize == 128, "history memory accounting");
            history.Clear(); Check(history.Items.Count == 0 && history.ByteSize == 0, "clear releases history references");
            using (var decoded = new MagickImage(service.EncodeBitmap(small, new(OutputFormat.PNG))))
                Check(decoded.Width == 4 && decoded.Height == 4, "screenshot encoder preserves dimensions");
            var sourceCapture = new CaptureEntry(small, "Region", DateTime.Now);
            Throws(() => new CaptureHistory(2, 32).Add(sourceCapture), "oversized capture rejected without evicting history");
            var previousMemory = ResourceLimits.Memory;
            ResourceLimits.Memory = 1;
            Throws(() => service.EncodeFile(png, new(OutputFormat.PNG), default), "memory exhaustion fails instead of spilling to disk");
            ResourceLimits.Memory = previousMemory;
            Check(!Directory.EnumerateFiles(cache, "*", SearchOption.AllDirectories).Any(), "codec cache directory stays empty under memory pressure");
            ShortcutChecks.Validation(Check);
            Console.WriteLine($"\n{_passed} checks passed. Outputs: {output}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static int DesktopChecks()
    {
        Exception? failure = null;
        using var form = new System.Windows.Forms.Form
        {
            Text = "Memory Capture verification target", BackColor = System.Drawing.Color.FromArgb(32, 96, 192),
            ClientSize = new System.Drawing.Size(400, 300), StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen, TopMost = true
        };
        form.Shown += async (_, _) =>
        {
            try
            {
                await Task.Delay(300);
                var captured = await WindowCapture.CaptureAsync(form.Handle);
                Check(captured.PixelWidth >= 400 && captured.PixelHeight >= 300, "Windows Graphics Capture returns the test window");
                byte[] center = new byte[4];
                captured.CopyPixels(new Int32Rect(captured.PixelWidth / 2, captured.PixelHeight / 2, 1, 1), center, 4, 0);
                Check(Math.Abs(center[0] - 192) < 5 && Math.Abs(center[1] - 96) < 5 && Math.Abs(center[2] - 32) < 5, "window capture contains expected pixels");
                var screen = System.Windows.Forms.Screen.FromHandle(form.Handle).Bounds;
                using var desktop = CaptureService.CaptureDesktopSnapshot(screen);
                Check(desktop.Width == screen.Width && desktop.Height == screen.Height, "full-display capture uses physical screen dimensions");
                var point = form.PointToScreen(new System.Drawing.Point(200, 150));
                var color = desktop.GetPixel(point.X - screen.X, point.Y - screen.Y);
                Check(Math.Abs(color.B - 192) < 5 && Math.Abs(color.G - 96) < 5 && Math.Abs(color.R - 32) < 5, "display capture contains the expected test-window pixels");
                using var region = desktop.Clone(new System.Drawing.Rectangle(point.X - screen.X, point.Y - screen.Y, 40, 30), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var regionImage = CaptureService.FromDrawingBitmap(region);
                Check(regionImage.PixelWidth == 40 && regionImage.PixelHeight == 30, "region crop retains physical dimensions");
                using var bitmap = new System.Drawing.Bitmap(40, 30);
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.Clear(System.Drawing.Color.Coral);
                var converted = CaptureService.FromDrawingBitmap(bitmap);
                Check(converted.PixelWidth == 40 && converted.PixelHeight == 30, "GDI capture bitmap converts to a frozen preview");
                // Copy to the real Windows clipboard; no image is written to disk.
                await ClipboardService.CopyAsync(converted);
                var pasted = Clipboard.GetImage();
                Check(pasted is { PixelWidth: 40, PixelHeight: 30 }, "clipboard copy/paste round trip");
                Check(Clipboard.ContainsData("PNG"), "clipboard includes a PNG representation");
                Console.WriteLine($"{_passed} desktop checks passed.");
            }
            catch (Exception ex) { failure = ex; Console.Error.WriteLine(ex); }
            finally { form.Close(); }
        };
        System.Windows.Forms.Application.Run(form);
        return failure is null ? 0 : 1;
    }
}
