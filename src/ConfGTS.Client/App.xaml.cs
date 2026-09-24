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
        StartupDiagnostics.Log("Unhandled WinUI exception.", e.Exception);
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
