namespace ConfGTS.Client.Services;

/// <summary>
/// Conservative guard for the in-app audio diagnostic. Opening the microphone
/// endpoint of a Bluetooth Classic headset forces an A2DP -> HFP transition.
/// Some USB Bluetooth radio/driver combinations reset on that transition.
/// We never attempt to reconnect/restart adapters automatically.
/// </summary>
internal static class BluetoothAudioSafety
{
    private static readonly string[] BluetoothSignals =
    [
        "bluetooth", "блютуз", "блютус", "беспровод",
        "hands-free", "hands free", "handsfree", "hfp", "a2dp",
        "ag audio", "headset (bluetooth", "stereo (bluetooth",
        "свободные руки", "громкая связь", "телефонный режим",
        "bthenum", "bthle", "bthhf", "bth\\", "bth#",
        "airpods", "galaxy buds", "redmi buds", "freebuds", "soundcore",
        "jbl tune", "jbl live", "sony wh-", "sony wf-", "qcy ", "haylou",
        "buds pro", "buds live", "buds2", "buds3", "buds4"
    ];

    internal static bool IsLikelyBluetooth(string? name, string? id)
    {
        var description = ((name ?? "") + " " + (id ?? "")).ToLowerInvariant();
        return BluetoothSignals.Any(signal => description.Contains(signal, StringComparison.Ordinal));
    }

    internal const string DiagnosticWarning =
        "Проверка Bluetooth-аудиоустройства в ConfGTS временно отключена для защиты адаптера. " +
        "При открытии микрофона Windows переключает гарнитуру в режим Hands-Free (HFP), " +
        "что на некоторых USB Bluetooth-адаптерах приводит к отключению. " +
        "Для теста выберите встроенный или проводной USB-микрофон/динамики. " +
        "Bluetooth-гарнитура остаётся доступной для выбора в конференции, " +
        "но её работоспособность нужно отдельно проверить после обновления драйвера.";
}
