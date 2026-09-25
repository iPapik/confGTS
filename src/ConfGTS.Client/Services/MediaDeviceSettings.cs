using System.Text.Json;

namespace ConfGTS.Client.Services;

public sealed class MediaDeviceSettings
{
    public string MicrophoneId { get; set; } = "";
    public string SpeakerId { get; set; } = "";
    public string CameraId { get; set; } = "";
    public double MicrophoneVolume { get; set; } = 100;
    public double SpeakerVolume { get; set; } = 70;

    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ConfGTS", "media-devices.json");

    public static MediaDeviceSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<MediaDeviceSettings>(
                           File.ReadAllText(SettingsPath),
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? new MediaDeviceSettings();
            }
        }
        catch
        {
        }

        return new MediaDeviceSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Device preferences should never prevent the client from running.
        }
    }
}
