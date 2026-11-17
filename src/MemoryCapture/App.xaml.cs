namespace MemoryCapture;

public partial class App : Application
{
    private Mutex? _instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppDomain.CurrentDomain.UnhandledException += (_, error) => Console.Error.WriteLine(error.ExceptionObject);
        _instance = new Mutex(true, "Local\\MemoryCapture.Desktop", out var created);
        if (!created)
        {
            MessageBox.Show("Memory Capture is already running. Open it from the system tray.", "Memory Capture");
            Shutdown();
            return;
        }
        try
        {
            ImagingService.Initialize();
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Memory Capture could not start.\n\n" + ex.Message, "Memory Capture", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { _instance?.Dispose(); base.OnExit(e); }
}
