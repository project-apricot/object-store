using System.Security.Cryptography;
using ApricotFramework.ObjectStore.Exceptions;
using ApricotFramework.ObjectStore.Model;

namespace ApricotFramework.ObjectStore.Impl;

/// <summary>
/// Keeps every object in memory, so a test or a development host runs without a real store.
/// </summary>
/// <remarks>
/// Implements the whole contract except signing: presigned URLs and forms throw
/// <see cref="ObjectStoreErrorCode.NotSupported"/>, since there is no server to honor them. Multipart
/// parts have no minimum size here, unlike S3. Everything is lost with the process, and the memory is
/// the host's to bound.
/// <para>
/// Every failure, including a bad argument, reaches the caller through the returned task, as it does with a
/// real provider, so code that stores a task and awaits it later behaves the same against either.
/// </para>
/// </remarks>
public class MemoryObjectStore : IObjectStore
{
    /// <summary>
    /// The most parts a multipart upload can have, which is S3's limit.
    /// </summary>
    private const int MaximumPartNumber = 10000;

    /// <summary>
    /// The most objects one listing page holds, which is what S3 allows too.
    /// </summary>
    private const int MaximumPageSize = 1000;

    /// <summary>
    /// Guards <see cref="entries"/>, <see cref="keyIndex"/> and <see cref="uploads"/>.
    /// </summary>
    private readonly Lock gate = new();

    /// <summary>
    /// Every object, by key.
    /// </summary>
    private readonly Dictionary<string, MemoryEntry> entries = new(StringComparer.Ordinal);

    /// <summary>
    /// Every key in key order, so a page of a listing is found without sorting the whole store.
    /// </summary>
    private readonly SortedSet<string> keyIndex = new(StringComparer.Ordinal);

    /// <summary>
    /// Every multipart upload in progress, by upload id.
    /// </summary>
    private readonly Dictionary<string, MemoryUpload> uploads = new(StringComparer.Ordinal);

    /// <summary>
    /// Where modification times come from.
    /// </summary>
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Creates a new instance of the store.
    /// </summary>
    /// <param name="name">The name the store is configured under.</param>
    /// <param name="timeProvider">Where modification times come from, or null for the system clock.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    public MemoryObjectStore(string name, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        this.Name = name;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Provider => MemoryObjectStoreProvider.ProviderName;

    /// <inheritdoc />
    public virtual async Task<ObjectInfo> PutAsync(string key, Stream content, ObjectPutOptions? options = null, CancellationToken cancellationToken = default)
    {
        ObjectKey.Validate(key, this.Name, this.Provider);
        ArgumentNullException.ThrowIfNull(content);

        var data = await ReadAsync(content, cancellationToken).ConfigureAwait(false);

        lock (this.gate)
        {
            this.entries.TryGetValue(key, out var existing);

            this.CheckConditions(key, existing, options);

            var entry = this.Build(key, data, options?.ContentType, options?.ContentDisposition, options?.CacheControl, options?.Metadata);

            this.Keep(entry);

            return entry.Info;
        }
    }

