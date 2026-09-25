using System.Text.Json;
using ConfGTS.Client.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace ConfGTS.Client;

public sealed class MainWindow : Window
{
    private const string Navy = "#0B2F5B";
    private const string Blue = "#168CB8";
    private const string Cyan = "#16B6C2";
    private const string Text = "#28445F";
    private const string Muted = "#6A7E93";
    private const string Line = "#CFE0EA";
    private const string Background = "#EEF7FC";

    private readonly ApiClient _api = new();

    private readonly Grid _loginView = new();
    private readonly Grid _dashboardView = new();
    private readonly TextBox _loginBox = new();
    private readonly PasswordBox _passwordBox = new();
    private readonly Button _loginButton = new();
    private readonly TextBlock _loginError = new();
    private readonly TextBlock _serverText = new();
    private readonly Ellipse _serverDot = new();

    private readonly StackPanel _contactsPanel = new();
    private readonly StackPanel _conferenceSidebarPanel = new();
    private readonly StackPanel _mainConferencePanel = new();
    private readonly TextBlock _dashboardServerText = new();

    private bool _passwordVisible;
    private bool _dashboardRefreshRunning;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _dashboardTimer;
    private readonly Grid _mainContentHost = new();
    private ScrollViewer? _dashboardMainScroll;
    private MediaSettingsPanel? _mediaSettingsPanel;
    private WebView2? _conferenceWebView;
    private Grid? _conferenceHost;
    private string _activeRoomId = "";

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
        var root = new Grid { Background = Brush(Background) };
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
            Padding = new Thickness(46, 34, 46, 30),
            Background = Brush("#FCFFFFFF"),
            BorderBrush = Brush("#D3E3EE"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var panel = new StackPanel { Spacing = 12 };

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
            Background = Gradient()
        };
        logo.Child = new TextBlock
        {
            Text = "ГТС",
            FontSize = 23,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("#FFFFFF"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var brandText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        brandText.Children.Add(new TextBlock
        {
            Text = "ГТС",
            FontSize = 37,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(Navy)
        });
        brandText.Children.Add(new TextBlock
        {
            Text = "Городские тепловые сети",
            FontSize = 14,
            Foreground = Brush(Muted)
        });
        brandRow.Children.Add(logo);
        brandRow.Children.Add(brandText);
        panel.Children.Add(brandRow);

        panel.Children.Add(new TextBlock
        {
            Text = "Тепло наших сердец\nв ваших квартирах",
            FontSize = 30,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(Navy),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 38,
            Margin = new Thickness(0, 14, 0, 16)
        });

        panel.Children.Add(Label("Логин"));
        _loginBox.PlaceholderText = "Доменная учетная запись";
        _loginBox.IsSpellCheckEnabled = false;
        _loginBox.IsTextPredictionEnabled = false;
        StyleLoginTextBox(_loginBox);
        _loginBox.KeyDown += LoginField_KeyDown;
        panel.Children.Add(_loginBox);

        panel.Children.Add(Label("Пароль"));
        _passwordBox.PlaceholderText = "Введите пароль";
        _passwordBox.PasswordRevealMode = PasswordRevealMode.Hidden;
        StyleLoginPasswordBox(_passwordBox);
        _passwordBox.KeyDown += LoginField_KeyDown;

        var passwordHost = new Grid();
        passwordHost.Children.Add(_passwordBox);

        var revealButton = new Button
        {
            Width = 38,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
            Padding = new Thickness(0),
            Background = Brush("#00FFFFFF"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Content = new FontIcon
            {
                Glyph = "\uE890",
                FontSize = 16,
                Foreground = Brush("#5D7891")
            }
        };
        revealButton.Resources["ButtonBackground"] = Brush("#00FFFFFF");
        revealButton.Resources["ButtonBackgroundPointerOver"] = Brush("#E8F4F9");
        revealButton.Resources["ButtonBackgroundPressed"] = Brush("#DCEEF6");
        revealButton.Click += (_, _) =>
        {
            _passwordVisible = !_passwordVisible;
            _passwordBox.PasswordRevealMode = _passwordVisible
                ? PasswordRevealMode.Visible
                : PasswordRevealMode.Hidden;
        };
        ToolTipService.SetToolTip(revealButton, "Показать или скрыть пароль");
        passwordHost.Children.Add(revealButton);
        panel.Children.Add(passwordHost);

        _loginButton.Height = 54;
        _loginButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _loginButton.Background = Brush(Blue);
        _loginButton.Foreground = Brush("#FFFFFF");
        _loginButton.BorderThickness = new Thickness(0);
        _loginButton.CornerRadius = new CornerRadius(10);
        _loginButton.Resources["ButtonBackground"] = Brush(Blue);
        _loginButton.Resources["ButtonBackgroundPointerOver"] = Brush(Navy);
        _loginButton.Resources["ButtonBackgroundPressed"] = Brush("#0F6F98");
        _loginButton.Resources["ButtonForeground"] = Brush("#FFFFFF");
        _loginButton.Resources["ButtonForegroundPointerOver"] = Brush("#FFFFFF");
        _loginButton.Resources["ButtonForegroundPressed"] = Brush("#FFFFFF");
        _loginButton.Content = new TextBlock
        {
            Text = "Войти",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        };
        _loginButton.Click += LoginButton_Click;
        panel.Children.Add(_loginButton);

        panel.Children.Add(new Border
        {
            Height = 1,
            Background = Brush(Line),
            Margin = new Thickness(0, 24, 0, 6)
        });

        var status = new Grid();
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.ColumnDefinitions.Add(new ColumnDefinition());
        status.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _serverDot.Width = 12;
        _serverDot.Height = 12;
        _serverDot.Fill = Brush("#BAC5D1");
        _serverDot.VerticalAlignment = VerticalAlignment.Center;

        _serverText.Text = "Проверка сервера…";
        _serverText.FontSize = 15;
        _serverText.Foreground = Brush(Text);
        _serverText.VerticalAlignment = VerticalAlignment.Center;
        _serverText.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(_serverText, 1);

        var settings = IconButton("\uE713", "Настройки подключения");
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
            Text = "Версия 0.18.0 beta  |  © ГТС, 2026",
            FontSize = 11,
            Foreground = Brush("#8A9BAC"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        });

        card.Child = panel;
        _loginView.Children.Add(card);
    }

    private void BuildDashboardView()
    {
        _dashboardView.Visibility = Visibility.Collapsed;
        _dashboardView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        _dashboardView.ColumnDefinitions.Add(new ColumnDefinition());

        var sidebar = new Border
        {
            Background = Brush("#FFFFFF"),
            BorderBrush = Brush(Line),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };

        var sideGrid = new Grid { Padding = new Thickness(22, 20, 22, 18) };
        sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sideGrid.RowDefinitions.Add(new RowDefinition());
        sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        sideGrid.Children.Add(BuildSidebarBrand());

        var sidebarScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 24, 0, 14)
        };
        Grid.SetRow(sidebarScroll, 1);

