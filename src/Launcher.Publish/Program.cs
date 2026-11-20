using Launcher.Publish;
using Launcher.Services;
using Launcher.Services.Providers;
using Launcher.Storage.GoogleDrive;

// Every host releases can be published to. Register new hosts here and in the launcher's App.CreateProviders.
var providers = new ProviderRegistry(
    storageProviders: [new GoogleDriveStorageProvider()],
    sourceFactories: [new HttpJsonSourceFactory()]);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

return await new PublishCommand(providers, Console.Out, Console.Error).RunAsync(args, cancellation.Token);
