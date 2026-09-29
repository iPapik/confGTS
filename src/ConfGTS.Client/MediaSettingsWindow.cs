using ConfGTS.Client.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Devices.Enumeration;

namespace ConfGTS.Client;

public sealed class MediaSettingsPanel : Grid
{
    private const string Navy = "#0B2F5B";
    private const string Blue = "#168CB8";
    private const string Text = "#28445F";
    private const string Muted = "#6A7E93";
    private const string Line = "#CFE0EA";
    private const string Bg = "#EEF7FC";

    private readonly MediaDeviceSettings _settings = MediaDeviceSettings.Load();

    private readonly ComboBox _microphone = new();
    private readonly ComboBox _speaker = new();
    private readonly ComboBox _camera = new();
    private readonly Slider _microphoneVolume = new();
    private readonly Slider _speakerVolume = new();
    private readonly ToggleSwitch _microphoneEnabled = new();
    private readonly ToggleSwitch _speakerEnabled = new();
    private readonly ToggleSwitch _cameraEnabled = new();
    private readonly TextBlock _microphoneStatus = new();
    private readonly TextBlock _speakerStatus = new();
    private readonly TextBlock _cameraStatus = new();
    private readonly Button _refreshButton = new();

    private List<DeviceChoice> _microphoneDevices = [];
    private List<DeviceChoice> _speakerDevices = [];
    private List<DeviceChoice> _cameraDevices = [];

    private bool _loading = true;
    private bool _closed;

    public event EventHandler? CloseRequested;

    public MediaSettingsPanel()
    {
        StartupDiagnostics.Log("MediaSettingsPanel 0.18.8 constructor started.");
        Background = Brush(Bg);
        Children.Add(BuildUi());
        StartupDiagnostics.Log("MediaSettingsPanel 0.18.8 constructor completed.");
    }

    public async Task InitializeAsync()
    {
        ApplySavedValues();
        await RefreshDevicesAsync();
        StartupDiagnostics.Log("In-app media settings initialized.");
    }

    public Task ShutdownAsync()
    {
        if (_closed)
            return Task.CompletedTask;

        _closed = true;
        SaveSettings();
        StartupDiagnostics.Log("In-app media settings closed.");
        return Task.CompletedTask;
    }

    private UIElement BuildUi()
    {
        var root = new Grid
        {
            Background = Brush(Bg),
            Padding = new Thickness(34)
        };

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var outer = new StackPanel
        {
            Spacing = 18,
            MaxWidth = 980,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        outer.Children.Add(BuildHeader());

        outer.Children.Add(BuildMicrophoneCard());
        outer.Children.Add(BuildSpeakerCard());
        outer.Children.Add(BuildCameraCard());

        scroll.Content = outer;
        root.Children.Add(scroll);
        return root;
    }

    private UIElement BuildHeader()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel { Spacing = 3 };
        title.Children.Add(new TextBlock
        {
            Text = "Настройки устройств",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(Navy)
        });
        grid.Children.Add(title);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        _refreshButton.Content = "↻ Обновить устройства";
        StyleSecondaryButton(_refreshButton);
        _refreshButton.Click += async (_, _) => await RefreshDevicesAsync();
        actions.Children.Add(_refreshButton);

        var back = SecondaryButton("← К конференциям");
        back.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        actions.Children.Add(back);

        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    private UIElement BuildMicrophoneCard()
    {
        var panel = CardPanel("\uE720", "Микрофон");

        ConfigureCombo(_microphone, "Выберите микрофон");
        _microphone.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            SaveSelectedDevice(
                _microphone,
                _microphoneDevices,
                (id, name) =>
                {
                    _settings.MicrophoneId = id;
                    _settings.MicrophoneName = name;
                });
            SaveSettings();
        };

        _microphoneEnabled.Header = "Использовать микрофон в конференции";
        _microphoneEnabled.Toggled += (_, _) =>
        {
            if (_loading) return;
            _settings.MicrophoneEnabled = _microphoneEnabled.IsOn;
            _microphone.IsEnabled = _microphoneEnabled.IsOn;
            _microphoneVolume.IsEnabled = _microphoneEnabled.IsOn;
            SaveSettings();
        };

        _microphoneVolume.Minimum = 0;
        _microphoneVolume.Maximum = 100;
        _microphoneVolume.StepFrequency = 1;
        _microphoneVolume.Header = "Уровень передачи микрофона";
        _microphoneVolume.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.MicrophoneVolume = _microphoneVolume.Value;
            SaveSettings();
        };