        var sidebarContent = new StackPanel { Spacing = 12 };
        sidebarContent.Children.Add(SidebarSectionHeader("\uE787", "Конференции"));
        _conferenceSidebarPanel.Spacing = 6;
        sidebarContent.Children.Add(_conferenceSidebarPanel);

        sidebarContent.Children.Add(new Border
        {
            Height = 1,
            Background = Brush("#E2ECF2"),
            Margin = new Thickness(0, 8, 0, 4)
        });

        sidebarContent.Children.Add(SidebarSectionHeader("\uE716", "Контакты"));
        _contactsPanel.Spacing = 6;
        sidebarContent.Children.Add(_contactsPanel);

        sidebarScroll.Content = sidebarContent;
        sideGrid.Children.Add(sidebarScroll);

        var footer = new StackPanel { Spacing = 9 };
        Grid.SetRow(footer, 2);

        var deviceSettings = SidebarButton("\uE713", "Настройки устройств");
        deviceSettings.Click += MediaSettingsButton_Click;
        footer.Children.Add(deviceSettings);

        _dashboardServerText.Text = "●  Сервер доступен";
        _dashboardServerText.Foreground = Brush("#278E55");
        _dashboardServerText.FontSize = 12;
        _dashboardServerText.TextWrapping = TextWrapping.Wrap;
        footer.Children.Add(_dashboardServerText);

        var logout = SidebarButton("\uE8AC", "Выйти");
        logout.Click += LogoutButton_Click;
        footer.Children.Add(logout);

        sideGrid.Children.Add(footer);
        sidebar.Child = sideGrid;
        _dashboardView.Children.Add(sidebar);

        var mainScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _dashboardMainScroll = mainScroll;

        var main = new StackPanel
        {
            Padding = new Thickness(34),
            Spacing = 16
        };

