using SdkLspServer.Services.Connections;

namespace Server.Tests;

[TestClass]
public class ConnectionsHelperTests
{
    [TestMethod]
    public void GetDirectClientRuntimeUrl_ReturnsUrl_ForDirectClientConnection()
    {
        var config = new ConnectionsConfig
        {
            DirectClientConnections = new Dictionary<string, DirectClientConnection>
            {
                ["SharePoint"] = new DirectClientConnection
                {
                    ConnectorType = "sharepointonline",
                    ConnectionRuntimeUrl = "https://instance.azure-apihub.net/apim/sharepointonline/abc123",
                },
            },
        };

        string? result = ConnectionsHelper.GetDirectClientRuntimeUrl(config, "SharePoint");

        Assert.AreEqual("https://instance.azure-apihub.net/apim/sharepointonline/abc123", result);
    }

    [TestMethod]
    public void GetDirectClientRuntimeUrl_ReturnsNull_ForManagedApiConnection()
    {
        var config = new ConnectionsConfig
        {
            ManagedApiConnections = new Dictionary<string, ManagedApiConnection>
            {
                ["myConnection"] = new ManagedApiConnection
                {
                    Api = new ApiInfo { Id = "/subscriptions/sub/providers/Microsoft.Web/locations/westus/managedApis/office365" },
                    Connection = new ConnectionInfo { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Web/connections/myConnection" },
                },
            },
        };

        string? result = ConnectionsHelper.GetDirectClientRuntimeUrl(config, "myConnection");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetDirectClientRuntimeUrl_ReturnsNull_WhenConfigIsNull()
    {
        string? result = ConnectionsHelper.GetDirectClientRuntimeUrl(null, "SharePoint");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetDirectClientRuntimeUrl_ReturnsNull_WhenConnectionNotFound()
    {
        var config = new ConnectionsConfig
        {
            DirectClientConnections = new Dictionary<string, DirectClientConnection>
            {
                ["SharePoint"] = new DirectClientConnection
                {
                    ConnectorType = "sharepointonline",
                    ConnectionRuntimeUrl = "https://instance.azure-apihub.net/apim/sharepointonline/abc123",
                },
            },
        };

        string? result = ConnectionsHelper.GetDirectClientRuntimeUrl(config, "NonExistent");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ResolveArmConnectionName_ReturnsDictionaryKey_ForManagedApiConnection()
    {
        var config = new ConnectionsConfig
        {
            ManagedApiConnections = new Dictionary<string, ManagedApiConnection>
            {
                ["myConnection"] = new ManagedApiConnection
                {
                    Api = new ApiInfo { Id = "/subscriptions/sub/providers/Microsoft.Web/locations/westus/managedApis/office365" },
                    Connection = new ConnectionInfo { Id = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Web/connections/myConnection" },
                },
            },
        };

        string? result = ConnectionsHelper.ResolveArmConnectionName(config, "myConnection");

        Assert.AreEqual("myConnection", result);
    }

    [TestMethod]
    public void ResolveArmConnectionName_ExtractsFromRuntimeUrl_ForDirectClientConnection()
    {
        var config = new ConnectionsConfig
        {
            DirectClientConnections = new Dictionary<string, DirectClientConnection>
            {
                ["SharePoint"] = new DirectClientConnection
                {
                    ConnectorType = "sharepointonline",
                    ConnectionRuntimeUrl = "https://instance.azure-apihub.net/apim/sharepointonline/0011fe19224c49eab97e35d9637f4fd2",
                },
            },
        };

        string? result = ConnectionsHelper.ResolveArmConnectionName(config, "SharePoint");

        Assert.AreEqual("0011fe19224c49eab97e35d9637f4fd2", result);
    }

    [TestMethod]
    public void GetConnectionNamesForConnector_FindsDirectClientByConnectorType()
    {
        var config = new ConnectionsConfig
        {
            DirectClientConnections = new Dictionary<string, DirectClientConnection>
            {
                ["SharePoint"] = new DirectClientConnection
                {
                    ConnectorType = "sharepointonline",
                    ConnectionRuntimeUrl = "https://instance.azure-apihub.net/apim/sharepointonline/abc123",
                },
                ["Office365"] = new DirectClientConnection
                {
                    ConnectorType = "office365",
                    ConnectionRuntimeUrl = "https://instance.azure-apihub.net/apim/office365/def456",
                },
            },
        };

        IEnumerable<string> result = ConnectionsHelper.GetConnectionNamesForConnector(config, "sharepointonline");

        CollectionAssert.AreEqual(new[] { "SharePoint" }, result.ToList());
    }

    [TestMethod]
    public void ExtractConnectionResourceName_ExtractsFromApimUrl()
    {
        string? result = ConnectionsHelper.ExtractConnectionResourceName(
            "https://a9ab15f5a12185bf.07.common.logic-df.azure-apihub.net/apim/sharepointonline/0011fe19224c49eab97e35d9637f4fd2");

        Assert.AreEqual("0011fe19224c49eab97e35d9637f4fd2", result);
    }

    [TestMethod]
    public void ExtractConnectionResourceName_ReturnsNull_ForNullInput()
    {
        string? result = ConnectionsHelper.ExtractConnectionResourceName(null);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void ExtractConnectorType_ExtractsFromManagedApiId()
    {
        string result = ConnectionsHelper.ExtractConnectorType(
            "/subscriptions/sub/providers/Microsoft.Web/locations/westus/managedApis/office365");

        Assert.AreEqual("office365", result);
    }

    [TestMethod]
    public void ExtractConnectorType_ReturnsUnknown_ForEmptyInput()
    {
        string result = ConnectionsHelper.ExtractConnectorType(string.Empty);

        Assert.AreEqual("unknown", result);
    }
}
