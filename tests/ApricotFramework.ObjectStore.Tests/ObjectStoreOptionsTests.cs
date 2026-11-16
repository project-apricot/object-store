using ApricotFramework.ObjectStore.Exceptions;
using ApricotFramework.ObjectStore.Options;

namespace ApricotFramework.ObjectStore.Tests;

public class ObjectStoreOptionsTests
{
    [Fact]
    public void ToDefinition_MapsProviderAndSettings()
    {
        var entry = new ObjectStoreEntry { Provider = " s3 " };

        entry.Settings["Bucket"] = "b";

        var definition = entry.ToDefinition("uploads");

        Assert.Equal("uploads", definition.Name);
        Assert.Equal("s3", definition.Provider);
        Assert.Equal("b", definition.Settings.GetString("bucket"));
    }

    [Fact]
    public void ToDefinition_NoProvider_ThrowsNamingTheStore()
    {
        var e = Assert.Throws<ObjectStoreConfigurationException>(() => new ObjectStoreEntry().ToDefinition("uploads"));

        Assert.Contains("uploads", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToDefinition_SettingsErrorsNameTheStore()
    {
        var definition = new ObjectStoreEntry { Provider = "s3" }.ToDefinition("uploads");

        var e = Assert.Throws<ObjectStoreConfigurationException>(() => definition.Settings.GetRequiredString("Bucket"));

        Assert.Contains("uploads", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Stores_AreMatchedWithoutRegardToCase()
    {
        var options = new ObjectStoreOptions();

        options.Stores["Uploads"] = new ObjectStoreEntry();

        Assert.True(options.Stores.ContainsKey("uploads"));
    }
}
