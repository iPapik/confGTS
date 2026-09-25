using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using WinRT.Interop;

namespace ConfGTS.Server.Settings;

public sealed class SettingsWindow : Window
{
    private readonly ComboBox _bindBox = new();
    private readonly TextBox _portBox = new();
    private readonly TextBox _nameBox = new();
    private readonly ComboBox _schemeBox = new();
    private readonly TextBlock _publicUrl = new();
    private readonly TextBlock _dnsStatus = new();
    private readonly TextBlock _serviceStatus = new();
    private readonly Button _saveButton = new();

    private const string Navy = "#0B2F5B";
    private const string Blue = "#168CB8";
    private const string Teal = "#16B6C2";
    private const string Text = "#28445F";
    private const string Muted = "#6A7E93";
    private const string Line = "#CFE0EA";
    private const string Bg = "#EEF7FC";

    public SettingsWindow()
    {
        Diagnostics.Log("Constructing programmatic SettingsWindow");
        Title = "ConfGTS Server Settings";
        SetSize(980, 760);
        Content = BuildUi();
        LoadValues();
        _ = RefreshServiceAsync();
    }

    private UIElement BuildUi()
    {
        var root = new Grid { Background = Brush(Bg) };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var outer = new StackPanel
        {
            Padding = new Thickness(34),
            Spacing = 18,
            MaxWidth = 980,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());

        var mark = new Border
        {
            Width = 72,
            Height = 72,
            CornerRadius = new CornerRadius(20),
            Background = Gradient(),
            Margin = new Thickness(0, 0, 18, 0)
        };
        mark.Child = new TextBlock
        {
            Text = "ГТС",
            Foreground = Brush("#FFFFFF"),
            FontWeight = FontWeights.Bold,
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(mark);

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        Grid.SetColumn(titles, 1);
        titles.Children.Add(new TextBlock
        {
            Text = "ConfGTS Server",
            FontSize = 31,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(Navy)
        });
        titles.Children.Add(new TextBlock
        {
            Text = "Настройка серверной части",
            FontSize = 15,
            Foreground = Brush(Muted)
        });
        header.Children.Add(titles);
        outer.Children.Add(header);

        outer.Children.Add(Card(BuildNetworkPanel()));
        outer.Children.Add(Card(BuildServicePanel()));

        var footer = new TextBlock
        {
            Text = "ConfGTS Server Settings 0.17.0  |  Городские тепловые сети",
            Foreground = Brush("#8194A7"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12)
        };
        outer.Children.Add(footer);

        scroll.Content = outer;
        root.Children.Add(scroll);
        return root;
    }

    private UIElement BuildNetworkPanel()
    {
        var panel = new StackPanel { Spacing = 11 };
        panel.Children.Add(SectionTitle("Сетевое подключение"));
        panel.Children.Add(Hint("Сервер может слушать конкретный IPv4-адрес или все сетевые интерфейсы. Для подключения по имени используйте имя компьютера/FQDN, зарегистрированное в DNS."));

        panel.Children.Add(Label("IP-адрес прослушивания"));
        _bindBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        _bindBox.MinHeight = 48;
        panel.Children.Add(_bindBox);

        panel.Children.Add(Label("Порт"));
        StyleInput(_portBox, "8090");
        panel.Children.Add(_portBox);

        panel.Children.Add(Label("Имя сервера / FQDN"));
        StyleInput(_nameBox, "confgts.teplo.local");
        _nameBox.TextChanged += (_, _) => UpdatePublicUrl();
        panel.Children.Add(_nameBox);

        panel.Children.Add(Label("Протокол"));
        _schemeBox.ItemsSource = new[] { "http", "https" };
        _schemeBox.MinHeight = 48;
        _schemeBox.SelectionChanged += (_, _) => UpdatePublicUrl();
        panel.Children.Add(_schemeBox);
        panel.Children.Add(Hint("При выборе HTTPS ConfGTS автоматически создаст серверный сертификат с DNS-именем и локальными IPv4-адресами, если собственный сертификат ещё не настроен."));

        panel.Children.Add(Label("Адрес для клиентов"));
        _publicUrl.FontSize = 16;
        _publicUrl.FontWeight = FontWeights.SemiBold;
        _publicUrl.Foreground = Brush(Navy);
        _publicUrl.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(_publicUrl);

        var dnsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var dnsButton = SecondaryButton("Проверить DNS");
        dnsButton.Click += async (_, _) => await CheckDnsAsync();
        dnsRow.Children.Add(dnsButton);
        _dnsStatus.VerticalAlignment = VerticalAlignment.Center;
        _dnsStatus.Foreground = Brush(Muted);
        _dnsStatus.TextWrapping = TextWrapping.Wrap;
        dnsRow.Children.Add(_dnsStatus);
        panel.Children.Add(dnsRow);

        _saveButton.Content = "Сохранить и перезапустить службу";
        _saveButton.Height = 52;
        _saveButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _saveButton.Background = Brush(Blue);
        _saveButton.Foreground = Brush("#FFFFFF");
        _saveButton.FontWeight = FontWeights.SemiBold;
        _saveButton.Click += async (_, _) => await SaveAsync();
        _saveButton.PointerEntered += (_, _) => _saveButton.Background = Brush(Navy);
        _saveButton.PointerExited += (_, _) => _saveButton.Background = Brush(Blue);
        panel.Children.Add(_saveButton);

        return panel;
    }

    private UIElement BuildServicePanel()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(SectionTitle("Служба и веб-интерфейс"));

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        statusRow.Children.Add(new Ellipse { Width = 11, Height = 11, Fill = Brush("#2CB968"), VerticalAlignment = VerticalAlignment.Center });
        _serviceStatus.Text = "Проверка службы…";
        _serviceStatus.Foreground = Brush(Text);
        _serviceStatus.VerticalAlignment = VerticalAlignment.Center;
        statusRow.Children.Add(_serviceStatus);
        panel.Children.Add(statusRow);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var restart = SecondaryButton("Перезапустить службу");
        restart.Click += async (_, _) =>
        {
            try
            {
                await ConfigManager.RestartServiceAsync();
                await RefreshServiceAsync();
            }
            catch (Exception ex) { await ShowAsync("Служба ConfGTS", ex.Message); }
        };
        buttons.Children.Add(restart);

        var open = SecondaryButton("Открыть веб-интерфейс");
        open.Click += (_, _) =>
        {
            var url = _publicUrl.Text.TrimEnd('/') + "/server-login";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        };
        buttons.Children.Add(open);
        panel.Children.Add(buttons);

        panel.Children.Add(Hint("Для работы по имени во всех подсетях рекомендуется A-запись в корпоративном DNS. Клиент 0.17.0 также умеет находить ConfGTS в своей локальной сети по логическому имени. Для автоматически созданного HTTPS-сертификата клиент использует доверие по отпечатку при первом подключении."));
        return panel;
    }

