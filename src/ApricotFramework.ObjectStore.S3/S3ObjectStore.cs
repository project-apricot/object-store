using System.Runtime.ExceptionServices;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.Credentials;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using ApricotFramework.ObjectStore.Exceptions;
using ApricotFramework.ObjectStore.Impl;
using ApricotFramework.ObjectStore.Model;
using ApricotFramework.ObjectStore.S3.Impl;

// the AWS SDK has a type of this name too, and the one in the signatures of IObjectStore is ours
using MultipartUpload = ApricotFramework.ObjectStore.Model.MultipartUpload;

namespace ApricotFramework.ObjectStore.S3;

/// <summary>
/// A store over one bucket of AWS S3 or an S3-compatible server, confined to a key prefix.
/// </summary>
/// <remarks>
/// Owns its SDK clients and releases them when disposed. The factory that made it does that.
/// <para>
/// Uploads that fit in one part go as a single request; larger ones, and any stream whose length is
/// unknown, are sent as a multipart upload one part at a time, so memory use is one part however big the
/// object. An upload that fails part-way is aborted rather than left to be billed.
/// </para>
/// </remarks>
public class S3ObjectStore : IObjectStore, IDisposable
{
    /// <summary>
    /// The prefix S3 puts on the name of every user metadata header.
    /// </summary>
    private const string MetadataPrefix = "x-amz-meta-";

    /// <summary>
    /// The most keys S3 returns for one listing request.
    /// </summary>
    private const int MaximumPageSize = 1000;

    /// <summary>
    /// The highest part number a multipart upload can use.
    /// </summary>
    private const int MaximumPartNumber = 10000;

    /// <summary>
    /// The most keys one batch delete request can carry.
    /// </summary>
    private const int MaximumDeleteBatch = 1000;

    /// <summary>
    /// How many parts of a stream of unknown length are sent before the part size doubles, so that however
    /// long the stream turns out to be, the parts do not run out.
    /// </summary>
    private const int PartsPerDoubling = 1000;

    /// <summary>
    /// A mebibyte, which part sizes are rounded up to.
    /// </summary>
    private const long Mebibyte = 1024 * 1024;

    /// <summary>
    /// What the store was configured with.
    /// </summary>
    private readonly S3StoreSettings settings;

    /// <summary>
    /// Talks to the server at <c>ServiceUrl</c>, or to AWS when there is none.
    /// </summary>
    private readonly AmazonS3Client client;

    /// <summary>
    /// Signs URLs. A client of its own only when they must be signed for <c>PublicServiceUrl</c> rather than for the address this service uses.
    /// </summary>
    private readonly AmazonS3Client presignClient;

    /// <summary>
    /// The credentials set in the store's settings, or null to use the SDK's default credential chain.
    /// </summary>
    private readonly AWSCredentials? explicitCredentials;

    /// <summary>
    /// Whether the clients have been released, so that releasing them twice does nothing.
    /// </summary>
    private int disposed;

