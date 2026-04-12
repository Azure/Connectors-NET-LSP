using System.Text.Json;

using SdkLspServer.Handlers.HoverHandler;
using SdkLspServer.Services.Api;
using SdkLspServer.Services.Connections;

namespace SdkLspServer.Handlers.CodeActionHandler;

/// <summary>
/// Fetches JSON Schema from a connector's dynamic schema discovery endpoint.
/// Reuses the same API infrastructure as dynamic values fetching.
/// </summary>
internal sealed class DynamicSchemaFetcher
{
    private readonly ConnectionsService connectionsService;
    private readonly ApiService apiService;

    public DynamicSchemaFetcher(ConnectionsService connectionsService, ApiService apiService)
    {
        this.connectionsService = connectionsService;
        this.apiService = apiService;
    }

    /// <summary>
    /// Fetches the JSON Schema for a dynamic schema operation.
    /// </summary>
    /// <param name="connectorName">The connector name (e.g., "teams").</param>
    /// <param name="operationId">The schema discovery operation ID from the [DynamicSchema] attribute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parsed JSON Schema element, or null if the schema could not be fetched.</returns>
    public async Task<JsonElement?> FetchSchemaAsync(
        string connectorName,
        string operationId,
        CancellationToken cancellationToken)
    {
        try
        {
            DynamicOperationMetadata? metadata = DynamicOperationsRegistry.GetOperationMetadata(connectorName, operationId);
            if (metadata == null)
            {
                await Console.Error.WriteLineAsync($"[DynamicSchemaFetcher] No metadata found for {connectorName}:{operationId}");
                return null;
            }

            string? connectionName = DynamicValuesHelper.ResolveConnectionByConnectorType(connectionsService, connectorName);
            if (string.IsNullOrEmpty(connectionName))
            {
                await Console.Error.WriteLineAsync($"[DynamicSchemaFetcher] No connection found for connector '{connectorName}'");
                return null;
            }

            // Substitute path parameters with defaults for schema discovery
            DynamicOperationMetadata resolvedMetadata = ResolvePathParameters(metadata);

            (string? apiUrl, bool isDirectClient) = DynamicValuesHelper.BuildApiUrl(
                connectionsService,
                connectionName,
                resolvedMetadata,
                apiService.Config);

            if (string.IsNullOrEmpty(apiUrl))
            {
                await Console.Error.WriteLineAsync($"[DynamicSchemaFetcher] Could not build API URL for {connectorName}:{operationId}");
                return null;
            }

            await Console.Error.WriteLineAsync($"[DynamicSchemaFetcher] Fetching schema from {apiUrl} (DirectClient={isDirectClient})");

            JsonElement? response;
            if (string.Equals(metadata.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                response = await apiService.GetJsonAsync<JsonElement?>(apiUrl, cancellationToken);
            }
            else
            {
                // POST with query parameters in body when present; otherwise use empty object
                object body = resolvedMetadata.QueryParameters.Count > 0
                    ? resolvedMetadata.QueryParameters
                    : new { };
                response = await apiService.PostJsonAsync<JsonElement?>(apiUrl, body, cancellationToken);
            }

            if (response == null)
            {
                await Console.Error.WriteLineAsync($"[DynamicSchemaFetcher] No response from {apiUrl}");
                return null;
            }

            // Try to extract the schema from a "schema" wrapper if present
            JsonElement result = response.Value;
            if (result.TryGetProperty("schema", out JsonElement schemaProperty))
            {
                return schemaProperty;
            }

            // The response itself may be the schema
            if (result.TryGetProperty("properties", out _) ||
                result.TryGetProperty("type", out _))
            {
                return result;
            }

            await Console.Error.WriteLineAsync($"[DynamicSchemaFetcher] Response does not look like a JSON Schema; returning null.");
            return null;
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
            await Console.Error.WriteLineAsync($"[DynamicSchemaFetcher] Error fetching schema for {connectorName}:{operationId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Substitutes path parameters like {actionType}, {poster}, {recipientType} with
    /// reasonable defaults for schema discovery. The schema shape is typically the same
    /// for all parameter combinations; the defaults just need to produce a valid response.
    /// </summary>
    private static DynamicOperationMetadata ResolvePathParameters(DynamicOperationMetadata metadata)
    {
        string path = metadata.Path;

        // Teams schema discovery defaults from the swagger:
        // actionType values: "Message", "ReplyWithMessage", "UnifiedAdaptiveCard", "GatherInput", "ReplyWithAdaptiveCard"
        path = path.Replace("{actionType}", "Message", StringComparison.OrdinalIgnoreCase);
        path = path.Replace("{poster}", "User", StringComparison.OrdinalIgnoreCase);
        path = path.Replace("{recipientType}", "channel", StringComparison.OrdinalIgnoreCase);
        path = path.Replace("{threadType}", "channel", StringComparison.OrdinalIgnoreCase);
        path = path.Replace("{notificationType}", "notification", StringComparison.OrdinalIgnoreCase);
        path = path.Replace("{triggerType}", "selectedMessage", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(path, metadata.Path, StringComparison.Ordinal))
        {
            return metadata;
        }

        return new DynamicOperationMetadata
        {
            Path = path,
            Method = metadata.Method,
            QueryParameters = metadata.QueryParameters,
        };
    }
}
