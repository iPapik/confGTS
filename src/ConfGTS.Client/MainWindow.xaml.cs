using ConfGTS.Client.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
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
    private MediaSettingsWindow? _mediaSettingsWindow;

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
            Text = "ConfGTS",
            FontSize = 46,
            FontWeight = FontWeights.Bold,
            Foreground = Brush(Navy),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Сервис видеоконференцсвязи",
            FontSize = 18,
            Foreground = Brush(Muted),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, -5, 0, 16)
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
            Text = "ConfGTS 0.17.0  |  © ГТС, 2026",
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
        sidebarContent.Children.Add(SidebarSectionHeader("\uE716", "Контакты"));
        _contactsPanel.Spacing = 6;
        sidebarContent.Children.Add(_contactsPanel);

        sidebarContent.Children.Add(new Border
        {
            Height = 1,
            Background = Brush("#E2ECF2"),
            Margin = new Thickness(0, 8, 0, 4)
        });

        sidebarContent.Children.Add(SidebarSectionHeader("\uE787", "Конференции"));
        _conferenceSidebarPanel.Spacing = 6;
        sidebarContent.Children.Add(_conferenceSidebarPanel);

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
        Grid.SetColumn(mainScroll, 1);

        var main = new StackPanel
        {
            Padding = new Thickness(34),
            Spacing = 16
        };

        _mainConferencePanel.Spacing = 14;
        main.Children.Add(_mainConferencePanel);

        mainScroll.Content = main;
        _dashboardView.Children.Add(mainScroll);

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
            var border = new Border
            {
                Background = Brush("#F5FAFD"),
                BorderBrush = Brush("#D8E6EF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8)
            };

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

            border.Child = stack;
            _conferenceSidebarPanel.Children.Add(border);
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
        conferenceCard.Child = grid;
        _mainConferencePanel.Children.Add(conferenceCard);
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

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        StopDashboardTimer();
        _dashboardView.Visibility = Visibility.Collapsed;
        _loginView.Visibility = Visibility.Visible;
        _passwordBox.Password = "";
    }

    private void MediaSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaSettingsWindow is not null)
        {
            _mediaSettingsWindow.Activate();
            return;
        }

        _mediaSettingsWindow = new MediaSettingsWindow();
        _mediaSettingsWindow.Closed += (_, _) => _mediaSettingsWindow = null;
        _mediaSettingsWindow.Activate();
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

    private static TextBlock SidebarSectionHeader(string glyph, string text)
    {
        var line = new TextBlock
        {
            Text = glyph + "  " + text,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe UI"),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(Navy)
        };
        return line;
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
                new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
            }
        };
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
        ToolTipService.SetToolTip(button, tooltip);
        return button;
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
