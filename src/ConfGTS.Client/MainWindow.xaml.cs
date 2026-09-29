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
    private readonly CheckBox _rememberMeBox = new();

    private readonly StackPanel _contactsPanel = new();
    private readonly StackPanel _conferenceSidebarPanel = new();
    private readonly StackPanel _mainConferencePanel = new();
    private readonly TextBlock _dashboardServerText = new();

    private bool _passwordVisible;
    private bool _dashboardRefreshRunning;
    private int _dashboardFailureCount;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _dashboardTimer;
    private readonly Grid _mainContentHost = new();
    private readonly ColumnDefinition _dashboardSidebarColumn = new() { Width = new GridLength(330) };
    private Border? _dashboardSidebar;
    private ScrollViewer? _dashboardMainScroll;
    private MediaSettingsPanel? _mediaSettingsPanel;
    private WebView2? _conferenceWebView;
    private Grid? _conferenceHost;
    private bool _conferenceSidebarVisible;
    private bool _settingsOpenedFromConference;
    private bool _settingsConferenceSidebarVisible;
    private string _activeRoomId = "";

    public MainWindow()
    {
        StartupDiagnostics.Log("MainWindow C# UI construction started.");

        Title = "ConfGTS";
        ExtendsContentIntoTitleBar = false;
        SetWindowSize(1400, 900);

        Content = BuildRoot();
        LoadRememberedCredentials();
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
        _loginBox.PlaceholderText = "Логин";
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

        _rememberMeBox.Content = null;
        _rememberMeBox.MinWidth = 0;
        _rememberMeBox.Width = 20;
        _rememberMeBox.MaxWidth = 20;
        _rememberMeBox.Height = 20;
        _rememberMeBox.Padding = new Thickness(0);
        _rememberMeBox.Margin = new Thickness(0);
        _rememberMeBox.HorizontalAlignment = HorizontalAlignment.Left;

        // Keep the check mark and its caption in adjacent fixed/auto columns.
        // The default WinUI CheckBox template reserves additional horizontal space,
        // which previously pushed the visible caption far away from the square.
        var rememberRow = new Grid
        {
            ColumnSpacing = 7,
            Margin = new Thickness(0, 3, 0, 3),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        rememberRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        rememberRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rememberRow.Children.Add(_rememberMeBox);

        var rememberLabel = new TextBlock
        {
            Text = "Запомнить меня",
            FontSize = 13,
            Foreground = Brush(Text),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(rememberLabel, 1);
        rememberRow.Children.Add(rememberLabel);
        panel.Children.Add(rememberRow);

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
            Text = "Версия 0.18.11 beta  |  © ГТС, 2026",
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
        _dashboardView.ColumnDefinitions.Add(_dashboardSidebarColumn);
        _dashboardView.ColumnDefinitions.Add(new ColumnDefinition());

        var sidebar = new Border
        {
            Background = Brush("#FFFFFF"),
            BorderBrush = Brush(Line),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };

        _dashboardSidebar = sidebar;

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

        var logout = SidebarButton("\uE72B", "Выйти");
        logout.Click += LogoutButton_Click;
        footer.Children.Add(logout);

        _dashboardServerText.Text = "●  Сервер доступен";
        _dashboardServerText.Foreground = Brush("#278E55");
        _dashboardServerText.FontSize = 12;
        _dashboardServerText.TextWrapping = TextWrapping.Wrap;
        _dashboardServerText.Margin = new Thickness(2, 0, 0, 0);
        footer.Children.Add(_dashboardServerText);

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
        if (_dashboardRefreshRunning ||
            _dashboardView.Visibility != Visibility.Visible ||
            _conferenceHost is not null ||
            _mediaSettingsPanel is not null)
        {
            return;
        }

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

            _dashboardFailureCount = 0;
            _dashboardServerText.Text = "●  Подключено";
            _dashboardServerText.Foreground = Brush("#278E55");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Dashboard refresh failed.", ex);
            _dashboardFailureCount++;
            if (_dashboardFailureCount >= 3)
            {
                _dashboardServerText.Text = "●  Сервер недоступен";
                _dashboardServerText.Foreground = Brush("#B54242");
            }
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
        StopDashboardTimer();

        _dashboardServerText.Text = "●  Подключено";
        _dashboardServerText.Foreground = Brush("#278E55");

        if (_dashboardMainScroll is not null)
            _dashboardMainScroll.Visibility = Visibility.Collapsed;

        SetConferenceLayout(true);
        _activeRoomId = room.Id;

        var host = new Grid
        {
            Background = Brush(Background),
            Padding = new Thickness(8)
        };
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition());

        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };

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
            Text = "",
            FontSize = 12,
            Foreground = Brush("#B54242"),
            Visibility = Visibility.Collapsed
        };
        titlePanel.Children.Add(status);
        header.Children.Add(titlePanel);
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
            // Never let WebView2 use its default user-data folder next to the EXE.
            // ConfGTS is installed under Program Files, which is not writable by a
            // standard user and can make EnsureCoreWebView2Async fail or return
            // without an initialized CoreWebView2 instance.
            var server = new Uri(_api.EffectiveBaseUrl);
            var webViewDataFolder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ConfGTS",
                "WebView2",
                server.Host.Replace(':', '_') + "_" + server.Port);
            Directory.CreateDirectory(webViewDataFolder);

            CoreWebView2EnvironmentOptions? webViewOptions = null;
            if (server.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                var origin = server.GetLeftPart(UriPartial.Authority);
                webViewOptions = new CoreWebView2EnvironmentOptions
                {
                    AdditionalBrowserArguments =
                        "--unsafely-treat-insecure-origin-as-secure=" + origin +
                        " --autoplay-policy=no-user-gesture-required"
                };
                StartupDiagnostics.Log("Conference WebView will trust configured HTTP origin for media APIs: " + origin);
            }

            var webViewEnvironment = await CoreWebView2Environment.CreateWithOptionsAsync(
                browserExecutableFolder: null,
                userDataFolder: webViewDataFolder,
                options: webViewOptions);

            StartupDiagnostics.Log(
                "WebView2 environment created. Runtime=" +
                webViewEnvironment.BrowserVersionString +
                "; UserDataFolder=" + webViewDataFolder);

            await web.EnsureCoreWebView2Async(webViewEnvironment);
            if (web.CoreWebView2 is null)
                throw new InvalidOperationException(
                    "WebView2 не удалось инициализировать. Проверьте установку Microsoft Edge WebView2 Runtime.");

            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.PermissionRequested += ConferencePermissionRequested;
            web.CoreWebView2.ServerCertificateErrorDetected += ConferenceCertificateErrorDetected;
            web.CoreWebView2.WebMessageReceived += ConferenceWebMessageReceived;

            var session = _api.GetSessionCookie();
            if (session is null || string.IsNullOrWhiteSpace(session.Value))
                throw new InvalidOperationException("Сессия авторизации отсутствует. Войдите в клиент повторно.");

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
                    status.Visibility = Visibility.Visible;
                    return;
                }

                try
                {
                    await ConfigureConferenceDocumentAsync(web, room.Id);
                    status.Text = "";
                    status.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    status.Text = "Ошибка входа в конференцию: " + ex.Message;
                    status.Foreground = Brush("#B54242");
                    status.Visibility = Visibility.Visible;
                }
            };

            web.Source = new Uri(_api.EffectiveBaseUrl.TrimEnd('/') + "/app");
        }
        catch (Exception ex)
        {
            status.Text = "Не удалось запустить конференцию: " + ex.Message;
            status.Foreground = Brush("#B54242");
            status.Visibility = Visibility.Visible;
        }
    }

    private async Task ConfigureConferenceDocumentAsync(WebView2 web, string roomId)
    {
        var roomJson = JsonSerializer.Serialize(roomId);
        var media = MediaDeviceSettings.Load();
        var mediaJson = JsonSerializer.Serialize(new
        {
            microphoneName = media.MicrophoneName,
            speakerName = media.SpeakerName,
            cameraName = media.CameraName,
            microphoneEnabled = media.MicrophoneEnabled,
            speakerEnabled = media.SpeakerEnabled,
            cameraEnabled = media.CameraEnabled,
            microphoneVolume = Math.Clamp(media.MicrophoneVolume / 100.0, 0, 1),
            speakerVolume = Math.Clamp(media.SpeakerVolume / 100.0, 0, 1)
        });

        var script = """
        (() => {
          const nativeStyle = document.createElement('style');
          nativeStyle.id = 'confgts-native-shell-style';
          nativeStyle.textContent = `
            html, body {
              width:100% !important;
              height:100% !important;
              margin:0 !important;
              overflow:hidden !important;
              background:#111418 !important;
            }
            .topbar, .client-left, .home-video, .hero { display:none !important; }
            .client-shell, .client-main {
              display:block !important;
              width:100% !important;
              height:100vh !important;
              min-height:0 !important;
              max-width:none !important;
              margin:0 !important;
              padding:0 !important;
              overflow:hidden !important;
            }
            #conference {
              display:block !important;
              position:relative !important;
              width:100% !important;
              height:100vh !important;
              min-height:0 !important;
              margin:0 !important;
              padding:0 !important;
              overflow:hidden !important;
              background:#111418 !important;
            }
            #conference > .toolbar { display:none !important; }
            #chatPanel { display:none !important; }
            .video-grid {
              position:absolute !important;
              inset:0 !important;
              display:grid !important;
              grid-template-columns:repeat(auto-fit,minmax(min(420px,100%),1fr)) !important;
              grid-auto-rows:minmax(0,1fr) !important;
              gap:8px !important;
              width:auto !important;
              height:auto !important;
              min-height:0 !important;
              margin:0 !important;
              padding:8px 8px 88px !important;
              box-sizing:border-box !important;
              overflow:hidden !important;
              background:#111418 !important;
            }
            .video-tile {
              position:relative !important;
              width:100% !important;
              height:100% !important;
              min-width:0 !important;
              min-height:0 !important;
              max-width:none !important;
              max-height:none !important;
              margin:0 !important;
              border-radius:14px !important;
              overflow:hidden !important;
              background:#111418 !important;
            }
            .video-tile video {
              width:100% !important;
              height:100% !important;
              max-width:none !important;
              max-height:none !important;
              object-fit:contain !important;
              background:#111418 !important;
            }
            .controls {
              position:absolute !important;
              left:50% !important;
              bottom:18px !important;
              transform:translateX(-50%) !important;
              z-index:1000 !important;
              display:flex !important;
              align-items:center !important;
              justify-content:center !important;
              gap:10px !important;
              margin:0 !important;
              padding:9px 11px !important;
              border-radius:18px !important;
              background:rgba(9,31,55,.88) !important;
              box-shadow:0 10px 32px rgba(0,0,0,.28) !important;
              backdrop-filter:blur(12px) !important;
              white-space:nowrap !important;
            }
            .controls .btn {
              display:inline-flex !important;
              align-items:center !important;
              justify-content:center !important;
              min-width:108px !important;
              height:44px !important;
              margin:0 !important;
              padding:0 15px !important;
              border:1px solid #245781 !important;
              border-radius:12px !important;
              background:#0B3E72 !important;
              color:#fff !important;
              font:600 14px/1 "Segoe UI",sans-serif !important;
              box-shadow:none !important;
            }
            .controls .btn:hover { background:#15568F !important; }
            .controls .btn.native-disabled {
              background:#7A3440 !important;
              border-color:#A5525C !important;
            }
            .controls .btn.native-pending {
              background:#40556D !important;
              border-color:#60758B !important;
            }
            .controls .btn.native-settings {
              min-width:118px !important;
              background:#164C79 !important;
            }
            .controls .btn.native-participants {
              min-width:118px !important;
              background:#164C79 !important;
            }
            .controls .btn.native-hangup {
              min-width:108px !important;
              width:auto !important;
              height:44px !important;
              padding:0 15px !important;
              border-radius:12px !important;
              border:1px solid #E46B6B !important;
              background:#C93F48 !important;
              font-size:14px !important;
            }
            .controls .btn.native-hangup:hover { background:#B92F2F !important; }
            #confgts-native-participants {
              position:absolute;
              right:18px;
              bottom:82px;
              z-index:1100;
              width:min(330px,calc(100vw - 36px));
              max-height:min(55vh,460px);
              overflow:auto;
              padding:14px;
              border:1px solid #CFE0EA;
              border-radius:16px;
              background:#F7FCFF;
              color:#0B2F5B;
              box-shadow:0 14px 40px rgba(0,0,0,.25);
              font:14px/1.4 "Segoe UI",sans-serif;
            }
            #confgts-native-participants[hidden] { display:none !important; }
            .confgts-participants-title {
              display:flex;
              justify-content:space-between;
              align-items:center;
              margin-bottom:10px;
              font-size:16px;
              font-weight:700;
            }
            .confgts-participant-row {
              padding:9px 10px;
              margin-top:6px;
              border-radius:10px;
              background:#E8F4F9;
              color:#28445F;
            }
            #confgts-native-toast {
              position:absolute;
              top:16px;
              left:50%;
              transform:translateX(-50%);
              z-index:1200;
              max-width:min(720px,calc(100vw - 32px));
              padding:11px 16px;
              border-radius:12px;
              background:#FFF3D7;
              color:#8A5A00;
              box-shadow:0 8px 28px rgba(0,0,0,.18);
              font:600 13px/1.35 "Segoe UI",sans-serif;
            }
            #confgts-native-toast[hidden] { display:none !important; }
            @media (max-width:760px) {
              .controls {
                gap:6px !important;
                width:calc(100% - 18px) !important;
                box-sizing:border-box !important;
              }
              .controls .btn {
                min-width:0 !important;
                flex:1 1 auto !important;
                padding:0 8px !important;
                font-size:12px !important;
              }
              .controls .btn.native-hangup {
                flex:1 1 auto !important;
                width:auto !important;
              }
              .video-grid { padding:6px 6px 80px !important; }
            }
          `;
          document.getElementById(nativeStyle.id)?.remove();
          document.head.appendChild(nativeStyle);

          const roomId = __CONFGTS_ROOM_JSON__;
          let nativePrefs = __CONFGTS_MEDIA_JSON__;
          const emptyStream = () => new MediaStream();

          const findBrowserDevice = async (kind, wantedName) => {
            if (!wantedName || !navigator.mediaDevices?.enumerateDevices) return null;
            try {
              const devices = await navigator.mediaDevices.enumerateDevices();
              const normalized = String(wantedName).trim().toLocaleLowerCase();
              return devices.find(d => d.kind === kind && String(d.label || '').trim().toLocaleLowerCase() === normalized)
                  || devices.find(d => d.kind === kind && String(d.label || '').toLocaleLowerCase().includes(normalized))
                  || null;
            } catch {
              return null;
            }
          };

          const showNativeToast = (message) => {
            let toast = document.getElementById('confgts-native-toast');
            if (!toast) {
              toast = document.createElement('div');
              toast.id = 'confgts-native-toast';
              document.getElementById('conference')?.appendChild(toast);
            }
            toast.textContent = String(message || '');
            toast.hidden = false;
            clearTimeout(window.__confgtsToastTimer);
            window.__confgtsToastTimer = setTimeout(() => { toast.hidden = true; }, 4200);
          };

          const currentParticipants = () => {
            const ps = Array.isArray(roomState?.participants) ? roomState.participants : [];
            return ps.map(p => p?.display_name || p?.username || 'Участник').filter(Boolean);
          };

          const refreshParticipantsPanel = () => {
            const panel = document.getElementById('confgts-native-participants');
            if (!panel || panel.hidden) return;
            const list = panel.querySelector('[data-list]');
            const names = currentParticipants();
            list.innerHTML = names.length
              ? names.map(name => '<div class="confgts-participant-row"></div>').join('')
              : '<div class="confgts-participant-row">Нет участников</div>';
            if (names.length) {
              [...list.children].forEach((row, index) => { row.textContent = names[index]; });
            }
          };

          const syncMediaButtons = () => {
            const micBtn = document.getElementById('muteBtn');
            const camBtn = document.getElementById('camBtn');
            const micTrack = localStream?.getAudioTracks?.()[0];
            const camTrack = localStream?.getVideoTracks?.()[0];
            const mediaReady = window.__confgtsMediaReady === true;

            if (micBtn) {
              const configured = !!nativePrefs.microphoneEnabled;
              const enabled = !!micTrack?.enabled;
              micBtn.classList.toggle('native-pending', configured && !mediaReady);
              micBtn.classList.toggle('native-disabled', !configured || (mediaReady && !enabled));
              micBtn.textContent = enabled ? '🎙 Микрофон' : '🔇 Микрофон';
              micBtn.title = !configured
                ? 'Микрофон отключён в настройках'
                : (!mediaReady ? 'Проверка микрофона…' : (micTrack ? (enabled ? 'Выключить микрофон' : 'Включить микрофон') : 'Микрофон не найден'));
            }

            if (camBtn) {
              const configured = !!nativePrefs.cameraEnabled;
              const enabled = !!camTrack?.enabled;
              camBtn.classList.toggle('native-pending', configured && !mediaReady);
              camBtn.classList.toggle('native-disabled', !configured || (mediaReady && !enabled));
              camBtn.textContent = enabled ? '▣ Камера' : '▢ Камера';
              camBtn.title = !configured
                ? 'Камера отключена в настройках'
                : (!mediaReady ? 'Проверка камеры…' : (camTrack ? (enabled ? 'Выключить камеру' : 'Включить камеру') : 'Камера не найдена'));
            }
          };

          const installNativeControls = () => {
            const controls = document.querySelector('#conference .controls');
            if (!controls || controls.dataset.nativeReady === '1') return;
            controls.dataset.nativeReady = '1';

            const micBtn = document.getElementById('muteBtn');
            if (micBtn) {
              micBtn.onclick = () => {
                const track = localStream?.getAudioTracks?.()[0];
                if (!track) {
                  showNativeToast('Микрофон не найден или недоступен.');
                  syncMediaButtons();
                  return;
                }
                track.enabled = !track.enabled;
                syncMediaButtons();
              };
            }

            const camBtn = document.getElementById('camBtn');
            if (camBtn) {
              camBtn.onclick = () => {
                const track = localStream?.getVideoTracks?.()[0];
                if (!track) {
                  showNativeToast('Камера не найдена или недоступна.');
                  syncMediaButtons();
                  return;
                }
                track.enabled = !track.enabled;
                syncMediaButtons();
              };
            }

            const shareBtn = [...controls.querySelectorAll('button')].find(
              b => b !== micBtn && b !== camBtn && /Экран/i.test(b.textContent || '')
            );
            if (shareBtn) {
              shareBtn.textContent = '▣ Экран';
              shareBtn.onclick = async () => {
                try {
                  if (typeof shareScreen !== 'function') {
                    throw new Error('Демонстрация экрана недоступна в этой версии сервера.');
                  }
                  await shareScreen();
                } catch (e) {
                  console.error('ConfGTS screen share failed', e);
                  showNativeToast('Не удалось включить демонстрацию экрана: ' + (e?.message || e));
                }
              };
            }

            const settingsBtn = document.createElement('button');
            settingsBtn.type = 'button';
            settingsBtn.className = 'btn native-settings';
            settingsBtn.textContent = '⚙ Настройки';
            settingsBtn.title = 'Камера, микрофон, динамики и громкость';
            settingsBtn.onclick = () => {
              if (window.chrome?.webview) {
                window.chrome.webview.postMessage('open-media-settings');
              }
            };
            controls.appendChild(settingsBtn);

            const participantsBtn = document.createElement('button');
            participantsBtn.type = 'button';
            participantsBtn.className = 'btn native-participants';
            participantsBtn.textContent = '👥 Участники';
            participantsBtn.onclick = () => {
              const panel = document.getElementById('confgts-native-participants');
              if (!panel) return;
              panel.hidden = !panel.hidden;
              refreshParticipantsPanel();
            };
            controls.appendChild(participantsBtn);

            const hangupBtn = document.createElement('button');
            hangupBtn.type = 'button';
            hangupBtn.className = 'btn native-hangup';
            hangupBtn.title = 'Выйти из конференции';
            hangupBtn.setAttribute('aria-label', 'Выйти из конференции');
            hangupBtn.textContent = '☎ Выйти';
            hangupBtn.disabled = true;
            hangupBtn.onclick = async () => {
              try {
                if (typeof leaveRoom === 'function') {
                  await leaveRoom();
                }
              } catch (e) {
                console.error('ConfGTS conference leave failed', e);
              } finally {
                if (window.chrome?.webview) {
                  window.chrome.webview.postMessage('leave-conference');
                }
              }
            };
            controls.appendChild(hangupBtn);

            const participantPanel = document.createElement('div');
            participantPanel.id = 'confgts-native-participants';
            participantPanel.hidden = true;
            participantPanel.innerHTML = '<div class="confgts-participants-title"><span>Участники</span><span>×</span></div><div data-list></div>';
            participantPanel.querySelector('.confgts-participants-title span:last-child').style.cursor = 'pointer';
            participantPanel.querySelector('.confgts-participants-title span:last-child').onclick = () => {
              participantPanel.hidden = true;
            };
            document.getElementById('conference')?.appendChild(participantPanel);

            window.__confgtsParticipantsTimer && clearInterval(window.__confgtsParticipantsTimer);
            window.__confgtsParticipantsTimer = setInterval(refreshParticipantsPanel, 1500);
            syncMediaButtons();
          };

          const applyOutputSettings = async () => {
            const volume = nativePrefs.speakerEnabled ? Number(nativePrefs.speakerVolume ?? 0.7) : 0;
            let sinkId = '';
            if (nativePrefs.speakerEnabled && nativePrefs.speakerName) {
              const match = await findBrowserDevice('audiooutput', nativePrefs.speakerName);
              sinkId = match?.deviceId || '';
            }

            for (const mediaElement of document.querySelectorAll('video, audio')) {
              if (!mediaElement.muted) mediaElement.volume = Math.max(0, Math.min(1, volume));
              if (sinkId && typeof mediaElement.setSinkId === 'function') {
                try { await mediaElement.setSinkId(sinkId); } catch (e) { console.debug('ConfGTS setSinkId:', e); }
              }
            }
          };

          const nativeEnsureMedia = async () => {
            if (localStream) return localStream;

            const useAudio = !!nativePrefs.microphoneEnabled;
            const useVideo = !!nativePrefs.cameraEnabled;
            const devices = navigator.mediaDevices;

            // Lack of WebRTC capture API, a missing device, denied permission or
            // an insecure HTTP origin must never prevent joining the room.
            if ((!useAudio && !useVideo) || !devices || typeof devices.getUserMedia !== 'function') {
              localStream = emptyStream();
              window.__confgtsMediaReady = true;
              syncMediaButtons();
              console.info('ConfGTS: joining without local media.');
              return localStream;
            }

            try {
              let capture = await devices.getUserMedia({
                audio: useAudio ? { echoCancellation:true, noiseSuppression:true, autoGainControl:true } : false,
                video: useVideo ? true : false
              });

              // After the first permission grant Chromium exposes device labels.
              // Match the device names selected in native ConfGTS settings and
              // reacquire only when a matching browser device exists.
              try {
                const mic = useAudio ? await findBrowserDevice('audioinput', nativePrefs.microphoneName) : null;
                const cam = useVideo ? await findBrowserDevice('videoinput', nativePrefs.cameraName) : null;
                if ((mic && mic.deviceId) || (cam && cam.deviceId)) {
                  const exactCapture = await devices.getUserMedia({
                    audio: useAudio
                      ? {
                          ...(mic?.deviceId ? {deviceId:{exact:mic.deviceId}} : {}),
                          echoCancellation:true,
                          noiseSuppression:true,
                          autoGainControl:true
                        }
                      : false,
                    video: useVideo
                      ? (cam?.deviceId ? {deviceId:{exact:cam.deviceId}} : true)
                      : false
                  });
                  capture.getTracks().forEach(t => t.stop());
                  capture = exactCapture;
                }
              } catch (e) {
                console.debug('ConfGTS: selected device fallback to browser default:', e);
              }

              rawStream = capture;
              const tracks = [];
              capture.getVideoTracks().forEach(t => tracks.push(t));

              const audioTrack = capture.getAudioTracks()[0];
              if (audioTrack) {
                const gainValue = Math.max(0, Math.min(1, Number(nativePrefs.microphoneVolume ?? 1)));
                try {
                  const AudioCtx = window.AudioContext || window.webkitAudioContext;
                  if (AudioCtx) {
                    mediaAudioCtx = new AudioCtx();
                    const src = mediaAudioCtx.createMediaStreamSource(new MediaStream([audioTrack]));
                    const gain = mediaAudioCtx.createGain();
                    gain.gain.value = gainValue;
                    const dst = mediaAudioCtx.createMediaStreamDestination();
                    src.connect(gain).connect(dst);
                    dst.stream.getAudioTracks().forEach(t => tracks.push(t));
                  } else {
                    tracks.push(audioTrack);
                  }
                } catch (e) {
                  console.debug('ConfGTS microphone gain fallback:', e);
                  tracks.push(audioTrack);
                }
              }

              localStream = new MediaStream(tracks);
              window.__confgtsMediaReady = true;
              syncMediaButtons();
              await applyOutputSettings();
              return localStream;
            } catch (e) {
              console.warn('ConfGTS: media unavailable, joining without local media.', e);
              try { rawStream?.getTracks?.().forEach(t => t.stop()); } catch {}
              rawStream = null;
              localStream = emptyStream();
              window.__confgtsMediaReady = true;
              syncMediaButtons();
              return localStream;
            }
          };

          try {
            ensureMedia = nativeEnsureMedia;
          } catch {
            window.ensureMedia = nativeEnsureMedia;
          }

          // Apply settings saved by the native device-settings panel without
          // leaving the conference. Existing RTCPeerConnections stay alive; only
          // their outgoing microphone/camera tracks and audio output are changed.
          window.__confgtsApplyMediaSettings = async (nextPrefs) => {
            nativePrefs = { ...nativePrefs, ...(nextPrefs || {}) };

            const oldLocal = localStream;
            const oldRaw = rawStream;
            const oldAudioContext = mediaAudioCtx;

            localStream = null;
            rawStream = null;
            mediaAudioCtx = null;
            window.__confgtsMediaReady = false;
            syncMediaButtons();

            const nextStream = await nativeEnsureMedia();
            const screenTrack = window.__confgtsScreenTrack?.readyState === 'live'
              ? window.__confgtsScreenTrack
              : null;
            const videoTrack = screenTrack || nextStream?.getVideoTracks?.()[0] || null;
            const audioTrack = nextStream?.getAudioTracks?.()[0] || null;

            for (const [, pc] of peers) {
              const videoSender =
                pc.__confgtsVideoSender ||
                pc.getTransceivers?.().find(t => t.receiver?.track?.kind === 'video')?.sender ||
                pc.getSenders().find(s => s.track?.kind === 'video');
              const audioSender =
                pc.__confgtsAudioSender ||
                pc.getTransceivers?.().find(t => t.receiver?.track?.kind === 'audio')?.sender ||
                pc.getSenders().find(s => s.track?.kind === 'audio');

              try { if (videoSender) await videoSender.replaceTrack(videoTrack); } catch (e) { console.warn('ConfGTS video device switch', e); }
              try { if (audioSender) await audioSender.replaceTrack(audioTrack); } catch (e) { console.warn('ConfGTS audio device switch', e); }
            }

            try { oldLocal?.getTracks?.().forEach(t => t.stop()); } catch {}
            try { oldRaw?.getTracks?.().forEach(t => t.stop()); } catch {}
            try { if (oldAudioContext && oldAudioContext !== mediaAudioCtx) await oldAudioContext.close(); } catch {}

            const ownPreview = screenTrack
              ? new MediaStream([screenTrack, ...(nextStream?.getAudioTracks?.() || [])])
              : nextStream;
            if (typeof addVideo === 'function') {
              addVideo('me', ownPreview || emptyStream(), ME.display_name + (screenTrack ? ' · экран' : ' (Вы)'), true);
            }

            window.__confgtsMediaReady = true;
            syncMediaButtons();
            await applyOutputSettings();
            showNativeToast('Настройки устройств применены.');
          };

          // Apply speaker level/output to remote media elements as they appear.
          const outputObserver = new MutationObserver(() => { applyOutputSettings().catch(() => {}); });
          outputObserver.observe(document.documentElement, { childList:true, subtree:true });
          window.__confgtsNativeOutputObserver?.disconnect?.();
          window.__confgtsNativeOutputObserver = outputObserver;
          applyOutputSettings().catch(() => {});

          if (typeof selectRoom !== 'function' || typeof joinRoom !== 'function') {
            throw new Error('Серверная WebRTC-страница не содержит функций конференции.');
          }

          const waitForRoom = async () => {
            for (let attempt = 0; attempt < 50; attempt++) {
              if (Array.isArray(rooms) && rooms.some(r => r && r.id === roomId)) {
                return true;
              }

              if (attempt === 0 && typeof loadRooms === 'function') {
                try { await loadRooms(); } catch (e) { console.debug('ConfGTS room preload:', e); }
              }

              await new Promise(resolve => setTimeout(resolve, 100));
            }

            return false;
          };

          (async () => {
            const roomReady = await waitForRoom();
            if (!roomReady) {
              throw new Error('Список конференций не успел загрузиться с сервера.');
            }

            await selectRoom(roomId);
            window.__confgtsMediaReady = false;
            const conferenceNode = document.getElementById('conference');
            if (conferenceNode) conferenceNode.style.display = 'block';
            installNativeControls();
            syncMediaButtons();

            await joinRoom();

            const hangupBtn = document.querySelector('.controls .native-hangup');
            if (hangupBtn) hangupBtn.disabled = false;
            window.__confgtsMediaReady = true;
            syncMediaButtons();
            await applyOutputSettings();
          })().catch(err => {
            console.error('ConfGTS native conference join failed', err);
            showNativeToast('Не удалось полностью открыть конференцию: ' + (err?.message || err));
          });
        })();
        """;
        script = script
            .Replace("__CONFGTS_ROOM_JSON__", roomJson, StringComparison.Ordinal)
            .Replace("__CONFGTS_MEDIA_JSON__", mediaJson, StringComparison.Ordinal);
        await web.ExecuteScriptAsync(script);
    }

    private async Task ApplyConferenceMediaSettingsAsync()
    {
        var web = _conferenceWebView;
        if (web?.CoreWebView2 is null || _conferenceHost is null || string.IsNullOrWhiteSpace(_activeRoomId))
            return;

        var media = MediaDeviceSettings.Load();
        var mediaJson = JsonSerializer.Serialize(new
        {
            microphoneName = media.MicrophoneName,
            speakerName = media.SpeakerName,
            cameraName = media.CameraName,
            microphoneEnabled = media.MicrophoneEnabled,
            speakerEnabled = media.SpeakerEnabled,
            cameraEnabled = media.CameraEnabled,
            microphoneVolume = Math.Clamp(media.MicrophoneVolume / 100.0, 0, 1),
            speakerVolume = Math.Clamp(media.SpeakerVolume / 100.0, 0, 1)
        });

        try
        {
            var script = "(async()=>{if(typeof window.__confgtsApplyMediaSettings==='function'){await window.__confgtsApplyMediaSettings(" +
                         mediaJson +
                         ");}})()";
            await web.ExecuteScriptAsync(script);
            StartupDiagnostics.Log("Active conference media settings applied.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Failed to apply media settings to the active conference.", ex);
        }
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
                await web.ExecuteScriptAsync("if (typeof leaveRoom === 'function') { await leaveRoom(); }");
            }
            catch
            {
            }
        }

        if (host is not null)
            _mainContentHost.Children.Remove(host);

        SetConferenceLayout(false);

        if (showDashboard && _dashboardMainScroll is not null)
        {
            _dashboardMainScroll.Visibility = Visibility.Visible;
            await RefreshDashboardAsync();
            StartDashboardTimer();
        }
    }

    private async void ConferenceWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = e.TryGetWebMessageAsString();
            if (string.Equals(message, "leave-conference", StringComparison.Ordinal))
            {
                await LeaveConferenceAsync(true);
            }
            else if (string.Equals(message, "open-media-settings", StringComparison.Ordinal))
            {
                await ShowInlineSettingsAsync();
            }
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Conference WebView message handling failed.", ex);
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
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (await _api.HealthAsync())
                {
                    _serverText.Text = "Сервер доступен";
                    _serverDot.Fill = Brush("#31B657");
                    return;
                }
            }
            catch
            {
            }

            if (attempt < 2)
                await Task.Delay(350);
        }

        _serverText.Text = "Сервер недоступен";
        _serverDot.Fill = Brush("#D14343");
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
            StartupDiagnostics.Log("Login requested in automatic authentication mode.");
            await _api.LoginAsync(_loginBox.Text.Trim(), _passwordBox.Password);

            if (_rememberMeBox.IsChecked == true)
                RememberedCredentials.Save(_loginBox.Text.Trim(), _passwordBox.Password);
            else
                RememberedCredentials.Clear();

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

    private void LoadRememberedCredentials()
    {
        try
        {
            if (!RememberedCredentials.TryRead(out var username, out var password))
                return;

            _loginBox.Text = username;
            _passwordBox.Password = password;
            _rememberMeBox.IsChecked = true;
            StartupDiagnostics.Log("Remembered login restored from Windows Credential Manager.");
        }
        catch (Exception ex)
        {
            StartupDiagnostics.Log("Failed to restore remembered login.", ex);
        }
    }

    private void SetConferenceLayout(bool conferenceMode)
    {
        _conferenceSidebarVisible = false;

        if (_dashboardSidebar is not null)
            _dashboardSidebar.Visibility = conferenceMode ? Visibility.Collapsed : Visibility.Visible;

        _dashboardSidebarColumn.Width = conferenceMode
            ? new GridLength(0)
            : new GridLength(330);

        Grid.SetColumn(_mainContentHost, conferenceMode ? 0 : 1);
        Grid.SetColumnSpan(_mainContentHost, conferenceMode ? 2 : 1);
    }

    private void SetConferenceSidebarVisible(bool visible)
    {
        if (_conferenceHost is null)
            return;

        _conferenceSidebarVisible = visible;

        if (_dashboardSidebar is not null)
            _dashboardSidebar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        _dashboardSidebarColumn.Width = visible
            ? new GridLength(330)
            : new GridLength(0);

        Grid.SetColumn(_mainContentHost, visible ? 1 : 0);
        Grid.SetColumnSpan(_mainContentHost, visible ? 1 : 2);
    }

    internal async Task RunSettingsSmokeTestAsync()
    {
        StartupDiagnostics.Log("Settings smoke test requested.");
        _loginView.Visibility = Visibility.Collapsed;
        _dashboardView.Visibility = Visibility.Visible;
        await ShowInlineSettingsAsync();
        StartupDiagnostics.Log("Settings smoke test completed.");
    }

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        StopDashboardTimer();
        await CloseInlineSettingsAsync();
        await LeaveConferenceAsync(false);
        await _api.LogoutAsync();
        _dashboardView.Visibility = Visibility.Collapsed;
        _loginView.Visibility = Visibility.Visible;
        if (_rememberMeBox.IsChecked != true)
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
            StartupDiagnostics.Log("Failed to open media settings.", ex);

            _mediaSettingsPanel = null;
            if (_dashboardMainScroll is not null)
                _dashboardMainScroll.Visibility = _conferenceHost is null
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            _dashboardServerText.Text = "Не удалось открыть настройки устройств: " + ex.Message;
            _dashboardServerText.Foreground = Brush("#B54242");
        }
    }

    private async Task ShowInlineSettingsAsync()
    {
        StartupDiagnostics.Log("Opening media settings.");

        if (_mediaSettingsPanel is not null)
            return;

        // Do not leave an active conference when device settings are opened from
        // the conference sidebar. Keep WebView2, WebRTC peers and recording alive,
        // temporarily hide only the conference surface, and restore the exact room
        // when the user closes settings.
        _settingsOpenedFromConference = _conferenceHost is not null && !string.IsNullOrWhiteSpace(_activeRoomId);
        _settingsConferenceSidebarVisible = _conferenceSidebarVisible;
        if (_settingsOpenedFromConference && _conferenceHost is not null)
            _conferenceHost.Visibility = Visibility.Collapsed;

        var panel = new MediaSettingsPanel();
        panel.CloseRequested += async (_, _) =>
        {
            try
            {
                await CloseInlineSettingsAsync();
            }
            catch (Exception ex)
            {
                StartupDiagnostics.Log("Failed to close media settings.", ex);
            }
        };

        _mediaSettingsPanel = panel;

        if (_dashboardMainScroll is not null)
            _dashboardMainScroll.Visibility = Visibility.Collapsed;

        _mainContentHost.Children.Add(panel);

        try
        {
            // This initialization is UI-only. Hardware enumeration happens only after
            // the user explicitly presses "Обновить устройства" inside the panel.
            await panel.InitializeAsync();
            StartupDiagnostics.Log("Media settings opened successfully.");
        }
        catch
        {
            _mediaSettingsPanel = null;
            _mainContentHost.Children.Remove(panel);
            _settingsOpenedFromConference = false;

            if (_conferenceHost is not null && !string.IsNullOrWhiteSpace(_activeRoomId))
            {
                _conferenceHost.Visibility = Visibility.Visible;
                if (_dashboardMainScroll is not null)
                    _dashboardMainScroll.Visibility = Visibility.Collapsed;
                SetConferenceSidebarVisible(_settingsConferenceSidebarVisible);
            }
            else if (_dashboardMainScroll is not null)
            {
                _dashboardMainScroll.Visibility = Visibility.Visible;
            }

            throw;
        }
    }

    private async Task CloseInlineSettingsAsync()
    {
        var panel = _mediaSettingsPanel;
        var returnToConference = _settingsOpenedFromConference &&
                                 _conferenceHost is not null &&
                                 !string.IsNullOrWhiteSpace(_activeRoomId);
        var restoreSidebar = _settingsConferenceSidebarVisible;

        _settingsOpenedFromConference = false;
        _settingsConferenceSidebarVisible = false;

        if (panel is not null)
        {
            _mediaSettingsPanel = null;
            await panel.ShutdownAsync();
            _mainContentHost.Children.Remove(panel);
        }

        if (returnToConference && _conferenceHost is not null)
        {
            await ApplyConferenceMediaSettingsAsync();
            _conferenceHost.Visibility = Visibility.Visible;
            if (_dashboardMainScroll is not null)
                _dashboardMainScroll.Visibility = Visibility.Collapsed;
            SetConferenceSidebarVisible(restoreSidebar);
            StartupDiagnostics.Log("Returned from device settings to active conference " + _activeRoomId + ".");
            return;
        }

        if (_dashboardMainScroll is not null)
            _dashboardMainScroll.Visibility = Visibility.Visible;
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