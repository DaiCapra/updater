using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Core;
using Launcher.Core.Storage;

namespace Launcher;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private UpdateRelease? availableUpdate;
    private CancellationTokenSource? downloadCancellation;
    private string? downloadedPath;

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
        ShowInFolderButton.Visibility = Visibility.Collapsed;
        availableUpdate = null;
        downloadedPath = null;
        UpdateStatus.Text = "Checking for updates…";

        try
        {
            availableUpdate = await viewModel.CheckForUpdatesAsync();
            if (availableUpdate is { } update)
            {
                UpdateStatus.Text = $"Version {update.Version} is available.";
                ResetDownloadButton(update);
            }
            else
            {
                UpdateStatus.Text = "You’re up to date.";
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or JsonException
            or OperationCanceledException or ReleaseStoreException)
        {
            UpdateStatus.Text = $"Could not check for updates. {error.Message}";
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void ResetDownloadButton(UpdateRelease update)
    {
        var canDownload = viewModel.CanDownload(update);
        DownloadButton.Content = canDownload ? "Download" : "Open download";
        DownloadButton.Visibility = canDownload || update.DownloadPage is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnDownloadClicked(object sender, RoutedEventArgs e)
    {
        if (downloadCancellation is { } running)
        {
            running.Cancel();
            return;
        }

        if (availableUpdate is not { } update)
        {
            return;
        }

        if (!viewModel.CanDownload(update))
        {
            OpenDownloadPage(update);
            return;
        }

        using var cancellation = new CancellationTokenSource();
        downloadCancellation = cancellation;
        CheckUpdatesButton.IsEnabled = false;
        DownloadButton.Content = "Cancel";
        UpdateStatus.Text = $"Downloading version {update.Version}…";
        var progress = new Progress<double>(fraction =>
        {
            // Reports are posted to the UI thread and can arrive after the download finished.
            if (downloadCancellation == cancellation)
            {
                UpdateStatus.Text = $"Downloading version {update.Version}… {fraction:P0}";
            }
        });

        var downloaded = false;
        string status;
        try
        {
            downloadedPath = await viewModel.DownloadAsync(update, progress, cancellation.Token);
            downloaded = true;
            status = $"Downloaded version {update.Version}.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            status = "Download canceled.";
        }
        catch (Exception error) when (error is ReleaseStoreException or HttpRequestException or IOException
            or InvalidDataException or NotSupportedException or UnauthorizedAccessException or OperationCanceledException)
        {
            status = $"Could not download the update. {error.Message}";
        }
        finally
        {
            // Cleared before the final status is written so late progress reports are ignored.
            downloadCancellation = null;
            CheckUpdatesButton.IsEnabled = true;
        }

        UpdateStatus.Text = status;
        if (downloaded)
        {
            DownloadButton.Visibility = Visibility.Collapsed;
            ShowInFolderButton.Visibility = Visibility.Visible;
        }
        else
        {
            ResetDownloadButton(update);
        }
    }

    private void OpenDownloadPage(UpdateRelease update)
    {
        if (update.DownloadPage is not { } page)
        {
            return;
        }

        try
        {
            using var browser = Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Win32Exception error)
        {
            UpdateStatus.Text = $"Could not open the download. {error.Message}";
        }
    }

    private void ShowInFolder(object sender, RoutedEventArgs e)
    {
        if (downloadedPath is not { } path)
        {
            return;
        }

        try
        {
            using var explorer = Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Win32Exception error)
        {
            UpdateStatus.Text = $"Could not open the folder. {error.Message}";
        }
    }
}
