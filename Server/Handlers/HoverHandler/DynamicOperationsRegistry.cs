namespace SdkLspServer.Handlers.HoverHandler;

/// <summary>
/// Registry for dynamic operations that discovers metadata from SDK assemblies via reflection.
/// Uses convention-based mapping to infer API paths from operation names.
/// </summary>
internal static class DynamicOperationsRegistry
{
    private static readonly object LockObject = new();
    private static SdkIndex? sdkIndex;
    private static Services.CompilationService? compilationService;
    private static Dictionary<string, DynamicOperationMetadata>? discoveredOperations;
    private static bool discoveryAttempted = false;

    /// <summary>
    /// Initialize the registry with the SDK index.
    /// This should be called once at startup.
    /// </summary>
    public static void Initialize(SdkIndex? index, Services.CompilationService? compilation = null)
    {
        lock (LockObject)
        {
            sdkIndex = index;
            compilationService = compilation;
            discoveryAttempted = false;
            discoveredOperations = null;

            // Also clear the discovery cache to force re-discovery
            SdkDynamicOperationsDiscovery.ClearCache();
        }
    }

    /// <summary>
    /// Get API details for a dynamic operation.
    /// Discovers from SDK using reflection and convention-based mapping.
    /// </summary>
    /// <param name="connectorName">The name of the connector.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <returns>The metadata for the operation if found; otherwise, null.</returns>
    public static DynamicOperationMetadata? GetOperationMetadata(string connectorName, string operationName)
    {
        string key = $"{connectorName.ToLowerInvariant()}:{operationName}";

        // Try SDK discovery (lazy initialization)
        if (!discoveryAttempted)
        {
            lock (LockObject)
            {
                if (!discoveryAttempted)
                {
                    discoveredOperations = SdkDynamicOperationsDiscovery.DiscoverOperations(sdkIndex, compilationService);
                    discoveryAttempted = true;
                }
            }
        }

        // Check discovered operations
        if (discoveredOperations?.TryGetValue(key, out DynamicOperationMetadata? discoveredMetadata) == true)
        {
            return discoveredMetadata;
        }

        // Fallback: check the JSON configuration file directly.
        // Discovery only finds operations referenced by [DynamicValues] parameters in the SDK.
        // Some operations (like GetDataSets for SharePoint) exist in the config but may not
        // be discovered if the SDK scanner misses them.
        return SdkDynamicOperationsDiscovery.GetOperationFromConfig(connectorName, operationName);
    }
}
