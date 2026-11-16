using ApricotFramework.ObjectStore.Impl;

namespace ApricotFramework.ObjectStore.Tests;

internal sealed class TrackedStore(string name) : MemoryObjectStore(name), IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose() => this.Disposed = true;
}
