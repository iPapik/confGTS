using System.Runtime.InteropServices;
using ConfGTS.Client.Services;
using Microsoft.UI.Xaml;

namespace ConfGTS.Client;

public partial class App : Application
{
    public static MainWindow? MainWindowInstance { get; private set; }

    public App()
    {
        try
        {
            StartupDiagnostics.Log("App constructor started.");
            InitializeComponent();
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            StartupDiagnostics.Log("App resources initialized.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Fatal error in App constructor.", ex);
            ShowFatalError(ex);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            StartupDiagnostics.Log("OnLaunched started.");
            MainWindowInstance = new MainWindow();
            MainWindowInstance.Activate();
            StartupDiagnostics.Log("Main window activated.");

            if (Environment.GetCommandLineArgs().Any(
                    x => string.Equals(x, "--self-test-settings", StringComparison.OrdinalIgnoreCase)))
            {
                MainWindowInstance.DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        await MainWindowInstance.RunSettingsSmokeTestAsync();
                    }
                    catch (Exception ex)
                    {
                        StartupDiagnostics.Log("Settings smoke test failed.", ex);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Fatal error while creating the main window.", ex);
            ShowFatalError(ex);
            throw;
        }
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        StartupDiagnostics.Log("Unhandled WinUI exception (recovered).", e.Exception);

        // WinUI terminates the process by default when this flag is not set.
        // Device/settings UI can surface layout/dispatcher exceptions after the
        // original click handler has already returned, so the surrounding
        // try/catch in MainWindow cannot catch those. Keep the client alive and
        // record the full exception instead.
        e.Handled = true;
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        StartupDiagnostics.Log("Unobserved task exception (observed).", e.Exception);
        e.SetObserved();
    }

    private static void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            StartupDiagnostics.Log("AppDomain unhandled exception. IsTerminating=" + e.IsTerminating, ex);
        else
            StartupDiagnostics.Log("AppDomain unhandled non-Exception object. IsTerminating=" + e.IsTerminating);
    }

    private static void ShowFatalError(Exception ex)
    {
        try
        {
            MessageBox(IntPtr.Zero,
                "ConfGTS не удалось запустить.\n\n" +
                ex.Message +
                "\n\nЖурнал: " + StartupDiagnostics.LogPath,
                "ConfGTS — ошибка запуска",
                0x00000010);
        }
        catch
        {
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
