using System.Runtime.InteropServices;
using ConfGTS.Client.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;
using Windows.Media.Playback;
using Windows.Media.Render;
using Windows.Storage.Streams;

namespace ConfGTS.Client;

public sealed class MediaSettingsPanel : Grid
{
    private const string Navy = "#0B2F5B";
    private const string Blue = "#168CB8";
    private const string Text = "#28445F";
    private const string Muted = "#6A7E93";
    private const string Line = "#CFE0EA";
    private const string Bg = "#EEF7FC";

    private readonly ApiClient _api;
    private readonly MediaDeviceSettings _settings = MediaDeviceSettings.Load();

    private readonly TextBox _serverBox = new();
    private readonly TextBlock _serverStatus = new();
    private readonly Button _saveServerButton = new();
    private readonly Button _forgetHttpsButton = new();

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
    private readonly ProgressBar _microphoneLevel = new();
    private readonly TextBlock _microphoneLevelCaption = new();
    private readonly Button _speakerTestButton = new();
    private readonly Image _cameraPreview = new();
    private readonly SoftwareBitmapSource _cameraBitmapSource = new();
    private readonly Button _refreshButton = new();

    private List<DeviceChoice> _microphoneDevices = [];
    private List<DeviceChoice> _speakerDevices = [];
    private List<DeviceChoice> _cameraDevices = [];

    private readonly SemaphoreSlim _diagnosticsGate = new(1, 1);
    private AudioGraph? _microphoneGraph;
    private AudioDeviceInputNode? _microphoneInputNode;
    private AudioFrameOutputNode? _microphoneFrameOutput;
    private MediaCapture? _cameraCapture;
    private MediaFrameReader? _cameraReader;
    private long _lastMicrophoneUiTick;
    private long _lastCameraFrameTick;

    private bool _loading = true;
    private bool _closed;

    public event EventHandler? CloseRequested;

    public MediaSettingsPanel(ApiClient api)
    {
        _api = api;
        StartupDiagnostics.Log("MediaSettingsPanel 0.18.15 constructor started.");
        Background = Brush(Bg);
        Children.Add(BuildUi());
        StartupDiagnostics.Log("MediaSettingsPanel 0.18.15 constructor completed.");
    }

    public async Task InitializeAsync()
    {
        ApplySavedValues();
        _serverBox.Text = _api.BaseUrl;
        await RefreshDevicesAsync();
        await RestartDiagnosticsAsync();
        StartupDiagnostics.Log("Integrated ConfGTS settings initialized.");
    }

    public async Task ShutdownAsync()
    {
        if (_closed)
            return;

        _closed = true;
        SaveSettings();
        await StopDiagnosticsAsync();
        StartupDiagnostics.Log("Integrated ConfGTS settings closed.");
    }

