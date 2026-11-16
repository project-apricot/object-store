using ApricotFramework.ObjectStore.Impl;

namespace ApricotFramework.ObjectStore.Tests;

internal sealed class NamedProvider(string name) : IObjectStoreProvider
{
    public string Name => name;

    public IReadOnlyList<string> Validate(ObjectStoreDefinition definition) => [];

    public IObjectStore Create(ObjectStoreDefinition definition) => new MemoryObjectStore(definition.Name);
}
