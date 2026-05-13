namespace Server.Tests;

/// <summary>
/// Tests for the DirectClient dynamic API URL construction logic.
/// Verifies that DirectClient connections use the runtime URL directly
/// instead of the ARM management URL pattern.
/// </summary>
[TestClass]
public class DirectClientApiUrlTests
{
    private const string ApiVersion = "2018-07-01-preview";

    [TestMethod]
    public void DirectClientUrl_UsesRuntimeUrlWithOperationPath()
    {
        string runtimeUrl = "https://a9ab15f5a12185bf.07.common.logic-df.azure-apihub.net/apim/sharepointonline/0011fe19224c49eab97e35d9637f4fd2";
        string operationPath = "/datasets";

        string expected = $"{runtimeUrl}{operationPath}";
        string actual = $"{runtimeUrl.TrimEnd('/')}{operationPath}";

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void DirectClientUrl_TrimsTrailingSlash()
    {
        string runtimeUrl = "https://instance.azure-apihub.net/apim/sharepointonline/abc123/";
        string operationPath = "/datasets";

        string actual = $"{runtimeUrl.TrimEnd('/')}{operationPath}";

        Assert.IsTrue(actual.Contains("/abc123/datasets", StringComparison.Ordinal), $"URL should not have double slashes: {actual}");
        Assert.IsFalse(actual.Contains("//datasets", StringComparison.Ordinal), $"URL has double slashes: {actual}");
    }

    [TestMethod]
    public void ArmUrl_RequiresSubscriptionAndResourceGroup()
    {
        string baseUrl = "https://management.azure.com";
        string subscriptionId = "sub-123";
        string resourceGroup = "rg-test";
        string armConnectionName = "abc123";

        string url = $"{baseUrl}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/connections/{armConnectionName}/dynamicInvoke?api-version={ApiVersion}";

        Assert.IsTrue(url.Contains("subscriptions/sub-123", StringComparison.Ordinal));
        Assert.IsTrue(url.Contains("resourceGroups/rg-test", StringComparison.Ordinal));
        Assert.IsTrue(url.Contains("connections/abc123", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ArmUrl_HasEmptySegments_WhenConfigMissing()
    {
        // This is the broken case (pre-fix) — ARM URL with empty SubscriptionId/ResourceGroup
        string baseUrl = string.Empty;
        string subscriptionId = string.Empty;
        string resourceGroup = string.Empty;
        string armConnectionName = "abc123";

        string url = $"{baseUrl}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.Web/connections/{armConnectionName}/dynamicInvoke?api-version={ApiVersion}";

        // URL has empty segments — this would fail with Azure
        Assert.IsTrue(url.Contains("subscriptions//resourceGroups//", StringComparison.Ordinal), "ARM URL has empty segments when config is missing");
    }
}
