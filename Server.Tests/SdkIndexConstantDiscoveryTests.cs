namespace Server.Tests;

/// <summary>
/// Tests for SdkIndex constant discovery and type-to-operation mapping.
/// Uses the actual SDK nupkg to validate that constants are discovered correctly.
/// </summary>
[TestClass]
public class SdkIndexConstantDiscoveryTests
{
    private static SdkLspServer.SdkIndex? sdkIndex;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext context)
    {
        _ = context;

        // Use the SDK nupkg bundled with the LSP server
        string nupkgPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "SDK",
            "Microsoft.Azure.Connectors.Sdk.1.0.0.nupkg");

        if (!File.Exists(nupkgPath))
        {
            // CI builds may use a different relative path
            nupkgPath = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(),
                "..",
                "..",
                "..",
                "..",
                "SDK",
                "Microsoft.Azure.Connectors.Sdk.1.0.0.nupkg"));
        }

        sdkIndex = await SdkLspServer.SdkIndex.TryCreateAsync(nupkgPath);
    }

    [TestMethod]
    public void SdkIndex_IsCreated_Successfully()
    {
        Assert.IsNotNull(sdkIndex, "SdkIndex should be creatable from the bundled nupkg");
    }

    [TestMethod]
    public void ConnectorNameConstants_ContainsExpectedEntries()
    {
        Assert.IsNotNull(sdkIndex);
        Assert.IsTrue(
            sdkIndex.ConnectorNameConstants.Length >= 3,
            "Should have at least 3 connector names");

        var names = sdkIndex.ConnectorNameConstants.Select(c => c.Value).ToList();
        CollectionAssert.Contains(names, "office365");
        CollectionAssert.Contains(names, "sharepointonline");
        CollectionAssert.Contains(names, "teams");
    }

    [TestMethod]
    public void ConnectorNameConstants_HaveCorrectFieldNames()
    {
        Assert.IsNotNull(sdkIndex);

        var office365 = sdkIndex.ConnectorNameConstants.FirstOrDefault(c => c.Value == "office365");
        Assert.IsNotNull(office365);
        Assert.AreEqual("Office365", office365.FieldName);
        Assert.AreEqual("ConnectorNames", office365.ClassName);
    }

    [TestMethod]
    public void TriggerOperationsByConnector_ContainsOffice365()
    {
        Assert.IsNotNull(sdkIndex);
        Assert.IsTrue(
            sdkIndex.TriggerOperationsByConnector.ContainsKey("office365"),
            "Should have office365 trigger operations");
    }

    [TestMethod]
    public void TriggerOperationsByConnector_ContainsSharepointonline()
    {
        Assert.IsNotNull(sdkIndex);
        Assert.IsTrue(
            sdkIndex.TriggerOperationsByConnector.ContainsKey("sharepointonline"),
            "Should have sharepointonline trigger operations");
    }

    [TestMethod]
    public void TriggerOperationsByConnector_ContainsTeams()
    {
        Assert.IsNotNull(sdkIndex);
        Assert.IsTrue(
            sdkIndex.TriggerOperationsByConnector.ContainsKey("teams"),
            "Should have teams trigger operations");
    }

    [TestMethod]
    public void GetTriggerOperations_Office365_ReturnsExpectedOperations()
    {
        Assert.IsNotNull(sdkIndex);
        var ops = sdkIndex.GetTriggerOperations("office365");

        Assert.IsTrue(ops.Length > 0, "Should have office365 operations");
        var opNames = ops.Select(o => o.Value).ToList();
        CollectionAssert.Contains(opNames, "OnNewEmailV3");
        CollectionAssert.Contains(opNames, "OnUpcomingEventsV3");
    }

    [TestMethod]
    public void GetTriggerOperations_CaseInsensitive()
    {
        Assert.IsNotNull(sdkIndex);
        var ops1 = sdkIndex.GetTriggerOperations("office365");
        var ops2 = sdkIndex.GetTriggerOperations("Office365");

        Assert.AreEqual(ops1.Length, ops2.Length, "Should be case-insensitive");
    }

    [TestMethod]
    public void GetTriggerOperations_UnknownConnector_ReturnsEmpty()
    {
        Assert.IsNotNull(sdkIndex);
        var ops = sdkIndex.GetTriggerOperations("nonexistent");
        Assert.AreEqual(0, ops.Length);
    }

    [TestMethod]
    public void GetAllTriggerOperations_ReturnsFromAllConnectors()
    {
        Assert.IsNotNull(sdkIndex);
        var allOps = sdkIndex.GetAllTriggerOperations().ToList();

        Assert.IsTrue(allOps.Count > 10, "Should have many operations across all connectors");

        // Verify operations come from multiple connectors
        var classNames = allOps.Select(o => o.ClassName).Distinct().ToList();
        Assert.IsTrue(classNames.Count >= 3, "Should have operations from at least 3 connector classes");
    }

    [TestMethod]
    public void GetPayloadTypeForOperation_KnownOperation_ReturnsType()
    {
        Assert.IsNotNull(sdkIndex);
        string? payloadType = sdkIndex.GetPayloadTypeForOperation("office365", "OnNewEmailV3");

        Assert.IsNotNull(payloadType);
        Assert.IsTrue(payloadType.EndsWith("Office365OnNewEmailV3TriggerPayload", StringComparison.Ordinal));
    }

    [TestMethod]
    public void GetPayloadTypeForOperation_UnknownOperation_ReturnsNull()
    {
        Assert.IsNotNull(sdkIndex);
        string? payloadType = sdkIndex.GetPayloadTypeForOperation("office365", "NonexistentOperation");

        Assert.IsNull(payloadType);
    }

    [TestMethod]
    public void GetPayloadTypeForOperation_UnknownConnector_ReturnsNull()
    {
        Assert.IsNotNull(sdkIndex);
        string? payloadType = sdkIndex.GetPayloadTypeForOperation("nonexistent", "OnNewEmailV3");

        Assert.IsNull(payloadType);
    }

    [TestMethod]
    public void TriggerPayloadTypes_ExistInTypeNames()
    {
        Assert.IsNotNull(sdkIndex);
        var payloadTypes = sdkIndex.TypeNames
            .Where(t => t.EndsWith("TriggerPayload", StringComparison.Ordinal))
            .ToList();

        Assert.IsTrue(payloadTypes.Count >= 5, $"Should have at least 5 trigger payload types, found {payloadTypes.Count}");
    }
}