    private void LoadValues()
    {
        var cfg = ConfigManager.Load();
        _bindBox.ItemsSource = ConfigManager.LocalIPv4();
        _bindBox.SelectedItem = ConfigManager.LocalIPv4().Contains(cfg.BindAddress) ? cfg.BindAddress : "0.0.0.0";
        _portBox.Text = cfg.Port.ToString();
        _nameBox.Text = cfg.ServerName;
        _schemeBox.SelectedItem = cfg.Scheme;
        UpdatePublicUrl();
        _dnsStatus.Text = "Нажмите «Проверить DNS».";
    }

    private void UpdatePublicUrl()
    {
        var name = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = ConfigManager.SuggestedServerName();
        var scheme = _schemeBox.SelectedItem?.ToString() ?? "http";
        var port = int.TryParse(_portBox.Text, out var p) ? p : 8090;
        _publicUrl.Text = $"{scheme}://{name}:{port}";
    }

    private async Task CheckDnsAsync()
    {
        _dnsStatus.Text = "Проверка…";
        var result = await ConfigManager.ResolveNameAsync(_nameBox.Text);
        _dnsStatus.Text = result;
        _dnsStatus.Foreground = result.StartsWith("DNS:") ? Brush("#218B52") : Brush("#B25C28");
    }

    private async Task SaveAsync()
    {
        _saveButton.IsEnabled = false;
        try
        {
            if (!int.TryParse(_portBox.Text, out var port) || port < 1 || port > 65535)
                throw new InvalidOperationException("Порт должен быть от 1 до 65535.");

            var bind = _bindBox.SelectedItem?.ToString() ?? "0.0.0.0";
            var name = _nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = ConfigManager.SuggestedServerName();

            var scheme = _schemeBox.SelectedItem?.ToString() ?? "http";
            var publicUrl = $"{scheme}://{name}:{port}";
            ConfigManager.Save(new NetworkSettings(bind, port, name, scheme, publicUrl));

            _serviceStatus.Text = "Настройки сохранены. Перезапуск…";
            await ConfigManager.RestartServiceAsync();
            await RefreshServiceAsync();
            await CheckDnsAsync();
            await ShowAsync("ConfGTS Server", "Настройки сохранены. Служба перезапущена.");
        }
        catch (Exception ex)
        {
            Diagnostics.Log("Save/restart failed", ex);
            await ShowAsync("Ошибка сохранения", ex.Message);
        }
        finally
        {
            _saveButton.IsEnabled = true;
        }
    }

