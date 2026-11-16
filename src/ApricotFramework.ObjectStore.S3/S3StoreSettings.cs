using System.Security.Cryptography;
using System.Text;
using ApricotFramework.ObjectStore.Exceptions;

namespace ApricotFramework.ObjectStore.S3;

/// <summary>
/// The settings <see cref="S3ObjectStoreProvider"/> reads from a store definition.
/// </summary>
/// <remarks>
/// A class rather than a record, and with no <c>ToString</c>, because it holds a secret key.
/// </remarks>
public sealed class S3StoreSettings
{
    /// <summary>
    /// The setting naming the bucket.
    /// </summary>
    public const string BucketSetting = "Bucket";

    /// <summary>
    /// The setting naming the key prefix every object of the store lives under.
    /// </summary>
    public const string PrefixSetting = "Prefix";

    /// <summary>
    /// The setting naming the AWS region, such as <c>eu-west-1</c>.
    /// </summary>
    public const string RegionSetting = "Region";

    /// <summary>
    /// The setting naming a custom endpoint, for S3-compatible servers.
    /// </summary>
    public const string ServiceUrlSetting = "ServiceUrl";

    /// <summary>
    /// The setting naming the endpoint presigned URLs are signed for, when clients reach the server by
    /// a different address than this service does.
    /// </summary>
    public const string PublicServiceUrlSetting = "PublicServiceUrl";

    /// <summary>
    /// The setting choosing path-style addressing, <c>host/bucket/key</c>, over virtual-hosted.
    /// </summary>
    public const string ForcePathStyleSetting = "ForcePathStyle";

    /// <summary>
    /// The setting naming the access key to authenticate with.
    /// </summary>
    public const string AccessKeyIdSetting = "AccessKeyId";

    /// <summary>
    /// The setting naming the secret key to authenticate with.
    /// </summary>
    public const string SecretAccessKeySetting = "SecretAccessKey";

    /// <summary>
    /// The setting naming the session token that goes with temporary credentials.
    /// </summary>
    public const string SessionTokenSetting = "SessionToken";

    /// <summary>
    /// The setting naming how long to wait on the server for one request.
    /// </summary>
    public const string TimeoutSetting = "Timeout";

    /// <summary>
    /// The setting naming how long a presigned URL works when the caller does not say.
    /// </summary>
    public const string PresignedExpirySetting = "PresignedExpiry";

    /// <summary>
    /// The setting naming the longest a presigned URL may work.
    /// </summary>
    public const string MaxPresignedExpirySetting = "MaxPresignedExpiry";

    /// <summary>
    /// The setting naming the size of each part of an upload too large to send in one request.
    /// </summary>
    public const string MultipartPartSizeSetting = "MultipartPartSize";

    /// <summary>
    /// The setting choosing when the SDK adds integrity checksums.
    /// </summary>
    public const string ChecksumModeSetting = "ChecksumMode";

    /// <summary>
    /// The region assumed for a custom endpoint that is given none, which servers other than AWS ignore
    /// but request signing still needs.
    /// </summary>
    public const string DefaultServiceRegion = "us-east-1";

    /// <summary>
    /// The smallest part S3 accepts, except for the last.
    /// </summary>
    public const long MinimumPartSize = 5L * 1024 * 1024;

    /// <summary>
    /// The largest part this provider will send. S3 takes up to 5 GiB, but a part is held in memory while it is
    /// sent, so the limit is what a process can reasonably hold. Ten thousand of them cover objects up to about
    /// 4.9 TiB, a little under the 5 TiB S3 allows; anything larger is refused before it is sent.
    /// </summary>
    public const long MaximumPartSize = 512L * 1024 * 1024;

    private S3StoreSettings(string bucket, string prefix)
    {
        this.Bucket = bucket;
        this.Prefix = prefix;
    }

