using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Launcher.Core;
using Launcher.Tests.Fakes;
using Xunit;

namespace Launcher.Tests;

/// <summary>
/// The button styles share one template that reads ButtonHoverBrush / ButtonPressedBrush from the style.
/// These tests resolve those keys from inside each button's template, the same lookup WPF does on hover.
/// </summary>
public class ThemeTests
{
    [Fact]
    public void EachButtonStyleResolvesItsOwnHoverAndPressedColors()
    {
        // WPF objects belong to the thread that created them, so every case runs on one STA thread.
        var actual = RunOnSta(() =>
        {
            new App().InitializeComponent();
            var window = new MainWindow(new MainWindowViewModel(new LauncherProfile("Theme"), new FakeShell()));
            return new[] { "Button", "AccentButton", "CaptionButtonStyle", "CloseButtonStyle" }.ToDictionary(
                key => key,
                key =>
                {
                    var button = new Button();
                    if (key != "Button")
                    {
                        button.Style = (Style)window.FindResource(key);
                    }

                    window.Content = button;
                    button.ApplyTemplate();
                    var border = (FrameworkElement)button.Template.FindName("Bd", button);
                    return (Hover: Color(border, "ButtonHoverBrush"), Pressed: Color(border, "ButtonPressedBrush"));
                });
        });

        Assert.Equal(("#FF464A51", "#FF52565E"), actual["Button"]);
        Assert.Equal(("#FFC5606F", "#FFA14453"), actual["AccentButton"]);
        Assert.Equal(("#FF3F4249", "#FF4E525A"), actual["CaptionButtonStyle"]);
        Assert.Equal(("#FFE81123", "#FFF1707A"), actual["CloseButtonStyle"]);
    }

    private static string Color(FrameworkElement element, string key) =>
        ((SolidColorBrush)element.TryFindResource(key)).Color.ToString();

    private static T RunOnSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return failure is null ? result : throw new InvalidOperationException("STA test body failed.", failure);
    }
}