    private async Task RefreshServiceAsync()
    {
        _serviceStatus.Text = await ConfigManager.ServiceStatusAsync();
    }

    private async Task ShowAsync(string title, string text)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }

    private static Border Card(UIElement child) => new()
    {
        Background = Brush("#FFFFFF"),
        BorderBrush = Brush(Line),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(20),
        Padding = new Thickness(24),
        Child = child
    };

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 21,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brush(Navy)
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brush(Text),
        Margin = new Thickness(0, 3, 0, -3)
    };

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Brush(Muted),
        TextWrapping = TextWrapping.Wrap
    };

    private static void StyleInput(TextBox box, string placeholder)
    {
        box.PlaceholderText = placeholder;
        box.MinHeight = 48;
        box.FontSize = 16;
        box.Background = Brush("#FFFFFF");
        box.BorderBrush = Brush(Line);
        box.BorderThickness = new Thickness(1);
        box.CornerRadius = new CornerRadius(10);
    }

    private static Button SecondaryButton(string text) => new()
    {
        Content = text,
        MinHeight = 43,
        Padding = new Thickness(15, 9, 15, 9),
        Background = Brush("#E8F4F9"),
        Foreground = Brush(Navy),
        BorderBrush = Brush(Line),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10)
    };

    private static LinearGradientBrush Gradient()
    {
        var b = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };
        b.GradientStops.Add(new GradientStop { Color = Color(Blue), Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = Color(Teal), Offset = 1 });
        return b;
    }

    private void SetSize(int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow.GetFromWindowId(id)?.Resize(new SizeInt32(width, height));
    }

    private static SolidColorBrush Brush(string hex) => new(Color(hex));
    private static Windows.UI.Color Color(string hex)
    {
        var v = hex.TrimStart('#');
        var a = (byte)255;
        var offset = 0;
        if (v.Length == 8) { a = Convert.ToByte(v[..2], 16); offset = 2; }
        var r = Convert.ToByte(v.Substring(offset, 2), 16);
        var g = Convert.ToByte(v.Substring(offset + 2, 2), 16);
        var b = Convert.ToByte(v.Substring(offset + 4, 2), 16);
        return ColorHelper.FromArgb(a, r, g, b);
    }
}
