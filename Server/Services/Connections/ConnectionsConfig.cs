using System.Text.Json.Serialization;

namespace SdkLspServer.Services.Connections;

/// <summary>
/// Represents the unified connections configuration supporting both Codeful (connections.json)
/// and DirectClient (local.settings.json / DI-based) connection patterns.
/// </summary>
public class ConnectionsConfig
{
    /// <summary>
    /// Managed API connections from a connections.json file (Codeful SDK pattern).
    /// </summary>
    [JsonPropertyName("managedApiConnections")]
    public Dictionary<string, ManagedApiConnection> ManagedApiConnections { get; set; } = [];

    /// <summary>
    /// DirectClient connections from local.settings.json or app configuration (DirectClient SDK pattern).
    /// Keys are logical connection names used in code (e.g., "SharePoint", "Office365").
    /// </summary>
    [JsonPropertyName("directClientConnections")]
    public Dictionary<string, DirectClientConnection> DirectClientConnections { get; set; } = [];
}
