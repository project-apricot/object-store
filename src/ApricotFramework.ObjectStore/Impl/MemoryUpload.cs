using ApricotFramework.ObjectStore.Model;

namespace ApricotFramework.ObjectStore.Impl;

/// <summary>
/// A multipart upload in progress in <see cref="MemoryObjectStore"/>.
/// </summary>
/// <param name="key">The key the finished object will have.</param>
/// <param name="options">How the finished object is stored, or null for the defaults.</param>
internal sealed class MemoryUpload(string key, MultipartUploadOptions? options)
{
    /// <summary>
    /// Gets the key the finished object will have.
    /// </summary>
    public string Key { get; } = key;

    /// <summary>
    /// Gets how the finished object is stored, or null for the defaults.
    /// </summary>
    public MultipartUploadOptions? Options { get; } = options;

    /// <summary>
    /// Gets the parts uploaded so far, by part number.
    /// </summary>
    public SortedDictionary<int, MemoryPart> Parts { get; } = [];
}
