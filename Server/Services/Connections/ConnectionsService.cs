namespace SdkLspServer.Services.Connections;

/// <summary>
/// Service for managing connections configuration with support for runtime updates.
/// Provides thread-safe access to connections data that can be updated via LSP notifications.
/// </summary>
public class ConnectionsService
{
    private readonly object lockObject = new();
    private ConnectionsConfig? connections;

    /// <summary>
    /// Gets the current connections configuration.
    /// </summary>
    /// <returns>The current connections configuration, or null if not set.</returns>
    public ConnectionsConfig? GetConnections()
    {
        lock (lockObject)
        {
            return connections;
        }
    }

    /// <summary>
    /// Updates the connections configuration.
    /// </summary>
    /// <param name="config">The new connections configuration, or null to clear.</param>
    public void UpdateConnections(ConnectionsConfig? config)
    {
        lock (lockObject)
        {
            connections = config;
        }
    }

    /// <summary>
    /// Gets the total count of connections from all sources.
    /// </summary>
    /// <returns>The total number of connections, or 0 if no connections are configured.</returns>
    public int GetConnectionCount()
    {
        lock (lockObject)
        {
            int managed = connections?.ManagedApiConnections?.Count ?? 0;
            int directClient = connections?.DirectClientConnections?.Count ?? 0;
            return managed + directClient;
        }
    }
}
