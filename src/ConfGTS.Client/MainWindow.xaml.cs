using ConfGTS.Client.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace ConfGTS.Client;

public sealed partial class MainWindow : Window
{
    private readonly ApiClient _api = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = "ConfGTS";
        ExtendsContentIntoTitleBar = false;
        SetWindowSize(1400, 900);
        _ = CheckServerAsync();
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
            ServerText.Text = ok ? "Сервер доступен" : "Сервер недоступен";
            ServerDot.Fill = new SolidColorBrush(ok ? ColorHelper.FromArgb(255, 49, 182, 87) : ColorHelper.FromArgb(255, 209, 67, 67));
        }
        catch
        {
            ServerText.Text = "Сервер недоступен";
            ServerDot.Fill = new SolidColorBrush(ColorHelper.FromArgb(255, 209, 67, 67));
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        LoginError.Visibility = Visibility.Collapsed;
        LoginButton.IsEnabled = false;
        try
        {
            await _api.LoginAsync(LoginBox.Text.Trim(), PasswordBox.Password);
            WelcomeName.Text = string.IsNullOrWhiteSpace(LoginBox.Text) ? "Пользователь!" : LoginBox.Text.Trim() + "!";
            LoginView.Visibility = Visibility.Collapsed;
            DashboardView.Visibility = Visibility.Visible;
            await LoadRoomsAsync();
        }
        catch (Exception ex)
        {
            LoginError.Text = "Не удалось войти: " + ex.Message;
            LoginError.Visibility = Visibility.Visible;
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void GuestButton_Click(object sender, RoutedEventArgs e)
    {
        WelcomeName.Text = "Гость!";
        LoginView.Visibility = Visibility.Collapsed;
        DashboardView.Visibility = Visibility.Visible;
        RoomsList.ItemsSource = Array.Empty<RoomInfo>();
        RoomsEmptyText.Visibility = Visibility.Visible;
    }

    private async void RefreshRoomsButton_Click(object sender, RoutedEventArgs e) => await LoadRoomsAsync();

    private async Task LoadRoomsAsync()
    {
        try
        {
            var rooms = await _api.RoomsAsync();
            RoomsList.ItemsSource = rooms;
            RoomsEmptyText.Visibility = rooms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            RoomsList.ItemsSource = Array.Empty<RoomInfo>();
            RoomsEmptyText.Visibility = Visibility.Visible;
        }
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        DashboardView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
        PasswordBox.Password = "";
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var box = new Microsoft.UI.Xaml.Controls.TextBox
        {
            Text = _api.BaseUrl,
            Header = "Адрес сервера",
            PlaceholderText = "https://confgts.company.local"
        };

        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Настройки подключения",
            Content = box,
            PrimaryButtonText = "Сохранить",
            CloseButtonText = "Отмена",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
        {
            if (Uri.TryCreate(box.Text.Trim(), UriKind.Absolute, out _))
            {
                _api.BaseUrl = box.Text.Trim();
                await CheckServerAsync();
            }
        }
    }
}
