using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ConfGTS.Server.Settings;

internal sealed record NetworkSettings(
    string BindAddress,
    int Port,
    string ServerName,
    string Scheme,
    string PublicUrl);

internal static class ConfigManager
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ConfGTS");

    public static string ConfigPath => Path.Combine(DataDirectory, "config.json");
    public static string ServerNamePath => Path.Combine(DataDirectory, "server-name.txt");

    public static NetworkSettings Load()
    {
        var bind = "0.0.0.0";
        var port = 8090;
        var name = SuggestedServerName();
        var scheme = "http";
        var publicUrl = $"http://{name}:8090";

        try
        {
            if (!File.Exists(ConfigPath))
                return new(bind, port, name, scheme, publicUrl);

            var node = JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject;
            if (node is null)
                return new(bind, port, name, scheme, publicUrl);

            var listen = node["listen_addr"]?.GetValue<string>()?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(listen))
            {
                if (listen.StartsWith(":"))
                {
                    if (int.TryParse(listen[1..], out var p)) port = p;
                }
                else
                {
                    var lastColon = listen.LastIndexOf(':');
                    if (lastColon > 0 && int.TryParse(listen[(lastColon + 1)..], out var p))
                    {
                        bind = listen[..lastColon].Trim('[', ']');
                        port = p;
                    }
                }
            }

            var savedName = node["server_name"]?.GetValue<string>()?.Trim();
            if (File.Exists(ServerNamePath))
            {
                var separateName = File.ReadAllText(ServerNamePath).Trim();
                if (!string.IsNullOrWhiteSpace(separateName)) savedName = separateName;
            }
            if (!string.IsNullOrWhiteSpace(savedName)) name = savedName;

            var pu = node["public_url"]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrWhiteSpace(pu))
            {
                publicUrl = pu;
                if (Uri.TryCreate(pu, UriKind.Absolute, out var uri))
                {
                    scheme = uri.Scheme is "https" ? "https" : "http";
                    if (string.IsNullOrWhiteSpace(savedName) && !IPAddress.TryParse(uri.Host, out _))
                        name = uri.Host;
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log("Config load failed", ex);
        }

        return new(bind, port, name, scheme, publicUrl);
    }

    public static void Save(NetworkSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        JsonObject root;
        try
        {
            root = File.Exists(ConfigPath)
                ? (JsonNode.Parse(File.ReadAllText(ConfigPath)) as JsonObject ?? new JsonObject())
                : new JsonObject();
        }
        catch
        {
            root = new JsonObject();
        }

        root["listen_addr"] = $"{settings.BindAddress}:{settings.Port}";
        root["server_name"] = settings.ServerName;
        root["public_url"] = settings.PublicUrl;
        File.WriteAllText(ServerNamePath, settings.ServerName);

        var https = root["https"] as JsonObject ?? new JsonObject();
        https["enabled"] = settings.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        root["https"] = https;

        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, ConfigPath, true);
    }

    public static IReadOnlyList<string> LocalIPv4()
    {
        var result = new List<string> { "0.0.0.0" };
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ua.Address))
                {
                    var s = ua.Address.ToString();
                    if (!result.Contains(s)) result.Add(s);
                }
            }
        }
        return result;
    }

    public static string SuggestedServerName()
    {
        try
        {
            var host = Dns.GetHostName();
            var entry = Dns.GetHostEntry(host);
            if (!string.IsNullOrWhiteSpace(entry.HostName) && entry.HostName.Contains('.'))
                return entry.HostName.ToLowerInvariant();

            var domain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
            if (!string.IsNullOrWhiteSpace(domain))
                return $"{host}.{domain}".ToLowerInvariant();

            return host.ToLowerInvariant();
        }
        catch
        {
            return Environment.MachineName.ToLowerInvariant();
        }
    }

    public static async Task<string> ResolveNameAsync(string name)
    {
        var host = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(host)) return "Имя сервера не указано.";
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            var ipv4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString()).Distinct().ToArray();
            return ipv4.Length == 0
                ? "DNS-имя разрешилось, но IPv4-адрес не найден."
                : "DNS: " + string.Join(", ", ipv4);
        }
        catch (Exception ex)
        {
            return "DNS-имя пока не разрешается: " + ex.Message;
        }
    }

    public static async Task RestartServiceAsync()
    {
        await RunAsync("sc.exe", "stop ConfGTSServer", ignoreExitCode: true);
        await Task.Delay(700);
        await RunAsync("sc.exe", "start ConfGTSServer", ignoreExitCode: false);
    }

    public static async Task<string> ServiceStatusAsync()
    {
        try
        {
            var output = await RunAsync("sc.exe", "query ConfGTSServer", ignoreExitCode: true);
            return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)
                ? "Служба запущена"
                : output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)
                    ? "Служба остановлена"
                    : "Состояние службы неизвестно";
        }
        catch (Exception ex)
        {
            return "Служба недоступна: " + ex.Message;
        }
    }

    private static async Task<string> RunAsync(string file, string args, bool ignoreExitCode)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        await process.WaitForExitAsync(cts.Token);
        var output = (await stdout) + Environment.NewLine + (await stderr);
        if (!ignoreExitCode && process.ExitCode != 0)
            throw new InvalidOperationException(output.Trim());
        return output;
    }
}
