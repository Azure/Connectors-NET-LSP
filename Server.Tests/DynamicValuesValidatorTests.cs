//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using System.Collections.Immutable;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using SdkLspServer.Diagnostics;
using SdkLspServer.Diagnostics.Validators;
using SdkLspServer.Services;
using SdkLspServer.Services.Connections;
using SdkLspServer.Store;
using SdkLspServer.Store.DynamicData;

namespace SdkLspServer.Tests;

[TestClass]
public class DynamicValuesValidatorTests
{
    /// <summary>
    /// Preamble that defines the [DynamicValues] attribute and a mock connector client
    /// in the <c>Azure.Connectors.Sdk</c> namespace so that the semantic model resolves
    /// the attribute on parameters during call-site analysis.
    /// </summary>
    private const string SdkPreamble = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        namespace Azure.Connectors.Sdk
        {
            [AttributeUsage(AttributeTargets.Parameter)]
            public sealed class DynamicValuesAttribute : Attribute
            {
                public DynamicValuesAttribute(string operationId) { OperationId = operationId; }
                public string OperationId { get; }
            }
        }
        namespace Azure.Connectors.Sdk.SharePointOnline
        {
            public class SharePointOnlineClient
            {
                public virtual Task<object> GetAllTablesAsync(
                    [Azure.Connectors.Sdk.DynamicValues("GetDataSets")] string siteAddress,
                    CancellationToken cancellationToken = default) => Task.FromResult<object>(null);
            }
        }
        """;

    private static SdkIndex CreateMockSdkIndex()
    {
        return SdkIndex.CreateForTesting(
            connectorNames: new[]
            {
                new SdkConstant("SharePointOnline", "sharepointonline", "ConnectorNames", "Azure.Connectors.Sdk.ConnectorNames"),
            },
            triggerOperations: new Dictionary<string, ImmutableArray<SdkConstant>>());
    }

    /// <summary>
    /// Creates a validator with a pre-populated dynamic values cache.
    /// </summary>
    private static DynamicValuesValidator CreateValidatorWithCache(
        SdkIndex sdkIndex,
        string connector,
        string operation,
        string connectionName,
        List<DynamicValueItem> cachedValues)
    {
        var compilationService = new CompilationService(sdkIndex);
        var lspStore = new LSPStore();
        lspStore.DynamicData.Set(connector, operation, connectionName, cachedValues);

        var connectionsService = new ConnectionsService();
        var connectionsConfig = new ConnectionsConfig
        {
            DirectClientConnections = new Dictionary<string, DirectClientConnection>
            {
                [connectionName] = new DirectClientConnection
                {
                    ConnectorType = connector,
                },
            },
        };
        connectionsService.UpdateConnections(connectionsConfig);

        return new DynamicValuesValidator(compilationService, lspStore, connectionsService);
    }

    /// <summary>
    /// Creates a validator with no cached values and no connections.
    /// </summary>
    private static DynamicValuesValidator CreateValidatorWithoutCache(SdkIndex? sdkIndex = null)
    {
        var compilationService = new CompilationService(sdkIndex);
        var lspStore = new LSPStore();
        var connectionsService = new ConnectionsService();
        return new DynamicValuesValidator(compilationService, lspStore, connectionsService);
    }

    // ---------------------------------------------------------------
    // CSDK300: Invalid value against cached dynamic values
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_LiteralNotInCachedValues_EmitsCSdk300Async()
    {
        // Arrange
        var sdkIndex = DynamicValuesValidatorTests.CreateMockSdkIndex();
        var validator = DynamicValuesValidatorTests.CreateValidatorWithCache(
            sdkIndex,
            connector: "sharepointonline",
            operation: "GetDataSets",
            connectionName: "sp-conn",
            cachedValues:
            [
                new DynamicValueItem("\"https://contoso.sharepoint.com\"", "Contoso"),
                new DynamicValueItem("\"https://fabrikam.sharepoint.com\"", "Fabrikam"),
            ]);
        var uri = DocumentUri.From("file:///test.cs");
        string code = DynamicValuesValidatorTests.SdkPreamble + """

