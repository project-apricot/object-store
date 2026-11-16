namespace ApricotFramework.ObjectStore.Tests;

internal sealed class FixedDefinitionSource(ObjectStoreDefinition definition) : IObjectStoreDefinitionSource
{
    public int Asked { get; private set; }

    public ObjectStoreDefinition? Find(string name)
    {
        this.Asked++;

        return string.Equals(name, definition.Name, StringComparison.OrdinalIgnoreCase) ? definition : null;
    }
}
