using SdkLspServer.Handlers;
using SdkLspServer.Services.Api;
using SdkLspServer.Services.Connections;

namespace SdkLspServer;

/// <summary>
/// Shared helper methods for resolving connections and building API URLs for dynamic values.
/// Used by both CompletionHandler and HoverHandler to avoid code duplication.
/// </summary>
internal static class DynamicValuesHelper
{
    /// <summary>
    /// Resolves a connection name for a given connector type when the connection is injected via DI
    /// and not available as a method argument. Only auto-resolves when exactly one connection
    /// matches the connector type to avoid using the wrong connection in multi-connection scenarios.
    /// </summary>
    /// <param name="connectionsService">The connections service to query.</param>
    /// <param name="connectorName">The connector type (e.g., "sharepointonline").</param>
    /// <returns>The connection name if exactly one match exists; null otherwise.</returns>
    public static string? ResolveConnectionByConnectorType(ConnectionsService connectionsService, string connectorName)
    {
        ConnectionsConfig? connections = connectionsService.GetConnections();
        var matchingConnections = ConnectionsHelper.GetConnectionNamesForConnector(connections, connectorName).ToList();

        if (matchingConnections.Count == 1)
        {
            return matchingConnections[0];
        }

        return null;
    }

    /// <summary>
    /// Gets the count of connections matching a connector type.
    /// Used for diagnostic logging when resolution fails due to multiple matches.
    /// </summary>
    public static int GetConnectionCountForConnector(ConnectionsService connectionsService, string connectorName)
    {
        ConnectionsConfig? connections = connectionsService.GetConnections();
        return ConnectionsHelper.GetConnectionNamesForConnector(connections, connectorName).Count();
    }

    /// <summary>
    /// Checks if a connection name is a valid connection key in the connections config.
    /// Returns false for values that look like method arguments (URLs, IDs) rather than connection keys.
    /// </summary>
    public static bool IsValidConnectionKey(ConnectionsService connectionsService, string connectionName)
    {
        ConnectionsConfig? connections = connectionsService.GetConnections();
        return ConnectionsHelper.ContainsConnection(connections, connectionName);
    }

    /// <summary>
    /// Builds the full API URL for a dynamic values operation, branching between
    /// DirectClient (runtime URL + operation path) and ARM (/dynamicInvoke).
    /// </summary>
    /// <param name="connectionsService">The connections service.</param>
    /// <param name="connectionName">The resolved connection name.</param>
    /// <param name="metadata">The operation metadata with path and method.</param>
    /// <returns>The full API URL, or null if the URL cannot be constructed. Also returns whether this is a DirectClient connection.</returns>
    public static (string? Url, bool IsDirectClient) BuildApiUrl(
        ConnectionsService connectionsService,
        string connectionName,
        DynamicOperationMetadata metadata,
        ApiServiceConfig? apiConfig = null)
    {
        ConnectionsConfig? connections = connectionsService.GetConnections();
        string? runtimeUrl = ConnectionsHelper.GetDirectClientRuntimeUrl(connections, connectionName);

        if (!string.IsNullOrEmpty(runtimeUrl))
        {
            string directUrl = $"{runtimeUrl.TrimEnd('/')}{metadata.Path}";
            return (directUrl, true);
        }

        if (apiConfig == null)
        {
            return (null, false);
        }

        string? armConnectionName = ConnectionsHelper.ResolveArmConnectionName(connections, connectionName);
        if (string.IsNullOrEmpty(armConnectionName))
        {
            return (null, false);
        }

        string armUrl = $"{apiConfig.BaseUrl}/subscriptions/{apiConfig.SubscriptionId}/resourceGroups/{apiConfig.ResourceGroup}/providers/Microsoft.Web/connections/{armConnectionName}/dynamicInvoke?api-version={apiConfig.ApiVersion}";
        return (armUrl, false);
    }

    /// <summary>
    /// Infers the connector name from a method's containing type by stripping known suffixes.
    /// For example: SharepointonlineClient -> sharepointonline, TeamsClient -> teams.
    /// </summary>
    public static string? InferConnectorFromContainingType(string? containingTypeName)
    {
        if (string.IsNullOrEmpty(containingTypeName))
        {
            return null;
        }

        string[] suffixes = new[] { "Client", "Extensions", "Service", "Operations" };
        foreach (string suffix in suffixes)
        {
            if (containingTypeName.EndsWith(suffix, StringComparison.Ordinal) && containingTypeName.Length > suffix.Length)
            {
                return containingTypeName.Substring(0, containingTypeName.Length - suffix.Length).ToLowerInvariant();
            }
        }

        return null;
    }
}
