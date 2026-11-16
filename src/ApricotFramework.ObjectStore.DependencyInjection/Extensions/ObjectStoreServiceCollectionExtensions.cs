using ApricotFramework.ObjectStore.DependencyInjection.Validation;
using ApricotFramework.ObjectStore.Impl;
using ApricotFramework.ObjectStore.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ApricotFramework.ObjectStore.DependencyInjection.Extensions;

/// <summary>
/// Registers object stores on an <see cref="IServiceCollection"/>.
/// </summary>
public static class ObjectStoreServiceCollectionExtensions
{
    /// <summary>
    /// The configuration section bound when no other is named.
    /// </summary>
    public const string ConfigurationSectionName = "ObjectStore";

    /// <summary>
    /// Adds object stores, bound from the <c>ObjectStore</c> configuration section.
    /// </summary>
    /// <param name="services">The services' collection.</param>
    /// <param name="configuration">The configuration to bind from.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddObjectStores(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddObjectStores(configuration, ConfigurationSectionName);
    }

    /// <summary>
    /// Adds object stores, bound from a named configuration section.
    /// </summary>
    /// <param name="services">The services' collection.</param>
    /// <param name="configuration">The configuration to bind from.</param>
    /// <param name="sectionName">The section to bind.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Whatever the configuration providers contribute is bound, so environment variables override the
    /// file as they do everywhere else: <c>ObjectStore__Stores__uploads__Settings__Bucket</c>. Settings
    /// are read once: a change needs a restart. No provider
    /// is registered here, because which ones a host wants is its decision — add at least one, or startup
    /// validation fails. Registration order does not matter.
    /// <para>
    /// Three ways to get a store: take <see cref="IObjectStoreFactory"/> and ask by name; take
    /// <see cref="IObjectStore"/> for the default store; or take <c>[FromKeyedServices("name")]
    /// IObjectStore</c> for a named one.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sectionName"/> is null or blank.</exception>
    public static IServiceCollection AddObjectStores(this IServiceCollection services, IConfiguration configuration, string sectionName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var section = configuration.GetSection(sectionName);

        services.AddOptions<ObjectStoreOptions>().Bind(section);

        return AddObjectStoresCore(services, section);
    }

    /// <summary>
    /// Adds object stores, declared in code.
    /// </summary>
    /// <param name="services">The services' collection.</param>
    /// <param name="configure">Declares the stores.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="configure"/> is null.
    /// </exception>
    public static IServiceCollection AddObjectStores(this IServiceCollection services, Action<ObjectStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<ObjectStoreOptions>().Configure(configure);

        return AddObjectStoresCore(services, null);
    }

    /// <summary>
    /// Adds a source of store definitions, asked before configuration is.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <param name="services">The services' collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Sources accumulate: each is asked in the order it was registered, and the first one holding the
    /// name wins, with configuration asked last. Adding the same type twice does nothing. Registered as
    /// a singleton.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddObjectStoreDefinitionSource<TSource>(this IServiceCollection services)
        where TSource : class, IObjectStoreDefinitionSource
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IObjectStoreDefinitionSource, TSource>());

        return services;
    }

    /// <summary>
    /// Adds a source of store definitions that is already built.
    /// </summary>
    /// <param name="services">The services' collection.</param>
    /// <param name="source">The source.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="source"/> is null.
    /// </exception>
    public static IServiceCollection AddObjectStoreDefinitionSource(this IServiceCollection services, IObjectStoreDefinitionSource source)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(source);

        services.AddSingleton(source);

        return services;
    }

    /// <summary>
    /// Adds a provider that is already built.
    /// </summary>
    /// <param name="services">The services' collection.</param>
    /// <param name="provider">The provider.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="provider"/> is null.
    /// </exception>
    public static IServiceCollection AddObjectStoreProvider(this IServiceCollection services, IObjectStoreProvider provider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(provider);

        services.AddSingleton(provider);

        return services;
    }

    /// <summary>
    /// Adds a provider the container builds.
    /// </summary>
    /// <typeparam name="TProvider">The provider type.</typeparam>
    /// <param name="services">The services' collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Registered as a singleton and deduplicated by type. Two providers answering to the same name are
    /// not deduplicated: that fails when the factory is first resolved.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddObjectStoreProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IObjectStoreProvider
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IObjectStoreProvider, TProvider>());

        return services;
    }

    /// <summary>
    /// Adds the in-memory provider, which stores reach by setting <c>Provider</c> to
    /// <see cref="MemoryObjectStoreProvider.ProviderName"/>.
    /// </summary>
    /// <param name="services">The services' collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// The one instance is registered under both <see cref="IObjectStoreProvider"/> and its own type, so
    /// a test can take a <see cref="MemoryObjectStoreProvider"/> and read back what was written. Calling
    /// this twice is harmless. Nothing registers it for you: a store that forgets everything on restart
    /// is the last thing that should appear in a host by default.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddMemoryObjectStoreProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var provider = new MemoryObjectStoreProvider();

        // by instance rather than by factory: TryAddEnumerable refuses a factory, and both registrations
        // have to reach the same object for the stored data to be readable
        services.TryAddSingleton(provider);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IObjectStoreProvider>(provider));

        return services;
    }

    private static IServiceCollection AddObjectStoresCore(IServiceCollection services, IConfigurationSection? section)
    {
        // a factory, not a type: the validator needs the section the options came from, which only this
        // call knows, so it cannot be built from the container alone
        services.AddSingleton<IValidateOptions<ObjectStoreOptions>>(provider => new ObjectStoreOptionsValidator(
            provider.GetServices<IObjectStoreProvider>(),
            provider.GetServices<IObjectStoreDefinitionSource>(),
            section));

        // a misconfigured store surfaces when the host starts, not on the first upload
        services.AddOptions<ObjectStoreOptions>().ValidateOnStart();

        services.TryAddSingleton<IObjectStoreFactory>(provider => new DefaultObjectStoreFactory(
            provider.GetServices<IObjectStoreProvider>(),
            provider.GetRequiredService<IOptions<ObjectStoreOptions>>().Value,
            provider.GetServices<IObjectStoreDefinitionSource>()));

        services.TryAddSingleton(provider => provider.GetRequiredService<IObjectStoreFactory>().GetDefaultStore());

        services.TryAddKeyedSingleton<IObjectStore>(
            KeyedService.AnyKey,
            (provider, key) => provider.GetRequiredService<IObjectStoreFactory>().GetStore((string)key!));

        return services;
    }
}
