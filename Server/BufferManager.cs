using System.Collections.Concurrent;

namespace SdkLspServer;

internal class BufferManager
{
    private readonly ConcurrentDictionary<string, string> buffers = new();

    public void UpdateBuffer(string documentPath, string str)
    {
        buffers.AddOrUpdate(documentPath, str, (k, v) => str);
    }

    public string? GetBuffer(string documentPath)
    {
        return buffers.TryGetValue(documentPath, out string? buffer) ? buffer : null;
    }

    public bool RemoveBuffer(string documentPath)
    {
        return buffers.TryRemove(documentPath, out _);
    }

    /// <summary>
    /// Gets all currently tracked document URIs and their contents.
    /// Used to re-trigger diagnostics when external state (e.g., connections) changes.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAllBuffers()
    {
        return new Dictionary<string, string>(this.buffers, this.buffers.Comparer);
    }
}
