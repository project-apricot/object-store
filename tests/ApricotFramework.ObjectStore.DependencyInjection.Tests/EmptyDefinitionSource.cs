namespace ApricotFramework.ObjectStore.DependencyInjection.Tests;

internal sealed class EmptyDefinitionSource : IObjectStoreDefinitionSource
{
    public ObjectStoreDefinition? Find(string name) => null;
}
