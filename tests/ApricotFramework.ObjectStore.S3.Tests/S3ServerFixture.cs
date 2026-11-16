using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace ApricotFramework.ObjectStore.S3.Tests;

/// <summary>
/// An S3-compatible server in a container, shared by every integration test.
/// </summary>
/// <remarks>
/// Credentials are made up per run and never written anywhere. When Docker is not available the fixture
/// records why instead of failing, and <see cref="Require"/> turns that into a skipped test.
/// </remarks>
public sealed class S3ServerFixture : IAsyncLifetime
{
    /// <summary>
    /// Overrides the server image. It must answer S3 on port 9000, take its credentials from
    /// <c>RUSTFS_ACCESS_KEY</c> and <c>RUSTFS_SECRET_KEY</c>, and report health at <c>/health</c>.
    /// </summary>
    public const string ImageVariable = "OBJECTSTORE_TEST_S3_IMAGE";

    /// <summary>
    /// The image used when none is named in the environment.
    /// </summary>
    private const string DefaultImage = "rustfs/rustfs:latest";

    /// <summary>
    /// The port the S3 API listens on inside the container.
    /// </summary>
    private const int Port = 9000;

    /// <summary>
    /// The running server, or null when it could not be started.
    /// </summary>
    private IContainer? container;

    public string AccessKey { get; } = "ak" + Guid.NewGuid().ToString("N")[..12];

    public string SecretKey { get; } = "sk" + Guid.NewGuid().ToString("N");

    public Uri? Endpoint { get; private set; }

    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            this.container = new ContainerBuilder(Environment.GetEnvironmentVariable(ImageVariable) ?? DefaultImage)
                .WithPortBinding(Port, assignRandomHostPort: true)
                .WithEnvironment("RUSTFS_ACCESS_KEY", this.AccessKey)
                .WithEnvironment("RUSTFS_SECRET_KEY", this.SecretKey)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(Port).ForPath("/health")))
                .Build();

            await this.container.StartAsync();

            this.Endpoint = new Uri($"http://{this.container.Hostname}:{this.container.GetMappedPublicPort(Port)}");
        }
        catch (Exception e)
        {
            // Docker missing, not running, or the image not pullable: the integration tests skip, the rest still run
            this.UnavailableReason = $"No S3-compatible test server could be started ({e.GetType().Name}). Is Docker running?";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (this.container is not null)
        {
            await this.container.DisposeAsync();
        }
    }

    public void Require()
    {
        Assert.SkipWhen(this.UnavailableReason is not null, this.UnavailableReason ?? string.Empty);
    }

    /// <summary>
    /// Makes a bucket nobody else is using, so tests never see each other's objects.
    /// </summary>
    public async Task<string> CreateBucketAsync()
    {
        this.Require();

        var bucket = "t-" + Guid.NewGuid().ToString("N")[..20];

        using var client = new AmazonS3Client(
            new Amazon.Runtime.BasicAWSCredentials(this.AccessKey, this.SecretKey),
            new AmazonS3Config { ServiceURL = this.Endpoint!.AbsoluteUri.TrimEnd('/'), ForcePathStyle = true, AuthenticationRegion = "us-east-1" });

        await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket });

        return bucket;
    }

    /// <summary>
    /// Builds a store over a bucket, with whatever the test wants changed.
    /// </summary>
    public S3ObjectStore CreateStore(string bucket, Action<Dictionary<string, string?>>? tweak = null, string name = "test")
    {
        this.Require();

        var settings = new Dictionary<string, string?>
        {
            ["Bucket"] = bucket,
            ["ServiceUrl"] = this.Endpoint!.AbsoluteUri,
            ["AccessKeyId"] = this.AccessKey,
            ["SecretAccessKey"] = this.SecretKey,
        };

        tweak?.Invoke(settings);

        return new S3ObjectStore(name, S3StoreSettings.From(new ObjectStoreSettings(settings, name)));
    }

    /// <summary>
    /// Builds a store over a fresh bucket.
    /// </summary>
    public async Task<(S3ObjectStore Store, string Bucket)> CreateStoreAsync(Action<Dictionary<string, string?>>? tweak = null)
    {
        var bucket = await this.CreateBucketAsync();

        return (this.CreateStore(bucket, tweak), bucket);
    }

    /// <summary>
    /// Gets an SDK client for looking at the server behind the store's back.
    /// </summary>
    public AmazonS3Client CreateRawClient()
    {
        this.Require();

        return new AmazonS3Client(
            new Amazon.Runtime.BasicAWSCredentials(this.AccessKey, this.SecretKey),
            new AmazonS3Config { ServiceURL = this.Endpoint!.AbsoluteUri.TrimEnd('/'), ForcePathStyle = true, AuthenticationRegion = "us-east-1" });
    }
}