        _mainConferencePanel.Spacing = 14;
        main.Children.Add(_mainConferencePanel);

        mainScroll.Content = main;
        _mainContentHost.Children.Add(mainScroll);
        Grid.SetColumn(_mainContentHost, 1);
        _dashboardView.Children.Add(_mainContentHost);

        RenderLoadingDashboard();
    }

    private UIElement BuildSidebarBrand()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12
        };

        var logo = new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(15),
            Background = Gradient()
        };
        logo.Child = new TextBlock
        {
            Text = "ГТС",
            Foreground = Brush("#FFFFFF"),
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = "ConfGTS",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(Navy)
        });
        text.Children.Add(new TextBlock
        {
            Text = "Городские тепловые сети",
            FontSize = 11,
            Foreground = Brush(Muted)
        });

        row.Children.Add(logo);
        row.Children.Add(text);
        return row;
    }

    private void RenderLoadingDashboard()
    {
        _contactsPanel.Children.Clear();
        _conferenceSidebarPanel.Children.Clear();
        _mainConferencePanel.Children.Clear();

        _contactsPanel.Children.Add(MutedText("Загрузка контактов…"));
        _conferenceSidebarPanel.Children.Add(MutedText("Загрузка конференций…"));
        _mainConferencePanel.Children.Add(Card(new TextBlock
        {
            Text = "Загрузка конференции…",
            Foreground = Brush(Muted),
            FontSize = 15
        }));
    }

    private async Task RefreshDashboardAsync()
    {
        if (_dashboardRefreshRunning || _dashboardView.Visibility != Visibility.Visible)
            return;

        _dashboardRefreshRunning = true;
        try
        {
            var roomsTask = _api.RoomsAsync();
            var contactsTask = _api.ContactsAsync();

            await Task.WhenAll(roomsTask, contactsTask);

            var rooms = (await roomsTask)
                .Where(r => r.Enabled)
                .ToList();
            var contacts = (await contactsTask)
                .OrderBy(c => c.EffectiveName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            RenderContacts(contacts);
            RenderConferences(rooms);
            RenderMainConference(rooms);

            _dashboardServerText.Text = "●  " + _api.EffectiveBaseUrl.Replace("http://", "").Replace("https://", "");
            _dashboardServerText.Foreground = Brush("#278E55");
        }
        catch (Exception ex)
        {
            _dashboardServerText.Text = "●  Ошибка обновления: " + ex.Message;
            _dashboardServerText.Foreground = Brush("#B54242");
        }
        finally
        {
            _dashboardRefreshRunning = false;
        }
    }

    private void RenderContacts(IReadOnlyList<ContactInfo> contacts)
    {
        _contactsPanel.Children.Clear();

        if (contacts.Count == 0)
        {
            _contactsPanel.Children.Add(MutedText("Пока нет известных контактов"));
            return;
        }

        foreach (var contact in contacts.Take(40))
        {
            var row = new Grid
            {
                Padding = new Thickness(5, 6, 5, 6)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());

            var initials = Initials(contact.EffectiveName);
            var avatar = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = Brush("#E1F2F7"),
                Margin = new Thickness(0, 0, 9, 0)
            };
            avatar.Child = new TextBlock
            {
                Text = initials,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(Blue),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(avatar);

            var details = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(details, 1);
            details.Children.Add(new TextBlock
            {
                Text = contact.EffectiveName,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(Text),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            details.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(contact.Email) ? contact.Username : contact.Email,
                FontSize = 10,
                Foreground = Brush(Muted),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            row.Children.Add(details);

            _contactsPanel.Children.Add(row);
        }
    }

    private void RenderConferences(IReadOnlyList<RoomInfo> rooms)
    {
        _conferenceSidebarPanel.Children.Clear();

        if (rooms.Count == 0)
        {
            _conferenceSidebarPanel.Children.Add(MutedText("Нет доступных конференций"));
            return;
        }

        foreach (var room in rooms)
        {
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10, 8, 10, 8),
                Background = Brush("#F5FAFD"),
                Foreground = Brush(Navy),
                BorderBrush = Brush("#D8E6EF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10)
            };
            ApplyButtonVisuals(button, "#F5FAFD", "#E7F4FA", "#D9EDF5", Navy);

            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(new TextBlock
            {
                Text = room.Name,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(Navy),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = room.Id,
                FontSize = 9,
                Foreground = Brush(Muted),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            button.Content = stack;
            button.Click += async (_, _) =>
            {
                await CloseInlineSettingsAsync();
                await LeaveConferenceAsync(false);
                if (_dashboardMainScroll is not null)
                    _dashboardMainScroll.Visibility = Visibility.Visible;
                RenderMainConference(new[] { room });
            };
            _conferenceSidebarPanel.Children.Add(button);
        }
    }

    private void RenderMainConference(IReadOnlyList<RoomInfo> rooms)
    {
        _mainConferencePanel.Children.Clear();

        var room = rooms.FirstOrDefault(r =>
                       r.Name.Equals("Общая конференция", StringComparison.OrdinalIgnoreCase))
                   ?? rooms.FirstOrDefault();

        if (room is null)
        {
            _mainConferencePanel.Children.Add(Card(new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "Постоянная конференция не найдена",
                        FontSize = 22,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = Brush(Navy)
                    },
                    new TextBlock
                    {
                        Text = "Проверьте конференции на сервере ConfGTS.",
                        Foreground = Brush(Muted),
                        Margin = new Thickness(0, 6, 0, 0)
                    }
                }
            }));
            return;
        }

        var conferenceCard = new Border
        {
            Background = Brush("#FFFFFF"),
            BorderBrush = Brush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(30),
            MinHeight = 190
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Border
        {
            Width = 78,
            Height = 78,
            CornerRadius = new CornerRadius(20),
            Background = Gradient(),
            Margin = new Thickness(0, 0, 22, 0)
        };
        icon.Child = new FontIcon
        {
            Glyph = "\uE714",
            FontSize = 32,
            Foreground = Brush("#FFFFFF"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(icon);

        var detail = new StackPanel
        {
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(detail, 1);
        detail.Children.Add(new TextBlock
        {
            Text = room.Name,
            FontSize = 30,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(Navy)
        });
        detail.Children.Add(new TextBlock
        {
            Text = "Постоянная конференция",
            FontSize = 16,
            Foreground = Brush(Blue)
        });
        if (!string.IsNullOrWhiteSpace(room.Description))
        {
            detail.Children.Add(new TextBlock
            {
                Text = room.Description,
                FontSize = 14,
                Foreground = Brush(Muted),
                TextWrapping = TextWrapping.Wrap
            });
        }
        detail.Children.Add(new TextBlock
        {
            Text = "Идентификатор: " + room.Id,
            FontSize = 12,
            Foreground = Brush(Muted)
        });
        detail.Children.Add(new TextBlock
        {
            Text = "●  Конференция доступна",
            FontSize = 13,
            Foreground = Brush("#278E55"),
            Margin = new Thickness(0, 5, 0, 0)
        });

        grid.Children.Add(detail);

        var join = PrimaryActionButton("Подключиться");
        join.MinWidth = 170;
        join.VerticalAlignment = VerticalAlignment.Center;
        join.Margin = new Thickness(24, 0, 0, 0);
        join.Click += async (_, _) => await OpenConferenceAsync(room);
        Grid.SetColumn(join, 2);
        grid.Children.Add(join);

        conferenceCard.Child = grid;
        _mainConferencePanel.Children.Add(conferenceCard);
    }

    private async Task OpenConferenceAsync(RoomInfo room)
    {
        await CloseInlineSettingsAsync();
        await LeaveConferenceAsync(false);

        if (_dashboardMainScroll is not null)
            _dashboardMainScroll.Visibility = Visibility.Collapsed;

        _activeRoomId = room.Id;

        var host = new Grid
        {
            Background = Brush(Background),
            Padding = new Thickness(20)
        };
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition());

        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel { Spacing = 2 };
        titlePanel.Children.Add(new TextBlock
        {
            Text = room.Name,
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(Navy)
        });
        var status = new TextBlock
        {
            Text = "Подключение к конференции…",
            FontSize = 12,
            Foreground = Brush(Muted)
        };
        titlePanel.Children.Add(status);
        header.Children.Add(titlePanel);

        var leave = new Button
        {
            Content = "Выйти из конференции",
            MinHeight = 42,
            Padding = new Thickness(16, 9, 16, 9),
            Background = Brush("#FFF1F0"),
            Foreground = Brush("#A72E2E"),
            BorderBrush = Brush("#F3C7C4"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };
        ApplyButtonVisuals(leave, "#FFF1F0", "#FDE3E1", "#F9D2CF", "#A72E2E");
        leave.Click += async (_, _) => await LeaveConferenceAsync(true);
        Grid.SetColumn(leave, 1);
        header.Children.Add(leave);
        host.Children.Add(header);

        var web = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        Grid.SetRow(web, 1);
        host.Children.Add(web);

        _conferenceHost = host;
        _conferenceWebView = web;
        _mainContentHost.Children.Add(host);

        try
        {
            var env = await CreateWebView2EnvironmentAsync();
            await web.EnsureCoreWebView2Async(env);
            if (web.CoreWebView2 is null)
                throw new InvalidOperationException("WebView2 Runtime не инициализирован.");

            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.PermissionRequested += ConferencePermissionRequested;
            web.CoreWebView2.ServerCertificateErrorDetected += ConferenceCertificateErrorDetected;

            var session = _api.GetSessionCookie();
            if (session is null || string.IsNullOrWhiteSpace(session.Value))
                throw new InvalidOperationException("Сессия авторизации отсутствует. Войдите в клиент повторно.");

            var server = new Uri(_api.EffectiveBaseUrl);
            var cookie = web.CoreWebView2.CookieManager.CreateCookie("vc_session", session.Value, server.Host, "/");
            cookie.IsHttpOnly = true;
            cookie.IsSecure = server.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
            web.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);

            web.NavigationCompleted += async (_, args) =>
            {
                if (!args.IsSuccess)
                {
                    status.Text = "Не удалось открыть конференцию: " + args.WebErrorStatus;
                    status.Foreground = Brush("#B54242");
                    return;
                }

                try
                {
                    await ConfigureConferenceDocumentAsync(web, room.Id);
                    status.Text = "Конференция открыта. Разрешите доступ к камере и микрофону, если Windows запросит разрешение.";
                    status.Foreground = Brush("#278E55");
                }
                catch (Exception ex)
                {
                    status.Text = "Ошибка входа в конференцию: " + ex.Message;
                    status.Foreground = Brush("#B54242");
                }
            };

            web.Source = new Uri(_api.EffectiveBaseUrl.TrimEnd('/') + "/app");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Conference WebView2 startup failed.", ex);
            status.Text = "Не удалось запустить конференцию: " + FriendlyWebView2Error(ex);
            status.Foreground = Brush("#B54242");
        }
    }

    private static async Task<CoreWebView2Environment> CreateWebView2EnvironmentAsync()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            StartupDiagnostics.Log("WebView2 Runtime detected: " + version);
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("WebView2 Runtime discovery failed.", ex);
        }

        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConfGTS",
            "WebView2");
        Directory.CreateDirectory(userData);

        try
        {
            return await CoreWebView2Environment.CreateAsync(null, userData);
        }
        catch (Exception ex) when (IsMissingWebView2Runtime(ex))
        {
            throw new InvalidOperationException(
                "Microsoft Edge WebView2 Runtime не установлен или поврежден. " +
                "Установите WebView2 Runtime и перезапустите ConfGTS.", ex);
        }
    }

    private static bool IsMissingWebView2Runtime(Exception ex)
    {
        const int HResultFileNotFound = unchecked((int)0x80070002);
        const int HResultPathNotFound = unchecked((int)0x80070003);
        return ex is FileNotFoundException ||
               ex is DllNotFoundException ||
               ex.HResult == HResultFileNotFound ||
               ex.HResult == HResultPathNotFound ||
               ex.Message.Contains("WebView2", StringComparison.OrdinalIgnoreCase);
    }

    private static string FriendlyWebView2Error(Exception ex)
    {
        if (IsMissingWebView2Runtime(ex))
            return "Microsoft Edge WebView2 Runtime не найден. Переустановите клиент ConfGTS или установите WebView2 Runtime.";

        return ex.Message;
    }

    private async Task ConfigureConferenceDocumentAsync(WebView2 web, string roomId)
    {
        var roomJson = JsonSerializer.Serialize(roomId);
        var script = $$"""
        (() => {
          const nativeStyle = document.createElement('style');
          nativeStyle.id = 'confgts-native-shell-style';
          nativeStyle.textContent = `
            .topbar, .client-left, .home-video, .hero { display:none !important; }
            .client-shell { display:block !important; min-height:100vh !important; }
            .client-main { padding:0 !important; width:100% !important; max-width:none !important; }
            #conference { margin:0 !important; padding:0 !important; }
            body { background:#EEF7FC !important; overflow:auto !important; }
            .video-grid { min-height:420px !important; }
          `;
          document.getElementById(nativeStyle.id)?.remove();
          document.head.appendChild(nativeStyle);

          const roomId = {{roomJson}};
          if (typeof selectRoom !== 'function' || typeof joinRoom !== 'function') {
            throw new Error('Серверная WebRTC-страница не содержит функций конференции.');
          }

          selectRoom(roomId)
            .then(() => joinRoom())
            .catch(err => {
              console.error('ConfGTS native conference join failed', err);
              const box = document.createElement('div');
              box.style.cssText = 'margin:20px;padding:16px;border-radius:12px;background:#fff1f0;color:#a72e2e';
              box.textContent = 'Не удалось войти в конференцию: ' + (err?.message || err);
              document.body.prepend(box);
            });
        })();
        """;
        await web.ExecuteScriptAsync(script);
    }

    private async Task LeaveConferenceAsync(bool showDashboard)
    {
        var web = _conferenceWebView;
        var host = _conferenceHost;

        _conferenceWebView = null;
        _conferenceHost = null;
        _activeRoomId = "";

        if (web?.CoreWebView2 is not null)
        {
            try
            {
                await web.ExecuteScriptAsync("if (typeof leaveRoom === 'function') { leaveRoom(); }");
            }
            catch
            {
            }
        }

        if (host is not null)
            _mainContentHost.Children.Remove(host);

        if (showDashboard && _dashboardMainScroll is not null)
        {
            _dashboardMainScroll.Visibility = Visibility.Visible;
            await RefreshDashboardAsync();
        }
    }

    private void ConferencePermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (!IsConferenceOrigin(e.Uri))
            return;

        if (e.PermissionKind == CoreWebView2PermissionKind.Camera ||
            e.PermissionKind == CoreWebView2PermissionKind.Microphone)
        {
            e.State = CoreWebView2PermissionState.Allow;
            e.SavesInProfile = false;
        }
    }

    private void ConferenceCertificateErrorDetected(object? sender, CoreWebView2ServerCertificateErrorDetectedEventArgs e)
    {
        if (IsConferenceOrigin(e.RequestUri))
            e.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
    }

    private bool IsConferenceOrigin(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            !Uri.TryCreate(_api.EffectiveBaseUrl, UriKind.Absolute, out var expected))
        {
            return false;
        }

        return string.Equals(candidate.Host, expected.Host, StringComparison.OrdinalIgnoreCase) &&
               candidate.Port == expected.Port;
    }

    private void StartDashboardTimer()
    {
        _dashboardTimer?.Stop();
        _dashboardTimer = DispatcherQueue.CreateTimer();
        _dashboardTimer.Interval = TimeSpan.FromSeconds(10);
        _dashboardTimer.Tick += async (_, _) => await RefreshDashboardAsync();
        _dashboardTimer.Start();
    }

    private void StopDashboardTimer()
    {
        _dashboardTimer?.Stop();
        _dashboardTimer = null;
    }

    private async Task CheckServerAsync()
    {
        try
        {
            var ok = await _api.HealthAsync();
            _serverText.Text = ok
                ? "Сервер доступен · " + _api.EffectiveBaseUrl.Replace("http://", "").Replace("https://", "")
                : "Сервер недоступен";
            _serverDot.Fill = Brush(ok ? "#31B657" : "#D14343");
        }
        catch
        {
            _serverText.Text = "Сервер недоступен";
            _serverDot.Fill = Brush("#D14343");
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e) =>
        await PerformLoginAsync();

    private async void LoginField_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        e.Handled = true;
        await PerformLoginAsync();
    }

    private async Task PerformLoginAsync()
    {
        if (!_loginButton.IsEnabled)
            return;

        _loginError.Visibility = Visibility.Collapsed;
        _loginButton.IsEnabled = false;

        try
        {
            await _api.LoginAsync(_loginBox.Text.Trim(), _passwordBox.Password);

            _loginView.Visibility = Visibility.Collapsed;
            _dashboardView.Visibility = Visibility.Visible;
            RenderLoadingDashboard();

            await RefreshDashboardAsync();
            StartDashboardTimer();
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

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        StopDashboardTimer();
        await CloseInlineSettingsAsync();
        await LeaveConferenceAsync(false);
        _dashboardView.Visibility = Visibility.Collapsed;
        _loginView.Visibility = Visibility.Visible;
        _passwordBox.Password = "";
    }

    private async void MediaSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ShowInlineSettingsAsync();
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Media settings failed to open.", ex);
            await ShowErrorDialogAsync("Настройки устройств", "Не удалось открыть настройки устройств: " + ex.Message);
        }
    }

    private async Task ShowInlineSettingsAsync()
    {
        await LeaveConferenceAsync(false);

        if (_mediaSettingsPanel is not null)
            return;

        if (_dashboardMainScroll is not null)
            _dashboardMainScroll.Visibility = Visibility.Collapsed;

        try
        {
            _mediaSettingsPanel = new MediaSettingsPanel();
            _mediaSettingsPanel.CloseRequested += async (_, _) => await CloseInlineSettingsAsync();
            _mainContentHost.Children.Add(_mediaSettingsPanel);
        }
        catch
        {
            _mediaSettingsPanel = null;
            if (_dashboardMainScroll is not null)
                _dashboardMainScroll.Visibility = Visibility.Visible;
            throw;
        }
    }

    private async Task CloseInlineSettingsAsync()
    {
        var panel = _mediaSettingsPanel;
        if (panel is null)
        {
            if (_dashboardMainScroll is not null)
                _dashboardMainScroll.Visibility = Visibility.Visible;
            return;
        }

        _mediaSettingsPanel = null;
        await panel.ShutdownAsync();
        _mainContentHost.Children.Remove(panel);

        if (_dashboardMainScroll is not null)
            _dashboardMainScroll.Visibility = Visibility.Visible;
    }

    private async Task ShowErrorDialogAsync(string title, string message)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = (Content as FrameworkElement)?.XamlRoot,
                Title = title,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                CloseButtonText = "Закрыть"
            };
            await dialog.ShowAsync();
        }
        catch
        {
        }
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var box = new TextBox
        {
            Text = _api.BaseUrl,
            Header = "Имя сервера или адрес",
            PlaceholderText = "confgts, confgts.teplo.local:8090 или https://192.168.111.10:8090",
            MinWidth = 430
        };
        StyleLoginTextBox(box);

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(box);
        panel.Children.Add(new TextBlock
        {
            Text = "Для HTTPS ConfGTS может использовать автоматически созданный сертификат. " +
                   "Клиент запоминает его отпечаток при первом успешном подключении.",
            Foreground = Brush(Muted),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = (Content as FrameworkElement)?.XamlRoot,
            Title = "Настройки подключения",
            Content = panel,
            PrimaryButtonText = "Сохранить",
            SecondaryButtonText = "Сбросить доверие HTTPS",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                _api.BaseUrl = box.Text.Trim();
                await CheckServerAsync();
            }
            catch (Exception ex)
            {
                _loginError.Text = "Некорректный адрес сервера: " + ex.Message;
                _loginError.Visibility = Visibility.Visible;
            }
        }
        else if (result == ContentDialogResult.Secondary)
        {
            _api.ForgetAllCertificateTrust();
            await CheckServerAsync();
        }
    }

    private static void StyleLoginTextBox(TextBox box)
    {
        var foreground = Brush("#173B5C");

        box.FontSize = 16;
        box.MinHeight = 42;
        box.Padding = new Thickness(12, 5, 12, 5);
        box.Background = Brush("#FFFFFF");
        box.Foreground = foreground;
        box.BorderBrush = Brush("#BFD5E3");
        box.BorderThickness = new Thickness(1);
        box.CornerRadius = new CornerRadius(10);
        box.VerticalContentAlignment = VerticalAlignment.Center;

        box.Resources["TextControlBackground"] = Brush("#FFFFFF");
        box.Resources["TextControlBackgroundPointerOver"] = Brush("#FFFFFF");
        box.Resources["TextControlBackgroundFocused"] = Brush("#FFFFFF");
        box.Resources["TextControlForeground"] = foreground;
        box.Resources["TextControlForegroundPointerOver"] = foreground;
        box.Resources["TextControlForegroundFocused"] = foreground;
        box.Resources["TextControlBorderBrush"] = Brush("#BFD5E3");
        box.Resources["TextControlBorderBrushPointerOver"] = Brush(Blue);
        box.Resources["TextControlBorderBrushFocused"] = Brush(Blue);
        box.Resources["TextControlPlaceholderForeground"] = Brush("#8194A7");
        box.Resources["TextControlPlaceholderForegroundPointerOver"] = Brush("#8194A7");
        box.Resources["TextControlPlaceholderForegroundFocused"] = Brush("#8194A7");
    }

    private static void StyleLoginPasswordBox(PasswordBox box)
    {
        var foreground = Brush("#173B5C");

        box.FontSize = 16;
        box.MinHeight = 42;
        box.Padding = new Thickness(12, 4, 48, 4);
        box.Background = Brush("#FFFFFF");
        box.Foreground = foreground;
        box.BorderBrush = Brush("#BFD5E3");
        box.BorderThickness = new Thickness(1);
        box.CornerRadius = new CornerRadius(10);
        box.VerticalContentAlignment = VerticalAlignment.Center;

        box.Resources["TextControlBackground"] = Brush("#FFFFFF");
        box.Resources["TextControlBackgroundPointerOver"] = Brush("#FFFFFF");
        box.Resources["TextControlBackgroundFocused"] = Brush("#FFFFFF");
        box.Resources["TextControlForeground"] = foreground;
        box.Resources["TextControlForegroundPointerOver"] = foreground;
        box.Resources["TextControlForegroundFocused"] = foreground;
        box.Resources["TextControlBorderBrush"] = Brush("#BFD5E3");
        box.Resources["TextControlBorderBrushPointerOver"] = Brush(Blue);
        box.Resources["TextControlBorderBrushFocused"] = Brush(Blue);
        box.Resources["TextControlPlaceholderForeground"] = Brush("#8194A7");
        box.Resources["TextControlPlaceholderForegroundPointerOver"] = Brush("#8194A7");
        box.Resources["TextControlPlaceholderForegroundFocused"] = Brush("#8194A7");
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Foreground = Brush("#52637A"),
        FontWeight = FontWeights.SemiBold,
        FontSize = 13,
        Margin = new Thickness(0, 3, 0, -4)
    };

    private static UIElement SidebarSectionHeader(string glyph, string text)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        row.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 14,
            Foreground = Brush(Navy)
        });
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(Navy),
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }

    private static Button SidebarButton(string glyph, string text)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 9, 12, 9),
            Background = Brush("#F3F9FC"),
            Foreground = Brush(Navy),
            BorderBrush = Brush("#D9E7EF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9)
        };

        button.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            Children =
            {
                new FontIcon { Glyph = glyph, FontSize = 14, Foreground = Brush(Blue) },
                new TextBlock { Text = text, Foreground = Brush(Navy), VerticalAlignment = VerticalAlignment.Center }
            }
        };
        ApplyButtonVisuals(button, "#F3F9FC", "#E4F2F8", "#D6EAF3", Navy);
        return button;
    }

    private static Button IconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Width = 44,
            Height = 40,
            Padding = new Thickness(0),
            Background = Brush("#00FFFFFF"),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = glyph, FontSize = 17, Foreground = Brush("#658098") }
        };
        ApplyButtonVisuals(button, "#00FFFFFF", "#E8F4F9", "#DCEEF6", "#658098");
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static Button PrimaryActionButton(string text)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 48,
            Padding = new Thickness(22, 11, 22, 11),
            Background = Brush(Blue),
            Foreground = Brush("#FFFFFF"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(11),
            FontWeight = FontWeights.SemiBold,
            FontSize = 15
        };
        ApplyButtonVisuals(button, Blue, Navy, "#0F6F98", "#FFFFFF");
        return button;
    }

    private static void ApplyButtonVisuals(Button button, string background, string pointerOver, string pressed, string foreground)
    {
        button.Resources["ButtonBackground"] = Brush(background);
        button.Resources["ButtonBackgroundPointerOver"] = Brush(pointerOver);
        button.Resources["ButtonBackgroundPressed"] = Brush(pressed);
        button.Resources["ButtonForeground"] = Brush(foreground);
        button.Resources["ButtonForegroundPointerOver"] = Brush(foreground);
        button.Resources["ButtonForegroundPressed"] = Brush(foreground);
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

    private static TextBlock MutedText(string text) => new()
    {
        Text = text,
        Foreground = Brush(Muted),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap
    };

    private static string Initials(string value)
    {
        var parts = (value ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length >= 2)
            return (parts[0][0].ToString() + parts[1][0]).ToUpperInvariant();

        if (parts.Length == 1 && parts[0].Length >= 2)
            return parts[0][..2].ToUpperInvariant();

        return "??";
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
}
