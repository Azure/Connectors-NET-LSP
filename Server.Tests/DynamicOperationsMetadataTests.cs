using System.Text.Json;

using SdkLspServer.Handlers;
using SdkLspServer.Handlers.HoverHandler;

namespace Server.Tests;

[TestClass]
public class DynamicOperationsMetadataTests
{
    [TestMethod]
    public void MetadataJson_ContainsSharePointGetDataSets()
    {
        string json = LoadMetadataJson();
        var config = JsonSerializer.Deserialize<Dictionary<string, JsonOperationEntry>>(json);

        Assert.IsNotNull(config);
        Assert.IsTrue(config.ContainsKey("sharepointonline:GetDataSets"), "Missing sharepointonline:GetDataSets entry");
        Assert.AreEqual("/datasets", config["sharepointonline:GetDataSets"].Path);
        Assert.AreEqual("GET", config["sharepointonline:GetDataSets"].Method);
    }

    [TestMethod]
    public void MetadataJson_ContainsSharePointGetTables()
    {
        string json = LoadMetadataJson();
        var config = JsonSerializer.Deserialize<Dictionary<string, JsonOperationEntry>>(json);

        Assert.IsNotNull(config);
        Assert.IsTrue(config.ContainsKey("sharepointonline:GetTables"), "Missing sharepointonline:GetTables entry");
        Assert.AreEqual("/datasets/{siteAddress}/tables", config["sharepointonline:GetTables"].Path);
        Assert.AreEqual("GET", config["sharepointonline:GetTables"].Method);
    }

    [TestMethod]
    public void MetadataJson_ContainsAllSharePointOperations()
    {
        string json = LoadMetadataJson();
        var config = JsonSerializer.Deserialize<Dictionary<string, JsonOperationEntry>>(json);

        Assert.IsNotNull(config);

        string[] requiredOperations =
        [
            "sharepointonline:GetDataSets",
            "sharepointonline:GetTables",
            "sharepointonline:GetTablesForLibraries",
            "sharepointonline:GetTablesForListsAndLibraries",
            "sharepointonline:GetTablesForApproval",
            "sharepointonline:GetTableViews",
            "sharepointonline:GetListImageFields",
            "sharepointonline:GetViewScopeOptions",
            "sharepointonline:GetEntitiesForUser",
            "sharepointonline:GetApprovalTypes",
            "sharepointonline:GetContentAssemblyTemplates",
            "sharepointonline:GetAgreementsSolutionTemplates",
            "sharepointonline:GetTablesForLightweightApproval",
        ];

        foreach (string operation in requiredOperations)
        {
            Assert.IsTrue(config.ContainsKey(operation), $"Missing entry: {operation}");
        }
    }

    [TestMethod]
    public void MetadataJson_ContainsExistingConnectors()
    {
        string json = LoadMetadataJson();
        var config = JsonSerializer.Deserialize<Dictionary<string, JsonOperationEntry>>(json);

        Assert.IsNotNull(config);

        // Verify existing entries weren't broken
        Assert.IsTrue(config.ContainsKey("microsoftforms:ListForms"));
        Assert.IsTrue(config.ContainsKey("teams:GetAllTeams"));
        Assert.IsTrue(config.ContainsKey("office365:CalendarGetTables_V2"));
        Assert.IsTrue(config.ContainsKey("commondataservice:GetOrganizations"));
    }

    [TestMethod]
    public void MetadataJson_IsValidJson()
    {
        string json = LoadMetadataJson();

        // Should not throw
        var doc = JsonDocument.Parse(json);
        Assert.IsNotNull(doc);
    }

    private static string LoadMetadataJson()
    {
        // Walk up from the test assembly directory until we find the repo root containing "Server/"
        string? dir = AppContext.BaseDirectory;
        string? jsonPath = null;

        while (dir != null)
        {
            string candidate = Path.Combine(dir, "Server", "Handlers", "HoverHandler", "DynamicOperationsMetadata.json");
            if (File.Exists(candidate))
            {
                jsonPath = candidate;
                break;
            }

            dir = Path.GetDirectoryName(dir);
        }

        Assert.IsNotNull(jsonPath, $"Metadata JSON not found walking up from: {AppContext.BaseDirectory}");
        return File.ReadAllText(jsonPath);
    }

    private class JsonOperationEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("queries")]
        public Dictionary<string, string>? Queries { get; set; }
    }
}
