using ApricotFramework.ObjectStore.Model;

namespace ApricotFramework.ObjectStore.Impl;

/// <summary>
/// One object held by <see cref="MemoryObjectStore"/>.
/// </summary>
/// <param name="Data">The content. Never changed once stored, so copies may share it.</param>
/// <param name="Info">What the store reports about the object.</param>
internal sealed record MemoryEntry(byte[] Data, ObjectInfo Info);
