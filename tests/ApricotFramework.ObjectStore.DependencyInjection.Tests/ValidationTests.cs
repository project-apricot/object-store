using ApricotFramework.ObjectStore.DependencyInjection.Extensions;
using ApricotFramework.ObjectStore.DependencyInjection.Validation;
using ApricotFramework.ObjectStore.Impl;
using ApricotFramework.ObjectStore.Options;
using ApricotFramework.ObjectStore.S3.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ApricotFramework.ObjectStore.DependencyInjection.Tests;

public class ValidationTests
{
    private static OptionsValidationException Fails(Action<IServiceCollection> configure)
    {
        using var provider = Build.Provider(configure);

        // what the host's startup does, and what the first use of the options would do regardless
        return Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ObjectStoreOptions>>().Value);
    }

    private static string Reasons(OptionsValidationException e) => string.Join(" | ", e.Failures);

    [Fact]
    public void NoProviderRegistered_FailsAndSaysHowToFixIt()
    {
        var e = Fails(s => s.AddObjectStores(o => o.Stores["a"] = new ObjectStoreEntry { Provider = "s3" }));

        Assert.Contains("No object store provider is registered", Reasons(e), StringComparison.Ordinal);
        Assert.Contains("AddS3ObjectStoreProvider", Reasons(e), StringComparison.Ordinal);
    }

    [Fact]
    public void NoStoresAndNoSource_Fails()
    {
        var e = Fails(s => s.AddObjectStores(_ => { }).AddMemoryObjectStoreProvider());

        Assert.Contains("No object store is configured under 'ObjectStore:Stores'", Reasons(e), StringComparison.Ordinal);
    }

    [Fact]
    public void NoStores_ButASource_IsAllowed()
    {
        using var provider = Build.Provider(s => s
            .AddObjectStores(_ => { })
            .AddMemoryObjectStoreProvider()
            .AddObjectStoreDefinitionSource(new FixedSource(new ObjectStoreDefinition { Name = "x", Provider = "memory" })));

        Assert.NotNull(provider.GetRequiredService<IOptions<ObjectStoreOptions>>().Value);
    }

    [Fact]
    public void UnknownProvider_NamesTheRegisteredOnes()
    {
        var e = Fails(s => s.AddObjectStores(o =>
        {
            o.DefaultStore = "a";
            o.Stores["a"] = new ObjectStoreEntry { Provider = "gcs" };
        }).AddMemoryObjectStoreProvider());

        Assert.Contains("'gcs'", Reasons(e), StringComparison.Ordinal);
        Assert.Contains("memory", Reasons(e), StringComparison.Ordinal);
    }

    [Fact]
    public void StoreWithNoProvider_IsReported()
    {
        var e = Fails(s => s.AddObjectStores(o =>
        {
            o.DefaultStore = "a";
            o.Stores["a"] = new ObjectStoreEntry();
        }).AddMemoryObjectStoreProvider());

        Assert.Contains("does not name a provider", Reasons(e), StringComparison.Ordinal);
    }

    [Fact]
    public void NullEntry_IsReported()
    {
        var e = Fails(s => s.AddObjectStores(o =>
        {
            o.DefaultStore = "a";
            o.Stores["a"] = null!;
        }).AddMemoryObjectStoreProvider());

        Assert.Contains("no settings at all", Reasons(e), StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderProblems_AreReportedAtStartup_ForEveryStore()
    {
        var e = Fails(s => s
            .AddObjectStoreProvider(new CapturingProvider("capture", "Setting 'Bucket' is required."))
            .AddObjectStores(o =>
            {
                o.DefaultStore = "a";
                o.Stores["a"] = new ObjectStoreEntry { Provider = "capture" };
                o.Stores["b"] = new ObjectStoreEntry { Provider = "capture" };
            }));

        Assert.Equal(2, e.Failures.Count(f => f.Contains("Bucket", StringComparison.Ordinal)));
    }

    [Fact]
    public void ARealS3Provider_CatchesAMissingBucketAndAHalfAKeyPair_BeforeTheFirstUpload()
    {
        var e = Fails(s => s.AddS3ObjectStoreProvider().AddObjectStores(o =>
        {
            o.DefaultStore = "nobucket";
            o.Stores["nobucket"] = new ObjectStoreEntry { Provider = "s3" };
            o.Stores["halfkey"] = new ObjectStoreEntry { Provider = "s3", Settings = { ["Bucket"] = "b", ["AccessKeyId"] = "only-the-id" } };
        }));

        Assert.Contains("'Bucket' is required for object store 'nobucket'", Reasons(e), StringComparison.Ordinal);
        Assert.Contains("must be set together", Reasons(e), StringComparison.Ordinal);
        Assert.DoesNotContain("only-the-id", Reasons(e), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidS3Store_Passes_WithoutNeedingAServer()
    {
        using var provider = Build.Provider(s => s.AddS3ObjectStoreProvider().AddObjectStores(o =>
        {
            o.DefaultStore = "ok";
            o.Stores["ok"] = new ObjectStoreEntry { Provider = "s3", Settings = { ["Bucket"] = "b", ["Region"] = "eu-west-1" } };
        }));

        Assert.NotNull(provider.GetRequiredService<IOptions<ObjectStoreOptions>>().Value);
    }

    [Fact]
    public void DefaultStore_MustExist()
    {
        var e = Fails(s => s.AddObjectStores(o =>
        {
            o.DefaultStore = "missing";
            o.Stores["a"] = new ObjectStoreEntry { Provider = "memory" };
        }).AddMemoryObjectStoreProvider());

        Assert.Contains("'missing'", Reasons(e), StringComparison.Ordinal);
        Assert.Contains("Configured stores: a", Reasons(e), StringComparison.Ordinal);
    }

    [Fact]
    public void NoDefaultSet_IsNotAFailure_ForAHostThatOnlyUsesNamedStores()
    {
        using var provider = Build.Provider(s => s.AddObjectStores(o =>
        {
            o.Stores["uploads"] = new ObjectStoreEntry { Provider = "memory" };
            o.Stores["exports"] = new ObjectStoreEntry { Provider = "memory" };
        }).AddMemoryObjectStoreProvider());

        // startup passes, and the named stores work
        Assert.NotNull(provider.GetRequiredService<IOptions<ObjectStoreOptions>>().Value);
        Assert.Equal("uploads", provider.GetRequiredKeyedService<IObjectStore>("uploads").Name);
        Assert.Equal("exports", provider.GetRequiredService<IObjectStoreFactory>().GetStore("exports").Name);
    }

    [Fact]
    public void NoDefaultSet_OnlyFailsWhereTheDefaultIsAskedFor_NamingWhatIsMissing()
    {
        using var provider = Build.Provider(s => s.AddObjectStores(o => o.Stores["uploads"] = new ObjectStoreEntry { Provider = "memory" }).AddMemoryObjectStoreProvider());

        var e = Assert.Throws<ApricotFramework.ObjectStore.Exceptions.ObjectStoreException>(() => provider.GetRequiredService<IObjectStore>());

        Assert.Equal(ApricotFramework.ObjectStore.Exceptions.ObjectStoreErrorCode.StoreNotFound, e.ErrorCode);
        Assert.Contains("'default'", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADefaultStoreCalledDefault_NeedsNoSetting()
    {
        using var provider = Build.Provider(s => s.AddObjectStores(o => o.Stores["default"] = new ObjectStoreEntry { Provider = "memory" }).AddMemoryObjectStoreProvider());

        Assert.Equal("default", provider.GetRequiredService<IObjectStore>().Name);
    }

    [Fact]
    public void DefaultStoreCheck_IsRelaxedWhenASourceMightHoldIt()
    {
        using var provider = Build.Provider(s => s
            .AddObjectStores(o =>
            {
                o.DefaultStore = "from-source";
                o.Stores["a"] = new ObjectStoreEntry { Provider = "memory" };
            })
            .AddMemoryObjectStoreProvider()
            .AddObjectStoreDefinitionSource(new FixedSource(new ObjectStoreDefinition { Name = "from-source", Provider = "memory" })));

        Assert.NotNull(provider.GetRequiredService<IOptions<ObjectStoreOptions>>().Value);
    }

    [Fact]
    public void AStoreDeclaredAsABareValue_IsNotSilentlyDropped()
    {
        // the binder gives up on this without a word; nothing else would notice the store is simply gone
        var configuration = Build.Config(new Dictionary<string, string?>
        {
            ["ObjectStore:Stores:ok:Provider"] = "memory",
            ["ObjectStore:Stores:broken"] = "just-a-string",
        });

        var e = Fails(s => s.AddObjectStores(configuration).AddMemoryObjectStoreProvider());

        Assert.Contains("'broken' is declared as a single value", Reasons(e), StringComparison.Ordinal);
    }

    [Fact]
    public void AllProblems_AreReportedTogether_NotOneAtATime()
    {
        var e = Fails(s => s.AddObjectStores(o =>
        {
            o.DefaultStore = "missing";
            o.Stores["a"] = new ObjectStoreEntry();
            o.Stores["b"] = new ObjectStoreEntry { Provider = "gcs" };
        }).AddMemoryObjectStoreProvider());

        Assert.True(e.Failures.Count() >= 3);
    }

    [Fact]
    public void Validator_NullOptions_Throws()
    {
        var validator = new ObjectStoreOptionsValidator([], []);

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null, null!));
    }

    [Fact]
    public void Validator_DuplicateProviders_AreRefused()
    {
        Assert.Throws<ArgumentException>(() => new ObjectStoreOptionsValidator([new MemoryObjectStoreProvider(), new MemoryObjectStoreProvider()], []));
    }
}
