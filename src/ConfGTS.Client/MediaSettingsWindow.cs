using ConfGTS.Client.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Windows.Devices.Enumeration;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using WinRT.Interop;

namespace ConfGTS.Client;

public sealed class MediaSettingsWindow : Window
{
    private const string Navy = "#0B2F5B";
    private const string Blue = "#168CB8";
    private const string Cyan = "#16B6C2";
    private const string Text = "#28445F";
    private const string Muted = "#6A7E93";
    private const string Line = "#CFE0EA";
    private const string Bg = "#EEF7FC";

    private readonly MediaDeviceSettings _settings = MediaDeviceSettings.Load();
    private readonly MMDeviceEnumerator _audioEnumerator = new();

    private readonly ComboBox _microphoneCombo = new();
    private readonly Slider _microphoneVolume = new();
    private readonly ProgressBar _microphoneLevel = new();
    private readonly TextBlock _microphoneStatus = new();

    private readonly ComboBox _speakerCombo = new();
    private readonly Slider _speakerVolume = new();
    private readonly TextBlock _speakerStatus = new();

    private readonly ComboBox _cameraCombo = new();
    private readonly Image _cameraPreview = new();
    private readonly TextBlock _cameraStatus = new();

    private WasapiCapture? _microphoneCapture;
    private MMDevice? _microphoneDevice;
    private WasapiOut? _speakerOutput;
    private MMDevice? _speakerDevice;

    private MediaCapture? _mediaCapture;
    private MediaFrameReader? _frameReader;
    private int _framePending;
    private bool _loading = true;
    private bool _closed;

    public MediaSettingsWindow()
    {
        Title = "ConfGTS — настройки устройств";
        SetWindowSize(1120, 780);
        Content = BuildUi();

        Closed += async (_, _) =>
        {
            _closed = true;
            StopMicrophoneTest();
            StopSpeakerTest();
            await StopCameraPreviewAsync();
            _settings.Save();
        };

        _ = LoadDevicesAsync();
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Background = Brush(Bg) };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var content = new StackPanel
        {
            Padding = new Thickness(32),
            Spacing = 18,
            MaxWidth = 1100,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        content.Children.Add(BuildHeader());

        var audioGrid = new Grid { ColumnSpacing = 16 };
        audioGrid.ColumnDefinitions.Add(new ColumnDefinition());
        audioGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var micCard = Card(BuildMicrophonePanel());
        var speakerCard = Card(BuildSpeakerPanel());
        Grid.SetColumn(speakerCard, 1);
        audioGrid.Children.Add(micCard);
        audioGrid.Children.Add(speakerCard);
        content.Children.Add(audioGrid);

        content.Children.Add(Card(BuildCameraPanel()));

        content.Children.Add(new TextBlock
        {
            Text = "Настройки сохраняются для текущего пользователя Windows.",
            Foreground = Brush(Muted),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16)
        });

