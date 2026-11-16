namespace ApricotFramework.ObjectStore.Tests;

internal sealed class TrackingProvider : IObjectStoreProvider
{
    public List<TrackedStore> Created { get; } = [];

    public string Name => "tracking";

    public IReadOnlyList<string> Validate(ObjectStoreDefinition definition) => [];

    public IObjectStore Create(ObjectStoreDefinition definition)
    {
        var store = new TrackedStore(definition.Name);

        this.Created.Add(store);

        return store;
    }
}
