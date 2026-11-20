using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Launcher.Core;
using Launcher.Core.Releases;
using Launcher.Core.Storage;
using Launcher.Services;
using Launcher.Services.Providers;
using Launcher.Storage.GoogleDrive;
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
            var providers = CreateProviders();
            var profile = LauncherProfileLoader.Load(e.Args, AppContext.BaseDirectory, providers);
            services = CreateServices(profile, providers);
            MainWindow = services.GetRequiredService<MainWindow>();
            MainWindow.Show();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException or FormatException or ReleaseStoreException)
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

    /// <summary>Every update source the launcher understands. Register new hosts here.</summary>
    internal static ProviderRegistry CreateProviders() => new(
        storageProviders: [new GoogleDriveStorageProvider()],
        sourceFactories: [new HttpJsonSourceFactory()]);

    internal static ServiceProvider CreateServices(
        LauncherProfile? profile = null, ProviderRegistry? providers = null, string? downloadsRoot = null)
    {
        profile ??= LauncherProfile.Default;
        providers ??= CreateProviders();
        var registrations = new ServiceCollection()
            .AddSingleton(profile)
            .AddSingleton<MainWindow>()
            .AddSingleton<MainWindowViewModel>();

        if (profile.UpdateSource is { } source)
        {
            if (providers.TryGetStorage(source.Type, out var storage))
            {
                var stagingDirectory = Path.Combine(downloadsRoot ?? DefaultDownloadsRoot, StagingFolderName(source));
                registrations
                    .AddSingleton(_ => storage.CreateReader(source.Settings))
                    .AddSingleton(ReleaseFileNaming.Default)
                    .AddSingleton<IUpdateSource, StoreUpdateSource>()
                    .AddSingleton<IReleaseDownloader>(provider =>
                        new StoreReleaseDownloader(provider.GetRequiredService<IReleaseStoreReader>(), stagingDirectory));
            }
            else
            {
                var factory = providers.GetSourceFactory(source.Type);
                registrations.AddSingleton(_ => factory.Create(source.Settings));
            }

            registrations.AddSingleton<UpdateChecker>();
        }

        return registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static string DefaultDownloadsRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Launcher", "Downloads");

    /// <summary>A stable folder per release location, so two profiles never share staged files.</summary>
    private static string StagingFolderName(UpdateSourceConfig source)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{source.Type}\n{source.Settings}"));
        return $"{source.Type}-{Convert.ToHexStringLower(hash)[..16]}";
    }
}