    private UIElement BuildUi()
    {
        var root = new Grid
        {
            Background = Brush(Bg),
            Padding = new Thickness(28, 24, 28, 28)
        };

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var outer = new StackPanel
        {
            Spacing = 14,
            MaxWidth = 1120,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        outer.Children.Add(BuildHeader());
        outer.Children.Add(BuildConnectionCard());

        var audioGrid = new Grid { ColumnSpacing = 14 };
        audioGrid.ColumnDefinitions.Add(new ColumnDefinition());
        audioGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var microphoneCard = BuildMicrophoneCard();
        Grid.SetColumn(microphoneCard, 0);
        audioGrid.Children.Add(microphoneCard);

        var speakerCard = BuildSpeakerCard();
        Grid.SetColumn(speakerCard, 1);
        audioGrid.Children.Add(speakerCard);

        outer.Children.Add(audioGrid);
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
            Text = "Настройки ConfGTS",
            FontSize = 28,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(Navy)
        });
        title.Children.Add(new TextBlock
        {
            Text = "Подключение, звук, микрофон и камера — в одном окне программы",
            FontSize = 12,
            Foreground = Brush(Muted)
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
        _refreshButton.Click += async (_, _) =>
        {
            await StopDiagnosticsAsync();
            await RefreshDevicesAsync();
            await RestartDiagnosticsAsync();
        };
        actions.Children.Add(_refreshButton);

        var back = SecondaryButton("← Вернуться");
        back.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        actions.Children.Add(back);

        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    private UIElement BuildConnectionCard()
    {
        var panel = CardPanel("\uE774", "Подключение к серверу");

        _serverBox.Header = "Имя сервера или адрес";
        _serverBox.PlaceholderText = "confgts, confgts.teplo.local:8090 или https://192.168.111.10:8090";
        _serverBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        _serverBox.FontSize = 14;
        _serverBox.MinHeight = 44;
        _serverBox.Padding = new Thickness(12, 5, 12, 5);
        _serverBox.Background = Brush("#FFFFFF");
        _serverBox.Foreground = Brush(Text);
        _serverBox.BorderBrush = Brush(Line);
        _serverBox.BorderThickness = new Thickness(1);
        _serverBox.CornerRadius = new CornerRadius(10);
        panel.Children.Add(_serverBox);

        panel.Children.Add(new TextBlock
        {
            Text = "Для HTTPS клиент запоминает отпечаток автоматически созданного сертификата после первого успешного подключения.",
            FontSize = 12,
            Foreground = Brush(Muted),
            TextWrapping = TextWrapping.Wrap
        });

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        _saveServerButton.Content = "Сохранить и проверить";
        StylePrimaryButton(_saveServerButton);
        _saveServerButton.Click += async (_, _) => await SaveServerAsync();
        actions.Children.Add(_saveServerButton);

        _forgetHttpsButton.Content = "Сбросить доверие HTTPS";
        StyleSecondaryButton(_forgetHttpsButton);
        _forgetHttpsButton.Click += (_, _) =>
        {
            _api.ForgetAllCertificateTrust();
            _serverStatus.Text = "Сохранённые отпечатки HTTPS удалены. При следующем подключении сертификат будет проверен заново.";
            _serverStatus.Foreground = Brush(Muted);
        };
        actions.Children.Add(_forgetHttpsButton);
        panel.Children.Add(actions);

        ConfigureStatus(_serverStatus);
        _serverStatus.Text = "Текущий сервер: " + _api.BaseUrl;
        panel.Children.Add(_serverStatus);

        return Card(panel);
    }

    private async Task SaveServerAsync()
    {
        if (!_saveServerButton.IsEnabled)
            return;

        _saveServerButton.IsEnabled = false;
        try
        {
            _api.BaseUrl = _serverBox.Text.Trim();
            _serverBox.Text = _api.BaseUrl;
            _serverStatus.Text = "Проверка подключения…";
            _serverStatus.Foreground = Brush(Muted);

            var healthy = await _api.HealthAsync();
            _serverStatus.Text = healthy
                ? "Сервер доступен. Адрес сохранён."
                : "Адрес сохранён, но сервер сейчас недоступен.";
            _serverStatus.Foreground = Brush(healthy ? "#278E55" : "#B54242");
        }
        catch (Exception ex)
        {
            _serverStatus.Text = "Не удалось сохранить адрес: " + ex.Message;
            _serverStatus.Foreground = Brush("#B54242");
        }
        finally
        {
            _saveServerButton.IsEnabled = true;
        }
    }

    private FrameworkElement BuildMicrophoneCard()
    {
        var panel = CardPanel("\uE720", "Микрофон");

        ConfigureCombo(_microphone, "Выберите микрофон");
        _microphone.SelectionChanged += async (_, _) =>
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
            await RestartMicrophoneMeterAsync();
        };

        _microphoneEnabled.Header = "Использовать микрофон в конференции";
        _microphoneEnabled.Toggled += async (_, _) =>
        {
            if (_loading) return;
            _settings.MicrophoneEnabled = _microphoneEnabled.IsOn;
            _microphone.IsEnabled = _microphoneEnabled.IsOn;
            _microphoneVolume.IsEnabled = _microphoneEnabled.IsOn;
            SaveSettings();
            await RestartMicrophoneMeterAsync();
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

        _microphoneLevel.Minimum = 0;
        _microphoneLevel.Maximum = 100;
        _microphoneLevel.Value = 0;
        _microphoneLevel.Height = 10;
        _microphoneLevel.HorizontalAlignment = HorizontalAlignment.Stretch;
        _microphoneLevel.Foreground = Brush(Blue);
        _microphoneLevel.Background = Brush("#DDEAF1");

        _microphoneLevelCaption.Text = "Уровень микрофона — говорите, чтобы проверить сигнал";
        _microphoneLevelCaption.FontSize = 12;
        _microphoneLevelCaption.Foreground = Brush(Muted);

        ConfigureStatus(_microphoneStatus);
        panel.Children.Add(_microphoneEnabled);
        panel.Children.Add(DeviceLabel("Устройство"));
        panel.Children.Add(_microphone);
        panel.Children.Add(_microphoneVolume);
        panel.Children.Add(DeviceLabel("Индикация входного сигнала"));
        panel.Children.Add(_microphoneLevel);
        panel.Children.Add(_microphoneLevelCaption);
        panel.Children.Add(_microphoneStatus);
        return Card(panel);
    }

    private FrameworkElement BuildSpeakerCard()
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
            _speakerTestButton.IsEnabled = _speakerEnabled.IsOn;
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

        _speakerTestButton.Content = "▶ Проверить левый и правый канал";
        StyleSecondaryButton(_speakerTestButton);
        _speakerTestButton.HorizontalAlignment = HorizontalAlignment.Left;
        _speakerTestButton.Click += async (_, _) => await TestSpeakersAsync();

        ConfigureStatus(_speakerStatus);
        panel.Children.Add(_speakerEnabled);
        panel.Children.Add(DeviceLabel("Устройство"));
        panel.Children.Add(_speaker);
        panel.Children.Add(_speakerVolume);
        panel.Children.Add(_speakerTestButton);
        panel.Children.Add(_speakerStatus);
        return Card(panel);
    }

    private FrameworkElement BuildCameraCard()
    {
        var panel = CardPanel("\uE8B8", "Камера");

        ConfigureCombo(_camera, "Выберите камеру");
        _camera.SelectionChanged += async (_, _) =>
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
            await RestartCameraPreviewAsync();
        };

        _cameraEnabled.Header = "Использовать камеру в конференции";
        _cameraEnabled.Toggled += async (_, _) =>
        {
            if (_loading) return;
            _settings.CameraEnabled = _cameraEnabled.IsOn;
            _camera.IsEnabled = _cameraEnabled.IsOn;
            SaveSettings();
            await RestartCameraPreviewAsync();
        };

        ConfigureStatus(_cameraStatus);

        panel.Children.Add(_cameraEnabled);
        panel.Children.Add(DeviceLabel("Устройство"));
        panel.Children.Add(_camera);

        _cameraPreview.Source = _cameraBitmapSource;
        _cameraPreview.Stretch = Stretch.Uniform;
        _cameraPreview.HorizontalAlignment = HorizontalAlignment.Stretch;
        _cameraPreview.VerticalAlignment = VerticalAlignment.Stretch;

        var previewHost = new Border
        {
            Height = 300,
            MinHeight = 220,
            Background = Brush("#101820"),
            BorderBrush = Brush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = _cameraPreview
        };

        panel.Children.Add(DeviceLabel("Предпросмотр изображения"));
        panel.Children.Add(previewHost);
        panel.Children.Add(_cameraStatus);
        return Card(panel);
    }

