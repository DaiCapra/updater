using System.IO;
using Launcher.Core.Installation;
using Launcher.Tests.Fakes;
using Xunit;

namespace Launcher.Tests;

public class InstallationTests
{
    [Fact]
    public async Task InstallActivatesTheVersionAndRemovesTheArchive()
    {
        using var temp = new TempDirectory();
        var store = new InstallationStore(temp.Path);
        var archive = WriteZip(temp, ("Game.exe", "game"), ("Data/level.bin", "level"));

        var build = await new ZipReleaseInstaller(store).InstallAsync(archive, new Version(1, 2), null, default);

        Assert.Equal(new Version(1, 2), build.Version);
        Assert.Equal(build, store.Current);
        Assert.Equal("level", File.ReadAllText(Path.Combine(build.Directory, "Data", "level.bin")));
        Assert.False(File.Exists(archive));
        Assert.True(File.Exists(temp.Combine("installed.json")));
    }

    [Fact]
    public async Task ASingleTopLevelFolderBecomesTheBuildRoot()
    {
        using var temp = new TempDirectory();
        var store = new InstallationStore(temp.Path);
        var archive = WriteZip(temp, ("Erdenstone/Game.exe", "game"), ("Erdenstone/Game_Data/a.txt", "a"));

        var build = await new ZipReleaseInstaller(store).InstallAsync(archive, new Version(1, 0), null, default);

        Assert.True(File.Exists(Path.Combine(build.Directory, "Game.exe")));
    }

    [Fact]
    public async Task InstallingANewVersionRemovesTheOldOne()
    {
        using var temp = new TempDirectory();
        var store = new InstallationStore(temp.Path);
        var installer = new ZipReleaseInstaller(store);
        var first = await installer.InstallAsync(WriteZip(temp, ("Game.exe", "1")), new Version(1, 0), null, default);

        var second = await installer.InstallAsync(WriteZip(temp, ("Game.exe", "2")), new Version(2, 0), null, default);

        Assert.Equal(second, store.Current);
        Assert.False(Directory.Exists(first.Directory));
    }

    [Fact]
    public async Task EntriesOutsideTheInstallFolderAreRejectedAndTheCurrentBuildStays()
    {
        using var temp = new TempDirectory();
        var store = new InstallationStore(temp.Path);
        var installer = new ZipReleaseInstaller(store);
        var current = await installer.InstallAsync(WriteZip(temp, ("Game.exe", "1")), new Version(1, 0), null, default);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => installer.InstallAsync(WriteZip(temp, ("../../evil.txt", "x")), new Version(2, 0), null, default));

        Assert.Equal(current, store.Current);
        Assert.False(File.Exists(temp.Combine("evil.txt")));
        Assert.DoesNotContain(Directory.GetDirectories(store.VersionsDirectory), d => d.EndsWith(".partial"));
    }

    [Fact]
    public async Task CanceledInstallLeavesNoPartialFolder()
    {
        using var temp = new TempDirectory();
        var store = new InstallationStore(temp.Path);
        var archive = WriteZip(temp, ("Game.exe", new string('x', 500_000)));
        using var cancellation = new CancellationTokenSource();
        var progress = new SyncProgress<double>(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new ZipReleaseInstaller(store).InstallAsync(archive, new Version(1, 0), progress, cancellation.Token));

        Assert.Null(store.Current);
        Assert.Empty(Directory.GetDirectories(store.VersionsDirectory));
    }

    [Fact]
    public async Task InstallReportsProgressInSteps()
    {
        using var temp = new TempDirectory();
        var archive = WriteZip(temp, ("Game.exe", new string('x', 400_000)), ("b.bin", new string('y', 400_000)));
        var reports = new List<double>();

        await new ZipReleaseInstaller(new InstallationStore(temp.Path)).InstallAsync(archive, new Version(1, 0), new SyncProgress<double>(reports.Add), default);

        Assert.True(reports.Distinct().Count() > 5);
        Assert.Equal(reports.Order(), reports);
        Assert.Equal(1, reports.Last());
    }

    [Fact]
    public void MissingOrCorruptStateMeansNothingIsInstalled()
    {
        using var temp = new TempDirectory();
        var store = new InstallationStore(temp.Path);
        Assert.Null(store.Current);

        temp.Write("installed.json", "{ not json");
        Assert.Null(store.Current);

        temp.Write("installed.json", """{ "Version": "1.0", "Folder": "..\\.." }""");
        Assert.Null(store.Current);
    }

    [Fact]
    public void ExecutableIsFoundAutomaticallyIgnoringTheUnityCrashHandler()
    {
        using var temp = new TempDirectory();
        temp.Write("Erdenstone.exe", "");
        temp.Write("UnityCrashHandler64.exe", "");

        Assert.Equal(temp.Combine("Erdenstone.exe"), ExecutableLocator.Find(temp.Path, null));
    }

    [Fact]
    public void SeveralExecutablesNeedTheProfileSetting()
    {
        using var temp = new TempDirectory();
        temp.Write("A.exe", "");
        temp.Write("Tools/B.exe", "");
        temp.Write("B.exe", "");

        Assert.Throws<InvalidDataException>(() => ExecutableLocator.Find(temp.Path, null));
        Assert.Equal(temp.Combine("Tools", "B.exe"), ExecutableLocator.Find(temp.Path, "Tools/B.exe"));
        Assert.Throws<InvalidDataException>(() => ExecutableLocator.Find(temp.Path, "../B.exe"));
        Assert.Throws<InvalidDataException>(() => ExecutableLocator.Find(temp.Path, "Missing.exe"));
    }

    private static string WriteZip(TempDirectory temp, params (string Name, string Content)[] entries)
    {
        var path = temp.Combine("downloads", $"{Guid.NewGuid():N}.zip");
        Zips.Write(path, entries);
        return path;
    }
}
