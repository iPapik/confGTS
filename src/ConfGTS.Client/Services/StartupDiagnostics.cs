using System.Text;

namespace ConfGTS.Client.Services;

public static class StartupDiagnostics
{
    private static readonly object Gate = new();

    public static string LogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConfGTS", "logs");

    public static string LogPath => Path.Combine(LogDirectory, "startup.log");

    public static void Log(string message, Exception? exception = null)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz")).Append("] ");
            sb.Append(message);
            if (exception is not null)
            {
                sb.AppendLine();
                sb.Append(exception);
            }
            sb.AppendLine();
            lock (Gate)
            {
                File.AppendAllText(LogPath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Startup diagnostics must never crash the app.
        }
    }
}
