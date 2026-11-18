using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Launcher.Core;
using Launcher.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Launcher;

public partial class App : Application
{
    private ServiceProvider? services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var profile = LauncherProfileLoader.Load(e.Args, AppContext.BaseDirectory);
            services = CreateServices(profile);
            MainWindow = services.GetRequiredService<MainWindow>();
            MainWindow.Show();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException or FormatException)
        {
            MessageBox.Show(error.Message, "Could not start the launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            base.OnExit(e);
        }
        finally
        {
            services?.Dispose();
        }
    }

    internal static ServiceProvider CreateServices(LauncherProfile? profile = null)
    {
        profile ??= LauncherProfile.Default;
        var registrations = new ServiceCollection()
            .AddSingleton(profile)
            .AddSingleton<MainWindow>()
            .AddSingleton<MainWindowViewModel>();

        if (profile.UpdateFeedUrl is { } feedUrl)
        {
            registrations
                .AddSingleton(_ => new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(15),
                    MaxResponseContentBufferSize = 64 * 1024
                })
                .AddSingleton<IUpdateSource>(provider =>
                    new HttpJsonUpdateSource(provider.GetRequiredService<HttpClient>(), feedUrl))
                .AddSingleton<UpdateChecker>();
        }

        return registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}