    /// <summary>
    /// The time allowed on the server when the store does not say.
    /// </summary>
    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The lifetime of a presigned URL when neither the caller nor the store says.
    /// </summary>
    public static TimeSpan DefaultPresignedExpiry { get; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The longest a presigned URL may work unless the store allows more.
    /// </summary>
    public static TimeSpan DefaultMaxPresignedExpiry { get; } = TimeSpan.FromHours(1);

    /// <summary>
    /// The longest any presigned URL can work, which is the limit of AWS signature version 4.
    /// </summary>
    public static TimeSpan AbsoluteMaxPresignedExpiry { get; } = TimeSpan.FromDays(7);

    /// <summary>
    /// The part size used when the store does not say.
    /// </summary>
    public static long DefaultPartSize { get; } = 8L * 1024 * 1024;

    /// <summary>
    /// Gets the bucket.
    /// </summary>
    public string Bucket { get; }

    /// <summary>
    /// Gets the key prefix, which is empty or ends with <c>/</c>.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// Gets how many bytes of the longest key S3 allows the prefix takes up.
    /// </summary>
    public int PrefixBytes => Encoding.UTF8.GetByteCount(this.Prefix);

    /// <summary>
    /// Gets the AWS region, or null to let the SDK work it out.
    /// </summary>
    public string? Region { get; private init; }

    /// <summary>
    /// Gets the custom endpoint, or null for AWS.
    /// </summary>
    public Uri? ServiceUrl { get; private init; }

    /// <summary>
    /// Gets the endpoint presigned URLs are signed for, or null to use <see cref="ServiceUrl"/>.
    /// </summary>
    public Uri? PublicServiceUrl { get; private init; }

    /// <summary>
    /// Gets whether addressing is path-style.
    /// </summary>
    public bool ForcePathStyle { get; private init; }

    /// <summary>
    /// Gets the access key, or null to use the SDK's default credential chain.
    /// </summary>
    public string? AccessKeyId { get; private init; }

    /// <summary>
    /// Gets the secret key, or null when there is no access key.
    /// </summary>
    public string? SecretAccessKey { get; private init; }

    /// <summary>
    /// Gets the session token, or null for long-lived credentials.
    /// </summary>
    public string? SessionToken { get; private init; }

    /// <summary>
    /// Gets how long to wait on the server for one request.
    /// </summary>
    public TimeSpan Timeout { get; private init; }

    /// <summary>
    /// Gets how long a presigned URL works when the caller does not say.
    /// </summary>
    public TimeSpan PresignedExpiry { get; private init; }

    /// <summary>
    /// Gets the longest a presigned URL may work.
    /// </summary>
    public TimeSpan MaxPresignedExpiry { get; private init; }

    /// <summary>
    /// Gets the size of each part of an upload too large to send in one request.
    /// </summary>
    public long MultipartPartSize { get; private init; }

    /// <summary>
    /// Gets when the SDK adds integrity checksums.
    /// </summary>
    public S3ChecksumMode ChecksumMode { get; private init; }

    /// <summary>
    /// Gets the region requests are signed for.
    /// </summary>
    public string? SigningRegion => this.Region ?? (this.ServiceUrl is not null ? DefaultServiceRegion : null);

    /// <summary>
    /// Gets a label that is equal for two stores that reach the same server as the same identity, so a copy
    /// between them can happen on the server.
    /// </summary>
    /// <remarks>
    /// Exact, since access keys are case-sensitive, and it includes the secret and the session token, so two
    /// stores that share an access key but not what it is signed with are not the same identity. Those two
    /// only go in as a hash, so the label is safe to log.
    /// </remarks>
    public string ConnectionKey
    {
        get
        {
            var credentials = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{this.SecretAccessKey}\0{this.SessionToken}")));

            return string.Join('|', this.ServiceUrl?.AbsoluteUri, this.SigningRegion, this.AccessKeyId, credentials);
        }
    }

    /// <summary>
    /// Reads the S3 settings out of a definition.
    /// </summary>
    /// <param name="settings">The definition's settings.</param>
    /// <returns>The settings, validated.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
    /// <exception cref="ObjectStoreConfigurationException">
    /// Thrown when a setting is missing, unreadable or out of range. No message quotes a value.
    /// </exception>
    public static S3StoreSettings From(ObjectStoreSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var bucket = settings.GetRequiredString(BucketSetting);

        if (bucket.Any(c => c == '/' || char.IsWhiteSpace(c)))
        {
            throw settings.Unreadable(BucketSetting, "a bucket name without slashes or whitespace");
        }

        var serviceUrl = ReadUrl(settings, ServiceUrlSetting);
        var publicUrl = ReadUrl(settings, PublicServiceUrlSetting);

        if (publicUrl is not null && serviceUrl is null)
        {
            throw new ObjectStoreConfigurationException($"Setting '{PublicServiceUrlSetting}' only applies together with '{ServiceUrlSetting}'.");
        }

        var accessKey = settings.GetString(AccessKeyIdSetting);
        var secretKey = settings.GetString(SecretAccessKeySetting);
        var sessionToken = settings.GetString(SessionTokenSetting);

        // half a key pair is never what anyone meant, and falling back to the default chain would hide it
        if ((accessKey is null) != (secretKey is null))
        {
            throw new ObjectStoreConfigurationException($"Settings '{AccessKeyIdSetting}' and '{SecretAccessKeySetting}' must be set together.");
        }

        if (sessionToken is not null && accessKey is null)
        {
            throw new ObjectStoreConfigurationException($"Setting '{SessionTokenSetting}' needs '{AccessKeyIdSetting}' and '{SecretAccessKeySetting}'.");
        }

        var timeout = settings.GetTimeSpan(TimeoutSetting, DefaultTimeout);
        var expiry = settings.GetTimeSpan(PresignedExpirySetting, DefaultPresignedExpiry);
        var maxExpiry = settings.GetTimeSpan(MaxPresignedExpirySetting, DefaultMaxPresignedExpiry);
        var partSize = settings.GetInt64(MultipartPartSizeSetting, DefaultPartSize);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ObjectStoreConfigurationException($"Setting '{TimeoutSetting}' is expected to be a positive duration.");
        }

        if (maxExpiry <= TimeSpan.Zero || maxExpiry > AbsoluteMaxPresignedExpiry)
        {
            throw new ObjectStoreConfigurationException($"Setting '{MaxPresignedExpirySetting}' is expected to be between 00:00:01 and {AbsoluteMaxPresignedExpiry}, the most a signature allows.");
        }

        if (expiry <= TimeSpan.Zero || expiry > maxExpiry)
        {
            throw new ObjectStoreConfigurationException($"Setting '{PresignedExpirySetting}' is expected to be positive and no longer than '{MaxPresignedExpirySetting}'.");
        }

        if (partSize is < MinimumPartSize or > MaximumPartSize)
        {
            throw new ObjectStoreConfigurationException($"Setting '{MultipartPartSizeSetting}' is expected to be between {MinimumPartSize} and {MaximumPartSize} bytes.");
        }

        var region = settings.GetString(RegionSetting)?.Trim();

        return new S3StoreSettings(bucket.Trim(), NormalizePrefix(settings))
        {
            Region = region,
            ServiceUrl = serviceUrl,
            PublicServiceUrl = publicUrl,

            // S3-compatible servers mostly cannot do virtual-hosted addressing, which needs wildcard DNS
            ForcePathStyle = settings.GetBoolean(ForcePathStyleSetting, serviceUrl is not null),
            AccessKeyId = accessKey,
            SecretAccessKey = secretKey,
            SessionToken = sessionToken,
            Timeout = timeout,
            PresignedExpiry = expiry,
            MaxPresignedExpiry = maxExpiry,
            MultipartPartSize = partSize,

            // real AWS wants checksums; the rest generally rejects the encoding that carries them
            ChecksumMode = settings.GetEnum(ChecksumModeSetting, serviceUrl is null ? S3ChecksumMode.WhenSupported : S3ChecksumMode.WhenRequired),
        };
    }

    private static string NormalizePrefix(ObjectStoreSettings settings)
    {
        var prefix = settings.GetString(PrefixSetting)?.Trim();

        if (prefix is null)
        {
            return string.Empty;
        }

        if (ObjectKey.ExplainPrefix(prefix) is not null)
        {
            throw settings.Unreadable(PrefixSetting, "a key prefix that does not start with '/' or contain '..' segments");
        }

        // "avatars" and "avatars/" mean the same folder; without this "avatars" would also claim "avatars-old/"
        var folder = prefix.EndsWith('/') ? prefix : prefix + "/";

        // a prefix that fills the whole key leaves no room for a key
        if (Encoding.UTF8.GetByteCount(folder) >= ObjectKey.MaximumBytes)
        {
            throw settings.Unreadable(PrefixSetting, $"a key prefix shorter than {ObjectKey.MaximumBytes} bytes in UTF-8");
        }

        return folder;
    }

    private static Uri? ReadUrl(ObjectStoreSettings settings, string name)
    {
        var value = settings.GetString(name);

        if (value is null)
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw settings.Unreadable(name, "an absolute http or https URL");
        }

        return uri;
    }
}
