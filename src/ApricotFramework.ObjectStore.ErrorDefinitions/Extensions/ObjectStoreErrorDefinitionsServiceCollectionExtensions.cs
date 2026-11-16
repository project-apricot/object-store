using ApricotFramework.ErrorDefinitions.AspNetCore.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace ApricotFramework.ObjectStore.ErrorDefinitions.Extensions;

/// <summary>
/// Registers the mapper that turns object store failures into problem+json.
/// </summary>
public static class ObjectStoreErrorDefinitionsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the object store exception mapper.
    /// </summary>
    /// <param name="services">The services to add to.</param>
    /// <returns>The same collection, so calls chain.</returns>
    /// <remarks>
    /// A mapper with no handler does nothing. The host still has to call <c>AddErrorDefinitions</c>
    /// and <c>UseExceptionHandler</c>; where the handler sits in the pipeline is its decision, not
    /// this library's.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddObjectStoreErrorDefinitions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddExceptionErrorMapper<ObjectStoreExceptionMapper>();
    }
}
