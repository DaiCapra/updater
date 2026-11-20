using System.IO;
using Launcher.Core;
using Launcher.Core.Installation;
using Launcher.Core.Releases;
using Launcher.Core.Storage;
using Launcher.Tests.Fakes;
using Xunit;

namespace Launcher.Tests;

public class MainWindowViewModelTests
{
    public MainWindowViewModelTests()
    {
        // Run progress callbacks inline instead of posting them to xUnit's context.
        SynchronizationContext.SetSynchronizationContext(null);
    }

    [Fact]
    public async Task NothingInstalledOffersInstallThenPlay()
    {
        using var temp = new TempDirectory();
        var store = new InMemoryReleaseStore();
        store.Add("build-0.0.22.zip", Zips.Create(("Erdenstone.exe", "game")));
        var (viewModel, shell) = Create(temp, store);

        Assert.Equal("Not installed", viewModel.InstalledText);
        Assert.False(viewModel.ShowPlay);

        await viewModel.CheckForUpdatesAsync();
        Assert.True(viewModel.ShowInstall);
        Assert.Equal("Install 0.0.22", viewModel.InstallText);

        await viewModel.InstallUpdateAsync();
        Assert.Equal(LauncherState.Idle, viewModel.State);
        Assert.Equal("Installed version 0.0.22.", viewModel.StatusText);
        Assert.True(viewModel.ShowPlay);
        Assert.False(viewModel.ShowInstall);

        viewModel.Play();
        Assert.EndsWith("Erdenstone.exe", Assert.Single(shell.Apps));
    }

    [Fact]
    public async Task NewerReleaseOffersUpdateWhilePlayStaysAvailable()
    {
        using var temp = new TempDirectory();
        var (viewModel, _, store) = await CreateInstalledAsync(temp);
        store.Add("build-1.1.zip", Zips.Create(("Game.exe", "2")));

        await viewModel.CheckForUpdatesAsync();

        Assert.Equal("Update to 1.1", viewModel.InstallText);
        Assert.True(viewModel.ShowPlay);
        Assert.Equal("Installed version 1.0", viewModel.InstalledText);
    }

    [Fact]
    public async Task UpToDateMessageWhenNothingNewer()
    {
        using var temp = new TempDirectory();
        var (viewModel, _, _) = await CreateInstalledAsync(temp);

        await viewModel.CheckForUpdatesAsync();

        Assert.Equal("Erdenstone is up to date.", viewModel.StatusText);
        Assert.False(viewModel.ShowInstall);
    }

    [Fact]
    public async Task CheckingCanBeCanceled()
    {
        using var temp = new TempDirectory();
        var (viewModel, _) = Create(temp, new HangingStore());

        var check = viewModel.CheckForUpdatesAsync();
        Assert.Equal(LauncherState.Checking, viewModel.State);
        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.IsIdle);
        Assert.True(viewModel.IsProgressIndeterminate);
        viewModel.Cancel();
        await check;

        Assert.Equal(LauncherState.Idle, viewModel.State);
        Assert.Equal("Canceled.", viewModel.StatusText);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task DownloadAndInstallReportProgressInSteps()
    {
        using var temp = new TempDirectory();
        var store = new InMemoryReleaseStore();
        store.Add("build-1.0.zip", Zips.Create(("Game.exe", "game")));
        var (viewModel, _) = Create(temp, store);
        await viewModel.CheckForUpdatesAsync();
        var seen = new List<(LauncherState State, double? Progress, string Detail)>();
        viewModel.PropertyChanged += (_, _) => seen.Add((viewModel.State, viewModel.Progress, viewModel.ProgressDetail));

        await viewModel.InstallUpdateAsync();

        var downloading = seen.Where(s => s.State == LauncherState.Downloading && s.Progress > 0).ToList();
        Assert.True(downloading.Select(s => s.Progress).Distinct().Count() > 2);
        Assert.Contains(downloading, s => s.Detail.EndsWith(" MB"));
        Assert.Contains(seen, s => s.State == LauncherState.Installing && s.Progress == 1);
    }

    [Fact]
    public async Task FailedInstallKeepsTheCurrentBuildPlayable()
    {
        using var temp = new TempDirectory();
        var (viewModel, _, store) = await CreateInstalledAsync(temp);
        store.Add("build-2.0.zip", [1, 2, 3]);

        await viewModel.CheckForUpdatesAsync();
        await viewModel.InstallUpdateAsync();

        Assert.StartsWith("Could not install the update.", viewModel.StatusText);
        Assert.Equal(new Version(1, 0), viewModel.InstalledVersion);
        Assert.True(viewModel.ShowPlay);
    }

    [Fact]
    public void OpenFolderOpensTheGamesFolderEvenBeforeAnythingIsInstalled()
    {
        using var temp = new TempDirectory();
        var (viewModel, shell) = Create(temp, new InMemoryReleaseStore());

        Assert.True(viewModel.ShowOpenFolder);
        viewModel.OpenFolder();

        Assert.Equal(temp.Combine("install"), Assert.Single(shell.Folders));
        Assert.True(Directory.Exists(temp.Combine("install")));
    }

    [Fact]
    public void PlayReportsAMissingExecutable()
    {
        using var temp = new TempDirectory();
        var installation = new InstallationStore(temp.Path);
        temp.Write("versions/1.0/readme.txt", "");
        installation.Activate(new Version(1, 0), "1.0");
        var shell = new FakeShell();
        var viewModel = new MainWindowViewModel(new LauncherProfile("Game"), shell, installation: installation);

        viewModel.Play();

        Assert.StartsWith("Could not start the app.", viewModel.StatusText);
        Assert.Empty(shell.Apps);
    }

    /// <summary>Publishes build <paramref name="version"/>, then checks for and installs it, leaving the view model idle and installed.</summary>
    private static async Task<(MainWindowViewModel ViewModel, FakeShell Shell, InMemoryReleaseStore Store)> CreateInstalledAsync(TempDirectory temp, string version = "1.0")
    {
        var store = new InMemoryReleaseStore();
        store.Add($"build-{version}.zip", Zips.Create(("Game.exe", "1")));
        var (viewModel, shell) = Create(temp, store);
        await viewModel.CheckForUpdatesAsync();
        await viewModel.InstallUpdateAsync();
        return (viewModel, shell, store);
    }

    private static (MainWindowViewModel ViewModel, FakeShell Shell) Create(TempDirectory temp, IReleaseStoreReader store)
    {
        var installation = new InstallationStore(temp.Combine("install"));
        var shell = new FakeShell();
        var viewModel = new MainWindowViewModel(
            new LauncherProfile("Erdenstone"),
            shell,
            new UpdateChecker(new StoreUpdateSource(store, ReleaseFileNaming.Default)),
            new StoreReleaseDownloader(store, installation.DownloadsDirectory),
            installation,
            new ZipReleaseInstaller(installation));
        return (viewModel, shell);
    }

    private sealed class HangingStore : IReleaseStoreReader
    {
        public async Task<IReadOnlyList<StoredFile>> ListAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }

        public Task DownloadAsync(StoredFile file, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
