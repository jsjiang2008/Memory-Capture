using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MemoryCapture;

public sealed record CaptureEntry(BitmapSource Image, string Mode, DateTime Created)
{
    public string Title => $"{Mode} · {Created:h:mm:ss tt}";
    public string Detail => $"{Image.PixelWidth:N0} × {Image.PixelHeight:N0}";
    public long ByteSize => (long)Image.PixelWidth * Image.PixelHeight * 4;
}

public sealed class CaptureHistory(int maximumCount = 20, long maximumBytes = 256L * 1024 * 1024)
{
    public ObservableCollection<CaptureEntry> Items { get; } = [];
    public long ByteSize => Items.Sum(x => x.ByteSize);
    public int Add(CaptureEntry entry)
    {
        if (entry.ByteSize > maximumBytes) throw new InvalidOperationException("This screenshot is too large for the temporary history. Capture a smaller region.");
        int removed = 0;
        while (Items.Count >= maximumCount || ByteSize + entry.ByteSize > maximumBytes)
        { Items.RemoveAt(Items.Count - 1); removed++; }
        Items.Insert(0, entry);
        return removed;
    }
    public void Clear() => Items.Clear();
}

public sealed class ConversionItem(string path) : INotifyPropertyChanged
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public string Type => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
    private string _status = "Ready";
    public string Status { get => _status; set { _status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public enum OutputFormat { PNG, JPG, WebP }
public sealed record ExportOptions(OutputFormat Format, int Quality = 90)
{
    public string Extension => Format switch { OutputFormat.JPG => ".jpg", OutputFormat.WebP => ".webp", _ => ".png" };
}
public sealed record PreviewResult(BitmapSource Image, string Detail);
