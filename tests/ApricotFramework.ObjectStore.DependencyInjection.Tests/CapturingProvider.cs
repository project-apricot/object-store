namespace ApricotFramework.ObjectStore.DependencyInjection.Tests;

/// <summary>
/// A provider that remembers what it was asked to build, and what it built.
/// </summary>
internal sealed class CapturingProvider(string name = "capture", params string[] problems) : IObjectStoreProvider
{
    public List<ObjectStoreDefinition> Definitions { get; } = [];

    public List<TrackedStore> Created { get; } = [];

    public string Name => name;

    public IReadOnlyList<string> Validate(ObjectStoreDefinition definition) => problems;

    public IObjectStore Create(ObjectStoreDefinition definition)
    {
        var store = new TrackedStore(definition.Name);

        this.Definitions.Add(definition);
        this.Created.Add(store);

        return store;
    }
}
