using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Core;

namespace Launcher;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private UpdateRelease? availableUpdate;

    public MainWindow(MainWindowViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        if (viewModel.BackgroundPath is { } path)
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            Backdrop.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }
    }

    private void Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void ToggleMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Segoe MDL2 Assets: E922 = maximize, E923 = restore.
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }

    private async void CheckForUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        DownloadButton.Visibility = Visibility.Collapsed;
        availableUpdate = null;
        UpdateStatus.Text = "Checking for updates…";

        try
        {
            availableUpdate = await viewModel.CheckForUpdatesAsync();
            if (availableUpdate is { } update)
            {
                UpdateStatus.Text = $"Version {update.Version} is available.";
                DownloadButton.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateStatus.Text = "You’re up to date.";
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException
            or OperationCanceledException)
        {
            UpdateStatus.Text = $"Could not check for updates. {error.Message}";
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void OpenDownload(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is { } update)
        {
            try
            {
                using var browser = Process.Start(new ProcessStartInfo(update.DownloadUrl.AbsoluteUri)
                {
                    UseShellExecute = true
                });
            }
            catch (Win32Exception error)
            {
                UpdateStatus.Text = $"Could not open the download. {error.Message}";
            }
        }
    }
}
