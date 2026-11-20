using System.IO;
using System.Text.Json;
using System.Windows;
using Launcher.Core;
using Launcher.Core.Installation;
using Launcher.Core.Logging;
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

        // Until the profile names the app's folder, problems go to a shared log.
        ILauncherLog log = new RollingFileLog(LogDirectory(LauncherProfile.Default));
        DispatcherUnhandledException += (_, args) => log.Error("Unhandled error.", args.Exception);

        try
        {
            var providers = CreateProviders();
            var profile = LauncherProfileLoader.Load(e.Args, AppContext.BaseDirectory, providers);
            services = CreateServices(profile, providers);
            log = services.GetRequiredService<ILauncherLog>();
            log.Info($"Launcher {MainWindowViewModel.BuildVersion()} started for {profile.DisplayName}.");
            MainWindow = services.GetRequiredService<MainWindow>();
            MainWindow.Show();
        }
        catch (Exception error) when (IsStartupFailure(error))
        {
            log.Error("Could not start the launcher.", error);
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
        LauncherProfile? profile = null, ProviderRegistry? providers = null, string? installRoot = null, IShell? shell = null)
    {
        profile ??= LauncherProfile.Default;
        providers ??= CreateProviders();
        var installation = new InstallationStore(installRoot ?? InstallRoot(profile));
        var registrations = new ServiceCollection()
            .AddSingleton(profile)
            .AddSingleton(shell ?? new ProcessShell())
            .AddSingleton(installation)
            .AddSingleton<ILauncherLog>(new RollingFileLog(Path.Combine(installation.Root, "logs")))
            .AddSingleton<MainWindow>()
            .AddSingleton<MainWindowViewModel>();

        if (profile.UpdateSource is { } source)
        {
            if (providers.TryGetStorage(source.Type, out var storage))
            {
                registrations
                    .AddSingleton(_ => storage.CreateReader(source.Settings))
                    .AddSingleton(ReleaseFileNaming.Default)
                    .AddSingleton<IUpdateSource, StoreUpdateSource>()
                    .AddSingleton<IReleaseDownloader>(provider =>
                        new StoreReleaseDownloader(provider.GetRequiredService<IReleaseStoreReader>(), installation.DownloadsDirectory))
                    .AddSingleton<IReleaseInstaller, ZipReleaseInstaller>();
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

    /// <summary>Failures that are caused by the profile, the install folder or the update source, and so are shown to the user.</summary>
    private static bool IsStartupFailure(Exception error) =>
        error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException or FormatException or ReleaseStoreException;

    /// <summary>Each app keeps its builds, downloads and logs in its own folder under local app data.</summary>
    private static string InstallRoot(LauncherProfile profile) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), profile.InstallFolderName);

    private static string LogDirectory(LauncherProfile profile) => Path.Combine(InstallRoot(profile), "logs");
}
