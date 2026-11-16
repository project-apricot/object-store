using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApricotFramework.ObjectStore.DependencyInjection.Tests;

internal static class Build
{
    public static IConfiguration Config(IDictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    public static ServiceProvider Provider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();

        configure(services);

        // what a host does in Development: a singleton that captures a scoped service fails here
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
