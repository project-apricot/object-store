namespace ApricotFramework.ObjectStore.S3.Tests;

[CollectionDefinition(Name)]
public sealed class S3ServerDefinition : ICollectionFixture<S3ServerFixture>
{
    public const string Name = "s3-server";
}
