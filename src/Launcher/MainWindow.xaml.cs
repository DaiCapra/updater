using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Launcher;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        if (viewModel.BackgroundPath is { } background)
        {
            // Uniform shows the whole image; any spare space falls behind the header and footer or shows the window color.
            Backdrop.Background = new ImageBrush(LoadImage(background)) { Stretch = Stretch.Uniform };
        }

        Loaded += async (_, _) => await viewModel.CheckForUpdatesAsync();
    }

    private static BitmapImage LoadImage(string path)
    {
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>Moves the window when the user drags any empty area. Buttons handle their own clicks first.</summary>
    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize(sender, e);
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            RestoreUnderCursor(e);
        }

        DragMove();
    }

    /// <summary>Restores a maximized window so it sits under the cursor, as the native title bar does.</summary>
    private void RestoreUnderCursor(MouseButtonEventArgs e)
    {
        var position = e.GetPosition(this);
        var ratio = position.X / ActualWidth;
        var screen = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.Transform(PointToScreen(position))
            ?? PointToScreen(position);
        WindowState = WindowState.Normal;
        Left = screen.X - RestoreBounds.Width * ratio;
        Top = screen.Y - 16;
    }

    private void Minimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void ToggleMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow(object sender, RoutedEventArgs e)
    {
        viewModel.Cancel();
        Close();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Segoe MDL2 Assets: E922 = maximize, E923 = restore.
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
    }
}
