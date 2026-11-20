using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows.Input;
using Launcher.Core;
using Launcher.Core.Installation;
using Launcher.Core.Logging;
using Launcher.Core.Releases;

namespace Launcher;

public enum LauncherState
{
    Idle,
    Checking,
    Downloading,
    Installing
}

/// <summary>
/// The launcher's state machine: check for updates, download and install a release, and play the installed build.
/// Every busy state can be cancelled, and the installed build stays playable whenever the launcher is idle.
/// </summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly LauncherProfile profile;
    private readonly UpdateChecker? updateChecker;
    private readonly IReleaseDownloader? downloader;
    private readonly InstallationStore? installation;
    private readonly IReleaseInstaller? installer;
    private readonly IShell shell;
    private readonly ILauncherLog log;

    private CancellationTokenSource? operation;
    private UpdateRelease? availableUpdate;
    private InstalledBuild? installedBuild;

    public MainWindowViewModel(
        LauncherProfile profile,
        IShell shell,
        UpdateChecker? updateChecker = null,
        IReleaseDownloader? downloader = null,
        InstallationStore? installation = null,
        IReleaseInstaller? installer = null,
        ILauncherLog? log = null)
    {
        this.profile = profile;
        this.shell = shell;
        this.updateChecker = updateChecker;
        this.downloader = downloader;
        this.installation = installation;
        this.installer = installer;
        this.log = log ?? NullLog.Instance;
        installedBuild = installation?.Current;

        CheckCommand = new RelayCommand(() => _ = CheckForUpdatesAsync());
        InstallCommand = new RelayCommand(() => _ = InstallUpdateAsync());
        PlayCommand = new RelayCommand(Play);
        CancelCommand = new RelayCommand(Cancel);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        StatusText = installedBuild is null && CanCheckForUpdates ? "Check for updates to install." : "";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title => profile.DisplayName;
    public string? BackgroundPath => profile.BackgroundPath;

    public ICommand CheckCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public LauncherState State { get; private set; }
    public string StatusText { get; private set; }

    /// <summary>Progress from 0 to 1, or null while the amount of work is unknown.</summary>
    public double? Progress { get; private set; }
    public string ProgressDetail { get; private set; } = "";

    /// <summary>"Launcher 1.0.12": the launcher's own build version, stamped by the release build (-p:Version).</summary>
    public string LauncherVersionText { get; } = $"Launcher {BuildVersion()}";

    public bool IsBusy => State != LauncherState.Idle;
    public bool IsProgressIndeterminate => Progress is null;
    public double ProgressPercent => (Progress ?? 0) * 100;

    public bool CanCheckForUpdates => updateChecker is not null;
    public Version? InstalledVersion => installedBuild?.Version ?? profile.InstalledVersion;
    public string InstalledText => InstalledVersion is { } version ? $"Installed version {version}" : "Not installed";

    public bool ShowCheck => CanCheckForUpdates;
    /// <summary>The footer shows the action row while idle and the progress row (with Cancel) while busy.</summary>
    public bool IsIdle => !IsBusy;
    public bool ShowPlay => !IsBusy && installedBuild is not null;
    public bool ShowInstall => !IsBusy && availableUpdate is not null;
    public bool ShowOpenFolder => installation is not null;

    public string InstallText => availableUpdate switch
    {
        { File: not null } when !CanInstall => "Unavailable",
        { File: not null } update => installedBuild is null ? $"Install {update.Version}" : $"Update to {update.Version}",
        _ => "Open download"
    };

    private bool CanInstall => downloader is not null && installer is not null;

    public async Task CheckForUpdatesAsync()
    {
        if (updateChecker is null || IsBusy)
        {
            return;
        }

        await RunAsync(LauncherState.Checking, "Checking for updates…", async (_, cancellationToken) =>
        {
            availableUpdate = null;
            var release = await updateChecker.CheckAsync(InstalledVersion ?? new Version(0, 0), cancellationToken);
            availableUpdate = release;
            return release is null
                ? installedBuild is null ? "No builds are published yet." : $"{profile.DisplayName} is up to date."
                : $"Version {release.Version} is available.";
        });
    }

    public async Task InstallUpdateAsync()
    {
        if (availableUpdate is not { } update || IsBusy)
        {
            return;
        }

        if (update.File is null || !CanInstall)
        {
            OpenDownloadPage(update);
            return;
        }

        await RunAsync(LauncherState.Downloading, $"Downloading version {update.Version}…", async (report, cancellationToken) =>
        {
            var size = update.File.Size;
            var archive = await downloader!.DownloadAsync(update, report(fraction => $"{Megabytes(fraction * size)} of {Megabytes(size)}"), cancellationToken);

            SetState(LauncherState.Installing, $"Installing version {update.Version}…");
            installedBuild = await installer!.InstallAsync(archive, update.Version, report(fraction => $"{fraction:P0}"), cancellationToken);
            availableUpdate = null;
            return $"Installed version {update.Version}.";
        });
    }

    public void Play()
    {
        if (installedBuild is not { } build || IsBusy)
        {
            return;
        }

        TryRun(() =>
        {
            var executable = ExecutableLocator.Find(build.Directory, profile.Executable);
            log.Info($"Starting {executable}");
            shell.StartApp(executable);
            StatusText = $"Started {profile.DisplayName} {build.Version}.";
        }, "Could not start the app.");
    }

    public void Cancel() => operation?.Cancel();

    /// <summary>Opens the install folder (builds, downloads and logs) in Explorer.</summary>
    public void OpenFolder()
    {
        if (installation is null)
        {
            return;
        }

        TryRun(() =>
        {
            Directory.CreateDirectory(installation.Root);
            shell.OpenFolder(installation.Root);
        }, "Could not open the folder.");
    }

    /// <summary>
    /// Runs one cancellable operation. <paramref name="work"/> receives a factory for progress reporters
    /// (fraction → detail text) and returns the final status text.
    /// </summary>
    private async Task RunAsync(
        LauncherState state,
        string status,
        Func<Func<Func<double, string>, IProgress<double>>, CancellationToken, Task<string>> work)
    {
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        var context = SynchronizationContext.Current;
        SetState(state, status);

        IProgress<double> Reporter(Func<double, string> detail) => new ActionProgress<double>(fraction =>
        {
            void Apply()
            {
                // Reports can arrive after the operation finished; ignore them then.
                if (operation == cancellation)
                {
                    SetProgress(fraction, detail(fraction));
                }
            }

            if (context is null)
            {
                Apply();
            }
            else
            {
                context.Post(_ => Apply(), null);
            }
        });

        string result;
        try
        {
            result = await work(Reporter, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            result = "Canceled.";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            result = state == LauncherState.Checking
                ? $"Could not check for updates. {error.Message}"
                : $"Could not install the update. {error.Message}";
            log.Error($"{state} failed.", error);
        }
        finally
        {
            operation = null;
        }

        SetState(LauncherState.Idle, result);
    }

    private void OpenDownloadPage(UpdateRelease update)
    {
        if (update.DownloadPage is not { } page)
        {
            return;
        }

        TryRun(() => shell.OpenUrl(page), "Could not open the download.");
    }

    /// <summary>Runs <paramref name="action"/>, then refreshes the bindings. On failure, the status becomes <paramref name="failurePrefix"/> plus the error message.</summary>
    private void TryRun(Action action, string failurePrefix)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            StatusText = $"{failurePrefix} {error.Message}";
            log.Error(failurePrefix, error);
        }

        Refresh();
    }

    private void SetState(LauncherState state, string status)
    {
        State = state;
        StatusText = status;
        log.Info(status);
        Progress = state is LauncherState.Downloading or LauncherState.Installing ? 0 : null;
        ProgressDetail = "";
        Refresh();
    }

    private void SetProgress(double fraction, string detail)
    {
        // Thousands of reports can arrive for a large build; only redraw when something visible changes.
        var rounded = Math.Round(Math.Clamp(fraction, 0, 1), 3);
        if (rounded == Progress && detail == ProgressDetail)
        {
            return;
        }

        Progress = rounded;
        ProgressDetail = detail;
        Refresh();
    }

    private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    /// <summary>The launcher's build version without source metadata, e.g. "1.0.12".</summary>
    internal static string BuildVersion()
    {
        // The informational version can carry "+<commit>" source metadata; only the version itself is shown.
        var assembly = typeof(MainWindowViewModel).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        return version.Split('+')[0];
    }

    private static string Megabytes(double bytes) => $"{bytes / (1024 * 1024):0.0} MB";
}
