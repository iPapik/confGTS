using ConfGTS.Client.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using WinRT.Interop;

namespace ConfGTS.Client;

public sealed class MainWindow : Window
{
    private readonly ApiClient _api = new();

    private readonly Grid _loginView = new();
    private readonly Grid _dashboardView = new();
    private readonly TextBox _loginBox = new();
    private readonly PasswordBox _passwordBox = new();
    private readonly Button _loginButton = new();
    private readonly TextBlock _loginError = new();
    private readonly TextBlock _serverText = new();
    private readonly Ellipse _serverDot = new();
    private readonly TextBlock _welcomeName = new();
    private readonly StackPanel _roomsPanel = new();
    private readonly TextBlock _roomsEmptyText = new();

    public MainWindow()
    {
        StartupDiagnostics.Log("MainWindow C# UI construction started.");

        Title = "ConfGTS";
        ExtendsContentIntoTitleBar = false;
        SetWindowSize(1400, 900);

        Content = BuildRoot();
        StartupDiagnostics.Log("MainWindow C# UI construction completed.");

        _ = CheckServerAsync();
    }

    private UIElement BuildRoot()
    {
        var root = new Grid { Background = Brush("#EEF7FC") };
        BuildLoginView();
        BuildDashboardView();

        root.Children.Add(_loginView);
        root.Children.Add(_dashboardView);
        return root;
    }

