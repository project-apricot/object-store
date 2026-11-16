namespace ApricotFramework.ObjectStore;

/// <summary>
/// Builds stores of one kind, such as S3.
/// </summary>
/// <remarks>
/// An implementation must be stateless and safe to use from several threads: one instance is shared by
/// every store naming it, and everything it needs arrives on the definition. A store it creates that
/// holds resources, such as an HTTP client, should implement <see cref="IDisposable"/>; the factory
/// disposes it, and so may a container, so disposing it twice must do nothing.
/// </remarks>
public interface IObjectStoreProvider
{
    /// <summary>
    /// Gets the name a definition's <see cref="ObjectStoreDefinition.Provider"/> matches, such as
    /// <c>s3</c>.
    /// </summary>
    /// <remarks>
    /// Matched case-insensitively, and no two registered providers may share one.
    /// </remarks>
    string Name { get; }

    /// <summary>
    /// Checks a definition without creating anything.
    /// </summary>
    /// <param name="definition">The definition to check.</param>
    /// <returns>What is wrong with it, or an empty list when it is usable.</returns>
    /// <remarks>
    /// What lets startup validation report a missing bucket while the host is starting, rather than on
    /// the first upload. Messages name settings, never their values.
    /// </remarks>
    IReadOnlyList<string> Validate(ObjectStoreDefinition definition);

    /// <summary>
    /// Creates the store a definition describes.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The store.</returns>
    /// <exception cref="Exceptions.ObjectStoreConfigurationException">
    /// Thrown when the definition's settings are unusable.
    /// </exception>
    IObjectStore Create(ObjectStoreDefinition definition);
}
