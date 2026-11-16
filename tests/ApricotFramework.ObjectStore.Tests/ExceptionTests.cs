using ApricotFramework.ObjectStore.Exceptions;

namespace ApricotFramework.ObjectStore.Tests;

public class ExceptionTests
{
    [Theory]
    [InlineData(ObjectStoreErrorCode.Throttled, true)]
    [InlineData(ObjectStoreErrorCode.Unavailable, true)]
    [InlineData(ObjectStoreErrorCode.Timeout, true)]
    [InlineData(ObjectStoreErrorCode.NotFound, false)]
    [InlineData(ObjectStoreErrorCode.AlreadyExists, false)]
    [InlineData(ObjectStoreErrorCode.PreconditionFailed, false)]
    [InlineData(ObjectStoreErrorCode.InvalidKey, false)]
    [InlineData(ObjectStoreErrorCode.InvalidRequest, false)]
    [InlineData(ObjectStoreErrorCode.AccessDenied, false)]
    [InlineData(ObjectStoreErrorCode.Configuration, false)]
    [InlineData(ObjectStoreErrorCode.StoreNotFound, false)]
    [InlineData(ObjectStoreErrorCode.NotSupported, false)]
    [InlineData(ObjectStoreErrorCode.Unknown, false)]
    public void IsTransient_OnlyForConditionsThatClear(ObjectStoreErrorCode code, bool expected)
    {
        Assert.Equal(expected, new ObjectStoreException(code, "x").IsTransient());
    }

    [Fact]
    public void Subclasses_CarryTheirCode()
    {
        Assert.Equal(ObjectStoreErrorCode.NotFound, new ObjectNotFoundException().ErrorCode);
        Assert.Equal(ObjectStoreErrorCode.AlreadyExists, new ObjectAlreadyExistsException().ErrorCode);
        Assert.Equal(ObjectStoreErrorCode.Configuration, new ObjectStoreConfigurationException().ErrorCode);
    }

    [Fact]
    public void Subclasses_AreObjectStoreExceptions()
    {
        Assert.IsAssignableFrom<ObjectStoreException>(new ObjectNotFoundException("m"));
        Assert.IsAssignableFrom<ObjectStoreException>(new ObjectAlreadyExistsException("m"));
        Assert.IsAssignableFrom<ObjectStoreException>(new ObjectStoreConfigurationException("m"));
    }

    [Fact]
    public void Context_IsKeptAndTheInnerExceptionIsWrapped()
    {
        var inner = new InvalidOperationException("sdk");
        var e = new ObjectNotFoundException("m", "store", "s3", "k", inner);

        Assert.Equal("store", e.StoreName);
        Assert.Equal("s3", e.Provider);
        Assert.Equal("k", e.Key);
        Assert.Same(inner, e.InnerException);
    }
}
