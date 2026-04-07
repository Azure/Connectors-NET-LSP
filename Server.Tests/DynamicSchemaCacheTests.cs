using SdkLspServer.Handlers.CodeActionHandler;

namespace SdkLspServer.Tests;

[TestClass]
public class DynamicSchemaCacheTests
{
    [TestMethod]
    public void ScanAssemblyForDynamicSchema_WithRealSdkDll_FindsDynamicSchemaTypes()
    {
        // Arrange — find a real SDK DLL from the temp extraction directory
        string tempDir = Path.Combine(Path.GetTempPath(), "sdk-lsp-server");
        if (!Directory.Exists(tempDir))
        {
            Assert.Inconclusive("No extracted SDK DLLs found in temp. Run the LSP server first.");
            return;
        }

        string? sdkDll = Directory.GetFiles(tempDir, "Microsoft.Azure.Connectors.Sdk.dll", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (sdkDll == null)
        {
            Assert.Inconclusive("SDK DLL not found in temp directory.");
            return;
        }

        // Act — scan using System.Reflection.Metadata (the fast path)
        var result = DynamicSchemaCache.ScanAssemblyForDynamicSchema(sdkDll);

        // Assert
        Assert.IsTrue(result.Count > 0, message: $"Expected [DynamicSchema] types but found {result.Count}. DLL: {sdkDll}");

        // Verify specific known types from the Teams connector
        Assert.IsTrue(result.ContainsKey("DynamicPostMessageRequest"), message: "Should find DynamicPostMessageRequest.");
        Assert.AreEqual("GetUnifiedActionSchema", result["DynamicPostMessageRequest"], message: "DynamicPostMessageRequest should map to GetUnifiedActionSchema.");

        // Log all found types for debugging
        foreach (var kvp in result.OrderBy(k => k.Key))
        {
            Console.WriteLine($"  {kvp.Key} -> {kvp.Value}");
        }
    }

    [TestMethod]
    public void ScanAssemblyForDynamicSchema_WithNonExistentFile_ReturnsEmpty()
    {
        var result = DynamicSchemaCache.ScanAssemblyForDynamicSchema(@"C:\nonexistent\fake.dll");
        Assert.AreEqual(0, result.Count);
    }
}
