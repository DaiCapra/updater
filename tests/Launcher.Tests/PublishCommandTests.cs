using System.IO;
using Launcher.Publish;
using Launcher.Services;
using Launcher.Services.Providers;
using Launcher.Tests.Fakes;
using Xunit;

namespace Launcher.Tests;

public class PublishCommandTests
{
    [Theory]
    [InlineData]
    [InlineData("--target", "publish.json")]
    [InlineData("--file", "build-1.0.zip")]
    [InlineData("--target", "a.json", "--file", "build-1.0.zip", "--force")]
    [InlineData("--target", "a.json", "--target", "b.json", "--file", "build-1.0.zip")]
    public async Task InvalidArgumentsPrintUsage(params string[] args)
    {
        var (command, _, errors) = Create(new InMemoryReleaseStore());

        Assert.Equal(2, await command.RunAsync(args, default));
        Assert.Contains(PublishCommand.Usage, errors.ToString());
    }

    [Fact]
    public async Task PublishesThroughTheProviderNamedInTheTargetFile()
    {
        using var temp = new TempDirectory();
        var target = temp.Write("publish.json", """{ "type": "memory", "name": "releases" }""");
        var file = temp.Write("build-1.4.0.zip", "zip bytes");
        var store = new InMemoryReleaseStore();
        var (command, output, _) = Create(store);

        Assert.Equal(0, await command.RunAsync(["--target", target, "--file", file], default));

        Assert.Equal("build-1.4.0.zip", Assert.Single(await store.ListAsync(default)).Name);
        Assert.Contains("Published version 1.4.0", output.ToString());
    }

    [Fact]
    public async Task DryRunDoesNotUpload()
    {
        using var temp = new TempDirectory();
        var target = temp.Write("publish.json", """{ "type": "memory", "name": "releases" }""");
        var file = temp.Write("build-1.4.0.zip", "zip bytes");
        var store = new InMemoryReleaseStore();
        var (command, output, _) = Create(store);

        Assert.Equal(0, await command.RunAsync(["--target", target, "--file", file, "--dry-run"], default));

        Assert.Equal(0, store.Uploads);
        Assert.Contains("Dry run", output.ToString());
    }

    [Fact]
    public async Task ExistingVersionIsReportedAsAnError()
    {
        using var temp = new TempDirectory();
        var target = temp.Write("publish.json", """{ "type": "memory", "name": "releases" }""");
        var file = temp.Write("build-1.4.0.zip", "zip bytes");
        var store = new InMemoryReleaseStore();
        store.Add("build-1.4.zip", [1]);
        var (command, _, errors) = Create(store);

        Assert.Equal(1, await command.RunAsync(["--target", target, "--file", file], default));
        Assert.Contains("already published", errors.ToString());
    }

    [Fact]
    public async Task ManifestTargetsAreRejected()
    {
        using var temp = new TempDirectory();
        var target = temp.Write("publish.json", """{ "type": "http-json", "url": "https://example.com/latest.json" }""");
        var file = temp.Write("build-1.4.0.zip", "zip bytes");
        var (command, _, errors) = Create(new InMemoryReleaseStore());

        Assert.Equal(1, await command.RunAsync(["--target", target, "--file", file], default));
        Assert.Contains("not a file store", errors.ToString());
    }

    private static (PublishCommand Command, StringWriter Output, StringWriter Errors) Create(InMemoryReleaseStore store)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var providers = new ProviderRegistry([new InMemoryStorageProvider(store)], [new HttpJsonSourceFactory()]);
        return (new PublishCommand(providers, output, errors), output, errors);
    }
}
