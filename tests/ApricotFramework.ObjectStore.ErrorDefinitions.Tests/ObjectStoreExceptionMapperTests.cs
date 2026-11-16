using ApricotFramework.ErrorDefinitions;
using ApricotFramework.ErrorDefinitions.AspNetCore;
using ApricotFramework.ErrorDefinitions.AspNetCore.Extensions;
using ApricotFramework.ObjectStore.ErrorDefinitions.Extensions;
using ApricotFramework.ObjectStore.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ApricotFramework.ObjectStore.ErrorDefinitions.Tests;

public class ObjectStoreExceptionMapperTests
{
    private static IReadOnlyList<ErrorDefinition>? Map(Exception exception)
    {
        var services = new ServiceCollection();

        services.AddErrorDefinitions();
        services.AddObjectStoreErrorDefinitions();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        var mapper = provider.GetServices<IExceptionErrorMapper>().Single(m => m.GetType().Name == "ObjectStoreExceptionMapper");

        return mapper.Map(new DefaultHttpContext(), exception);
    }

    private static ErrorDefinition MapOne(ObjectStoreErrorCode code)
    {
        return Assert.Single(Map(new ObjectStoreException(code, "internal detail"))!);
    }

    [Theory]
    [InlineData(ObjectStoreErrorCode.NotFound, ErrorKinds.NotFound, ObjectStoreErrors.ObjectNotFound, 404)]
    [InlineData(ObjectStoreErrorCode.AlreadyExists, ErrorKinds.AlreadyExists, ObjectStoreErrors.ObjectAlreadyExists, 409)]
    [InlineData(ObjectStoreErrorCode.PreconditionFailed, ErrorKinds.PreconditionFailed, ObjectStoreErrors.ObjectPreconditionFailed, 412)]
    [InlineData(ObjectStoreErrorCode.InvalidKey, ErrorKinds.Validation, ObjectStoreErrors.ObjectKeyInvalid, 400)]
    [InlineData(ObjectStoreErrorCode.InvalidRequest, ErrorKinds.Validation, ObjectStoreErrors.ObjectRequestInvalid, 400)]
    [InlineData(ObjectStoreErrorCode.Throttled, ErrorKinds.Unavailable, ObjectStoreErrors.StoreUnavailable, 503)]
    [InlineData(ObjectStoreErrorCode.Unavailable, ErrorKinds.Unavailable, ObjectStoreErrors.StoreUnavailable, 503)]
    [InlineData(ObjectStoreErrorCode.Timeout, ErrorKinds.Unavailable, ObjectStoreErrors.StoreUnavailable, 503)]
    public void Map_ClassifiesWhatTheCallerCanActOn(ObjectStoreErrorCode code, string kind, string errorCode, int status)
    {
        var error = MapOne(code);

        Assert.Equal(kind, error.Kind);
        Assert.Equal(errorCode, error.Code);
        Assert.Equal(status, ErrorKindStatus.ToHttpStatusCode(error.Kind));
    }

    // Our credentials, our configuration, our bug: blaming the caller with a 403 would send them to fix
    // something they cannot reach.
    [Theory]
    [InlineData(ObjectStoreErrorCode.AccessDenied)]
    [InlineData(ObjectStoreErrorCode.Configuration)]
    [InlineData(ObjectStoreErrorCode.StoreNotFound)]
    [InlineData(ObjectStoreErrorCode.NotSupported)]
    [InlineData(ObjectStoreErrorCode.Unknown)]
    public void Map_TheServicesOwnFaults_AreInternal_NeverAClientError(ObjectStoreErrorCode code)
    {
        var error = MapOne(code);

        Assert.Equal(ErrorKinds.Internal, error.Kind);
        Assert.Equal(ObjectStoreErrors.StoreFailed, error.Code);
        Assert.Equal(500, ErrorKindStatus.ToHttpStatusCode(error.Kind));
    }