        ConfigureStatus(_microphoneStatus);
        panel.Children.Add(_microphoneEnabled);
        panel.Children.Add(DeviceLabel("Устройство"));
        panel.Children.Add(_microphone);
        panel.Children.Add(_microphoneVolume);
        panel.Children.Add(_microphoneStatus);
        return Card(panel);
    }

    private UIElement BuildSpeakerCard()
    {
        var panel = CardPanel("\uE767", "Динамики / наушники");

        ConfigureCombo(_speaker, "Выберите устройство вывода");
        _speaker.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            SaveSelectedDevice(
                _speaker,
                _speakerDevices,
                (id, name) =>
                {
                    _settings.SpeakerId = id;
                    _settings.SpeakerName = name;
                });
            SaveSettings();
        };

        _speakerEnabled.Header = "Воспроизводить звук конференции";
        _speakerEnabled.Toggled += (_, _) =>
        {
            if (_loading) return;
            _settings.SpeakerEnabled = _speakerEnabled.IsOn;
            _speaker.IsEnabled = _speakerEnabled.IsOn;
            _speakerVolume.IsEnabled = _speakerEnabled.IsOn;
            SaveSettings();
        };

        _speakerVolume.Minimum = 0;
        _speakerVolume.Maximum = 100;
        _speakerVolume.StepFrequency = 1;
        _speakerVolume.Header = "Громкость конференции";
        _speakerVolume.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.SpeakerVolume = _speakerVolume.Value;
            SaveSettings();
        };

        ConfigureStatus(_speakerStatus);
        panel.Children.Add(_speakerEnabled);
        panel.Children.Add(DeviceLabel("Устройство"));
        panel.Children.Add(_speaker);
        panel.Children.Add(_speakerVolume);
        panel.Children.Add(_speakerStatus);
        return Card(panel);
    }

    private UIElement BuildCameraCard()
    {
        var panel = CardPanel("\uE8B8", "Камера");

        ConfigureCombo(_camera, "Выберите камеру");
        _camera.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            SaveSelectedDevice(
                _camera,
                _cameraDevices,
                (id, name) =>
                {
                    _settings.CameraId = id;
                    _settings.CameraName = name;
                });
            SaveSettings();
        };

        _cameraEnabled.Header = "Использовать камеру в конференции";
        _cameraEnabled.Toggled += (_, _) =>
        {
            if (_loading) return;
            _settings.CameraEnabled = _cameraEnabled.IsOn;
            _camera.IsEnabled = _cameraEnabled.IsOn;
            SaveSettings();
        };

        ConfigureStatus(_cameraStatus);
        panel.Children.Add(_cameraEnabled);
        panel.Children.Add(DeviceLabel("Устройство"));
        panel.Children.Add(_camera);
        panel.Children.Add(_cameraStatus);
        return Card(panel);
    }

    private StackPanel CardPanel(string glyph, string title)
    {
        var panel = new StackPanel { Spacing = 12 };

        var heading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        heading.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 22,
            Foreground = Brush(Blue),
            VerticalAlignment = VerticalAlignment.Center
        });
        heading.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(Navy),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(heading);

        return panel;
    }

    private void ApplySavedValues()
    {
        _loading = true;
        _microphoneEnabled.IsOn = _settings.MicrophoneEnabled;
        _speakerEnabled.IsOn = _settings.SpeakerEnabled;
        _cameraEnabled.IsOn = _settings.CameraEnabled;
        _microphoneVolume.Value = Math.Clamp(_settings.MicrophoneVolume, 0, 100);
        _speakerVolume.Value = Math.Clamp(_settings.SpeakerVolume, 0, 100);
    }

    private async Task RefreshDevicesAsync()
    {
        if (_closed || !_refreshButton.IsEnabled)
            return;

        _loading = true;
        _refreshButton.IsEnabled = false;

        try
        {
            _microphoneDevices = await EnumerateAsync(DeviceClass.AudioCapture, "микрофонов");
            if (_closed) return;
            _speakerDevices = await EnumerateAsync(DeviceClass.AudioRender, "устройств вывода");
            if (_closed) return;
            _cameraDevices = await EnumerateAsync(DeviceClass.VideoCapture, "камер");
            if (_closed) return;

            BindDevices(
                _microphone,
                _microphoneDevices,
                _settings.MicrophoneId,
                _settings.MicrophoneName,
                "Микрофон не найден");
            BindDevices(
                _speaker,
                _speakerDevices,
                _settings.SpeakerId,
                _settings.SpeakerName,
                "Динамики / наушники не найдены");
            BindDevices(
                _camera,
                _cameraDevices,
                _settings.CameraId,
                _settings.CameraName,
                "Камера не найдена");

            _microphone.IsEnabled = _settings.MicrophoneEnabled;
            _speaker.IsEnabled = _settings.SpeakerEnabled;
            _camera.IsEnabled = _settings.CameraEnabled;
            _microphoneVolume.IsEnabled = _settings.MicrophoneEnabled;
            _speakerVolume.IsEnabled = _settings.SpeakerEnabled;

            _microphoneStatus.Text = _microphoneDevices.Count == 0
                ? "Микрофон не найден. В конференцию всё равно можно подключиться."
                : $"Найдено микрофонов: {_microphoneDevices.Count}.";
            _speakerStatus.Text = _speakerDevices.Count == 0
                ? "Динамики или наушники не найдены. Подключение к конференции не блокируется."
                : $"Найдено устройств вывода: {_speakerDevices.Count}.";
            _cameraStatus.Text = _cameraDevices.Count == 0
                ? "Камера не найдена. Поле выбора остаётся доступным, а в конференцию можно войти без видео."
                : $"Найдено камер: {_cameraDevices.Count}.";

            SyncSelectedValues();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Unexpected in-app device settings refresh failure.", ex);
            EnsureEmptyPlaceholder(_microphone, "Микрофон не найден");
            EnsureEmptyPlaceholder(_speaker, "Динамики / наушники не найдены");
            EnsureEmptyPlaceholder(_camera, "Камера не найдена");
        }
        finally
        {
            _loading = false;
            _refreshButton.IsEnabled = true;
            SaveSettings();
        }
    }

    private static async Task<List<DeviceChoice>> EnumerateAsync(DeviceClass deviceClass, string label)
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(deviceClass);
            return devices
                .Where(d => d.IsEnabled)
                .Select(d => new DeviceChoice(d.Id, string.IsNullOrWhiteSpace(d.Name) ? "Без названия" : d.Name))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log($"Failed to enumerate {label}.", ex);
            return [];
        }
    }

    private static void BindDevices(
        ComboBox combo,
        IReadOnlyList<DeviceChoice> devices,
        string savedId,
        string savedName,
        string emptyText)
    {
        combo.ItemsSource = null;

        if (devices.Count == 0)
        {
            combo.ItemsSource = new[] { emptyText };
            combo.SelectedIndex = 0;
            return;
        }

        var selectedIndex = 0;
        for (var i = 0; i < devices.Count; i++)
        {
            if ((!string.IsNullOrWhiteSpace(savedId) &&
                 string.Equals(devices[i].Id, savedId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(savedName) &&
                 string.Equals(devices[i].Name, savedName, StringComparison.CurrentCultureIgnoreCase)))
            {
                selectedIndex = i;
                break;
            }
        }

        combo.ItemsSource = devices.Select(d => d.Name).ToList();
        combo.SelectedIndex = selectedIndex;
    }

    private static void EnsureEmptyPlaceholder(ComboBox combo, string emptyText)
    {
        if (combo.Items.Count > 0)
            return;

        combo.ItemsSource = new[] { emptyText };
        combo.SelectedIndex = 0;
    }

    private static void SaveSelectedDevice(
        ComboBox combo,
        IReadOnlyList<DeviceChoice> devices,
        Action<string, string> save)
    {
        if (devices.Count == 0 || combo.SelectedIndex < 0 || combo.SelectedIndex >= devices.Count)
        {
            save("", "");
            return;
        }

        var selected = devices[combo.SelectedIndex];
        save(selected.Id, selected.Name);
    }

    private void SyncSelectedValues()
    {
        SaveSelectedDevice(
            _microphone,
            _microphoneDevices,
            (id, name) =>
            {
                _settings.MicrophoneId = id;
                _settings.MicrophoneName = name;
            });

        SaveSelectedDevice(
            _speaker,
            _speakerDevices,
            (id, name) =>
            {
                _settings.SpeakerId = id;
                _settings.SpeakerName = name;
            });

        SaveSelectedDevice(
            _camera,
            _cameraDevices,
            (id, name) =>
            {
                _settings.CameraId = id;
                _settings.CameraName = name;
            });
    }

    private void SaveSettings()
    {
        _settings.MicrophoneVolume = _microphoneVolume.Value;
        _settings.SpeakerVolume = _speakerVolume.Value;
        _settings.MicrophoneEnabled = _microphoneEnabled.IsOn;
        _settings.SpeakerEnabled = _speakerEnabled.IsOn;
        _settings.CameraEnabled = _cameraEnabled.IsOn;
        _settings.Save();
    }

    private static void ConfigureCombo(ComboBox combo, string placeholder)
    {
        combo.HorizontalAlignment = HorizontalAlignment.Stretch;
        combo.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        combo.MinHeight = 44;
        combo.Height = 44;
        combo.FontSize = 14;
        combo.Padding = new Thickness(12, 0, 38, 0);
        combo.PlaceholderText = placeholder;
        combo.Background = Brush("#FFFFFF");
        combo.Foreground = Brush(Text);
        combo.BorderBrush = Brush(Line);
        combo.BorderThickness = new Thickness(1);
        combo.CornerRadius = new CornerRadius(10);

        combo.Resources["ComboBoxBackground"] = Brush("#FFFFFF");
        combo.Resources["ComboBoxBackgroundPointerOver"] = Brush("#F7FCFF");
        combo.Resources["ComboBoxBackgroundPressed"] = Brush("#F1F8FC");
        combo.Resources["ComboBoxForeground"] = Brush(Text);
        combo.Resources["ComboBoxForegroundPointerOver"] = Brush(Text);
        combo.Resources["ComboBoxForegroundPressed"] = Brush(Text);
        combo.Resources["ComboBoxBorderBrush"] = Brush(Line);
        combo.Resources["ComboBoxBorderBrushPointerOver"] = Brush(Blue);
        combo.Resources["ComboBoxBorderBrushPressed"] = Brush(Blue);
        combo.Resources["ComboBoxPlaceholderForeground"] = Brush(Muted);
    }

    private static TextBlock DeviceLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brush(Text),
        Margin = new Thickness(0, 2, 0, -4)
    };

    private static void ConfigureStatus(TextBlock block)
    {
        block.FontSize = 12;
        block.Foreground = Brush(Muted);
        block.TextWrapping = TextWrapping.Wrap;
    }

    private static Border Card(UIElement child) => new()
    {
        Background = Brush("#FFFFFF"),
        BorderBrush = Brush(Line),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(18),
        Padding = new Thickness(22),
        Child = child
    };

    private static Button SecondaryButton(string text)
    {
        var button = new Button { Content = text };
        StyleSecondaryButton(button);
        return button;
    }

    private static void StyleSecondaryButton(Button button)
    {
        button.MinHeight = 44;
        button.Padding = new Thickness(16, 9, 16, 9);
        button.Background = Brush("#E8F4F9");
        button.Foreground = Brush(Navy);
        button.BorderBrush = Brush(Line);
        button.BorderThickness = new Thickness(1);
        button.CornerRadius = new CornerRadius(10);
        button.Resources["ButtonBackground"] = Brush("#E8F4F9");
        button.Resources["ButtonBackgroundPointerOver"] = Brush("#DCEEF6");
        button.Resources["ButtonBackgroundPressed"] = Brush("#CFE7F1");
    }

    private static SolidColorBrush Brush(string value) => new(Color(value));

    private static Windows.UI.Color Color(string value)
    {
        var hex = value.TrimStart('#');
        var alpha = (byte)255;
        var offset = 0;

        if (hex.Length == 8)
        {
            alpha = Convert.ToByte(hex[..2], 16);
            offset = 2;
        }

        var red = Convert.ToByte(hex.Substring(offset, 2), 16);
        var green = Convert.ToByte(hex.Substring(offset + 2, 2), 16);
        var blue = Convert.ToByte(hex.Substring(offset + 4, 2), 16);
        return ColorHelper.FromArgb(alpha, red, green, blue);
    }

    private sealed class DeviceChoice
    {
        public DeviceChoice(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }
    }
}
