//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using SdkLspServer.Diagnostics;
using SdkLspServer.Diagnostics.Validators;
using SdkLspServer.Services.Connections;

namespace SdkLspServer.Tests;

[TestClass]
public class ConnectionConfigValidatorTests
{
    private static ConnectionsConfig CreateTestConnections(
        Dictionary<string, ManagedApiConnection>? managedApiConnections = null,
        Dictionary<string, DirectClientConnection>? directClientConnections = null)
    {
        return new ConnectionsConfig
        {
            ManagedApiConnections = managedApiConnections ?? new Dictionary<string, ManagedApiConnection>(),
            DirectClientConnections = directClientConnections ?? new Dictionary<string, DirectClientConnection>(),
        };
    }

    private static ConnectionsConfig CreateOffice365Connections()
    {
        return ConnectionConfigValidatorTests.CreateTestConnections(
            managedApiConnections: new Dictionary<string, ManagedApiConnection>
            {
                ["office365-conn"] = new ManagedApiConnection
                {
                    Api = new ApiInfo { Id = "/subscriptions/sub1/providers/Microsoft.Web/locations/westus/managedApis/office365" },
                    Connection = new ConnectionInfo { Id = "/subscriptions/sub1/resourceGroups/rg1/providers/Microsoft.Web/connections/office365-conn" },
                    ConnectionRuntimeUrl = "https://instance.azure-apihub.net/apim/office365/abc123",
                    Authentication = "ManagedServiceIdentity",
                },
            });
    }

    private static ConnectionsConfig CreateMultipleOffice365Connections()
    {
        return ConnectionConfigValidatorTests.CreateTestConnections(
            managedApiConnections: new Dictionary<string, ManagedApiConnection>
            {
                ["office365-conn-1"] = new ManagedApiConnection
                {
                    Api = new ApiInfo { Id = "/subscriptions/sub1/providers/Microsoft.Web/locations/westus/managedApis/office365" },
                    Connection = new ConnectionInfo { Id = "/subscriptions/sub1/resourceGroups/rg1/providers/Microsoft.Web/connections/office365-conn-1" },
                },
                ["office365-conn-2"] = new ManagedApiConnection
                {
                    Api = new ApiInfo { Id = "/subscriptions/sub1/providers/Microsoft.Web/locations/westus/managedApis/office365" },
                    Connection = new ConnectionInfo { Id = "/subscriptions/sub1/resourceGroups/rg1/providers/Microsoft.Web/connections/office365-conn-2" },
                },
            });
    }

    private static ConnectionsConfig CreateSharePointConnections()
    {
        return ConnectionConfigValidatorTests.CreateTestConnections(
            directClientConnections: new Dictionary<string, DirectClientConnection>
            {
                ["sp-conn"] = new DirectClientConnection
                {
                    ConnectorType = "sharepointonline",
                    ConnectionRuntimeUrl = "https://instance.azure-apihub.net/apim/sharepointonline/sp123",
                },
            });
    }

    [TestMethod]
    public async Task ValidateAsync_NullConnections_ReturnsNoDiagnostics()
    {
        // Arrange
        var connectionsService = new ConnectionsService();
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                public void MyMethod(string connectionName)
                {
                    var client = new Office365Client(connectionName: "nonexistent");
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count, message: "Should suppress diagnostics when connections are null.");
    }