    [Fact]
    public void Map_EveryErrorCode_IsCovered()
    {
        foreach (var code in Enum.GetValues<ObjectStoreErrorCode>())
        {
            Assert.NotEmpty(Map(new ObjectStoreException(code, "x"))!);
        }
    }

    [Fact]
    public void Map_Unavailable_SaysItIsRetryable()
    {
        Assert.Equal(true, MapOne(ObjectStoreErrorCode.Throttled).Payload!["retryable"]);
        Assert.Equal(true, MapOne(ObjectStoreErrorCode.Timeout).Payload!["retryable"]);
    }

    [Fact]
    public void Map_Subclasses_AreMappedLikeTheirCode()
    {
        Assert.Equal(ObjectStoreErrors.ObjectNotFound, Map(new ObjectNotFoundException("x"))![0].Code);
        Assert.Equal(ObjectStoreErrors.ObjectAlreadyExists, Map(new ObjectAlreadyExistsException("x"))![0].Code);
        Assert.Equal(ObjectStoreErrors.StoreFailed, Map(new ObjectStoreConfigurationException("x"))![0].Code);
    }

    // The store name and the provider are internal labels, the exception message names settings and
    // endpoints, and the key very often echoes whatever the caller sent.
    [Theory]
    [InlineData(ObjectStoreErrorCode.NotFound)]
    [InlineData(ObjectStoreErrorCode.AlreadyExists)]
    [InlineData(ObjectStoreErrorCode.InvalidKey)]
    [InlineData(ObjectStoreErrorCode.AccessDenied)]
    [InlineData(ObjectStoreErrorCode.Unavailable)]
    [InlineData(ObjectStoreErrorCode.Configuration)]
    public void Map_NeverLeaksStoreNameBucketEndpointKeyOrMessage(ObjectStoreErrorCode code)
    {
        var exception = new ObjectStoreException(
            code,
            "request to http://10.1.2.3:9000/secret-bucket failed",
            "internal-store-label",
            "s3",
            "private/user-42/passport.png",
            new InvalidOperationException("inner detail secret=hunter2"));

        var error = Assert.Single(Map(exception)!);
        var everything = string.Join('|', error.Kind, error.Code, error.Message, string.Join('|', (error.Payload ?? new Dictionary<string, object?>()).Select(p => $"{p.Key}={p.Value}")));

        foreach (var secret in new[] { "10.1.2.3", "secret-bucket", "internal-store-label", "user-42", "passport", "hunter2", "inner detail" })
        {
            Assert.DoesNotContain(secret, everything, StringComparison.Ordinal);
        }
    }

    // Answering an exception it does not own would disable every mapper registered after it.
    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(OperationCanceledException))]
    [InlineData(typeof(IOException))]
    public void Map_ForeignException_ReturnsNull(Type exceptionType)
    {
        Assert.Null(Map((Exception)Activator.CreateInstance(exceptionType)!));
    }

    [Fact]
    public void AddObjectStoreErrorDefinitions_Always_RegistersTheMapper()
    {
        var services = new ServiceCollection();

        services.AddErrorDefinitions();
        services.AddObjectStoreErrorDefinitions();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        Assert.Contains(provider.GetServices<IExceptionErrorMapper>(), m => m.GetType().Name == "ObjectStoreExceptionMapper");
    }

    // Two libraries may each register support without coordinating.
    [Fact]
    public void AddObjectStoreErrorDefinitions_CalledTwice_RegistersOneMapper()
    {
        var services = new ServiceCollection();

        services.AddErrorDefinitions();
        services.AddObjectStoreErrorDefinitions();
        services.AddObjectStoreErrorDefinitions();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        Assert.Single(provider.GetServices<IExceptionErrorMapper>(), m => m.GetType().Name == "ObjectStoreExceptionMapper");
    }

    [Fact]
    public void AddObjectStoreErrorDefinitions_NullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ObjectStoreErrorDefinitionsServiceCollectionExtensions.AddObjectStoreErrorDefinitions(null!));
    }
}