    private StackPanel CardPanel(string glyph, string title)
    {
        var panel = new StackPanel { Spacing = 11 };

        var heading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        heading.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 21,
            Foreground = Brush(Blue),
            VerticalAlignment = VerticalAlignment.Center
        });
        heading.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 19,
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
            _microphoneDevices = await EnumerateAsync(MediaDevice.GetAudioCaptureSelector(), "микрофонов");
            if (_closed) return;
            _speakerDevices = await EnumerateAsync(MediaDevice.GetAudioRenderSelector(), "устройств вывода");
            if (_closed) return;
            _cameraDevices = await EnumerateAsync(MediaDevice.GetVideoCaptureSelector(), "камер");
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
            _speakerTestButton.IsEnabled = _settings.SpeakerEnabled && _speakerDevices.Count > 0;

            _microphoneStatus.Text = _microphoneDevices.Count == 0
                ? "Микрофон не найден. В конференцию всё равно можно подключиться."
                : $"Найдено микрофонов: {_microphoneDevices.Count}.";
            _speakerStatus.Text = _speakerDevices.Count == 0
                ? "Динамики или наушники не найдены. Подключение к конференции не блокируется."
                : $"Найдено устройств вывода: {_speakerDevices.Count}.";
            _cameraStatus.Text = _cameraDevices.Count == 0
                ? "Камера не найдена. В конференцию можно войти без видео."
                : $"Найдено камер: {_cameraDevices.Count}. Выбранная камера будет показана выше.";

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

    private async Task RestartDiagnosticsAsync()
    {
        await _diagnosticsGate.WaitAsync();
        try
        {
            await StopMicrophoneMeterCoreAsync();
            await StopCameraPreviewCoreAsync();

            if (_closed)
                return;

            await StartMicrophoneMeterCoreAsync();
            await StartCameraPreviewCoreAsync();
        }
        finally
        {
            _diagnosticsGate.Release();
        }
    }

    private async Task RestartMicrophoneMeterAsync()
    {
        await _diagnosticsGate.WaitAsync();
        try
        {
            await StopMicrophoneMeterCoreAsync();
            if (!_closed)
                await StartMicrophoneMeterCoreAsync();
        }
        finally
        {
            _diagnosticsGate.Release();
        }
    }

    private async Task RestartCameraPreviewAsync()
    {
        await _diagnosticsGate.WaitAsync();
        try
        {
            await StopCameraPreviewCoreAsync();
            if (!_closed)
                await StartCameraPreviewCoreAsync();
        }
        finally
        {
            _diagnosticsGate.Release();
        }
    }

    private async Task StopDiagnosticsAsync()
    {
        await _diagnosticsGate.WaitAsync();
        try
        {
            await StopMicrophoneMeterCoreAsync();
            await StopCameraPreviewCoreAsync();
        }
        finally
        {
            _diagnosticsGate.Release();
        }
    }

    private async Task StartMicrophoneMeterCoreAsync()
    {
        _microphoneLevel.Value = 0;

        if (!_settings.MicrophoneEnabled)
        {
            _microphoneLevelCaption.Text = "Микрофон отключён.";
            return;
        }

        var selected = SelectedDevice(_microphone, _microphoneDevices);
        if (selected is null)
        {
            _microphoneLevelCaption.Text = "Выберите микрофон для проверки.";
            return;
        }

        try
        {
            var device = await DeviceInformation.CreateFromIdAsync(selected.Id);
            var graphResult = await AudioGraph.CreateAsync(new AudioGraphSettings(AudioRenderCategory.Communications)
            {
                QuantumSizeSelectionMode = QuantumSizeSelectionMode.ClosestToDesired,
                DesiredSamplesPerQuantum = 480
            });

            if (graphResult.Status != AudioGraphCreationStatus.Success || graphResult.Graph is null)
                throw new InvalidOperationException("Windows не удалось создать аудиограф для проверки микрофона.");

            _microphoneGraph = graphResult.Graph;

            var inputResult = await _microphoneGraph.CreateDeviceInputNodeAsync(
                MediaCategory.Communications,
                _microphoneGraph.EncodingProperties,
                device);

            if (inputResult.Status != AudioDeviceNodeCreationStatus.Success || inputResult.DeviceInputNode is null)
                throw new InvalidOperationException("Не удалось открыть выбранный микрофон.");

            _microphoneInputNode = inputResult.DeviceInputNode;
            _microphoneFrameOutput = _microphoneGraph.CreateFrameOutputNode(_microphoneGraph.EncodingProperties);
            _microphoneInputNode.AddOutgoingConnection(_microphoneFrameOutput);
            _microphoneGraph.QuantumStarted += MicrophoneGraph_QuantumStarted;
            _microphoneGraph.Start();

            _microphoneLevelCaption.Text = "Говорите — полоса показывает реальный входной уровень.";
            _microphoneStatus.Text = "Микрофон открыт для проверки: " + selected.Name;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Microphone level meter start failed.", ex);
            _microphoneLevel.Value = 0;
            _microphoneLevelCaption.Text = "Не удалось получить уровень выбранного микрофона.";
            _microphoneStatus.Text = ex.Message;
        }
    }

    private async Task StopMicrophoneMeterCoreAsync()
    {
        var graph = _microphoneGraph;
        var input = _microphoneInputNode;
        var output = _microphoneFrameOutput;

        _microphoneGraph = null;
        _microphoneInputNode = null;
        _microphoneFrameOutput = null;

        if (graph is not null)
        {
            try
            {
                graph.QuantumStarted -= MicrophoneGraph_QuantumStarted;
                graph.Stop();
            }
            catch
            {
            }
        }

        try { input?.Dispose(); } catch { }
        try { output?.Dispose(); } catch { }
        try { graph?.Dispose(); } catch { }

        _microphoneLevel.Value = 0;
        await Task.CompletedTask;
    }

    private unsafe void MicrophoneGraph_QuantumStarted(AudioGraph sender, object args)
    {
        var output = _microphoneFrameOutput;
        if (output is null)
            return;

        try
        {
            using var frame = output.GetFrame();
            using var buffer = frame.LockBuffer(AudioBufferAccessMode.Read);
            using var reference = buffer.CreateReference();

            ((IMemoryBufferByteAccess)reference).GetBuffer(out var data, out var capacity);
            if (data is null || capacity < sizeof(float))
                return;

            var samples = (int)(capacity / sizeof(float));
            if (samples <= 0)
                return;

            double sum = 0;
            var ptr = (float*)data;
            for (var i = 0; i < samples; i++)
            {
                var sample = ptr[i];
                sum += sample * sample;
            }

            var rms = Math.Sqrt(sum / samples);
            var percent = Math.Clamp(rms * 320.0, 0, 100);

            var now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastMicrophoneUiTick) < 70)
                return;
            Interlocked.Exchange(ref _lastMicrophoneUiTick, now);

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_closed)
                    _microphoneLevel.Value = percent;
            });
        }
        catch
        {
            // Audio callbacks must never terminate the client.
        }
    }

    private async Task StartCameraPreviewCoreAsync()
    {
        _cameraPreview.Source = _cameraBitmapSource;

        if (!_settings.CameraEnabled)
        {
            _cameraStatus.Text = "Камера отключена.";
            return;
        }

        var selected = SelectedDevice(_camera, _cameraDevices);
        if (selected is null)
        {
            _cameraStatus.Text = "Выберите камеру для предпросмотра.";
            return;
        }

        try
        {
            var capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = selected.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu
            });

            var source = capture.FrameSources.Values.FirstOrDefault(x =>
                x.Info.SourceKind == MediaFrameSourceKind.Color);

            if (source is null)
            {
                capture.Dispose();
                throw new InvalidOperationException("Выбранная камера не предоставляет видеопоток.");
            }

            var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            reader.FrameArrived += CameraReader_FrameArrived;

            var start = await reader.StartAsync();
            if (start != MediaFrameReaderStartStatus.Success)
            {
                reader.FrameArrived -= CameraReader_FrameArrived;
                reader.Dispose();
                capture.Dispose();
                throw new InvalidOperationException("Не удалось запустить предпросмотр камеры.");
            }

            _cameraCapture = capture;
            _cameraReader = reader;
            _cameraStatus.Text = "Предпросмотр активен: " + selected.Name;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Camera preview start failed.", ex);
            _cameraStatus.Text = "Предпросмотр недоступен: " + ex.Message;
        }
    }

    private async Task StopCameraPreviewCoreAsync()
    {
        var reader = _cameraReader;
        var capture = _cameraCapture;
        _cameraReader = null;
        _cameraCapture = null;

        if (reader is not null)
        {
            try
            {
                reader.FrameArrived -= CameraReader_FrameArrived;
                await reader.StopAsync();
            }
            catch
            {
            }
            try { reader.Dispose(); } catch { }
        }

        try { capture?.Dispose(); } catch { }
    }

    private void CameraReader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastCameraFrameTick) < 90)
            return;
        Interlocked.Exchange(ref _lastCameraFrameTick, now);

        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            var source = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (source is null)
                return;

            var copy = SoftwareBitmap.Convert(
                source,
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);

            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (!_closed)
                        await _cameraBitmapSource.SetBitmapAsync(copy);
                }
                catch
                {
                }
                finally
                {
                    copy.Dispose();
                }
            });
        }
        catch
        {
        }
    }

    private async Task TestSpeakersAsync()
    {
        if (!_speakerTestButton.IsEnabled)
            return;

        var selected = SelectedDevice(_speaker, _speakerDevices);
        if (selected is null)
        {
            _speakerStatus.Text = "Выберите динамики или наушники.";
            return;
        }

        _speakerTestButton.IsEnabled = false;
        try
        {
            var device = await DeviceInformation.CreateFromIdAsync(selected.Id);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(CreateStereoTestWav());
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);

            using var player = new MediaPlayer
            {
                AudioDevice = device,
                Volume = Math.Clamp(_speakerVolume.Value / 100.0, 0, 1),
                Source = MediaSource.CreateFromStream(stream, "audio/wav")
            };

            _speakerStatus.Text = "Проверка: сейчас должен звучать ЛЕВЫЙ канал.";
            player.Play();
            await Task.Delay(950);

            _speakerStatus.Text = "Проверка: сейчас должен звучать ПРАВЫЙ канал.";
            await Task.Delay(1000);

            player.Pause();
            _speakerStatus.Text = "Проверка завершена: левый → правый. Устройство: " + selected.Name;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Speaker channel test failed.", ex);
            _speakerStatus.Text = "Не удалось воспроизвести тест: " + ex.Message;
        }
        finally
        {
            _speakerTestButton.IsEnabled = _settings.SpeakerEnabled && _speakerDevices.Count > 0;
        }
    }

    private static byte[] CreateStereoTestWav()
    {
        const int sampleRate = 48000;
        const short channels = 2;
        const short bitsPerSample = 16;
        const double toneSeconds = 0.8;
        const double gapSeconds = 0.18;

        var toneSamples = (int)(sampleRate * toneSeconds);
        var gapSamples = (int)(sampleRate * gapSeconds);
        var totalSamples = toneSamples + gapSamples + toneSamples;
        var dataBytes = totalSamples * channels * (bitsPerSample / 8);

        using var memory = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(memory);

        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);

        for (var i = 0; i < totalSamples; i++)
        {
            short left = 0;
            short right = 0;

            if (i < toneSamples)
            {
                var t = i / (double)sampleRate;
                left = (short)(Math.Sin(2 * Math.PI * 520 * t) * short.MaxValue * 0.20);
            }
            else if (i >= toneSamples + gapSamples)
            {
                var j = i - toneSamples - gapSamples;
                var t = j / (double)sampleRate;
                right = (short)(Math.Sin(2 * Math.PI * 680 * t) * short.MaxValue * 0.20);
            }

            writer.Write(left);
            writer.Write(right);
        }

        return memory.ToArray();
    }

    private static async Task<List<DeviceChoice>> EnumerateAsync(string selector, string label)
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(selector);
            var result = devices
                .Select(d => new DeviceChoice(d.Id, string.IsNullOrWhiteSpace(d.Name) ? "Без названия" : d.Name))
                .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            StartupDiagnostics.Log(
                $"Enumerated {label}: " +
                (result.Count == 0 ? "none" : string.Join(" | ", result.Select(d => d.Name))));
            return result;
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

    private static DeviceChoice? SelectedDevice(
        ComboBox combo,
        IReadOnlyList<DeviceChoice> devices)
    {
        if (devices.Count == 0 || combo.SelectedIndex < 0 || combo.SelectedIndex >= devices.Count)
            return null;

        return devices[combo.SelectedIndex];
    }

    private static void SaveSelectedDevice(
        ComboBox combo,
        IReadOnlyList<DeviceChoice> devices,
        Action<string, string> save)
    {
        var selected = SelectedDevice(combo, devices);
        if (selected is null)
        {
            save("", "");
            return;
        }

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
        Padding = new Thickness(20),
        Child = child
    };

    private static Button SecondaryButton(string text)
    {
        var button = new Button { Content = text };
        StyleSecondaryButton(button);
        return button;
    }

    private static void StylePrimaryButton(Button button)
    {
        button.MinHeight = 42;
        button.Padding = new Thickness(16, 8, 16, 8);
        button.Background = Brush(Blue);
        button.Foreground = Brush("#FFFFFF");
        button.BorderThickness = new Thickness(0);
        button.CornerRadius = new CornerRadius(10);
        button.Resources["ButtonBackground"] = Brush(Blue);
        button.Resources["ButtonBackgroundPointerOver"] = Brush(Navy);
        button.Resources["ButtonBackgroundPressed"] = Brush("#0F6F98");
        button.Resources["ButtonForeground"] = Brush("#FFFFFF");
        button.Resources["ButtonForegroundPointerOver"] = Brush("#FFFFFF");
        button.Resources["ButtonForegroundPressed"] = Brush("#FFFFFF");
    }

    private static void StyleSecondaryButton(Button button)
    {
        button.MinHeight = 42;
        button.Padding = new Thickness(14, 8, 14, 8);
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

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
