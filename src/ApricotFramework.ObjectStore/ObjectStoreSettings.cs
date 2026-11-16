using System.Globalization;
using ApricotFramework.ObjectStore.Exceptions;

namespace ApricotFramework.ObjectStore;

/// <summary>
/// A store's provider-specific settings, read by name.
/// </summary>
/// <remarks>
/// Deliberately untyped, so the core knows nothing about S3 or any vendor, and a new provider needs no
/// change here. Every provider reads what it needs through a typed wrapper of its own. Names are
/// matched case-insensitively, matching how configuration keys behave.
/// </remarks>
/// <param name="values">The settings, or null for none.</param>
/// <param name="storeName">
/// The store these belong to, used only to make a failure message say which store is misconfigured.
/// </param>
public sealed class ObjectStoreSettings(IReadOnlyDictionary<string, string?>? values = null, string? storeName = null)
{
    /// <summary>
    /// The settings, by name.
    /// </summary>
    private readonly Dictionary<string, string?> entries = values is null
        ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        : new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Settings with nothing in them.
    /// </summary>
    public static ObjectStoreSettings Empty { get; } = new();

    /// <summary>
    /// Gets the names present, whatever their values.
    /// </summary>
    /// <returns>The setting names.</returns>
    public IEnumerable<string> GetNames()
    {
        return this.entries.Keys;
    }

    /// <summary>
    /// Reads a setting as text.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <param name="fallback">What to return when the setting is absent, null, or blank.</param>
    /// <returns>The value, or the fallback.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    public string? GetString(string name, string? fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return this.entries.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
    }

    /// <summary>
    /// Reads a setting the provider cannot work without.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    /// <exception cref="ObjectStoreConfigurationException">Thrown when the setting is absent, null, or blank.</exception>
    public string GetRequiredString(string name)
    {
        return this.GetString(name) ?? throw this.Missing(name);
    }

    /// <summary>
    /// Reads a setting as a 32-bit whole number.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <param name="fallback">What to return when the setting is absent, null, or blank.</param>
    /// <returns>The value, or the fallback.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    /// <exception cref="ObjectStoreConfigurationException">Thrown when the value is present but not a number.</exception>
    public int GetInt32(string name, int fallback)
    {
        var value = this.GetString(name);

        if (value is null)
        {
            return fallback;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw this.Unreadable(name, "a whole number");
    }

    /// <summary>
    /// Reads a setting as a 64-bit whole number, such as a size in bytes.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <param name="fallback">What to return when the setting is absent, null, or blank.</param>
    /// <returns>The value, or the fallback.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    /// <exception cref="ObjectStoreConfigurationException">Thrown when the value is present but not a number.</exception>
    public long GetInt64(string name, long fallback)
    {
        var value = this.GetString(name);

        if (value is null)
        {
            return fallback;
        }

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw this.Unreadable(name, "a whole number");
    }

    /// <summary>
    /// Reads a setting as a boolean.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <param name="fallback">What to return when the setting is absent, null, or blank.</param>
    /// <returns>The value, or the fallback.</returns>
    /// <remarks>
    /// A JSON <c>true</c> reaches configuration as the text <c>True</c>, so parsing is
    /// case-insensitive.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    /// <exception cref="ObjectStoreConfigurationException">Thrown when the value is present but not a boolean.</exception>
    public bool GetBoolean(string name, bool fallback)
    {
        var value = this.GetString(name);

        if (value is null)
        {
            return fallback;
        }

        return bool.TryParse(value, out var parsed) ? parsed : throw this.Unreadable(name, "true or false");
    }

    /// <summary>
    /// Reads a setting as a duration, written as <c>hh:mm:ss</c>.
    /// </summary>
    /// <remarks>
    /// A bare number is rejected rather than read as days, which is what <see cref="TimeSpan"/>
    /// parsing would otherwise make of <c>30</c>.
    /// </remarks>
    /// <param name="name">The setting name.</param>
    /// <param name="fallback">What to return when the setting is absent, null, or blank.</param>
    /// <returns>The value, or the fallback.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    /// <exception cref="ObjectStoreConfigurationException">Thrown when the value is present but not a duration.</exception>
    public TimeSpan GetTimeSpan(string name, TimeSpan fallback)
    {
        var value = this.GetString(name);

        if (value is null)
        {
            return fallback;
        }

        // TimeSpan.Parse reads a bare "30" as thirty days, so an operator who writes the seconds they
        // meant would get a thirty-day timeout. Only the colon-separated form is accepted.
        if (!value.Contains(':', StringComparison.Ordinal) || !TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            throw this.Unreadable(name, "a duration written as hh:mm:ss, such as 00:00:30");
        }

        return parsed;
    }

    /// <summary>
    /// Reads a setting as one of an enumeration's names.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration.</typeparam>
    /// <param name="name">The setting name.</param>
    /// <param name="fallback">What to return when the setting is absent, null, or blank.</param>
    /// <returns>The value, or the fallback.</returns>
    /// <remarks>
    /// Matched by name and case-insensitively. A number is rejected because a stored ordinal would
    /// silently change meaning if the enumeration were ever reordered.
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    /// <exception cref="ObjectStoreConfigurationException">Thrown when the value is present but not a member name.</exception>
    public TEnum GetEnum<TEnum>(string name, TEnum fallback)
        where TEnum : struct, Enum
    {
        var value = this.GetString(name);

        if (value is null)
        {
            return fallback;
        }

        // Enum.TryParse also accepts an ordinal, so "1" would bind to whichever member happens to
        // sit at 1 today and to a different one after any reordering
        var numeric = value.Length > 0 && (char.IsAsciiDigit(value[0]) || value[0] is '-' or '+');

        if (numeric || !Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
        {
            throw this.Unreadable(name, $"one of {string.Join(", ", Enum.GetNames<TEnum>())}");
        }

        return parsed;
    }

    /// <summary>
    /// Builds the failure for a setting that is not there.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <returns>The exception to throw.</returns>
    public ObjectStoreConfigurationException Missing(string name)
    {
        return new ObjectStoreConfigurationException($"Setting '{name}' is required{this.Describe()}, and is missing or blank.");
    }

    /// <summary>
    /// Builds the failure for a setting that is there but cannot be read.
    /// </summary>
    /// <param name="name">The setting name.</param>
    /// <param name="expected">What the setting should have looked like.</param>
    /// <returns>The exception to throw.</returns>
    public ObjectStoreConfigurationException Unreadable(string name, string expected)
    {
        // the value is deliberately not quoted back: settings hold credentials, and this message
        // reaches logs
        return new ObjectStoreConfigurationException($"Setting '{name}'{this.Describe()} is expected to be {expected}.");
    }

    /// <summary>
    /// Names the owning store, when one was supplied.
    /// </summary>
    /// <returns>A phrase to splice into a message, which may be empty.</returns>
    private string Describe()
    {
        return storeName is null ? string.Empty : $" for object store '{storeName}'";
    }
}
