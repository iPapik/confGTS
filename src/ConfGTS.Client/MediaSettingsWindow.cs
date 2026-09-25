using ConfGTS.Client.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace ConfGTS.Client;

/// <summary>
/// Safe device-settings page.
///
/// The previous implementation loaded NAudio, MMDevice/COM and Windows MediaCapture
/// types into the settings page. On a subset of workstations an OEM multimedia
/// driver terminated the WinUI process as soon as the page type was created.
///
/// This page intentionally contains no audio/video enumeration code at all.
/// Device selection is delegated to the Windows settings pages. ConfGTS itself
/// obtains camera/microphone permission inside the conference WebView.
/// </summary>
public sealed class MediaSettingsPanel : Grid
{
    private const string Navy = "#0B2F5B";
    private const string Blue = "#168CB8";
    private const string Cyan = "#16B6C2";
    private const string Text = "#28445F";
    private const string Muted = "#6A7E93";
    private const string Line = "#CFE0EA";
    private const string Bg = "#EEF7FC";

    private readonly TextBlock _status = new();
    private bool _closed;

    public event EventHandler? CloseRequested;

    public MediaSettingsPanel()
    {
        StartupDiagnostics.Log("Safe MediaSettingsPanel constructor started.");
        Background = Brush(Bg);
        Children.Add(BuildUi());
        StartupDiagnostics.Log("Safe MediaSettingsPanel constructor completed.");
    }

    public Task InitializeAsync()
    {
        StartupDiagnostics.Log("Safe media settings initialized without loading hardware APIs.");
        _status.Text = "Настройки открыты. Драйверы камер и аудиоустройств ConfGTS на этой странице не загружает.";
        _status.Foreground = Brush("#278E55");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync()
    {
        if (_closed)
            return Task.CompletedTask;

        _closed = true;
        StartupDiagnostics.Log("Safe media settings closed.");
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

        _status.Text = "Открытие настроек…";
        _status.FontSize = 13;
        _status.Foreground = Brush(Muted);
        _status.TextWrapping = TextWrapping.Wrap;

        outer.Children.Add(new Border
        {
            Background = Brush("#F7FCFF"),
            BorderBrush = Brush("#C9E4EF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 12, 16, 12),
            Child = _status
        });

        outer.Children.Add(DeviceCard(
            "\uE720",
            "Микрофон и динамики",
            "Выбор устройства, уровень громкости и проверка звука выполняются штатными средствами Windows. " +
            "Так ConfGTS не загружает проблемные аудиодрайверы при открытии этой страницы.",
            ("Открыть параметры звука", "ms-settings:sound"),
            ("Разрешения микрофона", "ms-settings:privacy-microphone")));

        outer.Children.Add(DeviceCard(
            "\uE8B8",
            "Камера",
            "Камера открывается только непосредственно внутри конференции. На странице настроек камера не запускается.",
            ("Разрешения камеры", "ms-settings:privacy-webcam"),
            ("Bluetooth и устройства", "ms-settings:bluetooth")));

        outer.Children.Add(Card(BuildDiagnosticsPanel()));

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
        title.Children.Add(new TextBlock
        {
            Text = "Безопасный режим настроек ConfGTS",
            FontSize = 13,
            Foreground = Brush(Muted)
        });
        grid.Children.Add(title);

        var back = SecondaryButton("← К конференциям");
        back.VerticalAlignment = VerticalAlignment.Center;
        back.Click += (_, _) =>
        {
            StartupDiagnostics.Log("Media settings back button clicked.");
            CloseRequested?.Invoke(this, EventArgs.Empty);
        };
        Grid.SetColumn(back, 1);
        grid.Children.Add(back);

        return grid;
    }

    private UIElement DeviceCard(
        string glyph,
        string title,
        string description,
        (string Caption, string Uri) primary,
        (string Caption, string Uri) secondary)
    {
        var panel = new StackPanel { Spacing = 13 };

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

        panel.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = Brush(Text),
            LineHeight = 20
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };

        var first = PrimaryButton(primary.Caption);
        first.Click += async (_, _) => await OpenSystemSettingsAsync(primary.Uri);
        buttons.Children.Add(first);

        var second = SecondaryButton(secondary.Caption);
        second.Click += async (_, _) => await OpenSystemSettingsAsync(secondary.Uri);
        buttons.Children.Add(second);

        panel.Children.Add(buttons);
        return Card(panel);
    }

    private UIElement BuildDiagnosticsPanel()
    {
        var panel = new StackPanel { Spacing = 9 };
        panel.Children.Add(new TextBlock
        {
            Text = "Диагностика",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(Navy)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Журнал клиента:",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(Text)
        });
        panel.Children.Add(new TextBlock
        {
            Text = StartupDiagnostics.LogPath,
            FontSize = 12,
            Foreground = Brush(Muted),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Если приложение когда-либо завершится аварийно, этот файл позволяет определить последний успешно выполненный этап.",
            FontSize = 12,
            Foreground = Brush(Muted),
            TextWrapping = TextWrapping.Wrap
        });
        return panel;
    }

    private async Task OpenSystemSettingsAsync(string uri)
    {
        try
        {
            StartupDiagnostics.Log("Opening Windows settings: " + uri);
            var opened = await Launcher.LaunchUriAsync(new Uri(uri));
            _status.Text = opened
                ? "Системные параметры Windows открыты."
                : "Windows не смогла открыть выбранный раздел параметров.";
            _status.Foreground = Brush(opened ? "#278E55" : "#B25C28");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Failed to open Windows settings: " + uri, ex);
            _status.Text = "Не удалось открыть системные параметры: " + ex.Message;
            _status.Foreground = Brush("#B54242");
        }
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

    private static Button PrimaryButton(string text)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 44,
            Padding = new Thickness(16, 9, 16, 9),
            Background = Brush(Blue),
            Foreground = Brush("#FFFFFF"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(10),
            FontWeight = FontWeights.SemiBold
        };
        button.Resources["ButtonBackground"] = Brush(Blue);
        button.Resources["ButtonBackgroundPointerOver"] = Brush(Navy);
        button.Resources["ButtonBackgroundPressed"] = Brush("#0F6F98");
        button.Resources["ButtonForeground"] = Brush("#FFFFFF");
        button.Resources["ButtonForegroundPointerOver"] = Brush("#FFFFFF");
        button.Resources["ButtonForegroundPressed"] = Brush("#FFFFFF");
        return button;
    }

    private static Button SecondaryButton(string text)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 44,
            Padding = new Thickness(16, 9, 16, 9),
            Background = Brush("#E8F4F9"),
            Foreground = Brush(Navy),
            BorderBrush = Brush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };
        button.Resources["ButtonBackground"] = Brush("#E8F4F9");
        button.Resources["ButtonBackgroundPointerOver"] = Brush("#DCEEF6");
        button.Resources["ButtonBackgroundPressed"] = Brush("#CFE7F1");
        return button;
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
}
