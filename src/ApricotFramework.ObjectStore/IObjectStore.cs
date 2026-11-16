using ApricotFramework.ObjectStore.Model;

namespace ApricotFramework.ObjectStore;

/// <summary>
/// One configured store: a bucket, and a key prefix inside it.
/// </summary>
/// <remarks>
/// Callers address objects by key alone and never see the bucket, the endpoint or the prefix, the same
/// way a mail caller never sees an SMTP host. Keys are validated by <see cref="ObjectKey"/>.
/// <para>
/// Failures throw <see cref="Exceptions.ObjectStoreException"/>, or a subclass, with the vendor's own
/// error kept as the inner exception. Cancellation the caller asked for throws
/// <see cref="OperationCanceledException"/>.
/// </para>
/// <para>
/// Instances are shared and safe to use from several threads. They belong to the factory that made
/// them: do not dispose one.
/// </para>
/// </remarks>
public interface IObjectStore
{
    /// <summary>
    /// Gets the name this store is configured under.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the provider that backs this store, such as <c>s3</c>.
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Writes an object, replacing any that is there.
    /// </summary>
    /// <param name="key">The object key.</param>
    /// <param name="content">The content, read from its current position to the end.</param>
    /// <param name="options">How to write it, or null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the write operation.</param>
    /// <returns>What the store now holds under the key.</returns>
    /// <exception cref="Exceptions.ObjectAlreadyExistsException">
    /// Thrown when <see cref="ObjectPutOptions.IfNoneMatch"/> is <c>*</c> and the object exists.
    /// </exception>
    Task<ObjectInfo> PutAsync(string key, Stream content, ObjectPutOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an object.
    /// </summary>
    /// <param name="key">The object key.</param>
    /// <param name="options">What to read, or null for all of it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The object. Dispose it when done.</returns>
    /// <exception cref="Exceptions.ObjectNotFoundException">Thrown when the object does not exist.</exception>
    Task<StoredObject> GetAsync(string key, ObjectGetOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads an object that may not be there.
    /// </summary>
    /// <param name="key">The object key.</param>
    /// <param name="options">What to read, or null for all of it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The object, or null when it does not exist. Dispose it when done.</returns>
    Task<StoredObject?> TryGetAsync(string key, ObjectGetOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Describes an object without reading it.
    /// </summary>
    /// <param name="key">The object key.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the store knows about the object.</returns>
    /// <exception cref="Exceptions.ObjectNotFoundException">Thrown when the object does not exist.</exception>
    Task<ObjectInfo> GetInfoAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether an object exists.
    /// </summary>
    /// <param name="key">The object key.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>True when the object exists.</returns>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an object.
    /// </summary>
    /// <param name="key">The object key.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the object is gone.</returns>
    /// <remarks>
    /// Idempotent: deleting an object that is not there succeeds.
    /// </remarks>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes many objects, in as few requests as the store allows.
    /// </summary>
    /// <param name="keys">The object keys.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Which keys are gone and which could not be deleted.</returns>
    Task<ObjectDeleteResult> DeleteManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists objects, fetching pages as the caller reads.
    /// </summary>
    /// <param name="prefix">Only keys beginning with this, or null for every object.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The objects, in key order.</returns>
    IAsyncEnumerable<ObjectInfo> ListAsync(string? prefix = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists one page of objects.
    /// </summary>
    /// <param name="prefix">Only keys beginning with this, or null for every object.</param>
    /// <param name="continuationToken">The token from the previous page, or null for the first.</param>
    /// <param name="pageSize">The most objects to return, from 1 to 1000.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The page.</returns>
    Task<ObjectListPage> ListPageAsync(string? prefix = null, string? continuationToken = null, int pageSize = 1000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies an object within this store.
    /// </summary>
    /// <param name="sourceKey">The key to copy from.</param>
    /// <param name="destinationKey">The key to copy to, replacing any object there.</param>
    /// <param name="options">How to copy, or null to keep the source's content type and metadata.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>What the store now holds under the destination key.</returns>
    /// <exception cref="Exceptions.ObjectNotFoundException">Thrown when the source does not exist.</exception>
    Task<ObjectInfo> CopyAsync(string sourceKey, string destinationKey, ObjectCopyOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies an object into another store.
    /// </summary>
    /// <param name="sourceKey">The key to copy from.</param>
    /// <param name="destination">The store to copy into.</param>
    /// <param name="destinationKey">The key to copy to, replacing any object there.</param>
    /// <param name="options">How to copy, or null to keep the source's content type and metadata.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>What the destination now holds under the destination key.</returns>
    /// <remarks>
    /// Server-side, with no bytes passing through this process, when both stores sit on the same
    /// connection. Otherwise, the content is streamed through the caller.
    /// </remarks>
    /// <exception cref="Exceptions.ObjectNotFoundException">Thrown when the source does not exist.</exception>
    Task<ObjectInfo> CopyToAsync(string sourceKey, IObjectStore destination, string destinationKey, ObjectCopyOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a URL that lets its holder download an object.
    /// </summary>
    /// <param name="key">The object key.</param>
    /// <param name="options">How to make it, or null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The signed request.</returns>
    /// <remarks>
    /// Does not check that the object exists. The URL only fails when it is used.
    /// </remarks>
    /// <exception cref="Exceptions.ObjectStoreException">
    /// Thrown with <see cref="Exceptions.ObjectStoreErrorCode.NotSupported"/> when the provider cannot sign.
    /// </exception>
    Task<PresignedRequest> CreateDownloadUrlAsync(string key, PresignedDownloadOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a URL that lets its holder upload an object.
    /// </summary>
    /// <param name="key">The object key.</param>
    /// <param name="options">How to make it, or null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The signed request. The caller must send its <see cref="PresignedRequest.RequiredHeaders"/>.</returns>
    /// <exception cref="Exceptions.ObjectStoreException">
    /// Thrown with <see cref="Exceptions.ObjectStoreErrorCode.NotSupported"/> when the provider cannot sign.
    /// </exception>
    Task<PresignedRequest> CreateUploadUrlAsync(string key, PresignedUploadOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a browser upload form that holds an upload to a size and a content type.
    /// </summary>
    /// <param name="key">The object key.</param>
    /// <param name="options">The limits to sign in, or null for none.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The signed form.</returns>
    /// <exception cref="Exceptions.ObjectStoreException">
    /// Thrown with <see cref="Exceptions.ObjectStoreErrorCode.NotSupported"/> when the provider cannot sign.
    /// </exception>
    Task<PresignedPost> CreateUploadFormAsync(string key, PresignedPostOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a multipart upload.
    /// </summary>
    /// <param name="key">The key the finished object will have.</param>
    /// <param name="options">How to store the finished object, or null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The upload. Complete it or abort it, because the store keeps its parts until then.</returns>
    Task<MultipartUpload> StartMultipartUploadAsync(string key, MultipartUploadOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads one part of a multipart upload.
    /// </summary>
    /// <param name="upload">The upload.</param>
    /// <param name="partNumber">The part's position, from 1 to 10000.</param>
    /// <param name="content">The part's content. Every part but the last is at least 5 MiB on S3.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The part, to pass to <see cref="CompleteMultipartUploadAsync"/>.</returns>
    Task<UploadedPart> UploadPartAsync(MultipartUpload upload, int partNumber, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a URL that lets its holder upload one part of a multipart upload.
    /// </summary>
    /// <param name="upload">The upload.</param>
    /// <param name="partNumber">The part's position, from 1 to 10000.</param>
    /// <param name="expiry">How long the URL works, or null for the store's default.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The signed request. The ETag the store answers with is what completing the upload needs, so the
    /// client has to report it back.
    /// </returns>
    /// <exception cref="Exceptions.ObjectStoreException">
    /// Thrown with <see cref="Exceptions.ObjectStoreErrorCode.NotSupported"/> when the provider cannot sign.
    /// </exception>
    Task<PresignedRequest> CreatePartUploadUrlAsync(MultipartUpload upload, int partNumber, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the parts a multipart upload has so far.
    /// </summary>
    /// <param name="upload">The upload.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The parts, by part number.</returns>
    /// <exception cref="Exceptions.ObjectNotFoundException">Thrown when the upload does not exist.</exception>
    Task<IReadOnlyList<UploadedPart>> ListPartsAsync(MultipartUpload upload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finishes a multipart upload, assembling its parts into the object.
    /// </summary>
    /// <param name="upload">The upload.</param>
    /// <param name="parts">Every part, in ascending part-number order.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>What the store now holds under the key.</returns>
    /// <exception cref="Exceptions.ObjectNotFoundException">Thrown when the upload does not exist.</exception>
    Task<ObjectInfo> CompleteMultipartUploadAsync(MultipartUpload upload, IEnumerable<UploadedPart> parts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abandons a multipart upload and discards its parts.
    /// </summary>
    /// <param name="upload">The upload.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the parts are discarded.</returns>
    /// <exception cref="Exceptions.ObjectNotFoundException">Thrown when the upload does not exist.</exception>
    Task AbortMultipartUploadAsync(MultipartUpload upload, CancellationToken cancellationToken = default);
}
