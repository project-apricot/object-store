namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// An object being read: its description and a stream over its content.
/// </summary>
/// <remarks>
/// Dispose it when done. The stream may be tied to an open connection, so holding one undisposed holds
/// that connection.
/// </remarks>
public sealed class StoredObject : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Anything else released with the content, such as a response.
    /// </summary>
    private readonly IDisposable? owner;

    /// <summary>
    /// Creates a new instance of the object.
    /// </summary>
    /// <param name="info">What the store knows about the object.</param>
    /// <param name="content">The content.</param>
    /// <param name="owner">
    /// Anything else that must be released with the content, such as a response, or null.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="info"/> or <paramref name="content"/> is null.</exception>
    public StoredObject(ObjectInfo info, Stream content, IDisposable? owner = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(content);

        this.Info = info;
        this.Content = content;
        this.owner = owner;
    }

    /// <summary>
    /// Gets what the store knows about the object.
    /// </summary>
    public ObjectInfo Info { get; }

    /// <summary>
    /// Gets the content, read from the start.
    /// </summary>
    public Stream Content { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Content.Dispose();
        this.owner?.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this.Content.DisposeAsync().ConfigureAwait(false);
        this.owner?.Dispose();
    }
}