    private void BuildLoginView()
    {
        _loginView.Visibility = Visibility.Visible;

        var background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1)
        };
        background.GradientStops.Add(new GradientStop { Color = Color("#DDF2FF"), Offset = 0 });
        background.GradientStops.Add(new GradientStop { Color = Color("#F9FCFF"), Offset = 0.55 });
        background.GradientStops.Add(new GradientStop { Color = Color("#D8EEF8"), Offset = 1 });
        _loginView.Background = background;

        var card = new Border
        {
            Width = 580,
            MaxHeight = 780,
            Padding = new Thickness(46, 38, 46, 34),
            Background = Brush("#FAFFFFFF"),
            BorderBrush = Brush("#D3E3EE"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var panel = new StackPanel { Spacing = 14 };

        var brandRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var logo = new Border
        {
            Width = 76,
            Height = 76,
            CornerRadius = new CornerRadius(21),
            Background = Brush("#137CAE")
        };
        logo.Child = new TextBlock
        {
            Text = "ГТС",
            FontSize = 23,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = Brush("#FFFFFF"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var brandText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(new TextBlock
        {
            Text = "ГТС",
            FontSize = 37,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = Brush("#092B56")
        });
        brandText.Children.Add(new TextBlock
        {
            Text = "Городские тепловые сети",
            FontSize = 14,
            Foreground = Brush("#65748C")
        });
        brandRow.Children.Add(logo);
        brandRow.Children.Add(brandText);
        panel.Children.Add(brandRow);

        panel.Children.Add(new TextBlock
        {
            Text = "ConfGTS",
            FontSize = 46,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = Brush("#092B56"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Сервис видеоконференцсвязи",
            FontSize = 18,
            Foreground = Brush("#65748C"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, -6, 0, 18)
        });

        panel.Children.Add(Label("Логин"));
        _loginBox.PlaceholderText = "Доменная учетная запись";
        _loginBox.FontSize = 17;
        _loginBox.MinHeight = 50;
        panel.Children.Add(_loginBox);

        panel.Children.Add(Label("Пароль"));
        _passwordBox.PlaceholderText = "Введите пароль";
        _passwordBox.FontSize = 17;
        _passwordBox.MinHeight = 50;
        panel.Children.Add(_passwordBox);

        _loginButton.Height = 55;
        _loginButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _loginButton.Background = Brush("#167FAD");
        _loginButton.Foreground = Brush("#FFFFFF");
        _loginButton.Content = new TextBlock
        {
            Text = "Войти",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        _loginButton.Click += LoginButton_Click;
        panel.Children.Add(_loginButton);

        var guest = new Button
        {
            Height = 48,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = "Войти как гость"
        };
        guest.Click += GuestButton_Click;
        panel.Children.Add(guest);

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = Brush("#D9E4F1"),
            Margin = new Thickness(0, 8, 0, 4)
        });

        var status = new Grid();
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _serverDot.Width = 12;
        _serverDot.Height = 12;
        _serverDot.Fill = Brush("#BAC5D1");
        _serverDot.VerticalAlignment = VerticalAlignment.Center;

        _serverText.Text = "Проверка сервера…";
        _serverText.FontSize = 15;
        _serverText.Foreground = Brush("#52637A");
        _serverText.VerticalAlignment = VerticalAlignment.Center;
        _serverText.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(_serverText, 1);

        var settings = new Button { Content = "⚙", Width = 48, Height = 44 };
        settings.Click += SettingsButton_Click;
        Grid.SetColumn(settings, 2);

        status.Children.Add(_serverDot);
        status.Children.Add(_serverText);
        status.Children.Add(settings);
        panel.Children.Add(status);

        _loginError.Foreground = Brush("#B42318");
        _loginError.TextWrapping = TextWrapping.Wrap;
        _loginError.Visibility = Visibility.Collapsed;
        panel.Children.Add(_loginError);

        panel.Children.Add(new TextBlock
        {
            Text = "ConfGTS 0.15.2  |  © ГТС, 2026",
            FontSize = 11,
            Foreground = Brush("#8A9BAC"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 7, 0, 0)
        });

        card.Child = panel;
        _loginView.Children.Add(card);
    }

    private void BuildDashboardView()
    {
        _dashboardView.Visibility = Visibility.Collapsed;
        _dashboardView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        _dashboardView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var sidebar = new Border
        {
            Background = Brush("#FFFFFF"),
            BorderBrush = Brush("#DDE8F2"),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        var sideGrid = new Grid { Padding = new Thickness(22) };
        sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sideGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var sideBrand = new StackPanel();
        sideBrand.Children.Add(new TextBlock
        {
            Text = "ГТС",
            FontSize = 30,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = Brush("#092B56")
        });
        sideBrand.Children.Add(new TextBlock { Text = "ConfGTS", Foreground = Brush("#65748C") });
        sideGrid.Children.Add(sideBrand);

        var nav = new StackPanel { Spacing = 8, Margin = new Thickness(0, 36, 0, 0) };
        Grid.SetRow(nav, 1);
        nav.Children.Add(NavButton("Главная"));
        nav.Children.Add(NavButton("Конференции"));
        nav.Children.Add(NavButton("Контакты"));
        var settings = NavButton("Настройки");
        settings.Click += SettingsButton_Click;
        nav.Children.Add(settings);
        sideGrid.Children.Add(nav);

        var footer = new StackPanel { Spacing = 10 };
        Grid.SetRow(footer, 2);
        footer.Children.Add(new TextBlock { Text = "●  Сервер доступен", Foreground = Brush("#31A85B") });
        var logout = new Button { Content = "Выйти", HorizontalAlignment = HorizontalAlignment.Stretch };
        logout.Click += LogoutButton_Click;
        footer.Children.Add(logout);
        sideGrid.Children.Add(footer);

        sidebar.Child = sideGrid;
        _dashboardView.Children.Add(sidebar);

        var scroll = new ScrollViewer();
        Grid.SetColumn(scroll, 1);
        var main = new StackPanel { Padding = new Thickness(30), Spacing = 18 };

        var hero = new Border
        {
            Background = Brush("#DCEFF8"),
            BorderBrush = Brush("#C9DDE9"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(32)
        };
        var heroText = new StackPanel();
        heroText.Children.Add(new TextBlock { Text = "Добро пожаловать,", FontSize = 26, Foreground = Brush("#52637A") });
        _welcomeName.Text = "Пользователь!";
        _welcomeName.FontSize = 48;
        _welcomeName.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        _welcomeName.Foreground = Brush("#092B56");
        heroText.Children.Add(_welcomeName);
        heroText.Children.Add(new TextBlock
        {
            Text = "Проводите встречи, общайтесь и работайте вместе с ConfGTS",
            FontSize = 19,
            Foreground = Brush("#52637A"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        });
        hero.Child = heroText;
        main.Children.Add(hero);

        var actions = new Grid { ColumnSpacing = 14 };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.Children.Add(ActionCard("Создать конференцию", "Начать встречу сейчас", 0));
        actions.Children.Add(ActionCard("Подключиться по коду", "Войти в конференцию", 1));
        actions.Children.Add(ActionCard("Запланировать", "Создать встречу заранее", 2));
        main.Children.Add(actions);

        var roomsCard = new Border
        {
            Background = Brush("#FFFFFF"),
            BorderBrush = Brush("#DDE8F2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(22)
        };
        var rooms = new StackPanel { Spacing = 10 };
        var roomsHeader = new Grid();
        roomsHeader.ColumnDefinitions.Add(new ColumnDefinition());
        roomsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        roomsHeader.Children.Add(new TextBlock
        {
            Text = "Предстоящие конференции",
            FontSize = 21,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("#092B56")
        });
        var refresh = new Button { Content = "Обновить" };
        refresh.Click += RefreshRoomsButton_Click;
        Grid.SetColumn(refresh, 1);
        roomsHeader.Children.Add(refresh);
        rooms.Children.Add(roomsHeader);

        _roomsPanel.Spacing = 8;
        rooms.Children.Add(_roomsPanel);

        _roomsEmptyText.Text = "Нет доступных конференций";
        _roomsEmptyText.Foreground = Brush("#73839A");
        rooms.Children.Add(_roomsEmptyText);

        roomsCard.Child = rooms;
        main.Children.Add(roomsCard);

        scroll.Content = main;
        _dashboardView.Children.Add(scroll);
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = Brush("#52637A"),
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(0, 3, 0, -6)
    };

    private static Button NavButton(string text) => new()
    {
        Content = text,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(14, 10, 14, 10)
    };

    private static Border ActionCard(string title, string subtitle, int column)
    {
        var border = new Border
        {
            Background = Brush("#FFFFFF"),
            BorderBrush = Brush("#DDE8F2"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20),
            MinHeight = 105
        };
        Grid.SetColumn(border, column);
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush("#092B56")
        });
        panel.Children.Add(new TextBlock
        {
            Text = subtitle,
            Foreground = Brush("#73839A"),
            Margin = new Thickness(0, 6, 0, 0)
        });
        border.Child = panel;
        return border;
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(string hex) =>
        new(Color(hex));

    private static Windows.UI.Color Color(string hex)
    {
        var value = hex.TrimStart('#');
        byte a = 255;
        int offset = 0;
        if (value.Length == 8)
        {
            a = Convert.ToByte(value.Substring(0, 2), 16);
            offset = 2;
        }
        var r = Convert.ToByte(value.Substring(offset, 2), 16);
        var g = Convert.ToByte(value.Substring(offset + 2, 2), 16);
        var b = Convert.ToByte(value.Substring(offset + 4, 2), 16);
        return ColorHelper.FromArgb(a, r, g, b);
    }

    private void SetWindowSize(int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow.GetFromWindowId(id)?.Resize(new SizeInt32(width, height));
    }

    private async Task CheckServerAsync()
    {
        try
        {
            var ok = await _api.HealthAsync();
            _serverText.Text = ok ? "Сервер доступен" : "Сервер недоступен";
            _serverDot.Fill = Brush(ok ? "#31B657" : "#D14343");
        }
        catch
        {
            _serverText.Text = "Сервер недоступен";
            _serverDot.Fill = Brush("#D14343");
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        _loginError.Visibility = Visibility.Collapsed;
        _loginButton.IsEnabled = false;
        try
        {
            await _api.LoginAsync(_loginBox.Text.Trim(), _passwordBox.Password);
            _welcomeName.Text = string.IsNullOrWhiteSpace(_loginBox.Text)
                ? "Пользователь!"
                : _loginBox.Text.Trim() + "!";
            _loginView.Visibility = Visibility.Collapsed;
            _dashboardView.Visibility = Visibility.Visible;
            await LoadRoomsAsync();
        }
        catch (Exception ex)
        {
            _loginError.Text = "Не удалось войти: " + ex.Message;
            _loginError.Visibility = Visibility.Visible;
        }
        finally
        {
            _loginButton.IsEnabled = true;
        }
    }

    private void GuestButton_Click(object sender, RoutedEventArgs e)
    {
        _welcomeName.Text = "Гость!";
        _loginView.Visibility = Visibility.Collapsed;
        _dashboardView.Visibility = Visibility.Visible;
        _roomsPanel.Children.Clear();
        _roomsEmptyText.Visibility = Visibility.Visible;
    }

    private async void RefreshRoomsButton_Click(object sender, RoutedEventArgs e) => await LoadRoomsAsync();

    private async Task LoadRoomsAsync()
    {
        try
        {
            var rooms = await _api.RoomsAsync();
            _roomsPanel.Children.Clear();
            foreach (var room in rooms)
            {
                var card = new Border
                {
                    Background = Brush("#FBFDFF"),
                    BorderBrush = Brush("#DDE8F2"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(14)
                };
                var p = new StackPanel();
                p.Children.Add(new TextBlock
                {
                    Text = room.Name,
                    FontSize = 16,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = Brush("#092B56")
                });
                p.Children.Add(new TextBlock { Text = room.Id, Foreground = Brush("#73839A") });
                card.Child = p;
                _roomsPanel.Children.Add(card);
            }
            _roomsEmptyText.Visibility = rooms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            _roomsPanel.Children.Clear();
            _roomsEmptyText.Visibility = Visibility.Visible;
        }
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        _dashboardView.Visibility = Visibility.Collapsed;
        _loginView.Visibility = Visibility.Visible;
        _passwordBox.Password = "";
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var box = new TextBox
        {
            Text = _api.BaseUrl,
            Header = "Адрес сервера",
            PlaceholderText = "https://confgts.company.local"
        };

        var dialog = new ContentDialog
        {
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
            Title = "Настройки подключения",
            Content = box,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (Uri.TryCreate(box.Text.Trim(), UriKind.Absolute, out _))
            {
                _api.BaseUrl = box.Text.Trim();
                await CheckServerAsync();
            }
        }
    }
}
