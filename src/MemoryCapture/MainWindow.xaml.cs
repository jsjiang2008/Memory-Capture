using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Interop;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace MemoryCapture;

public partial class MainWindow : Window
{
    private readonly CaptureHistory _history = new();
    private readonly ObservableCollection<ConversionItem> _files = [];
    private readonly ImagingService _imaging = new();
    private readonly StartupService _startup = StartupService.ForCurrentApp();
    private bool _startupAvailable;
    private readonly SemaphoreSlim _imagingGate = new(1, 1);
    private readonly Forms.NotifyIcon _tray;
    private CancellationTokenSource? _previewCancellation;
    private CancellationTokenSource? _operation;
    private HotkeyService? _hotkey;
    private Shortcut _shortcut = Preferences.Load();
    private bool _ready, _busy, _exiting, _exitAfterWork, _previewReady, _choosingShortcut;

    public MainWindow()
    {
        InitializeComponent();
        var iconUri = new Uri("pack://application:,,,/Assets/App.ico");
        Icon = BitmapFrame.Create(iconUri);
        CaptureList.ItemsSource = _history.Items;
        FileList.ItemsSource = _files;
        _history.Items.CollectionChanged += (_, _) => RefreshControls();
        _files.CollectionChanged += (_, _) => RefreshControls();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Memory Capture", null, (_, _) => Dispatcher.Invoke(ShowApp));
        menu.Items.Add("New capture", null, (_, _) => Dispatcher.InvokeAsync(() => BeginCaptureAsync()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit and discard captures", null, (_, _) => Dispatcher.Invoke(RequestExit));
        using var iconStream = Application.GetResourceStream(iconUri).Stream;
        _tray = new Forms.NotifyIcon { Text = "Memory Capture", Icon = new System.Drawing.Icon(iconStream), Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowApp);
        SourceInitialized += (_, _) =>
        {
            _hotkey = new HotkeyService(this, () => _ = BeginCaptureAsync(chooseMode: _shortcut.IsPrintScreen));
            if (!_hotkey.Register(_shortcut)) SetStatus(_shortcut.IsPrintScreen
                ? "Print Screen is unavailable. Turn off Windows' Print Screen screen-capture setting, then select Use Print Screen again."
                : "The capture shortcut is already in use. Click the shortcut button to choose another.");
        };
        Closing += OnClosing;
        Activated += (_, _) => RefreshStartupOption();
        _ready = true;
        ShortcutButton.Content = _shortcut.ToString();
        RefreshStartupOption();
        RefreshControls();
    }

    private void RefreshStartupOption()
    {
        try
        {
            var registration = _startup.GetRegistration();
            StartupCheckBox.IsChecked = registration == StartupRegistration.ThisCopy;
            StartupCheckBox.ToolTip = registration == StartupRegistration.OtherCopy
                ? "Another copy of Memory Capture is configured to start. Check this box to use this version instead."
                : "Start quietly in the system tray when you sign in. Windows Startup Apps settings can override this choice.";
            _startupAvailable = true;
            StartupCheckBox.IsEnabled = !_busy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or InvalidOperationException)
        {
            _startupAvailable = false;
            StartupCheckBox.IsEnabled = false;
            StartupCheckBox.ToolTip = "Could not read the Windows startup setting: " + ex.Message;
        }
    }

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = StartupCheckBox.IsChecked == true;
        try
        {
            _startup.SetEnabled(enabled);
            SetStatus(enabled
                ? "Start with Windows enabled. This copy will launch in the tray when you sign in."
                : "Start with Windows disabled. The current session stays open.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or InvalidOperationException)
        {
            SetStatus("Could not change Start with Windows: " + ex.Message);
        }
        finally { RefreshStartupOption(); }
    }

    private void ShowApp() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_exiting) { e.Cancel = true; Hide(); _tray.ShowBalloonTip(2500, "Memory Capture is still running", "Open it from the tray. Exit discards temporary captures.", Forms.ToolTipIcon.Info); }
    }
    private void RequestExit()
    {
        if (_busy) { _exitAfterWork = true; _operation?.Cancel(); SetStatus("Finishing the current operation before exiting…"); return; }
        _exiting = true;
        _previewCancellation?.Cancel();
        _hotkey?.Dispose();
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Icon?.Dispose();
        _tray.Dispose();
        CapturePreview.Source = null; ConversionPreview.Source = null;
        _history.Clear(); _files.Clear();
        Close();
        Application.Current.Shutdown();
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => RequestExit();
    private void SetStatus(string message) => StatusText.Text = message;
    private ExportOptions Options => new((OutputFormat)Math.Max(0, OutputFormatBox.SelectedIndex), (int)QualitySlider.Value);

    private void RefreshControls()
    {
        if (!_ready) return;
        bool capture = CaptureList.SelectedItem is CaptureEntry;
        bool file = FileList.SelectedItem is ConversionItem;
        HistoryCount.Text = $"{_history.Items.Count} captures · {_history.ByteSize / (1024d * 1024):0.#} MB";
        FileCount.Text = $"IMAGE QUEUE · {_files.Count}";
        QueueEmpty.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NewCaptureButton.IsEnabled = CaptureModeBox.IsEnabled = ShortcutButton.IsEnabled = !_busy;
        StartupCheckBox.IsEnabled = !_busy && _startupAvailable;
        CopyCaptureButton.IsEnabled = SaveCaptureButton.IsEnabled = DeleteCaptureButton.IsEnabled = capture && !_busy;
        ClearCapturesButton.IsEnabled = _history.Items.Count > 0 && !_busy;
        CaptureList.IsEnabled = FileList.IsEnabled = AddFilesButton.IsEnabled = OutputFormatBox.IsEnabled = !_busy;
        RemoveFileButton.IsEnabled = SaveSelectedButton.IsEnabled = file && !_busy;
        CopyConvertedButton.IsEnabled = file && _previewReady && !_busy;
        SaveAllButton.IsEnabled = ClearFilesButton.IsEnabled = _files.Count > 0 && !_busy;
        QualitySlider.IsEnabled = OutputFormatBox.SelectedIndex > 0 && !_busy;
    }

    private async Task<T> ImageWork<T>(Func<T> work, CancellationToken token)
    {
        await _imagingGate.WaitAsync(token);
        try { return await Task.Run(work, token); }
        finally { _imagingGate.Release(); }
    }

    private async Task RunOperation(Func<CancellationToken, Task> work, bool cancellable = false)
    {
        if (_busy) return;
        _busy = true;
        _previewCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _operation = cancellation;
        WorkProgress.Visibility = Visibility.Visible;
        WorkProgress.IsIndeterminate = true;
        CancelButton.Visibility = cancellable ? Visibility.Visible : Visibility.Collapsed;
        RefreshControls();
        try { await work(cancellation.Token); }
        catch (OperationCanceledException) { SetStatus("Cancelled. Files already saved remain in the destination folder."); }
        catch (Exception ex) { SetStatus(ImagingService.FriendlyError(ex)); }
        finally
        {
            _busy = false; _operation = null;
            WorkProgress.Visibility = CancelButton.Visibility = Visibility.Collapsed;
            RefreshControls();
            if (_exitAfterWork) RequestExit();
            else if (!_previewReady && FileList.SelectedItem is ConversionItem) _ = RefreshPreviewAsync();
        }
    }

    private async Task BeginCaptureAsync(bool chooseMode = false)
    {
        if (_busy || _exiting || _choosingShortcut) return;
        var mode = (CaptureMode)CaptureModeBox.SelectedIndex;
        await RunOperation(async token =>
        {
            bool restoreWindow = IsVisible || !chooseMode;
            Hide();
            BitmapSource? bitmap = null;
            try
            {
                if (chooseMode)
                {
                    var chooser = new CaptureModeWindow();
                    using var cancelChooser = token.Register(() => Dispatcher.BeginInvoke(new Action(() => { if (chooser.IsVisible) chooser.Close(); })));
                    if (chooser.ShowDialog() != true || chooser.SelectedMode is not { } selected)
                    {
                        SetStatus("Capture cancelled. Nothing was saved.");
                        return;
                    }
                    mode = selected;
                    CaptureModeBox.SelectedIndex = (int)mode;
                }
                // Let the chooser and main window disappear before taking the snapshot.
                await Task.Delay(200, token);
                bitmap = await CaptureService.CaptureAsync(mode);
                if (bitmap is not null) restoreWindow = true;
            }
            catch { restoreWindow = true; throw; }
            finally { if (restoreWindow) ShowApp(); }
            if (bitmap is null) { SetStatus("Capture cancelled. Nothing was saved."); return; }
            var entry = new CaptureEntry(bitmap, mode == CaptureMode.Display ? "Display" : mode.ToString(), DateTime.Now);
            int removed = _history.Add(entry);
            Tabs.SelectedIndex = 0;
            CaptureList.SelectedItem = entry;
            SetStatus(removed > 0 ? $"Captured in memory. Removed {removed} oldest capture(s) to stay within the history limit." : "Captured in memory. Choose Copy or Save when you’re ready.");
        });
    }
    private async void Capture_Click(object sender, RoutedEventArgs e) => await BeginCaptureAsync();
    private void CaptureList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        var entry = CaptureList.SelectedItem as CaptureEntry;
        CapturePreview.Source = entry?.Image;
        CaptureDetail.Text = entry is null ? "Your next screenshot starts here" : entry.Title + "  ·  " + entry.Detail;
        CaptureEmpty.Visibility = entry is null ? Visibility.Visible : Visibility.Collapsed;
        RefreshControls();
    }
    private async void CopyCapture_Click(object sender, RoutedEventArgs e)
    {
        if (CaptureList.SelectedItem is not CaptureEntry entry) return;
        await RunOperation(async _ => { await ClipboardService.CopyAsync(entry.Image); SetStatus("Copied. Paste into your message, document, or image editor."); });
    }
    private async void SaveCapture_Click(object sender, RoutedEventArgs e)
    {
        if (CaptureList.SelectedItem is not CaptureEntry entry || _busy) return;
        var dialog = SaveDialog($"Capture-{entry.Created:yyyyMMdd-HHmmss}.png", OutputFormat.PNG);
        if (dialog.ShowDialog(this) != true) return;
        var options = new ExportOptions((OutputFormat)(dialog.FilterIndex - 1));
        string path = Path.ChangeExtension(dialog.FileName, options.Extension);
        await RunOperation(async token =>
        {
            var bytes = await ImageWork(() => _imaging.EncodeBitmap(entry.Image, options), token);
            string saved = await Task.Run(() => ExportService.SaveUnique(path, bytes, token), token);
            SetStatus("Saved: " + saved);
        });
    }
    private void DeleteCapture_Click(object sender, RoutedEventArgs e)
    {
        if (CaptureList.SelectedItem is CaptureEntry entry) _history.Items.Remove(entry);
        if (_history.Items.Count > 0) CaptureList.SelectedIndex = 0;
        SetStatus("Capture removed from memory.");
    }
    private void ClearCaptures_Click(object sender, RoutedEventArgs e)
    { _history.Clear(); SetStatus("Temporary captures cleared. Saved files and clipboard contents are unchanged."); }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose images to convert", Multiselect = true, Filter = "Images|*.heic;*.heif;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff" };
        if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames);
    }
    private void Files_DragOver(object sender, DragEventArgs e)
    { e.Effects = !_busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void Files_Drop(object sender, DragEventArgs e)
    { if (!_busy && e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths); }
    private void AddFiles(IEnumerable<string> paths)
    {
        if (_busy) return;
        int added = 0, skipped = 0;
        foreach (string path in paths)
        {
            if (!ImagingService.Supports(path) || !File.Exists(path)) { skipped++; continue; }
            if (_files.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            _files.Add(new ConversionItem(path)); added++;
        }
        Tabs.SelectedIndex = 1;
        if (FileList.SelectedIndex < 0 && _files.Count > 0) FileList.SelectedIndex = 0;
        SetStatus($"Added {added} image(s)." + (skipped > 0 ? $" Skipped {skipped} unsupported or unavailable item(s)." : " Originals stay untouched."));
    }
    private async void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (_ready) await RefreshPreviewAsync(); }
    private async void OutputOptions_Changed(object sender, SelectionChangedEventArgs e)
    { if (_ready) { UpdateQuality(); await RefreshPreviewAsync(); } }
    private async void Quality_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (_ready) { UpdateQuality(); await RefreshPreviewAsync(); } }
    private void UpdateQuality()
    {
        bool lossy = OutputFormatBox.SelectedIndex > 0;
        QualityPanel.Opacity = lossy ? 1 : 0.45;
        QualityLabel.Text = lossy ? $"Quality · {(int)QualitySlider.Value}%" : "PNG is lossless";
        RefreshControls();
    }
    private async Task RefreshPreviewAsync()
    {
        _previewCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        _previewReady = false;
        ConversionPreview.Source = null;
        ConversionEmpty.Visibility = Visibility.Visible;
        ConversionEmptyText.Text = "New format. Same memories.";
        ConversionDetail.Text = "Preview your converted image";
        RefreshControls();
        try
        {
            if (FileList.SelectedItem is not ConversionItem item || _busy) return;
            var options = Options;
            ConversionEmptyText.Text = "Preparing preview…";
            await Task.Delay(180, cancellation.Token);
            var preview = await ImageWork(() => _imaging.Preview(item.Path, options, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            ConversionPreview.Source = preview.Image;
            ConversionDetail.Text = preview.Detail;
            ConversionEmpty.Visibility = Visibility.Collapsed;
            _previewReady = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested)
            { ConversionEmptyText.Text = ImagingService.FriendlyError(ex); SetStatus(ConversionEmptyText.Text); }
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation)) _previewCancellation = null;
            RefreshControls();
        }
    }
    private void RemoveFile_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is ConversionItem item) _files.Remove(item);
        if (_files.Count > 0 && FileList.SelectedIndex < 0) FileList.SelectedIndex = 0;
    }
    private void ClearFiles_Click(object sender, RoutedEventArgs e)
    { _files.Clear(); SetStatus("Image queue cleared. Source and saved files are unchanged."); }
    private async void CopyConverted_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not ConversionItem item) return;
        var options = Options;
        await RunOperation(async token =>
        {
            SetStatus("Preparing full-resolution image for the clipboard…");
            var bitmap = await ImageWork(() => _imaging.ConvertForClipboard(item.Path, options, token), token);
            await ClipboardService.CopyAsync(bitmap);
            SetStatus("Converted image copied at full resolution. No image file was saved.");
        }, true);
    }
    private async void SaveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not ConversionItem item || _busy) return;
        var options = Options;
        var dialog = SaveDialog(Path.GetFileNameWithoutExtension(item.Name) + options.Extension, options.Format);
        if (dialog.ShowDialog(this) != true) return;
        options = options with { Format = (OutputFormat)(dialog.FilterIndex - 1) };
        string path = Path.ChangeExtension(dialog.FileName, options.Extension);
        await RunOperation(async token =>
        {
            try
            {
                item.Status = "Converting…";
                var bytes = await ImageWork(() => _imaging.EncodeFile(item.Path, options, token), token);
                string saved = await Task.Run(() => ExportService.SaveUnique(path, bytes, token), token);
                item.Status = "Saved: " + Path.GetFileName(saved); SetStatus("Saved: " + saved);
            }
            catch (OperationCanceledException) { item.Status = "Cancelled"; throw; }
            catch (Exception ex) { item.Status = ImagingService.FriendlyError(ex); throw; }
        }, true);
    }
    private async void SaveAll_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _files.Count == 0) return;
        var dialog = new OpenFolderDialog { Title = "Save converted images to this folder" };
        if (dialog.ShowDialog(this) != true) return;
        var items = _files.ToArray();
        var options = Options;
        await RunOperation(async token =>
        {
            int saved = 0, failed = 0;
            WorkProgress.IsIndeterminate = false; WorkProgress.Maximum = items.Length; WorkProgress.Value = 0;
            foreach (var item in items)
            {
                token.ThrowIfCancellationRequested();
                item.Status = "Converting…";
                SetStatus($"Saving {saved + failed + 1} of {items.Length}: {item.Name}");
                try
                {
                    var bytes = await ImageWork(() => _imaging.EncodeFile(item.Path, options, token), token);
                    string desired = Path.Combine(dialog.FolderName, Path.GetFileNameWithoutExtension(item.Name) + options.Extension);
                    string result = await Task.Run(() => ExportService.SaveUnique(desired, bytes, token), token);
                    item.Status = "Saved: " + Path.GetFileName(result); saved++;
                }
                catch (OperationCanceledException) { item.Status = "Cancelled"; throw; }
                catch (Exception ex) { item.Status = ImagingService.FriendlyError(ex); failed++; }
                WorkProgress.Value = saved + failed;
            }
            SetStatus($"Saved {saved} image(s) to {dialog.FolderName}." + (failed > 0 ? $" {failed} failed; see the queue for details." : ""));
        }, true);
    }
    private static SaveFileDialog SaveDialog(string name, OutputFormat format) => new()
    {
        Title = "Save image — existing filenames get a numbered suffix",
        FileName = name,
        Filter = "PNG image|*.png|JPG image|*.jpg|WebP image|*.webp",
        FilterIndex = (int)format + 1,
        AddExtension = true,
        OverwritePrompt = false
    };
    private void Cancel_Click(object sender, RoutedEventArgs e)
    { _operation?.Cancel(); SetStatus("Cancelling after the current image operation…"); }
    private void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ShortcutWindow(_shortcut) { Owner = this };
        bool? accepted;
        _choosingShortcut = true;
        try { accepted = dialog.ShowDialog(); }
        finally { _choosingShortcut = false; }
        if (accepted != true) return;
        if (_hotkey?.Register(dialog.Chosen) != true)
        {
            SetStatus(dialog.Chosen.IsPrintScreen
                ? "Print Screen is unavailable. Turn off Windows' Print Screen screen-capture setting and check other screenshot apps, then try again. The previous shortcut is unchanged."
                : "That shortcut is already in use. The previous shortcut is unchanged.");
            return;
        }
        _shortcut = dialog.Chosen; ShortcutButton.Content = _shortcut.ToString();
        try { Preferences.Save(_shortcut); SetStatus("Capture shortcut updated to " + _shortcut + "."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("Shortcut changed for this session, but preferences could not be saved."); }
    }
}
