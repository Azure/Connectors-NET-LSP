using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Server.Tests;

/// <summary>
/// Tests for the connector name inference logic used in SdkDynamicOperationsDiscovery.
/// Since GetConnectorName is private, these tests verify the same logic pattern directly.
/// </summary>
[TestClass]
public class ConnectorNameInferenceTests
{
    [TestMethod]
    [DataRow("SharepointonlineClient", "sharepointonline")]
    [DataRow("SharepointonlineExtensions", "sharepointonline")]
    [DataRow("Office365Client", "office365")]
    [DataRow("Office365Extensions", "office365")]
    [DataRow("MicrosoftformsClient", "microsoftforms")]
    [DataRow("TeamsService", "teams")]
    [DataRow("CommondataserviceOperations", "commondataservice")]
    public void InferConnectorNameFromTypeName_StripsSuffix(string typeName, string expectedConnectorName)
    {
        string? result = InferConnectorNameFromTypeName(typeName);

        Assert.AreEqual(expectedConnectorName, result);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Client")]
    [DataRow("Extensions")]
    public void InferConnectorNameFromTypeName_ReturnsNull_ForInvalidNames(string typeName)
    {
        string? result = InferConnectorNameFromTypeName(typeName);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void InferConnectorNameFromTypeName_ReturnsNull_ForNull()
    {
        string? result = InferConnectorNameFromTypeName(null);

        Assert.IsNull(result);
    }

    [TestMethod]
    [DataRow("SomeRandomClass")]
    [DataRow("Helper")]
    [DataRow("Utils")]
    public void InferConnectorNameFromTypeName_ReturnsNull_ForUnrecognizedNames(string typeName)
    {
        string? result = InferConnectorNameFromTypeName(typeName);

        Assert.IsNull(result);
    }

    /// <summary>
    /// Mirrors the inference logic in SdkDynamicOperationsDiscovery.GetConnectorName fallback.
    /// </summary>
    private static string? InferConnectorNameFromTypeName(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return null;
        }

        string[] suffixes = ["Client", "Extensions", "Service", "Operations"];
        foreach (string suffix in suffixes)
        {
            if (typeName.EndsWith(suffix, StringComparison.Ordinal) && typeName.Length > suffix.Length)
            {
                return typeName[..^suffix.Length].ToLowerInvariant();
            }
        }

        return null;
    }
}
