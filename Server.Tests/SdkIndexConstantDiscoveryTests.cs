namespace Server.Tests;

/// <summary>
/// Tests for SdkIndex constant discovery and type-to-operation mapping.
/// Uses the actual SDK nupkg to validate that constants are discovered correctly.
/// </summary>
[TestClass]
public class SdkIndexConstantDiscoveryTests
{
    private static SdkLspServer.SdkIndex? sdkIndex;

    private static void SkipIfNoSdk()
    {
        if (sdkIndex is null)
        {
            Assert.Inconclusive("SDK .nupkg not found — skipping. Place it in the SDK/ directory to enable this test.");
        }
    }

    private static SdkLspServer.SdkIndex SdkIndex
    {
        get
        {
            SkipIfNoSdk();
            return sdkIndex!;
        }
    }

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
        SkipIfNoSdk();
    }

    [TestMethod]
    public void ConnectorNameConstants_ContainsExpectedEntries()
    {
        SkipIfNoSdk();
        Assert.IsTrue(
            SdkIndex.ConnectorNameConstants.Length >= 3,
            "Should have at least 3 connector names");

        var names = SdkIndex.ConnectorNameConstants.Select(c => c.Value).ToList();
        CollectionAssert.Contains(names, "office365");
        CollectionAssert.Contains(names, "sharepointonline");
        CollectionAssert.Contains(names, "teams");
    }

    [TestMethod]
    public void ConnectorNameConstants_HaveCorrectFieldNames()
    {
        SkipIfNoSdk();

        var office365 = SdkIndex.ConnectorNameConstants.FirstOrDefault(c => c.Value == "office365");
        Assert.IsNotNull(office365);
        Assert.AreEqual("Office365", office365.FieldName);
        Assert.AreEqual("ConnectorNames", office365.ClassName);
    }

    [TestMethod]
    public void TriggerOperationsByConnector_ContainsOffice365()
    {
        SkipIfNoSdk();
        Assert.IsTrue(
            SdkIndex.TriggerOperationsByConnector.ContainsKey("office365"),
            "Should have office365 trigger operations");
    }

    [TestMethod]
    public void TriggerOperationsByConnector_ContainsSharepointonline()
    {
        SkipIfNoSdk();
        Assert.IsTrue(
            SdkIndex.TriggerOperationsByConnector.ContainsKey("sharepointonline"),
            "Should have sharepointonline trigger operations");
    }

    [TestMethod]
    public void TriggerOperationsByConnector_ContainsTeams()
    {
        SkipIfNoSdk();
        Assert.IsTrue(
            SdkIndex.TriggerOperationsByConnector.ContainsKey("teams"),
            "Should have teams trigger operations");
    }

    [TestMethod]
    public void GetTriggerOperations_Office365_ReturnsExpectedOperations()
    {
        SkipIfNoSdk();
        var ops = SdkIndex.GetTriggerOperations("office365");

        Assert.IsTrue(ops.Length > 0, "Should have office365 operations");
        var opNames = ops.Select(o => o.Value).ToList();
        CollectionAssert.Contains(opNames, "OnNewEmail");
        CollectionAssert.Contains(opNames, "OnUpcomingEventsV3");
    }

    [TestMethod]
    public void GetTriggerOperations_CaseInsensitive()
    {
        SkipIfNoSdk();
        var ops1 = SdkIndex.GetTriggerOperations("office365");
        var ops2 = SdkIndex.GetTriggerOperations("Office365");

        Assert.AreEqual(ops1.Length, ops2.Length, "Should be case-insensitive");
    }

    [TestMethod]
    public void GetTriggerOperations_UnknownConnector_ReturnsEmpty()
    {
        SkipIfNoSdk();
        var ops = SdkIndex.GetTriggerOperations("nonexistent");
        Assert.AreEqual(0, ops.Length);
    }

    [TestMethod]
    public void GetAllTriggerOperations_ReturnsFromAllConnectors()
    {
        SkipIfNoSdk();
        var allOps = SdkIndex.GetAllTriggerOperations().ToList();

        Assert.IsTrue(allOps.Count > 10, "Should have many operations across all connectors");

        // Verify operations come from multiple connectors
        var classNames = allOps.Select(o => o.ClassName).Distinct().ToList();
        Assert.IsTrue(classNames.Count >= 3, "Should have operations from at least 3 connector classes");
    }

    [TestMethod]
    public void GetPayloadTypeForOperation_KnownOperation_ReturnsType()
    {
        SkipIfNoSdk();
        string? payloadType = SdkIndex.GetPayloadTypeForOperation("office365", "OnNewEmail");

        Assert.IsNotNull(payloadType);
        Assert.IsTrue(payloadType.EndsWith("Office365OnNewEmailTriggerPayload", StringComparison.Ordinal));
    }

    [TestMethod]
    public void GetPayloadTypeForOperation_UnknownOperation_ReturnsNull()
    {
        SkipIfNoSdk();
        string? payloadType = SdkIndex.GetPayloadTypeForOperation("office365", "NonexistentOperation");

        Assert.IsNull(payloadType);
    }

    [TestMethod]
    public void GetPayloadTypeForOperation_UnknownConnector_ReturnsNull()
    {
        SkipIfNoSdk();
        string? payloadType = SdkIndex.GetPayloadTypeForOperation("nonexistent", "OnNewEmail");

        Assert.IsNull(payloadType);
    }

    [TestMethod]
    public void TriggerPayloadTypes_ExistInTypeNames()
    {
        SkipIfNoSdk();
        var payloadTypes = SdkIndex.TypeNames
            .Where(t => t.EndsWith("TriggerPayload", StringComparison.Ordinal))
            .ToList();

        Assert.IsTrue(payloadTypes.Count >= 5, $"Should have at least 5 trigger payload types, found {payloadTypes.Count}");
    }

    [TestMethod]
    public async Task TryCreateFromAssembliesAsync_ProducesSameResults_AsNupkgPath()
    {
        SkipIfNoSdk();

        // Get the DLL paths from the nupkg-based index
        var dllPaths = SdkIndex.AssemblyPaths
            .Where(assemblyPath => Path.GetFileName(assemblyPath).StartsWith("Microsoft.Azure.Connectors.Sdk", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.IsTrue(dllPaths.Length > 0, "Should have at least one SDK assembly");

        // Create a second index from the DLL paths directly
        var assemblyIndex = await SdkLspServer.SdkIndex.TryCreateFromAssembliesAsync(dllPaths);
        Assert.IsNotNull(assemblyIndex, "TryCreateFromAssembliesAsync should succeed for valid DLL paths");

        // Verify same connector constants are discovered
        Assert.AreEqual(
            SdkIndex.ConnectorNameConstants.Length,
            assemblyIndex.ConnectorNameConstants.Length,
            "Assembly-based index should discover the same connector name constants");

        var nupkgNames = SdkIndex.ConnectorNameConstants.Select(constant => constant.Value).OrderBy(name => name, StringComparer.Ordinal).ToList();
        var assemblyNames = assemblyIndex.ConnectorNameConstants.Select(constant => constant.Value).OrderBy(name => name, StringComparer.Ordinal).ToList();
        CollectionAssert.AreEqual(nupkgNames, assemblyNames, "Connector names should match between nupkg and assembly indexing");

        // Verify same trigger operations are discovered
        Assert.AreEqual(
            SdkIndex.TriggerOperationsByConnector.Count,
            assemblyIndex.TriggerOperationsByConnector.Count,
            "Assembly-based index should discover the same trigger operation groups");
    }

    [TestMethod]
    public async Task TryCreateFromAssembliesAsync_NonexistentPath_ReturnsNull()
    {
        var result = await SdkLspServer.SdkIndex.TryCreateFromAssembliesAsync("/nonexistent/path.dll");
        Assert.IsNull(result);
    }
}