    /// <inheritdoc />
    public virtual Task<StoredObject> GetAsync(string key, ObjectGetOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Run(
            () =>
            {
                ObjectKey.Validate(key, this.Name, this.Provider);

                MemoryEntry? entry;

                lock (this.gate)
                {
                    this.entries.TryGetValue(key, out entry);
                }

                if (entry is null)
                {
                    throw this.NotFound(key);
                }

                var (offset, length) = this.ResolveRange(key, entry.Data.Length, options);

                return new StoredObject(entry.Info, new MemoryStream(entry.Data, offset, length, writable: false));
            },
            cancellationToken);
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
    public virtual Task<ObjectInfo> GetInfoAsync(string key, CancellationToken cancellationToken = default)
    {
        return Run(
            () =>
            {
                ObjectKey.Validate(key, this.Name, this.Provider);

                lock (this.gate)
                {
                    return this.entries.TryGetValue(key, out var entry) ? entry.Info : throw this.NotFound(key);
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return Run(
            () =>
            {
                ObjectKey.Validate(key, this.Name, this.Provider);

                lock (this.gate)
                {
                    return this.entries.ContainsKey(key);
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        return Run(
            () =>
            {
                ObjectKey.Validate(key, this.Name, this.Provider);

                lock (this.gate)
                {
                    this.Forget(key);
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<ObjectDeleteResult> DeleteManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        return Run(
            () =>
            {
                ArgumentNullException.ThrowIfNull(keys);

                var deleted = new List<string>();
                var failures = new List<ObjectDeleteFailure>();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                lock (this.gate)
                {
                    foreach (var key in keys)
                    {
                        var problem = ObjectKey.Explain(key);

                        if (problem is not null)
                        {
                            failures.Add(new ObjectDeleteFailure(key, ObjectStoreErrorCode.InvalidKey, problem));

                            continue;
                        }

                        // a key named twice is deleted, and reported, once
                        if (seen.Add(key))
                        {
                            this.Forget(key);
                            deleted.Add(key);
                        }
                    }
                }

                return new ObjectDeleteResult { Deleted = deleted, Failures = failures };
            },
            cancellationToken);
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
    public virtual Task<ObjectListPage> ListPageAsync(string? prefix = null, string? continuationToken = null, int pageSize = MaximumPageSize, CancellationToken cancellationToken = default)
    {
        return Run(
            () =>
            {
                ObjectKey.ValidatePrefix(prefix, this.Name, this.Provider);
                ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaximumPageSize);

                var items = new List<ObjectInfo>(pageSize + 1);

                lock (this.gate)
                {
                    // the token is the last key of the previous page, so a key added or removed mid-listing moves
                    // nothing already returned. A string followed by NUL is the next one in key order, which makes
                    // the start of the page exclusive of the token.
                    var from = continuationToken is null ? prefix ?? string.Empty : continuationToken + '\0';

                    if (prefix is not null && string.CompareOrdinal(from, prefix) < 0)
                    {
                        from = prefix;
                    }

                    if (this.keyIndex.Count > 0 && string.CompareOrdinal(from, this.keyIndex.Max) <= 0)
                    {
                        // the view starts where the page does, so nothing before it is looked at
                        foreach (var key in this.keyIndex.GetViewBetween(from, this.keyIndex.Max!))
                        {
                            if (!string.IsNullOrEmpty(prefix) && !key.StartsWith(prefix, StringComparison.Ordinal))
                            {
                                break;
                            }

                            items.Add(this.entries[key].Info);

                            if (items.Count > pageSize)
                            {
                                break;
                            }
                        }
                    }
                }

                var more = items.Count > pageSize;

                if (more)
                {
                    items.RemoveAt(pageSize);
                }

                return new ObjectListPage { Items = items, NextContinuationToken = more ? items[^1].Key : null };
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<ObjectInfo> CopyAsync(string sourceKey, string destinationKey, ObjectCopyOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Run(
            () =>
            {
                ObjectKey.Validate(sourceKey, this.Name, this.Provider);
                ObjectKey.Validate(destinationKey, this.Name, this.Provider);

                lock (this.gate)
                {
                    if (!this.entries.TryGetValue(sourceKey, out var source))
                    {
                        throw this.NotFound(sourceKey);
                    }

                    // the bytes are never mutated, so the copy can share the source's array
                    var copy = options is { ReplacesMetadata: true } replacement
                        ? this.Build(destinationKey, source.Data, replacement.ContentType, replacement.ContentDisposition, replacement.CacheControl, replacement.Metadata)
                        : this.Build(destinationKey, source.Data, source.Info.ContentType, source.Info.ContentDisposition, source.Info.CacheControl, source.Info.Metadata);

                    this.Keep(copy);

                    return copy.Info;
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<ObjectInfo> CopyToAsync(string sourceKey, IObjectStore destination, string destinationKey, ObjectCopyOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (destination is null)
        {
            return Task.FromException<ObjectInfo>(new ArgumentNullException(nameof(destination)));
        }

        return ReferenceEquals(destination, this)
            ? this.CopyAsync(sourceKey, destinationKey, options, cancellationToken)
            : ObjectStoreCopier.CopyByStreamingAsync(this, sourceKey, destination, destinationKey, options, cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<PresignedRequest> CreateDownloadUrlAsync(string key, PresignedDownloadOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromException<PresignedRequest>(this.NotSupported("Presigned URLs"));
    }

    /// <inheritdoc />
    public virtual Task<PresignedRequest> CreateUploadUrlAsync(string key, PresignedUploadOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromException<PresignedRequest>(this.NotSupported("Presigned URLs"));
    }

    /// <inheritdoc />
    public virtual Task<PresignedPost> CreateUploadFormAsync(string key, PresignedPostOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromException<PresignedPost>(this.NotSupported("Presigned forms"));
    }

    /// <inheritdoc />
    public virtual Task<PresignedRequest> CreatePartUploadUrlAsync(MultipartUpload upload, int partNumber, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        return Task.FromException<PresignedRequest>(this.NotSupported("Presigned URLs"));
    }

    /// <inheritdoc />
    public virtual Task<MultipartUpload> StartMultipartUploadAsync(string key, MultipartUploadOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Run(
            () =>
            {
                ObjectKey.Validate(key, this.Name, this.Provider);

                var upload = new MultipartUpload(key, Guid.NewGuid().ToString("N"));

                lock (this.gate)
                {
                    this.uploads[upload.UploadId] = new MemoryUpload(key, options);
                }

                return upload;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task<UploadedPart> UploadPartAsync(MultipartUpload upload, int partNumber, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(content);
        this.ValidatePartNumber(upload, partNumber);

        var data = await ReadAsync(content, cancellationToken).ConfigureAwait(false);
        var etag = ComputeETag(data);

        lock (this.gate)
        {
            this.FindUpload(upload).Parts[partNumber] = new MemoryPart(data, etag);
        }

        return new UploadedPart(partNumber, etag, data.Length);
    }

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<UploadedPart>> ListPartsAsync(MultipartUpload upload, CancellationToken cancellationToken = default)
    {
        return Run(
            () =>
            {
                ArgumentNullException.ThrowIfNull(upload);

                lock (this.gate)
                {
                    IReadOnlyList<UploadedPart> parts = [.. this.FindUpload(upload).Parts.Select(part => new UploadedPart(part.Key, part.Value.ETag, part.Value.Data.Length))];

                    return parts;
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<ObjectInfo> CompleteMultipartUploadAsync(MultipartUpload upload, IEnumerable<UploadedPart> parts, CancellationToken cancellationToken = default)
    {
        return Run(
            () =>
            {
                ArgumentNullException.ThrowIfNull(upload);
                ArgumentNullException.ThrowIfNull(parts);

                var requested = parts.ToList();

                if (requested.Count == 0)
                {
                    throw this.InvalidRequest(upload.Key, "A multipart upload cannot be completed with no parts.");
                }

                lock (this.gate)
                {
                    var found = this.FindUpload(upload);
                    var previous = 0;
                    using var assembled = new MemoryStream();

                    foreach (var part in requested)
                    {
                        if (part.PartNumber <= previous)
                        {
                            throw this.InvalidRequest(upload.Key, "Parts must be listed in ascending part-number order, each once.");
                        }

                        previous = part.PartNumber;

                        if (!found.Parts.TryGetValue(part.PartNumber, out var stored) || !ETagsMatch(stored.ETag, part.ETag))
                        {
                            throw this.InvalidRequest(upload.Key, $"Part {part.PartNumber} was not uploaded, or its ETag does not match.");
                        }

                        assembled.Write(stored.Data);
                    }

                    var entry = this.Build(
                        upload.Key,
                        assembled.ToArray(),
                        found.Options?.ContentType,
                        found.Options?.ContentDisposition,
                        found.Options?.CacheControl,
                        found.Options?.Metadata);

                    this.Keep(entry);
                    this.uploads.Remove(upload.UploadId);

                    return entry.Info;
                }
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task AbortMultipartUploadAsync(MultipartUpload upload, CancellationToken cancellationToken = default)
    {
        return Run(
            () =>
            {
                ArgumentNullException.ThrowIfNull(upload);

                lock (this.gate)
                {
                    this.FindUpload(upload);
                    this.uploads.Remove(upload.UploadId);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs work that is already finished, delivering whatever it throws through the task.
    /// </summary>
    /// <typeparam name="T">What the work produces.</typeparam>
    /// <param name="work">The work.</param>
    /// <param name="cancellationToken">The caller's token, checked first.</param>
    /// <returns>A task that has completed, faulted or been canceled.</returns>
    private static Task<T> Run<T>(Func<T> work, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(work());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }
        catch (Exception e)
        {
            return Task.FromException<T>(e);
        }
    }

    /// <summary>
    /// Runs work that produces nothing, delivering whatever it throws through the task.
    /// </summary>
    /// <param name="work">The work.</param>
    /// <param name="cancellationToken">The caller's token, checked first.</param>
    /// <returns>A task that has completed, faulted or been canceled.</returns>
    private static Task<bool> Run(Action work, CancellationToken cancellationToken)
    {
        return Run(
            () =>
            {
                work();

                return true;
            },
            cancellationToken);
    }

    private static async Task<byte[]> ReadAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();

        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        return buffer.ToArray();
    }

    private static string ComputeETag(byte[] data)
    {
        // quoted, as S3 reports it. A content hash of the first 128 bits, since the point is only that
        // different content gets a different tag.
        return $"\"{Convert.ToHexStringLower(SHA256.HashData(data))[..32]}\"";
    }

    private static bool ETagsMatch(string? stored, string? candidate)
    {
        return stored is not null && candidate is not null && string.Equals(stored.Trim('"'), candidate.Trim('"'), StringComparison.Ordinal);
    }

    /// <summary>
    /// Stores an object, which the caller must already hold the lock to do.
    /// </summary>
    private void Keep(MemoryEntry entry)
    {
        this.entries[entry.Info.Key] = entry;
        this.keyIndex.Add(entry.Info.Key);
    }

    /// <summary>
    /// Removes an object, which the caller must already hold the lock to do.
    /// </summary>
    private void Forget(string key)
    {
        this.entries.Remove(key);
        this.keyIndex.Remove(key);
    }

    private MemoryEntry Build(string key, byte[] data, string? contentType, string? contentDisposition, string? cacheControl, IReadOnlyDictionary<string, string>? metadata)
    {
        var info = new ObjectInfo
        {
            Key = key,
            Size = data.Length,
            ETag = ComputeETag(data),
            LastModified = this.timeProvider.GetUtcNow(),
            ContentType = contentType,
            ContentDisposition = contentDisposition,
            CacheControl = cacheControl,
            Metadata = metadata is null
                ? ObjectInfo.EmptyMetadata
                : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase),
        };

        return new MemoryEntry(data, info);
    }

    private void CheckConditions(string key, MemoryEntry? existing, ObjectPutOptions? options)
    {
        if (options?.IfNoneMatch is { } ifNoneMatch && existing is not null)
        {
            if (ifNoneMatch == "*")
            {
                throw new ObjectAlreadyExistsException("The object already exists.", this.Name, this.Provider, key);
            }

            if (ETagsMatch(existing.Info.ETag, ifNoneMatch))
            {
                throw this.PreconditionFailed(key);
            }
        }

        if (options?.IfMatch is { } ifMatch && (existing is null || !ETagsMatch(existing.Info.ETag, ifMatch)))
        {
            throw this.PreconditionFailed(key);
        }
    }

    private (int Offset, int Length) ResolveRange(string key, int size, ObjectGetOptions? options)
    {
        if (options is not { RangeStart: not null } and not { RangeEnd: not null })
        {
            return (0, size);
        }

        var start = options.RangeStart ?? 0;
        var end = options.RangeEnd ?? (size - 1);

        if (start < 0 || end < start || start >= size)
        {
            throw this.InvalidRequest(key, "The requested range cannot be satisfied.");
        }

        end = Math.Min(end, size - 1);

        return ((int)start, (int)(end - start + 1));
    }

    private void ValidatePartNumber(MultipartUpload upload, int partNumber)
    {
        if (partNumber is < 1 or > MaximumPartNumber)
        {
            throw this.InvalidRequest(upload.Key, $"A part number must be between 1 and {MaximumPartNumber}.");
        }
    }

    /// <summary>
    /// Finds the upload, which the caller must already hold the lock to read.
    /// </summary>
    private MemoryUpload FindUpload(MultipartUpload upload)
    {
        if (this.uploads.TryGetValue(upload.UploadId, out var found) && string.Equals(found.Key, upload.Key, StringComparison.Ordinal))
        {
            return found;
        }

        throw new ObjectNotFoundException("The multipart upload was not found.", this.Name, this.Provider, upload.Key);
    }

    private ObjectNotFoundException NotFound(string key)
    {
        return new ObjectNotFoundException("The object was not found.", this.Name, this.Provider, key);
    }

    private ObjectStoreException PreconditionFailed(string key)
    {
        return new ObjectStoreException(ObjectStoreErrorCode.PreconditionFailed, "The condition on the request did not hold.", this.Name, this.Provider, key);
    }

    private ObjectStoreException InvalidRequest(string key, string message)
    {
        return new ObjectStoreException(ObjectStoreErrorCode.InvalidRequest, message, this.Name, this.Provider, key);
    }

    private ObjectStoreException NotSupported(string what)
    {
        return new ObjectStoreException(ObjectStoreErrorCode.NotSupported, $"{what} are not supported by the memory provider.", this.Name, this.Provider);
    }
}
