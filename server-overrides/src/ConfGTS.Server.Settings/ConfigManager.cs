using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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
    public static string CertificateDirectory => Path.Combine(DataDirectory, "certs");
    public static string AutoCertificatePath => Path.Combine(CertificateDirectory, "confgts-server.crt");
    public static string AutoKeyPath => Path.Combine(CertificateDirectory, "confgts-server.key");

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
                if (!string.IsNullOrWhiteSpace(separateName))
                    savedName = separateName;
            }
            if (!string.IsNullOrWhiteSpace(savedName))
                name = savedName;

            var httpsEnabled = node["https"]?["enabled"]?.GetValue<bool>() == true;

            var pu = node["public_url"]?.GetValue<string>()?.Trim();
            if (!string.IsNullOrWhiteSpace(pu))
            {
                publicUrl = pu;
                if (Uri.TryCreate(pu, UriKind.Absolute, out var uri))
                {
                    scheme = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                        ? "https"
                        : "http";
                    if (string.IsNullOrWhiteSpace(savedName) && !IPAddress.TryParse(uri.Host, out _))
                        name = uri.Host;
                }
            }

            if (httpsEnabled)
                scheme = "https";

            publicUrl = $"{scheme}://{name}:{port}";
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
        var useHttps = settings.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        https["enabled"] = useHttps;

        if (useHttps)
        {
            var currentCert = https["cert_file"]?.GetValue<string>()?.Trim() ?? "";
            var currentKey = https["key_file"]?.GetValue<string>()?.Trim() ?? "";

            var customPair = !string.IsNullOrWhiteSpace(currentCert) &&
                             !string.IsNullOrWhiteSpace(currentKey) &&
                             File.Exists(currentCert) &&
                             File.Exists(currentKey) &&
                             !PathsEqual(currentCert, AutoCertificatePath) &&
                             !PathsEqual(currentKey, AutoKeyPath);

            if (!customPair)
            {
                GenerateAutomaticCertificate(settings.ServerName);
                https["cert_file"] = AutoCertificatePath;
                https["key_file"] = AutoKeyPath;
            }
        }

        root["https"] = https;

        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, ConfigPath, true);
    }

    private static void GenerateAutomaticCertificate(string configuredName)
    {
        Directory.CreateDirectory(CertificateDirectory);

        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(
            "CN=ConfGTS Server",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));

        var eku = new OidCollection
        {
            new("1.3.6.1.5.5.7.3.1", "Server Authentication")
        };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        var dnsNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddDns(string? value)
        {
            var name = (value ?? "").Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(name) || IPAddress.TryParse(name, out _))
                return;
            if (dnsNames.Add(name))
                san.AddDnsName(name);
        }

        AddDns(configuredName);
        AddDns(Environment.MachineName);
        AddDns(Dns.GetHostName());
        AddDns(SuggestedServerName());
        AddDns("localhost");

        var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            IPAddress.Loopback.ToString()
        };

        foreach (var ipText in LocalIPv4().Where(x => x != "0.0.0.0"))
            ips.Add(ipText);

        if (IPAddress.TryParse(configuredName, out var configuredIp))
            ips.Add(configuredIp.ToString());

        foreach (var ipText in ips)
        {
            if (IPAddress.TryParse(ipText, out var ip))
                san.AddIpAddress(ip);
        }

        request.CertificateExtensions.Add(san.Build());

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.Now.AddDays(-1),
            DateTimeOffset.Now.AddYears(3));

        File.WriteAllText(AutoCertificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(AutoKeyPath, rsa.ExportPkcs8PrivateKeyPem());

        Diagnostics.Log(
            $"Automatic HTTPS certificate generated. Name={configuredName}; Cert={AutoCertificatePath}");
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static IReadOnlyList<string> LocalIPv4()
    {
        var result = new List<string> { "0.0.0.0" };
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ua.Address))
                {
                    var value = ua.Address.ToString();
                    if (!result.Contains(value))
                        result.Add(value);
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
        if (string.IsNullOrWhiteSpace(host))
            return "Имя сервера не указано.";

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            var ipv4 = addresses
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .Distinct()
                .ToArray();

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
        await Task.Delay(900);
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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await process.WaitForExitAsync(cts.Token);

        var output = (await stdout) + Environment.NewLine + (await stderr);
        if (!ignoreExitCode && process.ExitCode != 0)
            throw new InvalidOperationException(output.Trim());

        return output;
    }
}