            namespace TestApp
            {
                class MyFunctions
                {
                    private Azure.Connectors.Sdk.SharePointOnline.SharePointOnlineClient client;
                    async Task Run()
                    {
                        await client.GetAllTablesAsync("foobar");
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? result = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.DynamicValuesInvalidValue, StringComparison.Ordinal));
        Assert.IsNotNull(result, message: "Expected CSDK300 for 'foobar' not in cached values.");
        Assert.AreEqual(DiagnosticSeverity.Warning, result.Severity);
        Assert.IsTrue(result.Message.Contains("foobar", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_LiteralMatchesCachedValue_NoCSdk300Async()
    {
        // Arrange
        var sdkIndex = DynamicValuesValidatorTests.CreateMockSdkIndex();
        var validator = DynamicValuesValidatorTests.CreateValidatorWithCache(
            sdkIndex,
            connector: "sharepointonline",
            operation: "GetDataSets",
            connectionName: "sp-conn",
            cachedValues:
            [
                new DynamicValueItem("\"https://contoso.sharepoint.com\"", "Contoso"),
            ]);
        var uri = DocumentUri.From("file:///test.cs");
        string code = DynamicValuesValidatorTests.SdkPreamble + """

            namespace TestApp
            {
                class MyFunctions
                {
                    private Azure.Connectors.Sdk.SharePointOnline.SharePointOnlineClient client;
                    async Task Run()
                    {
                        await client.GetAllTablesAsync("https://contoso.sharepoint.com");
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? result = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.DynamicValuesInvalidValue, StringComparison.Ordinal));
        Assert.IsNull(result, message: "Should not emit CSDK300 when the literal matches a cached value.");
    }

    [TestMethod]
    public async Task ValidateAsync_NoCachedValues_NoDiagnosticsAsync()
    {
        // Arrange — no cache populated (hover hasn't been triggered yet)
        var sdkIndex = DynamicValuesValidatorTests.CreateMockSdkIndex();
        var validator = DynamicValuesValidatorTests.CreateValidatorWithoutCache(sdkIndex);
        var uri = DocumentUri.From("file:///test.cs");
        string code = DynamicValuesValidatorTests.SdkPreamble + """

            namespace TestApp
            {
                class MyFunctions
                {
                    private Azure.Connectors.Sdk.SharePointOnline.SharePointOnlineClient client;
                    async Task Run()
                    {
                        await client.GetAllTablesAsync("foobar");
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? result = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.DynamicValuesInvalidValue, StringComparison.Ordinal));
        Assert.IsNull(result, message: "Should not emit CSDK300 when no cached values exist.");
    }

    [TestMethod]
    public async Task ValidateAsync_VariableArgument_NoDiagnosticsAsync()
    {
        // Arrange
        var sdkIndex = DynamicValuesValidatorTests.CreateMockSdkIndex();
        var validator = DynamicValuesValidatorTests.CreateValidatorWithCache(
            sdkIndex,
            connector: "sharepointonline",
            operation: "GetDataSets",
            connectionName: "sp-conn",
            cachedValues: [new DynamicValueItem("valid", "Valid")]);
        var uri = DocumentUri.From("file:///test.cs");
        string code = DynamicValuesValidatorTests.SdkPreamble + """

            namespace TestApp
            {
                class MyFunctions
                {
                    private Azure.Connectors.Sdk.SharePointOnline.SharePointOnlineClient client;
                    async Task Run()
                    {
                        string site = "anything";
                        await client.GetAllTablesAsync(site);
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(
            expected: 0,
            actual: diagnostics.Count(diagnostic =>
                string.Equals(diagnostic.Code?.String, DiagnosticCodes.DynamicValuesInvalidValue, StringComparison.Ordinal)),
            message: "Should not emit CSDK300 for variable arguments.");
    }

    [TestMethod]
    public async Task ValidateAsync_NonSdkMethod_NoDiagnosticsAsync()
    {
        // Arrange
        var sdkIndex = DynamicValuesValidatorTests.CreateMockSdkIndex();
        var validator = DynamicValuesValidatorTests.CreateValidatorWithCache(
            sdkIndex,
            connector: "sharepointonline",
            operation: "GetDataSets",
            connectionName: "sp-conn",
            cachedValues: [new DynamicValueItem("valid", "Valid")]);
        var uri = DocumentUri.From("file:///test.cs");
        string code = DynamicValuesValidatorTests.SdkPreamble + """

            namespace MyApp
            {
                class MyClient
                {
                    public System.Threading.Tasks.Task<object> GetAllTablesAsync(string site) => null;
                }
                class MyFunctions
                {
                    private MyClient client;
                    async System.Threading.Tasks.Task Run()
                    {
                        await client.GetAllTablesAsync("foobar");
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(
            expected: 0,
            actual: diagnostics.Count(diagnostic =>
                string.Equals(diagnostic.Code?.String, DiagnosticCodes.DynamicValuesInvalidValue, StringComparison.Ordinal)),
            message: "Should not emit CSDK300 for non-SDK methods.");
    }

    [TestMethod]
    public async Task ValidateAsync_LiteralMatchesCachedDescription_NoCSdk300Async()
    {
        // Arrange — literal matches the Description (display name), not the Value
        var sdkIndex = DynamicValuesValidatorTests.CreateMockSdkIndex();
        var validator = DynamicValuesValidatorTests.CreateValidatorWithCache(
            sdkIndex,
            connector: "sharepointonline",
            operation: "GetDataSets",
            connectionName: "sp-conn",
            cachedValues: [new DynamicValueItem("https://contoso.sharepoint.com/sites/hr", "Contoso")]);
        var uri = DocumentUri.From("file:///test.cs");
        string code = DynamicValuesValidatorTests.SdkPreamble + """

            namespace TestApp
            {
                class MyFunctions
                {
                    private Azure.Connectors.Sdk.SharePointOnline.SharePointOnlineClient client;
                    async Task Run()
                    {
                        await client.GetAllTablesAsync("Contoso");
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? result = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.DynamicValuesInvalidValue, StringComparison.Ordinal));
        Assert.IsNull(result, message: "Should not emit CSDK300 when the literal matches a cached Description.");
    }

    // ---------------------------------------------------------------
    // Edge cases
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_EmptyDocument_ReturnsNoDiagnosticsAsync()
    {
        // Arrange
        var validator = DynamicValuesValidatorTests.CreateValidatorWithoutCache();
        var uri = DocumentUri.From("file:///test.cs");

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, string.Empty, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(expected: 0, actual: diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_NoSdkIndex_ReturnsNoDiagnosticsAsync()
    {
        // Arrange
        var validator = DynamicValuesValidatorTests.CreateValidatorWithoutCache();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Foo { void Bar() { } }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex: null, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(expected: 0, actual: diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_QuotedCachedValue_MatchesUnquotedLiteral_NoCSdk300Async()
    {
        // Arrange — cached value is quote-wrapped (as stored by hover handler)
        var sdkIndex = DynamicValuesValidatorTests.CreateMockSdkIndex();
        var validator = DynamicValuesValidatorTests.CreateValidatorWithCache(
            sdkIndex,
            connector: "sharepointonline",
            operation: "GetDataSets",
            connectionName: "sp-conn",
            cachedValues: [new DynamicValueItem("\"abc\"", "ABC Site")]);
        var uri = DocumentUri.From("file:///test.cs");
        string code = DynamicValuesValidatorTests.SdkPreamble + """

            namespace TestApp
            {
                class MyFunctions
                {
                    private Azure.Connectors.Sdk.SharePointOnline.SharePointOnlineClient client;
                    async Task Run()
                    {
                        await client.GetAllTablesAsync("abc");
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? result = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.DynamicValuesInvalidValue, StringComparison.Ordinal));
        Assert.IsNull(result, message: "Should not emit CSDK300 when quote-wrapped cached value matches unquoted literal.");
    }

    [TestMethod]
    public async Task ValidateAsync_CaseDifference_EmitsCSdk300Async()
    {
        // Arrange — cached value differs only in case; matching is case-sensitive
        var sdkIndex = DynamicValuesValidatorTests.CreateMockSdkIndex();
        var validator = DynamicValuesValidatorTests.CreateValidatorWithCache(
            sdkIndex,
            connector: "sharepointonline",
            operation: "GetDataSets",
            connectionName: "sp-conn",
            cachedValues: [new DynamicValueItem("\"https://Contoso.SharePoint.com\"", "Contoso")]);
        var uri = DocumentUri.From("file:///test.cs");
        string code = DynamicValuesValidatorTests.SdkPreamble + """

            namespace TestApp
            {
                class MyFunctions
                {
                    private Azure.Connectors.Sdk.SharePointOnline.SharePointOnlineClient client;
                    async Task Run()
                    {
                        await client.GetAllTablesAsync("https://contoso.sharepoint.com");
                    }
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert — case-sensitive comparison means different casing IS a mismatch
        Diagnostic? result = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.DynamicValuesInvalidValue, StringComparison.Ordinal));
        Assert.IsNotNull(result, message: "Expected CSDK300 when literal differs from cached value only by case.");
    }
}
