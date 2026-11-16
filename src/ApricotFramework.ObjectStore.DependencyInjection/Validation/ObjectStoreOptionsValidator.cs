using ApricotFramework.ObjectStore.DependencyInjection.Extensions;
using ApricotFramework.ObjectStore.Exceptions;
using ApricotFramework.ObjectStore.Impl;
using ApricotFramework.ObjectStore.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace ApricotFramework.ObjectStore.DependencyInjection.Validation;

/// <summary>
/// Refuses an object store configuration that cannot work, while the host is starting.
/// </summary>
/// <remarks>
/// Asks each provider to check the stores that name it, so a missing bucket or a half-set key pair stops
/// the host rather than the first upload. Definitions supplied by an
/// <see cref="IObjectStoreDefinitionSource"/> are not visible here; where one is registered the checks
/// relax, since a name this cannot find may still be resolvable later.
/// </remarks>
public class ObjectStoreOptionsValidator : IValidateOptions<ObjectStoreOptions>
{
    /// <summary>
    /// The providers a store can name.
    /// </summary>
    private readonly ObjectStoreProviderRegistry providers;

    /// <summary>
    /// Whether definitions can come from somewhere that cannot be asked while the host starts, which relaxes the checks that need to know every store.
    /// </summary>
    private readonly bool hasSources;

    /// <summary>
    /// The section the options were bound from, or null when they were configured in code.
    /// </summary>
    private readonly IConfigurationSection? section;

    /// <summary>
    /// Creates a new instance of the validator.
    /// </summary>
    /// <param name="providers">Every registered provider.</param>
    /// <param name="sources">Every registered definition source.</param>
    /// <param name="section">
    /// The configuration section the options were bound from, or null when they were configured in
    /// code. Supplied so a declaration the binder silently discarded can still be reported.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="providers"/> or <paramref name="sources"/> is null.</exception>
    public ObjectStoreOptionsValidator(
        IEnumerable<IObjectStoreProvider> providers,
        IEnumerable<IObjectStoreDefinitionSource> sources,
        IConfigurationSection? section = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(sources);

        this.providers = new ObjectStoreProviderRegistry(providers);
        this.hasSources = sources.Any();
        this.section = section;
    }

    /// <inheritdoc />
    public virtual ValidateOptionsResult Validate(string? name, ObjectStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (this.providers.GetNames().Count == 0)
        {
            failures.Add("No object store provider is registered, so nothing can be stored. Add one, for example with AddS3ObjectStoreProvider().");
        }

        if (options.Stores.Count == 0 && !this.hasSources)
        {
            failures.Add($"No object store is configured under '{ObjectStoreServiceCollectionExtensions.ConfigurationSectionName}:Stores' and no definition source is registered, so every lookup would fail.");
        }

        foreach (var (storeName, entry) in options.Stores)
        {
            this.ValidateStore(storeName, entry, failures);
        }

        this.ValidateDefaultStore(options, failures);
        this.ValidateNothingWasDiscarded(options, failures);

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }

    private void ValidateStore(string storeName, ObjectStoreEntry? entry, List<string> failures)
    {
        if (entry is null)
        {
            failures.Add($"Object store '{storeName}' is declared with no settings at all.");

            return;
        }

        ObjectStoreDefinition definition;

        try
        {
            // the mapping is what validates that a provider is named
            definition = entry.ToDefinition(storeName);
        }
        catch (ObjectStoreConfigurationException e)
        {
            failures.Add(e.Message);

            return;
        }

        var registered = this.providers.GetNames();
        var provider = this.providers.Find(definition.Provider);

        if (provider is null)
        {
            // with no provider registered at all, that has already been reported once
            if (registered.Count > 0)
            {
                failures.Add($"Object store '{storeName}' uses provider '{definition.Provider}', which is not registered. Registered providers: {string.Join(", ", registered)}.");
            }

            return;
        }

        failures.AddRange(provider.Validate(definition));
    }

    private void ValidateDefaultStore(ObjectStoreOptions options, List<string> failures)
    {
        // Only a default that was asked for by name is checked. With none set, a host that uses named stores
        // has no use for one and must not be refused for lacking it: asking for the default is what fails,
        // and it fails there, naming what is missing. A source may well hold the default too, and cannot be
        // asked from here.
        if (this.hasSources || string.IsNullOrWhiteSpace(options.DefaultStore))
        {
            return;
        }

        if (!options.Stores.ContainsKey(options.DefaultStore.Trim()))
        {
            failures.Add($"{nameof(ObjectStoreOptions.DefaultStore)} is '{options.DefaultStore}', which is not a configured store. Configured stores: {string.Join(", ", options.Stores.Keys)}.");
        }
    }

    private void ValidateNothingWasDiscarded(ObjectStoreOptions options, List<string> failures)
    {
        if (this.section is null)
        {
            return;
        }

        // a store declared as a bare value rather than an object binds to nothing, with no error from the
        // binder — the store is simply absent, and every other check here would pass
        foreach (var declared in this.section.GetSection(nameof(ObjectStoreOptions.Stores)).GetChildren())
        {
            if (!options.Stores.ContainsKey(declared.Key))
            {
                failures.Add($"Object store '{declared.Key}' is declared as a single value rather than an object, so nothing was read from it. It needs at least a Provider.");
            }
        }
    }
}