    [TestMethod]
    public async Task ValidateAsync_EmptyDocument_ReturnsNoDiagnostics()
    {
        // Arrange
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateOffice365Connections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, documentText: string.Empty, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count, message: "Should return no diagnostics for empty document.");
    }

    [TestMethod]
    public async Task ValidateAsync_ConnectionParameterValueNotInConfig_EmitsCSdk100()
    {
        // Arrange
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateOffice365Connections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                public void MyMethod()
                {
                    this.CallApi(connectionName: "nonexistent-conn");
                }
                public void CallApi(string connectionName) { }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? invalid = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectionParameterValueInvalid, StringComparison.Ordinal));
        Assert.IsNotNull(invalid, message: "Expected CSDK100 for connection value not in config.");
        Assert.AreEqual(DiagnosticSeverity.Warning, invalid.Severity);
        Assert.IsTrue(invalid.Message.Contains("nonexistent-conn", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_ConnectionParameterValueValid_NoDiagnostic()
    {
        // Arrange
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateOffice365Connections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                public void MyMethod()
                {
                    this.CallApi(connectionName: "office365-conn");
                }
                public void CallApi(string connectionName) { }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? invalid = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectionParameterValueInvalid, StringComparison.Ordinal));
        Assert.IsNull(invalid, message: "Should not emit CSDK100 for valid connection.");
    }

    [TestMethod]
    public async Task ValidateAsync_HardCodedConnectionString_EmitsCSdk101()
    {
        // Arrange
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateOffice365Connections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                public void MyMethod()
                {
                    this.CallApi(connectionName: "https://instance.azure-apihub.net/apim/office365/abc123");
                }
                public void CallApi(string connectionName) { }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? hardCoded = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectionParameterHardCoded, StringComparison.Ordinal));
        Assert.IsNotNull(hardCoded, message: "Expected CSDK101 for hard-coded connection URL.");
        Assert.AreEqual(DiagnosticSeverity.Information, hardCoded.Severity);
    }

    [TestMethod]
    public async Task ValidateAsync_SubscriptionPathHardCoded_EmitsCSdk101()
    {
        // Arrange
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateOffice365Connections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                public void MyMethod()
                {
                    this.CallApi(connectionName: "/subscriptions/sub1/resourceGroups/rg1/providers/Microsoft.Web/connections/conn1");
                }
                public void CallApi(string connectionName) { }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? hardCoded = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectionParameterHardCoded, StringComparison.Ordinal));
        Assert.IsNotNull(hardCoded, message: "Expected CSDK101 for hard-coded subscription path.");
    }

    [TestMethod]
    public async Task ValidateAsync_NoConnectionForConnector_EmitsCSdk102()
    {
        // Arrange — only SharePoint configured, code uses office365
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateSharePointConnections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod() { }
            }
            public sealed class ConnectorTriggerMetadataAttribute : System.Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? noConnection = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.NoConnectionConfigured, StringComparison.Ordinal));
        Assert.IsNotNull(noConnection, message: "Expected CSDK102 for connector with no configured connection.");
        Assert.AreEqual(DiagnosticSeverity.Error, noConnection.Severity);
        Assert.IsTrue(noConnection.Message.Contains("office365", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_ConnectionPresent_NoCSdk102()
    {
        // Arrange
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateOffice365Connections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod() { }
            }
            public sealed class ConnectorTriggerMetadataAttribute : System.Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? noConnection = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.NoConnectionConfigured, StringComparison.Ordinal));
        Assert.IsNull(noConnection, message: "Should not emit CSDK102 when connection is configured.");
    }

    [TestMethod]
    public async Task ValidateAsync_MissingConnectionInAttribute_EmitsCSdk103()
    {
        // Arrange
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateOffice365Connections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail", ConnectionName = "missing-conn")]
                public async Task MyMethod() { }
            }
            public sealed class ConnectorTriggerMetadataAttribute : System.Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
                public string ConnectionName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? missing = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectionMissing, StringComparison.Ordinal));
        Assert.IsNotNull(missing, message: "Expected CSDK103 for missing connection name in attribute.");
        Assert.AreEqual(DiagnosticSeverity.Warning, missing.Severity);
        Assert.IsTrue(missing.Message.Contains("missing-conn", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_ValidConnectionInAttribute_NoCSdk103()
    {
        // Arrange
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateOffice365Connections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail", ConnectionName = "office365-conn")]
                public async Task MyMethod() { }
            }
            public sealed class ConnectorTriggerMetadataAttribute : System.Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
                public string ConnectionName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? missing = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectionMissing, StringComparison.Ordinal));
        Assert.IsNull(missing, message: "Should not emit CSDK103 for valid connection in attribute.");
    }

    [TestMethod]
    public async Task ValidateAsync_MultipleConnectionsForConnectorInAttribute_EmitsCSdk104()
    {
        // Arrange
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateMultipleOffice365Connections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod() { }
            }
            public sealed class ConnectorTriggerMetadataAttribute : System.Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? ambiguous = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.MultipleConnectionsAmbiguous, StringComparison.Ordinal));
        Assert.IsNotNull(ambiguous, message: "Expected CSDK104 when multiple connections match connector.");
        Assert.AreEqual(DiagnosticSeverity.Warning, ambiguous.Severity);
        Assert.IsTrue(ambiguous.Message.Contains("office365-conn-1", StringComparison.Ordinal));
        Assert.IsTrue(ambiguous.Message.Contains("office365-conn-2", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_SingleConnectionForConnector_NoCSdk104()
    {
        // Arrange — only one office365 connection
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateOffice365Connections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod() { }
            }
            public sealed class ConnectorTriggerMetadataAttribute : System.Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? ambiguous = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.MultipleConnectionsAmbiguous, StringComparison.Ordinal));
        Assert.IsNull(ambiguous, message: "Should not emit CSDK104 when only one connection matches.");
    }

    [TestMethod]
    public async Task ValidateAsync_ConnectionTypeMismatch_EmitsCSdk105()
    {
        // Arrange — SharePoint connection used where office365 expected
        var connectionsService = new ConnectionsService();
        var mixedConnections = ConnectionConfigValidatorTests.CreateTestConnections(
            managedApiConnections: new Dictionary<string, ManagedApiConnection>
            {
                ["sp-as-office365"] = new ManagedApiConnection
                {
                    Api = new ApiInfo { Id = "/subscriptions/sub1/providers/Microsoft.Web/locations/westus/managedApis/sharepointonline" },
                    Connection = new ConnectionInfo { Id = "/subscriptions/sub1/resourceGroups/rg1/providers/Microsoft.Web/connections/sp-as-office365" },
                },
            });
        connectionsService.UpdateConnections(mixedConnections);
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail", ConnectionName = "sp-as-office365")]
                public async Task MyMethod() { }
            }
            public sealed class ConnectorTriggerMetadataAttribute : System.Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
                public string ConnectionName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? mismatch = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectionTypeMismatch, StringComparison.Ordinal));
        Assert.IsNotNull(mismatch, message: "Expected CSDK105 when connection type doesn't match connector type.");
        Assert.AreEqual(DiagnosticSeverity.Warning, mismatch.Severity);
        Assert.IsTrue(mismatch.Message.Contains("sharepointonline", StringComparison.Ordinal));
        Assert.IsTrue(mismatch.Message.Contains("office365", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_ConnectionTypeMatches_NoCSdk105()
    {
        // Arrange
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateOffice365Connections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail", ConnectionName = "office365-conn")]
                public async Task MyMethod() { }
            }
            public sealed class ConnectorTriggerMetadataAttribute : System.Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
                public string ConnectionName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? mismatch = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectionTypeMismatch, StringComparison.Ordinal));
        Assert.IsNull(mismatch, message: "Should not emit CSDK105 when connection type matches.");
    }

    [TestMethod]
    public async Task ValidateAsync_DirectClientConnection_NoFalsePositives()
    {
        // Arrange — DirectClient connection configured
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateSharePointConnections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "sharepointonline", OperationName = "OnNewFile", ConnectionName = "sp-conn")]
                public async Task MyMethod() { }
            }
            public sealed class ConnectorTriggerMetadataAttribute : System.Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
                public string ConnectionName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? missing = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectionMissing, StringComparison.Ordinal));
        Assert.IsNull(missing, message: "DirectClient connection should be recognized.");

        Diagnostic? mismatch = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectionTypeMismatch, StringComparison.Ordinal));
        Assert.IsNull(mismatch, message: "DirectClient connection type should match sharepointonline.");
    }

    [TestMethod]
    public async Task ValidateAsync_ConnectorOperationAttribute_EmitsCSdk102()
    {
        // Arrange — no teams connection configured
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateOffice365Connections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                [ConnectorOperation(ConnectorName = "teams", OperationName = "SendMessage")]
                public async Task MyMethod() { }
            }
            public sealed class ConnectorOperationAttribute : System.Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? noConnection = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.NoConnectionConfigured, StringComparison.Ordinal));
        Assert.IsNotNull(noConnection, message: "Expected CSDK102 for teams connector with no configured connection.");
        Assert.IsTrue(noConnection.Message.Contains("teams", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_NonSdkAttribute_NoDiagnostics()
    {
        // Arrange — custom attribute should not trigger connection diagnostics
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateOffice365Connections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                [MyCustomAttribute(ConnectorName = "nonexistent")]
                public void MyMethod() { }
            }
            public sealed class MyCustomAttribute : System.Attribute
            {
                public string ConnectorName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count, message: "Non-SDK attributes should not trigger connection diagnostics.");
    }

    [TestMethod]
    public async Task ValidateAsync_EmptyConnectionsConfig_NoConnectionDiagnostics()
    {
        // Arrange — connections config exists but is empty
        var connectionsService = new ConnectionsService();
        connectionsService.UpdateConnections(ConnectionConfigValidatorTests.CreateTestConnections());
        var validator = new ConnectionConfigValidator(connectionsService);
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod() { }
            }
            public sealed class ConnectorTriggerMetadataAttribute : System.Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? noConnection = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.NoConnectionConfigured, StringComparison.Ordinal));
        Assert.IsNotNull(noConnection, message: "Expected CSDK102 when config exists but has no matching connections.");
    }
}
