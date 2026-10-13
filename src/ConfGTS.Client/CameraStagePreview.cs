using ConfGTS.Client.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Devices;
using Windows.Media.MediaProperties;

namespace ConfGTS.Client;

/// <summary>
/// Always-visible local camera scene for the signed-in dashboard.
/// A conference owns its WebView2 media stream; the native scene releases
/// the camera before joining, then restarts after leaving.
/// </summary>
public sealed class CameraStagePreview : Grid
{
    private readonly Image _image = new()
    {
        Stretch = Stretch.UniformToFill,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch
    };
    private readonly TextBlock _status = new()
    {
        Text = "Подключение камеры…",
        FontSize = 18,
        Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 200, 214, 226)),
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(24)
    };
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly StackPanel _actions = new() { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _startButton = new() { Content = "Включить камеру", MinWidth = 180, Padding = new Thickness(18, 10, 18, 10) };
    private readonly Button _stopButton = new() { Content = "Остановить камеру", Padding = new Thickness(12, 7, 12, 7), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(20), Visibility = Visibility.Collapsed };
    public event EventHandler? StartRequested;
    public event EventHandler? StopRequested;
    public bool IsRunning => _playing;
    private SoftwareBitmapSource? _bitmap;
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private bool _playing;
    private int _frameQueued;
    private long _lastFrameTick;

    public CameraStagePreview()
    {
        Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 13, 23, 35));
        _status.Text = "Камера не включена. Для предпросмотра нажмите кнопку.";
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.VerticalAlignment = VerticalAlignment.Center;
        _status.Margin = new Thickness(20, 20, 20, 0);
        _actions.Children.Add(_status);
        _actions.Children.Add(_startButton);
        _startButton.HorizontalAlignment = HorizontalAlignment.Center;
        _startButton.Click += (_, _) => StartRequested?.Invoke(this, EventArgs.Empty);
        _stopButton.Click += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
        Children.Add(_image);
        Children.Add(_actions);
        Children.Add(_stopButton);
    }

    public void ShowPaused()
    {
        _status.Text = "Камера в режиме ожидания. Нажмите «Включить камеру».";
        _status.Visibility = Visibility.Visible;
        _actions.Visibility = Visibility.Visible;
        _startButton.Visibility = Visibility.Visible;
        _startButton.IsEnabled = true;
        _stopButton.Visibility = Visibility.Collapsed;
    }

    public async Task StartAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            await StopCoreAsync();
            _status.Text = "Подключение камеры…";
            _status.Visibility = Visibility.Visible;
            _actions.Visibility = Visibility.Visible;
            _startButton.IsEnabled = false;
            _stopButton.Visibility = Visibility.Collapsed;
            StartupDiagnostics.Log("Camera capture explicitly requested (not started by application startup).");

            var settings = MediaDeviceSettings.Load();
            if (!settings.CameraEnabled)
            {
                _status.Text = "Камера отключена в настройках";
                return;
            }

            var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetVideoCaptureSelector());
            if (devices.Count == 0)
            {
                _status.Text = "Камера не обнаружена";
                return;
            }

            var selected = devices.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(settings.CameraId) &&
                string.Equals(x.Id, settings.CameraId, StringComparison.OrdinalIgnoreCase));
            selected ??= devices.FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(settings.CameraName) &&
                string.Equals(x.Name, settings.CameraName, StringComparison.CurrentCultureIgnoreCase));
            selected ??= devices[0];

            var capture = new MediaCapture();
            try
            {
                await capture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    VideoDeviceId = selected.Id,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu
                });

                var source = capture.FrameSources.Values.FirstOrDefault(x =>
                    x.Info.SourceKind == MediaFrameSourceKind.Color);
                if (source is null)
                    throw new InvalidOperationException("Устройство не предоставляет видеопоток.");

                // Use a lightweight native camera mode for the non-conference
                // preview. Previously the driver could feed full 4K frames to
                // CPU BGRA conversion on every callback and degrade browser video.
                var mode = source.SupportedFormats
                    .Where(x => x.VideoFormat.Width > 0 && x.VideoFormat.Height > 0)
                    .OrderBy(x => x.VideoFormat.Width <= 640 && x.VideoFormat.Height <= 480 ? 0 : 1)
                    .ThenBy(x => x.VideoFormat.Width <= 640 && x.VideoFormat.Height <= 480
                        ? -((long)x.VideoFormat.Width * x.VideoFormat.Height)
                        : (long)x.VideoFormat.Width * x.VideoFormat.Height)
                    .FirstOrDefault();
                if (mode is not null)
                {
                    try
                    {
                        await source.SetFormatAsync(mode);
                        StartupDiagnostics.Log(
                            $"Camera preview format: {mode.VideoFormat.Width}x{mode.VideoFormat.Height}.");
                    }
                    catch (Exception ex)
                    {
                        StartupDiagnostics.Log("Camera did not accept lightweight preview format.", ex);
                    }
                }

                _bitmap ??= new SoftwareBitmapSource();
                _image.Source = _bitmap;

                var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
                _capture = capture;
                _reader = reader;
                _playing = true;
                reader.FrameArrived += OnFrameArrived;

                var startStatus = await reader.StartAsync();
                if (startStatus != MediaFrameReaderStartStatus.Success)
                    throw new InvalidOperationException("Не удалось запустить видеопоток камеры.");

                _actions.Visibility = Visibility.Collapsed;
                _stopButton.Visibility = Visibility.Visible;
                StartupDiagnostics.Log("Persistent camera stage started: " + selected.Name);
            }
            catch
            {
                await StopCoreAsync();
                capture.Dispose();
                throw;
            }
        }
        catch (Exception ex)
        {
            _status.Text = "Изображение с камеры недоступно";
            _status.Visibility = Visibility.Visible;
            StartupDiagnostics.Log("Persistent camera stage start failed.", ex);
            _actions.Visibility = Visibility.Visible;
        }
        finally
        {
            _startButton.IsEnabled = true;
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _lifecycle.Release(); }
    }

    private async Task StopCoreAsync()
    {
        _playing = false;
        var reader = _reader;
        var capture = _capture;
        _reader = null;
        _capture = null;
        if (reader is not null)
        {
            reader.FrameArrived -= OnFrameArrived;
            try { await reader.StopAsync(); }
            catch (Exception ex) { StartupDiagnostics.Log("Camera stage reader stop failed.", ex); }
            try { reader.Dispose(); } catch { }
        }
        try { capture?.Dispose(); } catch { }
        _image.Source = null;
        _status.Visibility = Visibility.Visible;
        _actions.Visibility = Visibility.Visible;
        _stopButton.Visibility = Visibility.Collapsed;
        Interlocked.Exchange(ref _frameQueued, 0);
        StartupDiagnostics.Log("Camera capture released.");
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (!_playing || !ReferenceEquals(sender, _reader))
            return;

        var tick = Environment.TickCount64;
        if (tick - Interlocked.Read(ref _lastFrameTick) < 125 ||
            Interlocked.CompareExchange(ref _frameQueued, 1, 0) != 0)
            return;
        Interlocked.Exchange(ref _lastFrameTick, tick);

        SoftwareBitmap? copy = null;
        try
        {
            using var frame = sender.TryAcquireLatestFrame();
            var source = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (source is null)
            {
                Interlocked.Exchange(ref _frameQueued, 0);
                return;
            }

            copy = SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var retained = copy;
            if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (_playing && _bitmap is not null)
                    {
                        await _bitmap.SetBitmapAsync(retained);
                        _status.Visibility = Visibility.Collapsed;
                    }
                }
                catch (Exception ex)
                {
                    StartupDiagnostics.Log("Camera stage frame update failed.", ex);
                }
                finally
                {
                    retained.Dispose();
                    Interlocked.Exchange(ref _frameQueued, 0);
                }
            }))
            {
                copy.Dispose();
                Interlocked.Exchange(ref _frameQueued, 0);
            }
        }
        catch (Exception ex)
        {
            try { copy?.Dispose(); } catch { }
            Interlocked.Exchange(ref _frameQueued, 0);
            StartupDiagnostics.Log("Camera stage frame acquisition failed.", ex);
        }
    }
}
