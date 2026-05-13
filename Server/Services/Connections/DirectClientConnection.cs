using System.Text.Json.Serialization;

namespace SdkLspServer.Services.Connections;

/// <summary>
/// Represents a connection configured via the DirectClient SDK pattern.
/// DirectClient connections are defined in local.settings.json or app configuration
/// rather than connections.json, but still use ARM connection resources for dynamic value resolution.
/// </summary>
public class DirectClientConnection
{
    /// <summary>
    /// Gets or sets the connector type identifier (e.g., "office365", "sharepointonline").
    /// </summary>
    [JsonPropertyName("connectorType")]
    public string ConnectorType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the connection runtime URL from which the ARM connection resource name is derived.
    /// Format: https://{instance}.azure-apihub.net/apim/{connectorType}/{connectionResourceName}.
    /// </summary>
    [JsonPropertyName("connectionRuntimeUrl")]
    public string ConnectionRuntimeUrl { get; set; } = string.Empty;
}
