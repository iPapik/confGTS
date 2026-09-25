using System.Net;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace ConfGTS.Client.Services;

public sealed class ApiClient
{
    private const int DefaultPort = 8090;
    private const int DiscoveryPort = 8091;
    private const string DiscoveryMagic = "CONFGTS_DISCOVER_V1";
    private static readonly object TrustGate = new();

    private readonly HttpClient _http;
    private string _configuredBaseUrl;
    private string _effectiveBaseUrl;

    public ApiClient()
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            ServerCertificateCustomValidationCallback = ValidateServerCertificate
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        _configuredBaseUrl = LoadSavedServer();
        _effectiveBaseUrl = _configuredBaseUrl;
    }

    public string BaseUrl
    {
        get => _configuredBaseUrl;
        set
        {
            _configuredBaseUrl = NormalizeServerAddress(value);
            _effectiveBaseUrl = _configuredBaseUrl;
            SaveServer(_configuredBaseUrl);
        }
    }

    public string EffectiveBaseUrl => _effectiveBaseUrl;

    private Uri UriFor(string path) =>
        new(new Uri(_effectiveBaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

    public static string NormalizeServerAddress(string input)
    {
        var value = (input ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Укажите имя сервера или IP-адрес.");

        if (!value.Contains("://", StringComparison.Ordinal))
            value = "http://" + value;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException(
                "Допустимы имя компьютера, DNS/FQDN или IP-адрес, например confgts, srv-vks.teplo.local:8090.");
        }

        var builder = new UriBuilder(uri);
        if (uri.IsDefaultPort)
            builder.Port = DefaultPort;

        builder.Path = "";
        builder.Query = "";
        builder.Fragment = "";
        return builder.Uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    public async Task<bool> HealthAsync(CancellationToken ct = default) =>
        await EnsureEffectiveEndpointAsync(ct);

    public async Task LoginAsync(string login, string password, CancellationToken ct = default)
    {
        if (!await EnsureEffectiveEndpointAsync(ct))
        {
            throw new InvalidOperationException(
                "Сервер недоступен. Проверьте имя/IP, протокол, порт и сетевое подключение.");
        }

        using var response = await _http.PostAsJsonAsync(
            UriFor("/api/login"),
            new { username = login, password },
            ct);

        if (response.IsSuccessStatusCode)
            return;

        var detail = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
            ? $"Сервер вернул HTTP {(int)response.StatusCode}."
            : detail.Trim());
    }

    public async Task<IReadOnlyList<RoomInfo>> RoomsAsync(CancellationToken ct = default)
    {
        if (!await EnsureEffectiveEndpointAsync(ct))
            return [];

        using var response = await _http.GetAsync(UriFor("/api/rooms"), ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        if (root.ValueKind == JsonValueKind.Array)
            return JsonSerializer.Deserialize<List<RoomInfo>>(root.GetRawText(), options) ?? [];

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("rooms", out var rooms) &&
            rooms.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<RoomInfo>>(rooms.GetRawText(), options) ?? [];
        }

        return [];
    }

    public async Task<IReadOnlyList<ContactInfo>> ContactsAsync(CancellationToken ct = default)
    {
        if (!await EnsureEffectiveEndpointAsync(ct))
            return [];

        using var response = await _http.GetAsync(UriFor("/api/contacts"), ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
            doc.RootElement.TryGetProperty("contacts", out var contacts) &&
            contacts.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<ContactInfo>>(contacts.GetRawText(), options) ?? [];
        }

        return [];
    }

    public void ForgetAllCertificateTrust()
    {
        lock (TrustGate)
        {
            try
            {
                if (File.Exists(TrustPath))
                    File.Delete(TrustPath);
            }
            catch
            {
            }
        }
    }

    private async Task<bool> EnsureEffectiveEndpointAsync(CancellationToken ct)
    {
        if (await ProbeAsync(_effectiveBaseUrl, ct))
            return true;

        if (!string.Equals(_effectiveBaseUrl, _configuredBaseUrl, StringComparison.OrdinalIgnoreCase) &&
            await ProbeAsync(_configuredBaseUrl, ct))
        {
            _effectiveBaseUrl = _configuredBaseUrl;
            return true;
        }

        var configuredUri = new Uri(_configuredBaseUrl);

        if (!IPAddress.TryParse(configuredUri.Host, out _) && !configuredUri.Host.Contains('.'))
        {
            var suffix = IPGlobalProperties.GetIPGlobalProperties().DomainName?.Trim('.');
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                var fqdn = ReplaceHost(configuredUri, configuredUri.Host + "." + suffix);
                if (await ProbeAsync(fqdn, ct))
                {
                    _effectiveBaseUrl = fqdn;
                    return true;
                }
            }
        }

        var discovered = await DiscoverAsync(configuredUri.Host, ct);
        if (!string.IsNullOrWhiteSpace(discovered) && await ProbeAsync(discovered, ct))
        {
            _effectiveBaseUrl = discovered;
            return true;
        }

        return false;
    }

    private async Task<bool> ProbeAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(2.5));
            var uri = new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "health");
            using var response = await _http.GetAsync(uri, linked.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> DiscoverAsync(string requestedName, CancellationToken ct)
    {
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork)
            {
                EnableBroadcast = true
            };
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            var payload = Encoding.UTF8.GetBytes($"{DiscoveryMagic}|{requestedName}");
            await udp.SendAsync(payload, payload.Length,
                new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(1.8));

            while (!linked.IsCancellationRequested)
            {
                UdpReceiveResult packet;
                try
                {
                    packet = await udp.ReceiveAsync(linked.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                using var doc = JsonDocument.Parse(packet.Buffer);
                var root = doc.RootElement;
                if (!root.TryGetProperty("protocol", out var protocol) ||
                    protocol.GetString() != "CONFGTS_V1")
                {
                    continue;
                }

                var port = root.TryGetProperty("port", out var p) && p.TryGetInt32(out var n)
                    ? n
                    : DefaultPort;

                var scheme = root.TryGetProperty("scheme", out var s) &&
                             s.GetString()?.Equals("https", StringComparison.OrdinalIgnoreCase) == true
                    ? "https"
                    : "http";

                return $"{scheme}://{packet.RemoteEndPoint.Address}:{port}";
            }
        }
        catch
        {
        }

        return null;
    }

    private static bool ValidateServerCertificate(
        HttpRequestMessage request,
        X509Certificate2? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
            return true;

        if (certificate is null || request.RequestUri is null)
            return false;

        if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0 ||
            (errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
        {
            return false;
        }

        if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            return false;

        if (!string.Equals(certificate.Subject, certificate.Issuer, StringComparison.OrdinalIgnoreCase))
            return false;

        var now = DateTime.Now;
        if (now < certificate.NotBefore || now > certificate.NotAfter)
            return false;

        var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        var host = request.RequestUri.Host.ToLowerInvariant();

        lock (TrustGate)
        {
            var trust = LoadTrust();
            if (trust.TryGetValue(host, out var saved))
                return string.Equals(saved, fingerprint, StringComparison.OrdinalIgnoreCase);

            trust[host] = fingerprint;
            SaveTrust(trust);
            return true;
        }
    }

    private static string ReplaceHost(Uri uri, string host)
    {
        var builder = new UriBuilder(uri) { Host = host };
        return builder.Uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConfGTS", "client-native.json");

    private static string TrustPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConfGTS", "https-trust.json");

    private static string LoadSavedServer()
    {
        const string fallback = "http://confgts:8090";
        try
        {
            if (!File.Exists(SettingsPath))
                return fallback;

            using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            if (doc.RootElement.TryGetProperty("server_url", out var value))
            {
                var saved = value.GetString();
                if (!string.IsNullOrWhiteSpace(saved))
                    return NormalizeServerAddress(saved);
            }
        }
        catch
        {
        }

        return fallback;
    }

    private static void SaveServer(string server)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);

            Dictionary<string, object?> data = new(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(SettingsPath))
            {
                try
                {
                    data = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                               File.ReadAllText(SettingsPath))
                           ?? new(StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                }
            }

            data["server_url"] = server;
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    private static Dictionary<string, string> LoadTrust()
    {
        try
        {
            if (!File.Exists(TrustPath))
                return new(StringComparer.OrdinalIgnoreCase);

            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(TrustPath))
                   ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveTrust(Dictionary<string, string> trust)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TrustPath)!);
            File.WriteAllText(TrustPath,
                JsonSerializer.Serialize(trust, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}

public sealed class RoomInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class ContactInfo
{
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";

    public string EffectiveName =>
        string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;
}
