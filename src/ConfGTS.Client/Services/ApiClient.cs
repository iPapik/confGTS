using System.Net.Http.Json;
using System.Text.Json;

namespace ConfGTS.Client.Services;

public sealed class ApiClient
{
    private readonly HttpClient _http = new(new HttpClientHandler { UseCookies = true })
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    private string _baseUrl;

    public ApiClient()
    {
        _baseUrl = LoadSavedServer();
    }

    public string BaseUrl
    {
        get => _baseUrl;
        set
        {
            _baseUrl = NormalizeServerAddress(value);
            SaveServer(_baseUrl);
        }
    }

    private Uri UriFor(string path) => new(new Uri(_baseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

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
            throw new InvalidOperationException("Допустимы DNS-имя, FQDN или IP-адрес, например confgts.teplo.local:8090.");
        }

        var builder = new UriBuilder(uri);
        if (uri.IsDefaultPort)
            builder.Port = 8090;

        builder.Path = "";
        builder.Query = "";
        builder.Fragment = "";
        return builder.Uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    public async Task<bool> HealthAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(UriFor("/health"), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task LoginAsync(string login, string password, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync(
            UriFor("/api/login"),
            new { username = login, password },
            ct);

        if (response.IsSuccessStatusCode) return;

        var detail = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
            ? $"Сервер вернул HTTP {(int)response.StatusCode}."
            : detail.Trim());
    }

    public async Task<IReadOnlyList<RoomInfo>> RoomsAsync(CancellationToken ct = default)
    {
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

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConfGTS", "client-native.json");

    private static string LoadSavedServer()
    {
        const string fallback = "http://confgts.teplo.local:8090";
        try
        {
            if (!File.Exists(SettingsPath)) return fallback;
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
                    data = JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(SettingsPath))
                           ?? new(StringComparer.OrdinalIgnoreCase);
                }
                catch
                {
                }
            }

            data["server_url"] = server;
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        catch
        {
            // A failed preference save must not block the client.
        }
    }
}

public sealed class RoomInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
