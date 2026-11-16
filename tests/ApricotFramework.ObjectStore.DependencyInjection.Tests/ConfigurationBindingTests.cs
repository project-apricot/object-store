using System.Text;
using ApricotFramework.ObjectStore.DependencyInjection.Extensions;
using ApricotFramework.ObjectStore.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApricotFramework.ObjectStore.DependencyInjection.Tests;

public class ConfigurationBindingTests
{
    private const string Json = """
        {
          "ObjectStore": {
            "DefaultStore": "uploads",
            "Stores": {
              "uploads": {
                "Provider": "capture",
                "Settings": { "Bucket": "from-file", "Prefix": "svc-a", "Port": 5, "Enabled": true }
              },
              "avatars": { "Provider": "capture", "Settings": { "Bucket": "avatars-bucket" } }
            }
          }
        }
        """;

    private static IConfigurationBuilder FromJson(string json = Json)
    {
        return new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Stores_AreBoundFromJson_WithNumbersAndBooleansAsText()
    {
        var capture = new CapturingProvider();

        using var provider = Build.Provider(s => s.AddObjectStores(FromJson().Build()).AddObjectStoreProvider(capture));

        var store = provider.GetRequiredService<IObjectStoreFactory>().GetStore("uploads");
        var settings = Assert.Single(capture.Definitions).Settings;

        Assert.Equal("uploads", store.Name);
        Assert.Equal("from-file", settings.GetString("Bucket"));
        Assert.Equal("svc-a", settings.GetString("Prefix"));
        Assert.Equal(5, settings.GetInt32("Port", 0));
        Assert.True(settings.GetBoolean("Enabled", false));
    }

    [Fact]
    public void EnvironmentVariables_OverrideTheFile()
    {
        // a real environment variable, in the shape a deployment would set it
        var prefix = $"OSTEST{Guid.NewGuid():N}_";

        Environment.SetEnvironmentVariable($"{prefix}ObjectStore__Stores__uploads__Settings__Bucket", "from-environment");
        Environment.SetEnvironmentVariable($"{prefix}ObjectStore__Stores__uploads__Settings__AccessKeyId", "env-only-key");

        try
        {
            var configuration = FromJson().AddEnvironmentVariables(prefix).Build();
            var capture = new CapturingProvider();

            using var provider = Build.Provider(s => s.AddObjectStores(configuration).AddObjectStoreProvider(capture));

            provider.GetRequiredService<IObjectStoreFactory>().GetStore("uploads");

            var settings = Assert.Single(capture.Definitions).Settings;

            Assert.Equal("from-environment", settings.GetString("Bucket"));
            Assert.Equal("env-only-key", settings.GetString("AccessKeyId"));

            // what the environment did not mention is still the file's
            Assert.Equal("svc-a", settings.GetString("Prefix"));
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{prefix}ObjectStore__Stores__uploads__Settings__Bucket", null);
            Environment.SetEnvironmentVariable($"{prefix}ObjectStore__Stores__uploads__Settings__AccessKeyId", null);
        }
    }

    [Fact]
    public void EnvironmentVariables_CanDeclareAWholeStore()
    {
        var prefix = $"OSTEST{Guid.NewGuid():N}_";

        Environment.SetEnvironmentVariable($"{prefix}ObjectStore__Stores__fromenv__Provider", "capture");
        Environment.SetEnvironmentVariable($"{prefix}ObjectStore__Stores__fromenv__Settings__Bucket", "b");
        Environment.SetEnvironmentVariable($"{prefix}ObjectStore__DefaultStore", "fromenv");

        try
        {
            var capture = new CapturingProvider();

            using var provider = Build.Provider(s => s.AddObjectStores(new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build()).AddObjectStoreProvider(capture));

            Assert.Equal("fromenv", provider.GetRequiredService<IObjectStore>().Name);
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{prefix}ObjectStore__Stores__fromenv__Provider", null);
            Environment.SetEnvironmentVariable($"{prefix}ObjectStore__Stores__fromenv__Settings__Bucket", null);
            Environment.SetEnvironmentVariable($"{prefix}ObjectStore__DefaultStore", null);
        }
    }

    [Fact]
    public void NamedSection_IsBound()
    {
        var capture = new CapturingProvider();
        var configuration = Build.Config(new Dictionary<string, string?>
        {
            ["Custom:Stores:x:Provider"] = "capture",
            ["Custom:DefaultStore"] = "x",
        });

        using var provider = Build.Provider(s => s.AddObjectStores(configuration, "Custom").AddObjectStoreProvider(capture));

        Assert.Equal("x", provider.GetRequiredService<IObjectStore>().Name);
    }

    [Fact]
    public void CodeConfiguration_Works()
    {
        var capture = new CapturingProvider();

        using var provider = Build.Provider(s => s
            .AddObjectStores(o =>
            {
                o.DefaultStore = "code";
                o.Stores["code"] = new ObjectStoreEntry { Provider = "capture" };
            })
            .AddObjectStoreProvider(capture));

        Assert.Equal("code", provider.GetRequiredService<IObjectStore>().Name);
    }

    [Fact]
    public void StoreNames_AreMatchedWithoutRegardToCase()
    {
        var capture = new CapturingProvider();

        using var provider = Build.Provider(s => s.AddObjectStores(FromJson().Build()).AddObjectStoreProvider(capture));

        Assert.Same(
            provider.GetRequiredService<IObjectStoreFactory>().GetStore("AVATARS"),
            provider.GetRequiredService<IObjectStoreFactory>().GetStore("avatars"));
    }
}