    /// <summary>
    /// Creates a new instance of the store.
    /// </summary>
    /// <param name="name">The name the store is configured under.</param>
    /// <param name="settings">The store's settings.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings"/> is null.</exception>
    public S3ObjectStore(string name, S3StoreSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(settings);

        this.Name = name;
        this.settings = settings;

        this.explicitCredentials = CreateCredentials(settings);

        try
        {
            this.client = CreateClient(settings, settings.ServiceUrl, this.explicitCredentials);

            // a server reached by one address inside and another outside signs for the one the browser uses
            this.presignClient = settings.PublicServiceUrl is null
                ? this.client
                : CreateClient(settings, settings.PublicServiceUrl, this.explicitCredentials);
        }
        catch (AmazonClientException e)
        {
            // with no Region and no ServiceUrl the SDK looks for a region in the environment, a profile and the
            // instance metadata service, and gives up with an exception of its own
            throw new ObjectStoreConfigurationException(
                $"Object store '{name}' could not set up its S3 client. Set '{S3StoreSettings.RegionSetting}' or '{S3StoreSettings.ServiceUrlSetting}', or provide a region through the environment.",
                e);
        }
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Provider => S3ObjectStoreProvider.ProviderName;

    /// <summary>
    /// Gets a label that is equal for two stores that reach the same server as the same identity.
    /// </summary>
    internal string ConnectionKey => this.settings.ConnectionKey;

    /// <inheritdoc />
    public virtual async Task<ObjectInfo> PutAsync(string key, Stream content, ObjectPutOptions? options = null, CancellationToken cancellationToken = default)
    {
        this.ValidateKey(key);
        ArgumentNullException.ThrowIfNull(content);

        return await this.RunAsync(key, options?.IfNoneMatch == "*", async () =>
        {
            var partSize = this.settings.MultipartPartSize;

            if (content.CanSeek)
            {
                var remaining = content.Length - content.Position;

                return remaining <= partSize
                    ? await this.PutSingleAsync(key, content, remaining, options, cancellationToken).ConfigureAwait(false)
                    : await this.PutMultipartAsync(key, content, null, remaining, options, cancellationToken).ConfigureAwait(false);
            }

            // the length is unknown, so read one part to find out whether it is the only one
            var first = await ReadChunkAsync(content, (int)partSize, cancellationToken).ConfigureAwait(false);

            if (first.Length < partSize)
            {
                using var single = new MemoryStream(first, writable: false);

                return await this.PutSingleAsync(key, single, first.Length, options, cancellationToken).ConfigureAwait(false);
            }

            return await this.PutMultipartAsync(key, content, first, null, options, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<StoredObject> GetAsync(string key, ObjectGetOptions? options = null, CancellationToken cancellationToken = default)
    {
        this.ValidateKey(key);

        return await this.RunAsync(key, false, async () =>
        {
            var request = new GetObjectRequest { BucketName = this.settings.Bucket, Key = this.Full(key) };

            if (options is { } o && (o.RangeStart is not null || o.RangeEnd is not null))
            {
                request.ByteRange = new ByteRange($"bytes={o.RangeStart ?? 0}-{o.RangeEnd}");
            }

            var response = await this.client.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);

            var info = new ObjectInfo
            {
                Key = key,

                // for a range the response length is the slice, and the object's own size is after the slash
                Size = ParseTotalSize(response.ContentRange) ?? response.ContentLength,
                ETag = response.ETag,
                LastModified = ToOffset(response.LastModified),
                ContentType = response.Headers.ContentType,
                ContentDisposition = response.Headers.ContentDisposition,
                CacheControl = response.Headers.CacheControl,
                Metadata = ReadMetadata(response.Metadata),
            };

            return new StoredObject(info, response.ResponseStream, response);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<StoredObject?> TryGetAsync(string key, ObjectGetOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            return await this.GetAsync(key, options, cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public virtual async Task<ObjectInfo> GetInfoAsync(string key, CancellationToken cancellationToken = default)
    {
        this.ValidateKey(key);

        return await this.RunAsync(key, false, async () =>
        {
            var response = await this.client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = this.settings.Bucket, Key = this.Full(key) },
                cancellationToken).ConfigureAwait(false);

            return new ObjectInfo
            {
                Key = key,
                Size = response.ContentLength,
                ETag = response.ETag,
                LastModified = ToOffset(response.LastModified),
                ContentType = response.Headers.ContentType,
                ContentDisposition = response.Headers.ContentDisposition,
                CacheControl = response.Headers.CacheControl,
                Metadata = ReadMetadata(response.Metadata),
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await this.GetInfoAsync(key, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public virtual async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        this.ValidateKey(key);

        // S3 answers 204 for a key that is not there, which is the idempotence the contract promises
        await this.RunAsync(key, false, async () =>
        {
            await this.client.DeleteObjectAsync(this.settings.Bucket, this.Full(key), cancellationToken).ConfigureAwait(false);

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<ObjectDeleteResult> DeleteManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var deleted = new List<string>();
        var failures = new List<ObjectDeleteFailure>();
        var valid = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            var problem = ObjectKey.Explain(key);

            if (problem is not null)
            {
                failures.Add(new ObjectDeleteFailure(key, ObjectStoreErrorCode.InvalidKey, problem));
            }
            else if (seen.Add(key))
            {
                valid.Add(key);
            }
        }

        foreach (var batch in valid.Chunk(MaximumDeleteBatch))
        {
            await this.RunAsync(null, false, async () =>
            {
                var request = new DeleteObjectsRequest
                {
                    BucketName = this.settings.Bucket,
                    Objects = [.. batch.Select(key => new KeyVersion { Key = this.Full(key) })],
                };

                DeleteObjectsResponse response;

                try
                {
                    response = await this.client.DeleteObjectsAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (DeleteObjectsException e)
                {
                    // the SDK throws when any key failed, but the response still says which did not
                    response = e.Response;
                }

                var errors = (response.DeleteErrors ?? []).ToDictionary(error => error.Key, StringComparer.Ordinal);

                foreach (var key in batch)
                {
                    if (errors.TryGetValue(this.Full(key), out var error))
                    {
                        failures.Add(new ObjectDeleteFailure(key, S3ExceptionTranslator.ClassifyCode(error.Code), error.Code));
                    }
                    else
                    {
                        deleted.Add(key);
                    }
                }

                return true;
            }, cancellationToken).ConfigureAwait(false);
        }

        return new ObjectDeleteResult { Deleted = deleted, Failures = failures };
    }

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<ObjectInfo> ListAsync(
        string? prefix = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? token = null;

        do
        {
            var page = await this.ListPageAsync(prefix, token, MaximumPageSize, cancellationToken).ConfigureAwait(false);

            foreach (var item in page.Items)
            {
                yield return item;
            }

            token = page.NextContinuationToken;
        }
        while (token is not null);
    }

    /// <inheritdoc />
    public virtual async Task<ObjectListPage> ListPageAsync(string? prefix = null, string? continuationToken = null, int pageSize = MaximumPageSize, CancellationToken cancellationToken = default)
    {
        ObjectKey.ValidatePrefix(prefix, this.Name, this.Provider);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaximumPageSize);

        return await this.RunAsync(prefix, false, async () =>
        {
            var response = await this.client.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = this.settings.Bucket,
                    Prefix = this.settings.Prefix + prefix,
                    ContinuationToken = continuationToken,
                    MaxKeys = pageSize,
                },
                cancellationToken).ConfigureAwait(false);

            var items = new List<ObjectInfo>();

            foreach (var item in response.S3Objects ?? [])
            {
                var relative = item.Key[this.settings.Prefix.Length..];

                // the zero-byte marker some consoles create for the prefix itself is not an object of ours
                if (relative.Length == 0)
                {
                    continue;
                }

                items.Add(new ObjectInfo
                {
                    Key = relative,
                    Size = item.Size ?? 0,
                    ETag = item.ETag,
                    LastModified = ToOffset(item.LastModified),
                });
            }

            return new ObjectListPage
            {
                Items = items,
                NextContinuationToken = response.IsTruncated == true ? response.NextContinuationToken : null,
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual Task<ObjectInfo> CopyAsync(string sourceKey, string destinationKey, ObjectCopyOptions? options = null, CancellationToken cancellationToken = default)
    {
        return this.CopyCoreAsync(sourceKey, this, destinationKey, options, null, cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<ObjectInfo> CopyToAsync(string sourceKey, IObjectStore destination, string destinationKey, ObjectCopyOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (ReferenceEquals(destination, this))
        {
            return this.CopyAsync(sourceKey, destinationKey, options, cancellationToken);
        }

        // the server can copy between buckets it can reach with one identity; anything else has to be
        // read out and written back in
        return destination is S3ObjectStore other && other.ConnectionKey == this.ConnectionKey
            ? this.CopyCoreAsync(sourceKey, other, destinationKey, options, null, cancellationToken)
            : ObjectStoreCopier.CopyByStreamingAsync(this, sourceKey, destination, destinationKey, options, cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<PresignedRequest> CreateDownloadUrlAsync(string key, PresignedDownloadOptions? options = null, CancellationToken cancellationToken = default)
    {
        this.ValidateKey(key);

        var expiry = this.ResolveExpiry(options?.Expiry, key);

        return await this.RunAsync(key, false, async () =>
        {
            var request = this.NewPresignRequest(key, HttpVerb.GET, expiry);

            if (options?.ContentDisposition is { } disposition)
            {
                request.ResponseHeaderOverrides.ContentDisposition = disposition;
            }

            if (options?.ContentType is { } contentType)
            {
                request.ResponseHeaderOverrides.ContentType = contentType;
            }

            var url = await this.presignClient.GetPreSignedURLAsync(request).ConfigureAwait(false);

            return new PresignedRequest
            {
                Url = new Uri(url),
                Method = "GET",
                ExpiresAt = DateTimeOffset.UtcNow + expiry,
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<PresignedRequest> CreateUploadUrlAsync(string key, PresignedUploadOptions? options = null, CancellationToken cancellationToken = default)
    {
        this.ValidateKey(key);

        var expiry = this.ResolveExpiry(options?.Expiry, key);

        return await this.RunAsync(key, false, async () =>
        {
            var request = this.NewPresignRequest(key, HttpVerb.PUT, expiry);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (options?.ContentType is { } contentType)
            {
                request.ContentType = contentType;
                headers["Content-Type"] = contentType;
            }

            foreach (var (name, value) in options?.Metadata ?? ObjectInfo.EmptyMetadata)
            {
                request.Metadata[name] = value;
                headers[MetadataPrefix + name.ToLowerInvariant()] = value;
            }

            var url = await this.presignClient.GetPreSignedURLAsync(request).ConfigureAwait(false);

            return new PresignedRequest
            {
                Url = new Uri(url),
                Method = "PUT",
                RequiredHeaders = headers,
                ExpiresAt = DateTimeOffset.UtcNow + expiry,
            };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<PresignedPost> CreateUploadFormAsync(string key, PresignedPostOptions? options = null, CancellationToken cancellationToken = default)
    {
        this.ValidateKey(key);

        var expiry = this.ResolveExpiry(options?.Expiry, key);

        if (options is { MinimumSize: < 0 } or { MaximumSize: < 0 } || (options?.MinimumSize > options?.MaximumSize))
        {
            throw new ObjectStoreException(ObjectStoreErrorCode.InvalidRequest, "The size limits of an upload form must not be negative or inverted.", this.Name, this.Provider, key);
        }

        return await this.RunAsync(key, false, async () =>
        {
            var expiresAt = DateTimeOffset.UtcNow + expiry;
            var fullKey = this.Full(key);
            var fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["key"] = fullKey };
            var conditions = new List<object>
            {
                new Dictionary<string, string> { ["bucket"] = this.settings.Bucket },
                new object[] { "eq", "$key", fullKey },
            };

            if (options?.ContentType is { } contentType)
            {
                fields["Content-Type"] = contentType;
                conditions.Add(new object[] { "eq", "$Content-Type", contentType });
            }

            foreach (var (name, value) in options?.Metadata ?? ObjectInfo.EmptyMetadata)
            {
                var field = MetadataPrefix + name.ToLowerInvariant();

                fields[field] = value;
                conditions.Add(new object[] { "eq", "$" + field, value });
            }

            if (options is { MinimumSize: not null } or { MaximumSize: not null })
            {
                // S3 insists on both ends, and 5 TiB is the most one object can be
                conditions.Add(new object[] { "content-length-range", options.MinimumSize ?? 0, options.MaximumSize ?? 5L * 1024 * 1024 * 1024 * 1024 });
            }

            var policy = JsonSerializer.Serialize(new
            {
                expiration = expiresAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture),
                conditions,
            });

            // the form is signed with the same identity the client uses, wherever that comes from
            var credentials = this.explicitCredentials ?? await DefaultAWSCredentialsIdentityResolver.GetCredentialsAsync(this.client.Config).ConfigureAwait(false);
            var signed = S3PostUploadSignedPolicy.GetSignedPolicy(policy, credentials, this.ResolveRegion());

            fields["policy"] = signed.Policy;
            fields["x-amz-algorithm"] = signed.Algorithm;
            fields["x-amz-credential"] = signed.Credential;
            fields["x-amz-date"] = signed.Date;
            fields["x-amz-signature"] = signed.Signature;

            if (!string.IsNullOrEmpty(signed.SecurityToken))
            {
                fields["x-amz-security-token"] = signed.SecurityToken;
            }

            return new PresignedPost { Url = this.GetFormUrl(), Fields = fields, ExpiresAt = expiresAt };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<MultipartUpload> StartMultipartUploadAsync(string key, MultipartUploadOptions? options = null, CancellationToken cancellationToken = default)
    {
        this.ValidateKey(key);

        return await this.RunAsync(key, false, async () =>
        {
            var request = new InitiateMultipartUploadRequest { BucketName = this.settings.Bucket, Key = this.Full(key) };

            ApplyHeaders(request, options?.ContentType, options?.ContentDisposition, options?.CacheControl, options?.Metadata);

            var response = await this.client.InitiateMultipartUploadAsync(request, cancellationToken).ConfigureAwait(false);

            return new MultipartUpload(key, response.UploadId);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<UploadedPart> UploadPartAsync(MultipartUpload upload, int partNumber, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(content);
        this.ValidateKey(upload.Key);
        this.ValidatePartNumber(upload, partNumber);

        return await this.RunAsync(upload.Key, false, async () =>
        {
            // a stream that cannot report its length cannot be sent as a part, so it is read out first
            var buffered = content.CanSeek ? null : new MemoryStream(await ReadChunkAsync(content, int.MaxValue, cancellationToken).ConfigureAwait(false));

            using (buffered)
            {
                var source = buffered ?? content;
                var length = source.Length - source.Position;

                var response = await this.client.UploadPartAsync(
                    new UploadPartRequest
                    {
                        BucketName = this.settings.Bucket,
                        Key = this.Full(upload.Key),
                        UploadId = upload.UploadId,
                        PartNumber = partNumber,
                        InputStream = source,
                        PartSize = length,
                    },
                    cancellationToken).ConfigureAwait(false);

                return new UploadedPart(partNumber, response.ETag, length);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<PresignedRequest> CreatePartUploadUrlAsync(MultipartUpload upload, int partNumber, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        this.ValidateKey(upload.Key);
        this.ValidatePartNumber(upload, partNumber);

        var lifetime = this.ResolveExpiry(expiry, upload.Key);

        return await this.RunAsync(upload.Key, false, async () =>
        {
            var request = this.NewPresignRequest(upload.Key, HttpVerb.PUT, lifetime);

            request.UploadId = upload.UploadId;
            request.PartNumber = partNumber;

            var url = await this.presignClient.GetPreSignedURLAsync(request).ConfigureAwait(false);

            return new PresignedRequest { Url = new Uri(url), Method = "PUT", ExpiresAt = DateTimeOffset.UtcNow + lifetime };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<UploadedPart>> ListPartsAsync(MultipartUpload upload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        this.ValidateKey(upload.Key);

        return await this.RunAsync(upload.Key, false, async () =>
        {
            var parts = new List<UploadedPart>();
            int? marker = null;

            do
            {
                var response = await this.client.ListPartsAsync(
                    new ListPartsRequest
                    {
                        BucketName = this.settings.Bucket,
                        Key = this.Full(upload.Key),
                        UploadId = upload.UploadId,
                        PartNumberMarker = marker?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    },
                    cancellationToken).ConfigureAwait(false);

                parts.AddRange((response.Parts ?? []).Select(part => new UploadedPart(part.PartNumber ?? 0, part.ETag, part.Size)));

                marker = response.IsTruncated == true ? response.NextPartNumberMarker : null;
            }
            while (marker is not null);

            return (IReadOnlyList<UploadedPart>)parts;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task<ObjectInfo> CompleteMultipartUploadAsync(MultipartUpload upload, IEnumerable<UploadedPart> parts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(parts);
        this.ValidateKey(upload.Key);

        var ordered = parts.ToList();

        if (ordered.Count == 0)
        {
            throw new ObjectStoreException(ObjectStoreErrorCode.InvalidRequest, "A multipart upload cannot be completed with no parts.", this.Name, this.Provider, upload.Key);
        }

        await this.RunAsync(upload.Key, false, async () =>
        {
            await this.client.CompleteMultipartUploadAsync(
                new CompleteMultipartUploadRequest
                {
                    BucketName = this.settings.Bucket,
                    Key = this.Full(upload.Key),
                    UploadId = upload.UploadId,
                    PartETags = [.. ordered.Select(part => new PartETag(part.PartNumber, part.ETag))],
                },
                cancellationToken).ConfigureAwait(false);

            return true;
        }, cancellationToken).ConfigureAwait(false);

        // completing reports no size, and the caller is owed an accurate description of what now exists
        return await this.GetInfoAsync(upload.Key, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async Task AbortMultipartUploadAsync(MultipartUpload upload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        this.ValidateKey(upload.Key);

        await this.RunAsync(upload.Key, false, async () =>
        {
            await this.client.AbortMultipartUploadAsync(
                new AbortMultipartUploadRequest { BucketName = this.settings.Bucket, Key = this.Full(upload.Key), UploadId = upload.UploadId },
                cancellationToken).ConfigureAwait(false);

            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Copies an object by having the server read it in ranges, for sources too large for one request.
    /// </summary>
    /// <param name="sourceKey">The key to copy from.</param>
    /// <param name="destination">The store to copy into, which must be reachable with the same identity.</param>
    /// <param name="destinationKey">The key to copy to.</param>
    /// <param name="options">How to copy.</param>
    /// <param name="partSize">The size of each range, or null for one that fits the object in 10000 parts.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>What the destination now holds.</returns>
    internal async Task<ObjectInfo> CopyMultipartAsync(string sourceKey, S3ObjectStore destination, string destinationKey, ObjectCopyOptions? options, long? partSize, CancellationToken cancellationToken)
    {
        var source = await this.GetInfoAsync(sourceKey, cancellationToken).ConfigureAwait(false);

        // a multipart copy starts a fresh object, so nothing of the source's carries over unless said
        var (contentType, disposition, cacheControl, metadata) = options is { ReplacesMetadata: true } replacement
            ? (replacement.ContentType, replacement.ContentDisposition, replacement.CacheControl, replacement.Metadata)
            : (source.ContentType, source.ContentDisposition, source.CacheControl, (IReadOnlyDictionary<string, string>?)source.Metadata);

        var size = partSize ?? Math.Min(S3StoreSettings.MaximumPartSize, Math.Max(this.settings.MultipartPartSize, (source.Size / MaximumPartNumber) + 1));

        return await this.RunAsync(destinationKey, false, async () =>
        {
            var start = new InitiateMultipartUploadRequest { BucketName = destination.settings.Bucket, Key = destination.Full(destinationKey) };

            ApplyHeaders(start, contentType, disposition, cacheControl, metadata);

            var upload = await this.client.InitiateMultipartUploadAsync(start, cancellationToken).ConfigureAwait(false);

            try
            {
                var etags = new List<PartETag>();
                var number = 1;

                for (long first = 0; first < source.Size; first += size, number++)
                {
                    var response = await this.client.CopyPartAsync(
                        new CopyPartRequest
                        {
                            SourceBucket = this.settings.Bucket,
                            SourceKey = this.Full(sourceKey),
                            DestinationBucket = destination.settings.Bucket,
                            DestinationKey = destination.Full(destinationKey),
                            UploadId = upload.UploadId,
                            PartNumber = number,
                            FirstByte = first,
                            LastByte = Math.Min(first + size, source.Size) - 1,
                        },
                        cancellationToken).ConfigureAwait(false);

                    etags.Add(new PartETag(number, response.ETag));
                }

                var done = await this.client.CompleteMultipartUploadAsync(
                    new CompleteMultipartUploadRequest
                    {
                        BucketName = destination.settings.Bucket,
                        Key = destination.Full(destinationKey),
                        UploadId = upload.UploadId,
                        PartETags = etags,
                    },
                    cancellationToken).ConfigureAwait(false);

                return new ObjectInfo
                {
                    Key = destinationKey,
                    Size = source.Size,
                    ETag = done.ETag,
                    ContentType = contentType,
                    ContentDisposition = disposition,
                    CacheControl = cacheControl,
                    Metadata = metadata ?? ObjectInfo.EmptyMetadata,
                };
            }
            catch
            {
                await this.TryAbortAsync(destination, destinationKey, upload.UploadId).ConfigureAwait(false);

                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases the SDK clients.
    /// </summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || Interlocked.Exchange(ref this.disposed, 1) == 1)
        {
            return;
        }

        this.client.Dispose();

        if (!ReferenceEquals(this.presignClient, this.client))
        {
            this.presignClient.Dispose();
        }
    }

    private static AWSCredentials? CreateCredentials(S3StoreSettings settings)
    {
        if (settings.AccessKeyId is null)
        {
            return null;
        }

        return settings.SessionToken is null
            ? new BasicAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey)
            : new SessionAWSCredentials(settings.AccessKeyId, settings.SecretAccessKey, settings.SessionToken);
    }

    private static AmazonS3Client CreateClient(S3StoreSettings settings, Uri? serviceUrl, AWSCredentials? credentials)
    {
        var config = new AmazonS3Config
        {
            ForcePathStyle = settings.ForcePathStyle,
            Timeout = settings.Timeout,
            RequestChecksumCalculation = settings.ChecksumMode == S3ChecksumMode.WhenSupported
                ? RequestChecksumCalculation.WHEN_SUPPORTED
                : RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = settings.ChecksumMode == S3ChecksumMode.WhenSupported
                ? ResponseChecksumValidation.WHEN_SUPPORTED
                : ResponseChecksumValidation.WHEN_REQUIRED,
        };

        if (serviceUrl is not null)
        {
            config.ServiceURL = serviceUrl.AbsoluteUri.TrimEnd('/');
            config.AuthenticationRegion = settings.SigningRegion;
        }
        else if (settings.Region is not null)
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region);
        }

        return credentials is null ? new AmazonS3Client(config) : new AmazonS3Client(credentials, config);
    }

    private static async Task<byte[]> ReadChunkAsync(Stream content, int size, CancellationToken cancellationToken)
    {
        // reads until the chunk is full or the stream ends, since one read returns whatever has arrived
        using var buffer = new MemoryStream();
        var block = new byte[Math.Min(size, 81920)];

        while (buffer.Length < size)
        {
            int read;

            try
            {
                read = await content.ReadAsync(block.AsMemory(0, (int)Math.Min(block.Length, size - buffer.Length)), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new ContentReadException(e);
            }

            if (read == 0)
            {
                break;
            }

            buffer.Write(block, 0, read);
        }

        return buffer.ToArray();
    }

    private static void ApplyHeaders(InitiateMultipartUploadRequest request, string? contentType, string? disposition, string? cacheControl, IReadOnlyDictionary<string, string>? metadata)
    {
        if (contentType is not null)
        {
            request.ContentType = contentType;
        }

        if (disposition is not null)
        {
            request.Headers.ContentDisposition = disposition;
        }

        if (cacheControl is not null)
        {
            request.Headers.CacheControl = cacheControl;
        }

        foreach (var (name, value) in metadata ?? ObjectInfo.EmptyMetadata)
        {
            request.Metadata[name] = value;
        }
    }

    private static Dictionary<string, string> ReadMetadata(MetadataCollection collection)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in collection.Keys)
        {
            // the SDK reports names with the wire prefix still on
            var name = key.StartsWith(MetadataPrefix, StringComparison.OrdinalIgnoreCase) ? key[MetadataPrefix.Length..] : key;

            metadata[name] = collection[key];
        }

        return metadata;
    }

    private static long? ParseTotalSize(string? contentRange)
    {
        // "bytes 0-9/100"
        var slash = contentRange?.LastIndexOf('/') ?? -1;

        return slash >= 0 && long.TryParse(contentRange![(slash + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var total)
            ? total
            : null;
    }

    private static DateTimeOffset? ToOffset(DateTime? value)
    {
        return value is { } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)) : null;
    }

    private void ValidateKey(string? key)
    {
        // the prefix is sent with the key, so it counts against the length S3 allows
        ObjectKey.Validate(key, this.Name, this.Provider, this.settings.PrefixBytes);
    }

    private string Full(string key)
    {
        return this.settings.Prefix + key;
    }

    private async Task<T> RunAsync<T>(string? key, bool createOnly, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (ContentReadException e)
        {
            // the caller's stream failed, which is theirs to handle as it came
            ExceptionDispatchInfo.Capture(e.InnerException!).Throw();

            throw;
        }
        catch (Exception e) when (S3ExceptionTranslator.IsTranslatable(e, cancellationToken))
        {
            throw S3ExceptionTranslator.Translate(e, this.Name, key, createOnly);
        }
    }

    private async Task<ObjectInfo> PutSingleAsync(string key, Stream content, long length, ObjectPutOptions? options, CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = this.settings.Bucket,
            Key = this.Full(key),
            InputStream = content,
            AutoCloseStream = false,
            IfNoneMatch = options?.IfNoneMatch,
            IfMatch = options?.IfMatch,
        };

        request.Headers.ContentLength = length;

        if (options?.ContentType is { } contentType)
        {
            request.ContentType = contentType;
        }

        if (options?.ContentDisposition is { } disposition)
        {
            request.Headers.ContentDisposition = disposition;
        }

        if (options?.CacheControl is { } cacheControl)
        {
            request.Headers.CacheControl = cacheControl;
        }

        foreach (var (name, value) in options?.Metadata ?? ObjectInfo.EmptyMetadata)
        {
            request.Metadata[name] = value;
        }

        var response = await this.client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);

        return DescribeWritten(key, length, response.ETag, options);
    }

    private async Task<ObjectInfo> PutMultipartAsync(string key, Stream content, byte[]? firstChunk, long? knownLength, ObjectPutOptions? options, CancellationToken cancellationToken)
    {
        // chosen before anything is started, so an object too big for any part size is refused up front and
        // not after gigabytes have been sent
        var initialPartSize = this.ChoosePartSize(key, knownLength);

        var upload = await this.StartMultipartUploadAsync(
            key,
            new MultipartUploadOptions
            {
                ContentType = options?.ContentType,
                ContentDisposition = options?.ContentDisposition,
                CacheControl = options?.CacheControl,
                Metadata = options?.Metadata,
            },
            cancellationToken).ConfigureAwait(false);

        try
        {
            var etags = new List<PartETag>();
            long total = 0;
            var chunk = firstChunk;

            for (var number = 1; ; number++)
            {
                if (number > MaximumPartNumber)
                {
                    throw new ObjectStoreException(ObjectStoreErrorCode.InvalidRequest, $"The content needs more than {MaximumPartNumber} parts, which is the most S3 allows.", this.Name, this.Provider, key);
                }

                var chunkSize = (int)PartSizeAt(initialPartSize, number, growing: knownLength is null);

                chunk ??= await ReadChunkAsync(content, chunkSize, cancellationToken).ConfigureAwait(false);

                // an empty chunk after the first means the previous part was the last, and it was full
                if (chunk.Length == 0 && number > 1)
                {
                    break;
                }

                using var part = new MemoryStream(chunk, writable: false);

                var uploaded = await this.client.UploadPartAsync(
                    new UploadPartRequest
                    {
                        BucketName = this.settings.Bucket,
                        Key = this.Full(key),
                        UploadId = upload.UploadId,
                        PartNumber = number,
                        InputStream = part,
                        PartSize = chunk.Length,
                    },
                    cancellationToken).ConfigureAwait(false);

                etags.Add(new PartETag(number, uploaded.ETag));
                total += chunk.Length;

                if (chunk.Length < chunkSize)
                {
                    break;
                }

                chunk = null;
            }

            var done = await this.client.CompleteMultipartUploadAsync(
                new CompleteMultipartUploadRequest
                {
                    BucketName = this.settings.Bucket,
                    Key = this.Full(key),
                    UploadId = upload.UploadId,
                    PartETags = etags,
                    IfNoneMatch = options?.IfNoneMatch,
                    IfMatch = options?.IfMatch,
                },
                cancellationToken).ConfigureAwait(false);

            return DescribeWritten(key, total, done.ETag, options);
        }
        catch
        {
            await this.TryAbortAsync(this, key, upload.UploadId).ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>
    /// Chooses how big each part of an upload is.
    /// </summary>
    /// <param name="key">The key being written, for the failure.</param>
    /// <param name="knownLength">The length of the content, or null when it is not known up front.</param>
    /// <returns>The configured part size, or a larger one when the content would otherwise need over 10000 parts.</returns>
    internal long ChoosePartSize(string key, long? knownLength)
    {
        var size = this.settings.MultipartPartSize;

        if (knownLength is not { } length)
        {
            return size;
        }

        var needed = ((((length + MaximumPartNumber) - 1) / MaximumPartNumber) + Mebibyte - 1) / Mebibyte * Mebibyte;

        if (needed > S3StoreSettings.MaximumPartSize)
        {
            throw new ObjectStoreException(
                ObjectStoreErrorCode.InvalidRequest,
                $"The content is too large to be uploaded in {MaximumPartNumber} parts of at most {S3StoreSettings.MaximumPartSize} bytes.",
                this.Name,
                this.Provider,
                key);
        }

        return Math.Max(size, needed);
    }

    /// <summary>
    /// Gets the size of one part of an upload.
    /// </summary>
    /// <param name="initial">The size of the first part.</param>
    /// <param name="number">The part number.</param>
    /// <param name="growing">
    /// True when the length is not known up front, so the size doubles every <see cref="PartsPerDoubling"/>
    /// parts, up to the largest part S3 takes here. False when <paramref name="initial"/> already fits the whole object.
    /// </param>
    /// <returns>The size in bytes.</returns>
    internal static long PartSizeAt(long initial, int number, bool growing)
    {
        var size = initial;

        if (growing)
        {
            for (var doublings = (number - 1) / PartsPerDoubling; doublings > 0 && size < S3StoreSettings.MaximumPartSize; doublings--)
            {
                size = Math.Min(size * 2, S3StoreSettings.MaximumPartSize);
            }
        }

        return size;
    }

    private async Task TryAbortAsync(S3ObjectStore store, string key, string uploadId)
    {
        try
        {
            // not the caller's token: the upload is being abandoned because that token may be what fired
            await this.client.AbortMultipartUploadAsync(
                new AbortMultipartUploadRequest { BucketName = store.settings.Bucket, Key = store.Full(key), UploadId = uploadId },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is AmazonServiceException or AmazonClientException or HttpRequestException)
        {
            // the original failure is the one worth reporting; a stranded upload is the bucket's lifecycle rule to reap
        }
    }

    private async Task<ObjectInfo> CopyCoreAsync(string sourceKey, S3ObjectStore destination, string destinationKey, ObjectCopyOptions? options, long? partSize, CancellationToken cancellationToken)
    {
        this.ValidateKey(sourceKey);
        destination.ValidateKey(destinationKey);

        try
        {
            var etag = await this.RunAsync(sourceKey, false, async () =>
            {
                var request = new CopyObjectRequest
                {
                    SourceBucket = this.settings.Bucket,
                    SourceKey = this.Full(sourceKey),
                    DestinationBucket = destination.settings.Bucket,
                    DestinationKey = destination.Full(destinationKey),
                    MetadataDirective = options is { ReplacesMetadata: true } ? S3MetadataDirective.REPLACE : S3MetadataDirective.COPY,
                };

                if (options is { ReplacesMetadata: true } replacement)
                {
                    if (replacement.ContentType is { } contentType)
                    {
                        request.ContentType = contentType;
                    }

                    if (replacement.ContentDisposition is { } disposition)
                    {
                        request.Headers.ContentDisposition = disposition;
                    }

                    if (replacement.CacheControl is { } cacheControl)
                    {
                        request.Headers.CacheControl = cacheControl;
                    }

                    foreach (var (name, value) in replacement.Metadata ?? ObjectInfo.EmptyMetadata)
                    {
                        request.Metadata[name] = value;
                    }
                }

                var response = await this.client.CopyObjectAsync(request, cancellationToken).ConfigureAwait(false);

                return response.ETag;
            }, cancellationToken).ConfigureAwait(false);

            // the response names no size, so ask the destination what it now holds
            var info = await destination.GetInfoAsync(destinationKey, cancellationToken).ConfigureAwait(false);

            return info with { ETag = etag ?? info.ETag };
        }
        catch (ObjectStoreException e) when (e is { ErrorCode: ObjectStoreErrorCode.InvalidRequest, InnerException: AmazonS3Exception { ErrorCode: "InvalidRequest" } inner } && inner.Message.Contains("maximum allowable size", StringComparison.OrdinalIgnoreCase))
        {
            // beyond 5 GB the server will not copy in one request
            return await this.CopyMultipartAsync(sourceKey, destination, destinationKey, options, partSize, cancellationToken).ConfigureAwait(false);
        }
    }

    private GetPreSignedUrlRequest NewPresignRequest(string key, HttpVerb verb, TimeSpan expiry)
    {
        var endpoint = this.settings.PublicServiceUrl ?? this.settings.ServiceUrl;

        return new GetPreSignedUrlRequest
        {
            BucketName = this.settings.Bucket,
            Key = this.Full(key),
            Verb = verb,
            Expires = (DateTimeOffset.UtcNow + expiry).UtcDateTime,
            Protocol = endpoint?.Scheme == Uri.UriSchemeHttp ? Protocol.HTTP : Protocol.HTTPS,
        };
    }

    private TimeSpan ResolveExpiry(TimeSpan? requested, string key)
    {
        var expiry = requested ?? this.settings.PresignedExpiry;

        if (expiry <= TimeSpan.Zero || expiry > this.settings.MaxPresignedExpiry)
        {
            // refused rather than quietly shortened, so nobody builds on a lifetime they did not get
            throw new ObjectStoreException(
                ObjectStoreErrorCode.InvalidRequest,
                $"A presigned URL must work for a positive time, and for no longer than {this.settings.MaxPresignedExpiry}.",
                this.Name,
                this.Provider,
                key);
        }

        return expiry;
    }

    private void ValidatePartNumber(MultipartUpload upload, int partNumber)
    {
        if (partNumber is < 1 or > MaximumPartNumber)
        {
            throw new ObjectStoreException(ObjectStoreErrorCode.InvalidRequest, $"A part number must be between 1 and {MaximumPartNumber}.", this.Name, this.Provider, upload.Key);
        }
    }

    /// <summary>
    /// Works out the region a form is signed for.
    /// </summary>
    /// <returns>The region.</returns>
    /// <exception cref="ObjectStoreConfigurationException">Thrown when neither the settings nor the SDK have one.</exception>
    private string ResolveRegion()
    {
        return this.settings.SigningRegion
            ?? this.client.Config.RegionEndpoint?.SystemName
            ?? throw new ObjectStoreConfigurationException($"Setting '{S3StoreSettings.RegionSetting}' is required for object store '{this.Name}' to sign an upload form, and the AWS SDK could not work one out.");
    }

    private Uri GetFormUrl()
    {
        var endpoint = this.settings.PublicServiceUrl ?? this.settings.ServiceUrl;

        if (endpoint is not null)
        {
            return new Uri($"{endpoint.AbsoluteUri.TrimEnd('/')}/{this.settings.Bucket}");
        }

        var region = this.ResolveRegion();
        var host = $"s3.{region}.{(region.StartsWith("cn-", StringComparison.Ordinal) ? "amazonaws.com.cn" : "amazonaws.com")}";

        return this.settings.ForcePathStyle
            ? new Uri($"https://{host}/{this.settings.Bucket}")
            : new Uri($"https://{this.settings.Bucket}.{host}/");
    }

    private static ObjectInfo DescribeWritten(string key, long size, string? etag, ObjectPutOptions? options)
    {
        return new ObjectInfo
        {
            Key = key,
            Size = size,
            ETag = etag,
            ContentType = options?.ContentType,
            ContentDisposition = options?.ContentDisposition,
            CacheControl = options?.CacheControl,
            Metadata = options?.Metadata is { } metadata
                ? new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase)
                : ObjectInfo.EmptyMetadata,
        };
    }
}
