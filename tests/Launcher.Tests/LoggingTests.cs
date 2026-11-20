using System.IO;
using Launcher.Core;
using Launcher.Core.Installation;
using Launcher.Core.Logging;
using Launcher.Tests.Fakes;
using Xunit;

namespace Launcher.Tests;

public class LoggingTests
{
    [Fact]
    public void LinesAreWrittenWithTimestampAndLevel()
    {
        using var temp = new TempDirectory();
        var log = new RollingFileLog(temp.Path);

        log.Info("Checking for updates…");
        log.Error("Install failed.", new InvalidDataException("bad zip"));

        var text = File.ReadAllText(log.CurrentPath);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[INFO\] Checking for updates…", text);
        Assert.Contains("[ERROR] Install failed.", text);
        Assert.Contains("System.IO.InvalidDataException: bad zip", text);
    }

    [Fact]
    public void FullLogRotatesAndKeepsOnlyTheNewestFiles()
    {
        using var temp = new TempDirectory();
        var log = new RollingFileLog(temp.Path, maxBytes: 200, keepFiles: 3);

        for (var i = 0; i < 40; i++)
        {
            log.Info($"line {i:00} " + new string('x', 40));
        }

        Assert.Equal(["launcher.1.log", "launcher.2.log", "launcher.log"], Directory.GetFiles(temp.Path).Select(Path.GetFileName).Order());
        Assert.All(Directory.GetFiles(temp.Path), file => Assert.True(new FileInfo(file).Length <= 200));
        Assert.Contains("line 39", File.ReadAllText(temp.Combine("launcher.log")));
        Assert.DoesNotContain("line 00", string.Concat(Directory.GetFiles(temp.Path).Select(File.ReadAllText)));
    }

    [Fact]
    public void LoggingNeverThrowsWhenTheFileIsLocked()
    {
        using var temp = new TempDirectory();
        var log = new RollingFileLog(temp.Path);
        log.Info("first");

        using (new FileStream(log.CurrentPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            log.Info("while locked");
        }

        log.Info("after");
        Assert.Contains("after", File.ReadAllText(log.CurrentPath));
    }

    [Fact]
    public void ViewModelLogsStatusChangesAndFailures()
    {
        using var temp = new TempDirectory();
        var installation = new InstallationStore(temp.Combine("install"));
        temp.Write("install/versions/1.0/readme.txt", "");
        installation.Activate(new Version(1, 0), "1.0");
        var log = new RollingFileLog(temp.Combine("logs"));
        var viewModel = new MainWindowViewModel(new LauncherProfile("Game"), new FakeShell(), installation: installation, log: log);

        viewModel.Play();

        var text = File.ReadAllText(log.CurrentPath);
        Assert.Contains("[ERROR] Could not start the app.", text);
        Assert.Contains("InvalidDataException", text);
    }
}
