using System.Net.Http.Json;
using System.Text.Json;

namespace ConfGTS.Client.Services;

public sealed class ApiClient
{
    private readonly HttpClient _http = new(new HttpClientHandler { UseCookies = true })
    {
        Timeout = TimeSpan.FromSeconds(8)
    };

    public string BaseUrl { get; set; } = "http://127.0.0.1:8080";

    private Uri UriFor(string path) => new(new Uri(BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

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
            : detail);
    }

    public async Task<IReadOnlyList<RoomInfo>> RoomsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(UriFor("/api/rooms"), ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<List<RoomInfo>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
    }
}

public sealed class RoomInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
