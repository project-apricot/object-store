namespace ApricotFramework.ObjectStore.DependencyInjection.Tests;

internal sealed class FixedSource(ObjectStoreDefinition definition) : IObjectStoreDefinitionSource
{
    public ObjectStoreDefinition? Find(string name)
    {
        return string.Equals(name, definition.Name, StringComparison.OrdinalIgnoreCase) ? definition : null;
    }
}
