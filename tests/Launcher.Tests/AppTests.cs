using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Launcher.Tests;

public class AppTests
{
    [Fact]
    public void StartupContainerRegistersMainWindowAndResolvesItsViewModel()
    {
        using var services = App.CreateServices();

        var registrations = services.GetRequiredService<IServiceProviderIsService>();
        Assert.True(registrations.IsService(typeof(MainWindow)));
        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        Assert.False(viewModel.CanCheckForUpdates);
    }
}
