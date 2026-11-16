namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// A browser upload form the store will accept for one object, within the limits it was signed with.
/// </summary>
/// <remarks>
/// POST <see cref="Fields"/> to <see cref="Url"/> as <c>multipart/form-data</c>, with the file as the
/// last field, named <c>file</c>. Like a presigned URL it is a bearer credential until it expires.
/// </remarks>
public sealed record PresignedPost
{
    /// <summary>
    /// Gets the URL to post to.
    /// </summary>
    public required Uri Url { get; init; }

    /// <summary>
    /// Gets the form fields to send, in order, before the file.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Fields { get; init; }

    /// <summary>
    /// Gets when the form stops working.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Presigned upload form, expires {this.ExpiresAt:O}.";
    }
}
