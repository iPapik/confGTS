using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace ConfGTS.Server.Settings;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        try
        {
            InitializeComponent();
            UnhandledException += (_, e) => Diagnostics.Log("Unhandled WinUI exception", e.Exception);
        }
        catch (Exception ex)
        {
            Diagnostics.Log("App initialization failed", ex);
            ShowFatal(ex);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Diagnostics.Log("Starting settings window");
            _window = new SettingsWindow();
            _window.Activate();
            Diagnostics.Log("Settings window activated");
        }
        catch (Exception ex)
        {
            Diagnostics.Log("Settings window startup failed", ex);
            ShowFatal(ex);
        }
    }

    private static void ShowFatal(Exception ex)
    {
        MessageBox(IntPtr.Zero,
            "Не удалось запустить ConfGTS Server Settings.\n\n" + ex.Message +
            "\n\nЖурнал: " + Diagnostics.LogPath,
            "ConfGTS Server Settings",
            0x10);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}

internal static class Diagnostics
{
    private static readonly object Gate = new();
    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ConfGTS", "server-settings.log");

    public static void Log(string text, Exception? ex = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}] {text}";
            if (ex is not null) line += Environment.NewLine + ex;
            lock (Gate) File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch { }
    }
}
