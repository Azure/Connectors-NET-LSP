using System.Text.RegularExpressions;

namespace SdkLspServer.Services.Connections;

/// <summary>
/// Helper class for extracting information from connection configurations.
/// Supports both Codeful (managedApiConnections) and DirectClient connection patterns.
/// </summary>
public static partial class ConnectionsHelper
{
    /// <summary>
    /// Extracts the connector type from an API ID.
    /// Example: "/subscriptions/.../managedApis/office365" -> "office365".
    /// </summary>
    /// <param name="apiId">The API ID string to extract the connector type from.</param>
    /// <returns>The extracted connector type, or "unknown" if extraction fails.</returns>
    public static string ExtractConnectorType(string apiId)
    {
        if (string.IsNullOrEmpty(apiId))
        {
            return "unknown";
        }

        Match match = ManagedApiRegex().Match(apiId);
        return match.Success ? match.Groups[1].Value : "unknown";
    }

    /// <summary>
    /// Extracts the ARM connection resource name from a connection runtime URL.
    /// Example: "https://instance.azure-apihub.net/apim/sharepointonline/abc123" -> "abc123".
    /// </summary>
    /// <param name="runtimeUrl">The connection runtime URL.</param>
    /// <returns>The extracted resource name, or null if extraction fails.</returns>
    public static string? ExtractConnectionResourceName(string? runtimeUrl)
    {
        if (string.IsNullOrEmpty(runtimeUrl))
        {
            return null;
        }

        Match match = RuntimeUrlRegex().Match(runtimeUrl);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Gets all unique connector types from both managed API and DirectClient connections.
    /// </summary>
    /// <param name="config">The connections configuration to extract connector types from.</param>
    /// <returns>An enumerable of unique connector types found in the configuration.</returns>
    public static IEnumerable<string> GetConnectorTypes(ConnectionsConfig? config)
    {
        if (config == null)
        {
            return Enumerable.Empty<string>();
        }

        IEnumerable<string> managedTypes = config.ManagedApiConnections?.Values
            .Select(conn => ExtractConnectorType(conn.Api.Id))
            ?? Enumerable.Empty<string>();

        IEnumerable<string> directClientTypes = config.DirectClientConnections?.Values
            .Select(conn => conn.ConnectorType)
            .Where(type => !string.IsNullOrEmpty(type))
            ?? Enumerable.Empty<string>();

        return managedTypes
            .Concat(directClientTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(type => !string.Equals(type, "unknown", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all connection names for a specific connector type from both connection sources.
    /// </summary>
    /// <param name="config">The connections configuration to search in.</param>
    /// <param name="connectorType">The type of connector to filter by.</param>
    /// <returns>An enumerable of connection names (dictionary keys) for the specified connector type.</returns>
    public static IEnumerable<string> GetConnectionNamesForConnector(ConnectionsConfig? config, string connectorType)
    {
        if (config == null)
        {
            return Enumerable.Empty<string>();
        }

        IEnumerable<string> managedNames = config.ManagedApiConnections?
            .Where(kvp => string.Equals(ExtractConnectorType(kvp.Value.Api.Id), connectorType, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            ?? Enumerable.Empty<string>();

        IEnumerable<string> directClientNames = config.DirectClientConnections?
            .Where(kvp => string.Equals(kvp.Value.ConnectorType, connectorType, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            ?? Enumerable.Empty<string>();

        return managedNames.Concat(directClientNames);
    }

    /// <summary>
    /// Gets all connection entries as a unified sequence of (key, connectorType, displayInfo) tuples.
    /// Used by handlers that need to enumerate all connections regardless of source.
    /// </summary>
    /// <param name="config">The connections configuration.</param>
    /// <returns>An enumerable of (connectionKey, connectorType, displayDetail) tuples.</returns>
    public static IEnumerable<(string Key, string ConnectorType, string Detail)> GetAllConnections(ConnectionsConfig? config)
    {
        if (config == null)
        {
            yield break;
        }

        if (config.ManagedApiConnections != null)
        {
            foreach (KeyValuePair<string, ManagedApiConnection> kvp in config.ManagedApiConnections)
            {
                string connectorType = ExtractConnectorType(kvp.Value.Api.Id);
                string detail = $"Connection ID: {kvp.Value.Connection?.Id ?? "N/A"}";
                yield return (kvp.Key, connectorType, detail);
            }
        }

        if (config.DirectClientConnections != null)
        {
            foreach (KeyValuePair<string, DirectClientConnection> kvp in config.DirectClientConnections)
            {
                string detail = $"DirectClient - {kvp.Value.ConnectorType}";
                yield return (kvp.Key, kvp.Value.ConnectorType, detail);
            }
        }
    }

    /// <summary>
    /// Checks whether a connection name exists in either managed API or DirectClient connections.
    /// </summary>
    /// <param name="config">The connections configuration.</param>
    /// <param name="connectionName">The connection name to look up.</param>
    /// <returns>True if the connection exists in any source.</returns>
    public static bool ContainsConnection(ConnectionsConfig? config, string connectionName)
    {
        if (config == null || string.IsNullOrEmpty(connectionName))
        {
            return false;
        }

        return (config.ManagedApiConnections?.ContainsKey(connectionName) == true) ||
            (config.DirectClientConnections?.ContainsKey(connectionName) == true);
    }

    /// <summary>
    /// Resolves the ARM connection resource name for use in dynamicInvoke API calls.
    /// For managed API connections, the dictionary key is the ARM resource name.
    /// For DirectClient connections, the resource name is extracted from the runtime URL.
    /// </summary>
    /// <param name="config">The connections configuration.</param>
    /// <param name="connectionName">The logical connection name (dictionary key).</param>
    /// <returns>The ARM connection resource name for dynamicInvoke, or null if not resolvable.</returns>
    public static string? ResolveArmConnectionName(ConnectionsConfig? config, string connectionName)
    {
        if (config == null || string.IsNullOrEmpty(connectionName))
        {
            return null;
        }

        // For managed API connections, the dictionary key IS the ARM resource name.
        if (config.ManagedApiConnections?.ContainsKey(connectionName) == true)
        {
            return connectionName;
        }

        // For DirectClient connections, extract from the runtime URL.
        if (config.DirectClientConnections?.TryGetValue(connectionName, out DirectClientConnection? directClient) == true)
        {
            return ExtractConnectionResourceName(directClient.ConnectionRuntimeUrl);
        }

        return null;
    }

    /// <summary>
    /// Gets the connection runtime URL for a DirectClient connection.
    /// Returns null for managed API connections (they use ARM-based URLs).
    /// </summary>
    /// <param name="config">The connections configuration.</param>
    /// <param name="connectionName">The logical connection name (dictionary key).</param>
    /// <returns>The full runtime URL for DirectClient connections, or null.</returns>
    public static string? GetDirectClientRuntimeUrl(ConnectionsConfig? config, string connectionName)
    {
        if (config?.DirectClientConnections?.TryGetValue(connectionName, out DirectClientConnection? directClient) == true)
        {
            return directClient.ConnectionRuntimeUrl;
        }

        return null;
    }

    [GeneratedRegex(@"/managedApis/([^/]+)")]
    private static partial Regex ManagedApiRegex();

    [GeneratedRegex(@"/apim/[^/]+/([^/?]+)")]
    private static partial Regex RuntimeUrlRegex();
}
