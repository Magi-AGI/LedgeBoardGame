using MagiGameServer.Modules;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MagiGameServer.Host.Tests
{
    // Wraps WebApplicationFactory to swap in a module registry
    // populated with the test-only CounterGameModule. The production
    // Program.CreateApp reads GameModuleRegistry from DI, so replacing
    // the singleton here is the whole customization the tests need.
    internal sealed class TestHostFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<GameModuleRegistry>();
                services.AddSingleton<GameModuleRegistry>(_ =>
                {
                    var registry = new GameModuleRegistry();
                    registry.Register(new TestCounterModule());
                    return registry;
                });
            });
        }
    }
}
