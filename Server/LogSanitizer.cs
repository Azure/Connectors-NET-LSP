using System.Text.RegularExpressions;

namespace SdkLspServer;

/// <summary>
/// Sanitizes strings for diagnostic logging to avoid leaking PII such as
/// subscription IDs, resource group names, connection resource IDs, and runtime URLs.
/// </summary>
internal static partial class LogSanitizer
{
    /// <summary>
    /// Redacts sensitive segments from URLs and identifiers commonly found in
    /// Azure API Hub URLs, ARM resource paths, and connection runtime URLs.
    /// </summary>
    public static string SanitizeUrl(string url)
    {
        // Redact subscription IDs: /subscriptions/{guid-or-id}/
        string result = SubscriptionRegex().Replace(url, "/subscriptions/***/");

        // Redact resource groups: /resourceGroups/{name}/
        result = ResourceGroupRegex().Replace(result, "/resourceGroups/***/");

        // Redact connection resource IDs in /connections/{id}/ paths
        result = ConnectionIdRegex().Replace(result, "/connections/***/");

        // Redact connection resource names in /apim/{connector}/{resourceId} runtime URLs
        result = ApimResourceRegex().Replace(result, "$1/***/");

        return result;
    }

    [GeneratedRegex(@"/subscriptions/[^/]+/")]
    private static partial Regex SubscriptionRegex();

    [GeneratedRegex(@"/resourceGroups/[^/]+/")]
    private static partial Regex ResourceGroupRegex();

    [GeneratedRegex(@"/connections/[^/]+/")]
    private static partial Regex ConnectionIdRegex();

    [GeneratedRegex(@"(/apim/[^/]+)/[^/?]+")]
    private static partial Regex ApimResourceRegex();
}
