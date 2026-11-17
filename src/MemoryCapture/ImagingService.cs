using ImageMagick;

namespace MemoryCapture;

/// <summary>Decodes and encodes to memory only. This service never creates an image file.</summary>
public sealed class ImagingService
{
    public const long MaximumPixels = 50_000_000;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".heic", ".heif", ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff" };
    public static bool Supports(string path) => Extensions.Contains(Path.GetExtension(path));
    public static void Initialize()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "policy.xml")))
            throw new InvalidOperationException("The image policy is missing. Extract the entire app folder and try again.");
        MagickNET.SetEnvironmentVariable("MAGICK_CONFIGURE_PATH", AppContext.BaseDirectory);
        MagickNET.Initialize();
        ResourceLimits.Memory = 512UL * 1024 * 1024;
        ResourceLimits.Disk = 0;
        ResourceLimits.Thread = 2;
        ResourceLimits.Width = 32768;
        ResourceLimits.Height = 32768;
        ResourceLimits.ListLength = 32;
    }

    private static MagickImage Read(string path)
    {
        if (!Supports(path)) throw new InvalidOperationException("Choose a HEIC, PNG, JPG, WebP, BMP, or TIFF image.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 100L * 1024 * 1024) throw new InvalidOperationException("This file exceeds the 100 MB input limit.");
        // HEIC's native decoder does not support blobs: passing a stream or byte array
        // would silently copy it to a temporary file. Read the EXISTING original instead.
        // The read-only handle above prevents concurrent modification during decoding.
        var source = new FileInfo(path);
        var settings = new MagickReadSettings { FrameIndex = 0, FrameCount = 1 };
        using (var header = new MagickImage())
        {
            header.Ping(source, settings);
            CheckSize(header.Width, header.Height);
        }
        var image = new MagickImage();
        try
        {
            image.Read(source, settings);
            CheckSize(image.Width, image.Height);
            Normalize(image);
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    private static void CheckSize(uint width, uint height)
    {
        if (width == 0 || height == 0 || (long)width * height > MaximumPixels)
            throw new InvalidOperationException("This image exceeds the 50 megapixel limit. Use a smaller image.");
    }
    private static void Normalize(MagickImage image)
    {
        image.AutoOrient();
        if (image.GetColorProfile() is not null) image.TransformColorSpace(ColorProfiles.SRGB);
        else image.ColorSpace = ColorSpace.sRGB;
        image.Strip();
        image.SetProfile(ColorProfiles.SRGB);
        image.Depth = 8;
    }
    private static MagickFormat Format(OutputFormat format) => format switch
    { OutputFormat.JPG => MagickFormat.Jpeg, OutputFormat.WebP => MagickFormat.WebP, _ => MagickFormat.Png };

    private static void Prepare(MagickImage image, ExportOptions options)
    {
        image.Quality = (uint)Math.Clamp(options.Quality, 1, 100);
        if (options.Format == OutputFormat.JPG)
        { image.BackgroundColor = MagickColors.White; image.Alpha(AlphaAction.Remove); }
    }

    public PreviewResult Preview(string path, ExportOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var image = Read(path);
        var dimensions = $"{image.Width:N0} × {image.Height:N0}";
        image.Resize(new MagickGeometry(1400, 1000) { Greater = true });
        Prepare(image, options);
        token.ThrowIfCancellationRequested();
        // Re-read the chosen encoding so the preview reflects quality and transparency settings.
        using var encoded = new MagickImage(image.ToByteArray(Format(options.Format)));
        var suffix = Path.GetExtension(path).ToLowerInvariant() switch
        { ".heic" or ".heif" => " · Primary image only", ".tif" or ".tiff" => " · First page only", _ => "" };
        token.ThrowIfCancellationRequested();
        return new PreviewResult(ToBitmap(encoded), dimensions + suffix);
    }

    public byte[] EncodeFile(string path, ExportOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var image = Read(path);
        Prepare(image, options);
        var bytes = image.ToByteArray(Format(options.Format));
        token.ThrowIfCancellationRequested();
        return bytes;
    }

    public BitmapSource ConvertForClipboard(string path, ExportOptions options, CancellationToken token)
    {
        using var converted = new MagickImage(EncodeFile(path, options, token));
        token.ThrowIfCancellationRequested();
        return ToBitmap(converted);
    }

    public byte[] EncodeBitmap(BitmapSource source, ExportOptions options)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = checked(bitmap.PixelWidth * 4);
        byte[] pixels = new byte[checked(stride * bitmap.PixelHeight)];
        bitmap.CopyPixels(pixels, stride, 0);
        using var image = new MagickImage();
        image.ReadPixels(pixels, new PixelReadSettings((uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, StorageType.Char, PixelMapping.BGRA));
        Normalize(image);
        Prepare(image, options);
        return image.ToByteArray(Format(options.Format));
    }

    public static BitmapSource ToBitmap(MagickImage image)
    {
        using var pixels = image.GetPixels();
        var bytes = pixels.ToByteArray(PixelMapping.BGRA)!;
        var bitmap = BitmapSource.Create((int)image.Width, (int)image.Height, 96, 96, PixelFormats.Bgra32, null, bytes, checked((int)image.Width * 4));
        bitmap.Freeze();
        return bitmap;
    }

    public static string FriendlyError(Exception ex) => ex switch
    {
        MagickResourceLimitErrorException => "This image needs more memory than allowed. No temporary image was saved. Try a smaller image.",
        MagickException => "This image could not be decoded or converted. It may be damaged, unsupported, or too large.",
        UnauthorizedAccessException => "Access was denied. Choose a folder you can write to, or check the source file permissions.",
        IOException => "The file could not be read or saved. Check that it is available and the destination has enough space.",
        _ => ex.Message
    };
}