        scroll.Content = content;
        root.Children.Add(scroll);
        return root;
    }

    private UIElement BuildHeader()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var mark = new Border
        {
            Width = 66,
            Height = 66,
            CornerRadius = new CornerRadius(19),
            Background = Gradient(),
            Margin = new Thickness(0, 0, 16, 0)
        };
        mark.Child = new TextBlock
        {
            Text = "ГТС",
            FontSize = 21,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = Brush("#FFFFFF"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(mark);

        var title = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 1);
        title.Children.Add(new TextBlock
        {
            Text = "Настройки устройств",
            FontSize = 30,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = Brush(Navy)
        });
        title.Children.Add(new TextBlock
        {
            Text = "Микрофон, камера и динамики ConfGTS",
            FontSize = 14,
            Foreground = Brush(Muted)
        });
        grid.Children.Add(title);
        return grid;
    }

    private UIElement BuildMicrophonePanel()
    {
        var panel = new StackPanel { Spacing = 11 };
        panel.Children.Add(SectionTitle("Микрофон"));
        panel.Children.Add(Hint("Выберите устройство, настройте уровень и запустите тест. Индикатор должен реагировать на голос."));

        panel.Children.Add(Label("Устройство"));
        StyleCombo(_microphoneCombo);
        _microphoneCombo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            StopMicrophoneTest();
            _settings.MicrophoneId = SelectedId(_microphoneCombo);
            LoadMicrophoneVolume();
            _settings.Save();
        };
        panel.Children.Add(_microphoneCombo);

        panel.Children.Add(Label("Уровень микрофона"));
        _microphoneVolume.Minimum = 0;
        _microphoneVolume.Maximum = 100;
        _microphoneVolume.StepFrequency = 1;
        _microphoneVolume.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.MicrophoneVolume = _microphoneVolume.Value;
            ApplyEndpointVolume(_settings.MicrophoneId, _microphoneVolume.Value);
            _settings.Save();
        };
        panel.Children.Add(_microphoneVolume);

        _microphoneLevel.Minimum = 0;
        _microphoneLevel.Maximum = 100;
        _microphoneLevel.Value = 0;
        _microphoneLevel.Height = 8;
        panel.Children.Add(_microphoneLevel);

        var test = SecondaryButton("Проверить микрофон");
        test.Click += (_, _) =>
        {
            if (_microphoneCapture is null)
                StartMicrophoneTest();
            else
                StopMicrophoneTest();
        };
        panel.Children.Add(test);

        _microphoneStatus.Text = "Тест не запущен";
        _microphoneStatus.Foreground = Brush(Muted);
        panel.Children.Add(_microphoneStatus);

        return panel;
    }

    private UIElement BuildSpeakerPanel()
    {
        var panel = new StackPanel { Spacing = 11 };
        panel.Children.Add(SectionTitle("Динамики"));
        panel.Children.Add(Hint("Выберите устройство вывода, задайте громкость и воспроизведите тестовый сигнал."));

        panel.Children.Add(Label("Устройство"));
        StyleCombo(_speakerCombo);
        _speakerCombo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            StopSpeakerTest();
            _settings.SpeakerId = SelectedId(_speakerCombo);
            LoadSpeakerVolume();
            _settings.Save();
        };
        panel.Children.Add(_speakerCombo);

        panel.Children.Add(Label("Громкость"));
        _speakerVolume.Minimum = 0;
        _speakerVolume.Maximum = 100;
        _speakerVolume.StepFrequency = 1;
        _speakerVolume.ValueChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.SpeakerVolume = _speakerVolume.Value;
            ApplyEndpointVolume(_settings.SpeakerId, _speakerVolume.Value);
            _settings.Save();
        };
        panel.Children.Add(_speakerVolume);

        var test = SecondaryButton("Проверить динамики");
        test.Click += async (_, _) => await TestSpeakersAsync();
        panel.Children.Add(test);

        _speakerStatus.Text = "Нажмите кнопку — прозвучит короткий тестовый сигнал";
        _speakerStatus.Foreground = Brush(Muted);
        _speakerStatus.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_speakerStatus);

        return panel;
    }

    private UIElement BuildCameraPanel()
    {
        var grid = new Grid { ColumnSpacing = 22 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var controls = new StackPanel { Spacing = 11 };
        controls.Children.Add(SectionTitle("Видеокамера"));
        controls.Children.Add(Hint("Выберите камеру. Предпросмотр справа запускается автоматически."));

        controls.Children.Add(Label("Устройство"));
        StyleCombo(_cameraCombo);
        _cameraCombo.SelectionChanged += async (_, _) =>
        {
            if (_loading) return;
            _settings.CameraId = SelectedId(_cameraCombo);
            _settings.Save();
            await StartCameraPreviewAsync();
        };
        controls.Children.Add(_cameraCombo);

        var restart = SecondaryButton("Перезапустить предпросмотр");
        restart.Click += async (_, _) => await StartCameraPreviewAsync();
        controls.Children.Add(restart);

        _cameraStatus.Text = "Камера не запущена";
        _cameraStatus.Foreground = Brush(Muted);
        _cameraStatus.TextWrapping = TextWrapping.Wrap;
        controls.Children.Add(_cameraStatus);
        grid.Children.Add(controls);

        var previewBorder = new Border
        {
            Background = Brush("#0A223A"),
            BorderBrush = Brush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            MinHeight = 330,
            Padding = new Thickness(8)
        };
        Grid.SetColumn(previewBorder, 1);

        _cameraPreview.Stretch = Stretch.Uniform;
        _cameraPreview.HorizontalAlignment = HorizontalAlignment.Stretch;
        _cameraPreview.VerticalAlignment = VerticalAlignment.Stretch;
        previewBorder.Child = _cameraPreview;
        grid.Children.Add(previewBorder);

        return grid;
    }

    private async Task LoadDevicesAsync()
    {
        try
        {
            var microphones = _audioEnumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(d => new DeviceChoice(d.ID, d.FriendlyName))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var speakers = _audioEnumerator
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(d => new DeviceChoice(d.ID, d.FriendlyName))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var cameras = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            var cameraChoices = cameras
                .Select(d => new DeviceChoice(d.Id, d.Name))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _microphoneCombo.ItemsSource = microphones;
            _speakerCombo.ItemsSource = speakers;
            _cameraCombo.ItemsSource = cameraChoices;

            SelectSaved(_microphoneCombo, microphones, _settings.MicrophoneId);
            SelectSaved(_speakerCombo, speakers, _settings.SpeakerId);
            SelectSaved(_cameraCombo, cameraChoices, _settings.CameraId);

            _settings.MicrophoneId = SelectedId(_microphoneCombo);
            _settings.SpeakerId = SelectedId(_speakerCombo);
            _settings.CameraId = SelectedId(_cameraCombo);

            LoadMicrophoneVolume();
            LoadSpeakerVolume();

            _loading = false;
            _settings.Save();

            if (cameraChoices.Count > 0)
                await StartCameraPreviewAsync();
            else
                _cameraStatus.Text = "Видеокамеры не найдены.";
        }
        catch (Exception ex)
        {
            _loading = false;
            _cameraStatus.Text = "Ошибка получения устройств: " + ex.Message;
        }
    }

    private void LoadMicrophoneVolume()
    {
        _microphoneVolume.Value = ReadEndpointVolume(_settings.MicrophoneId, _settings.MicrophoneVolume);
    }

    private void LoadSpeakerVolume()
    {
        _speakerVolume.Value = ReadEndpointVolume(_settings.SpeakerId, _settings.SpeakerVolume);
    }

    private double ReadEndpointVolume(string id, double fallback)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id)) return fallback;
            var device = _audioEnumerator.GetDevice(id);
            return Math.Clamp(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100.0, 0, 100);
        }
        catch
        {
            return fallback;
        }
    }

    private void ApplyEndpointVolume(string id, double value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            var device = _audioEnumerator.GetDevice(id);
            device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(value / 100.0, 0, 1);
        }
        catch
        {
        }
    }

    private void StartMicrophoneTest()
    {
        StopMicrophoneTest();

        var id = SelectedId(_microphoneCombo);
        if (string.IsNullOrWhiteSpace(id))
        {
            _microphoneStatus.Text = "Микрофон не выбран.";
            return;
        }

        try
        {
            _microphoneDevice = _audioEnumerator.GetDevice(id);
            _microphoneCapture = new WasapiCapture(_microphoneDevice);
            _microphoneCapture.DataAvailable += MicrophoneDataAvailable;
            _microphoneCapture.RecordingStopped += (_, e) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    _microphoneLevel.Value = 0;
                    if (!_closed)
                        _microphoneStatus.Text = e.Exception is null ? "Тест остановлен" : "Ошибка: " + e.Exception.Message;
                });
            };
            _microphoneCapture.StartRecording();
            _microphoneStatus.Text = "Говорите в микрофон…";
            _microphoneStatus.Foreground = Brush("#16804A");
        }
        catch (Exception ex)
        {
            StopMicrophoneTest();
            _microphoneStatus.Text = "Не удалось открыть микрофон: " + ex.Message;
            _microphoneStatus.Foreground = Brush("#B54242");
        }
    }

    private void StopMicrophoneTest()
    {
        try { _microphoneCapture?.StopRecording(); } catch { }
        try { _microphoneCapture?.Dispose(); } catch { }
        _microphoneCapture = null;
        _microphoneDevice = null;
        _microphoneLevel.Value = 0;
    }

    private void MicrophoneDataAvailable(object? sender, WaveInEventArgs e)
    {
        var capture = _microphoneCapture;
        if (capture is null || e.BytesRecorded <= 0) return;

        var format = capture.WaveFormat;
        double peak = 0;

        try
        {
            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                for (var i = 0; i + 4 <= e.BytesRecorded; i += 4)
                {
                    var sample = Math.Abs(BitConverter.ToSingle(e.Buffer, i));
                    if (sample > peak) peak = sample;
                }
            }
            else if (format.BitsPerSample == 16)
            {
                for (var i = 0; i + 2 <= e.BytesRecorded; i += 2)
                {
                    var sample = Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768.0);
                    if (sample > peak) peak = sample;
                }
            }
            else if (format.BitsPerSample == 32)
            {
                for (var i = 0; i + 4 <= e.BytesRecorded; i += 4)
                {
                    var sample = Math.Abs(BitConverter.ToInt32(e.Buffer, i) / 2147483648.0);
                    if (sample > peak) peak = sample;
                }
            }
        }
        catch
        {
            return;
        }

        var value = Math.Clamp(peak * 160.0, 0, 100);
        DispatcherQueue.TryEnqueue(() => _microphoneLevel.Value = value);
    }

    private async Task TestSpeakersAsync()
    {
        StopSpeakerTest();
        var id = SelectedId(_speakerCombo);
        if (string.IsNullOrWhiteSpace(id))
        {
            _speakerStatus.Text = "Динамики не выбраны.";
            return;
        }

        try
        {
            _speakerDevice = _audioEnumerator.GetDevice(id);
            _speakerOutput = new WasapiOut(_speakerDevice, AudioClientShareMode.Shared, true, 80);

            var signal = new SignalGenerator(44100, 1)
            {
                Gain = 0.18,
                Frequency = 620,
                Type = SignalGeneratorType.Sin
            };
            _speakerOutput.Init(new SampleToWaveProvider16(signal));
            _speakerOutput.Play();
            _speakerStatus.Text = "Воспроизводится тестовый сигнал…";
            _speakerStatus.Foreground = Brush("#16804A");

            await Task.Delay(1200);
            StopSpeakerTest();
            _speakerStatus.Text = "Тест завершён";
            _speakerStatus.Foreground = Brush(Muted);
        }
        catch (Exception ex)
        {
            StopSpeakerTest();
            _speakerStatus.Text = "Не удалось воспроизвести тест: " + ex.Message;
            _speakerStatus.Foreground = Brush("#B54242");
        }
    }

    private void StopSpeakerTest()
    {
        try { _speakerOutput?.Stop(); } catch { }
        try { _speakerOutput?.Dispose(); } catch { }
        _speakerOutput = null;
        _speakerDevice = null;
    }

    private async Task StartCameraPreviewAsync()
    {
        await StopCameraPreviewAsync();

        var id = SelectedId(_cameraCombo);
        if (string.IsNullOrWhiteSpace(id))
        {
            _cameraStatus.Text = "Камера не выбрана.";
            return;
        }

        try
        {
            _cameraStatus.Text = "Запуск камеры…";
            _mediaCapture = new MediaCapture();
            var init = new MediaCaptureInitializationSettings
            {
                VideoDeviceId = id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                SharingMode = MediaCaptureSharingMode.SharedReadOnly
            };
            await _mediaCapture.InitializeAsync(init);

            var source = _mediaCapture.FrameSources.Values
                .FirstOrDefault(s => s.Info.SourceKind == MediaFrameSourceKind.Color);
            if (source is null)
                throw new InvalidOperationException("Камера не предоставляет цветной видеопоток.");

            _frameReader = await _mediaCapture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
            _frameReader.FrameArrived += CameraFrameArrived;
            var status = await _frameReader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
                throw new InvalidOperationException("Не удалось запустить видеопоток: " + status);

            _cameraStatus.Text = "Камера работает";
            _cameraStatus.Foreground = Brush("#16804A");
        }
        catch (Exception ex)
        {
            await StopCameraPreviewAsync();
            _cameraStatus.Text = "Не удалось открыть камеру: " + ex.Message;
            _cameraStatus.Foreground = Brush("#B54242");
        }
    }

    private void CameraFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (_closed || Interlocked.Exchange(ref _framePending, 1) != 0)
            return;

        SoftwareBitmap? converted = null;
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap is null)
            {
                Interlocked.Exchange(ref _framePending, 0);
                return;
            }

            converted = SoftwareBitmap.Convert(
                bitmap,
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);

            var ownedBitmap = converted;
            converted = null;

            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (_closed)
                    {
                        ownedBitmap.Dispose();
                        return;
                    }

                    var source = new SoftwareBitmapSource();
                    await source.SetBitmapAsync(ownedBitmap);
                    _cameraPreview.Source = source;
                    ownedBitmap.Dispose();
                }
                catch
                {
                    try { ownedBitmap.Dispose(); } catch { }
                }
                finally
                {
                    Interlocked.Exchange(ref _framePending, 0);
                }
            });
        }
        catch
        {
            try { converted?.Dispose(); } catch { }
            Interlocked.Exchange(ref _framePending, 0);
        }
    }

    private async Task StopCameraPreviewAsync()
    {
        var reader = _frameReader;
        _frameReader = null;
        if (reader is not null)
        {
            try
            {
                reader.FrameArrived -= CameraFrameArrived;
                await reader.StopAsync();
            }
            catch
            {
            }
            try { reader.Dispose(); } catch { }
        }

        if (_mediaCapture is not null)
        {
            try { _mediaCapture.Dispose(); } catch { }
            _mediaCapture = null;
        }

        _cameraPreview.Source = null;
        Interlocked.Exchange(ref _framePending, 0);
    }

    private static string SelectedId(ComboBox combo) =>
        (combo.SelectedItem as DeviceChoice)?.Id ?? "";

    private static void SelectSaved(ComboBox combo, IReadOnlyList<DeviceChoice> items, string id)
    {
        if (items.Count == 0)
        {
            combo.SelectedIndex = -1;
            return;
        }

        var match = items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        combo.SelectedItem = match ?? items[0];
    }

    private static void StyleCombo(ComboBox combo)
    {
        combo.HorizontalAlignment = HorizontalAlignment.Stretch;
        combo.MinHeight = 44;
        combo.FontSize = 14;
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

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 21,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = Brush(Navy)
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = Brush(Text)
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Brush(Muted),
        TextWrapping = TextWrapping.Wrap
    };

    private static Button SecondaryButton(string text)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 42,
            Padding = new Thickness(15, 8, 15, 8),
            Background = Brush("#E8F4F9"),
            Foreground = Brush(Navy),
            BorderBrush = Brush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9)
        };
        button.Resources["ButtonBackgroundPointerOver"] = Brush("#D9EDF5");
        button.Resources["ButtonForegroundPointerOver"] = Brush(Navy);
        return button;
    }

    private static LinearGradientBrush Gradient()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop { Color = Color(Blue), Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Color(Cyan), Offset = 1 });
        return brush;
    }

    private void SetWindowSize(int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow.GetFromWindowId(id)?.Resize(new SizeInt32(width, height));
    }

    private static SolidColorBrush Brush(string hex) => new(Color(hex));

    private static Windows.UI.Color Color(string hex)
    {
        var value = hex.TrimStart('#');
        var alpha = (byte)255;
        var offset = 0;
        if (value.Length == 8)
        {
            alpha = Convert.ToByte(value[..2], 16);
            offset = 2;
        }

        var red = Convert.ToByte(value.Substring(offset, 2), 16);
        var green = Convert.ToByte(value.Substring(offset + 2, 2), 16);
        var blue = Convert.ToByte(value.Substring(offset + 4, 2), 16);
        return ColorHelper.FromArgb(alpha, red, green, blue);
    }

    private sealed record DeviceChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
