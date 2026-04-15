//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using System.Collections.Immutable;

using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using SdkLspServer.Diagnostics;
using SdkLspServer.Diagnostics.Validators;

namespace SdkLspServer.Tests;

[TestClass]
public class TriggerPayloadValidatorTests
{
    private static SdkIndex CreateMockSdkIndex()
    {
        return SdkIndex.CreateForTesting(
            connectorNames: new[]
            {
                new SdkConstant("Office365", "office365", "ConnectorNames", "Microsoft.Azure.Connectors.Sdk.ConnectorNames"),
                new SdkConstant("Teams", "teams", "ConnectorNames", "Microsoft.Azure.Connectors.Sdk.ConnectorNames"),
                new SdkConstant("SharepointOnline", "sharepointonline", "ConnectorNames", "Microsoft.Azure.Connectors.Sdk.ConnectorNames"),
            },
            triggerOperations: new Dictionary<string, ImmutableArray<SdkConstant>>
            {
                ["office365"] = ImmutableArray.Create(
                    new SdkConstant("OnNewEmail", "OnNewEmail", "Office365TriggerOperations", "Microsoft.Azure.Connectors.DirectClient.Office365.Office365TriggerOperations"),
                    new SdkConstant("OnNewEmailMentioningMe", "OnNewEmailMentioningMe", "Office365TriggerOperations", "Microsoft.Azure.Connectors.DirectClient.Office365.Office365TriggerOperations")),
                ["teams"] = ImmutableArray.Create(
                    new SdkConstant("OnNewChannelMessage", "OnNewChannelMessage", "TeamsTriggerOperations", "Microsoft.Azure.Connectors.DirectClient.Teams.TeamsTriggerOperations")),
            },
            typeNames: new[]
            {
                "Microsoft.Azure.Connectors.DirectClient.Office365.Office365OnNewEmailTriggerPayload",
                "Microsoft.Azure.Connectors.DirectClient.Office365.Office365OnNewEmailMentioningMeTriggerPayload",
                "Microsoft.Azure.Connectors.DirectClient.Teams.TeamsOnNewChannelMessageTriggerPayload",
                "Microsoft.Azure.Connectors.DirectClient.Office365.GraphClientReceiveMessage",
                "Microsoft.Azure.Connectors.Sdk.TriggerCallbackPayload",
            });
    }

    // ---------------------------------------------------------------
    // Correct usage — no diagnostics
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_CorrectPayloadType_NoDiagnostic()
    {
        // Arrange
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<Office365OnNewEmailTriggerPayload>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count, message: "No diagnostics expected for correct payload type.");
    }

    [TestMethod]
    public async Task ValidateAsync_NoTriggerAttribute_NoDiagnostic()
    {
        // Arrange
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<Office365OnNewEmailTriggerPayload>(body);
                }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count, message: "No diagnostics expected when method has no trigger attribute.");
    }

    [TestMethod]
    public async Task ValidateAsync_WeakTypeWithNoExpectedPayload_EmitsCSdk203()
    {
        // Arrange — SharepointOnline includes the OnNewItem trigger operation, but the mock SdkIndex
        // does not define a matching typed payload name. CSDK203 should fire because the operation
        // is unmapped, regardless of whether T is a weak type.
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = SdkIndex.CreateForTesting(
            connectorNames: new[]
            {
                new SdkConstant("SharepointOnline", "sharepointonline", "ConnectorNames", "Microsoft.Azure.Connectors.Sdk.ConnectorNames"),
            },
            triggerOperations: new Dictionary<string, ImmutableArray<SdkConstant>>
            {
                ["sharepointonline"] = ImmutableArray.Create(
                    new SdkConstant("OnNewItem", "OnNewItem", "SharepointonlineTriggerOperations", "Microsoft.Azure.Connectors.DirectClient.Sharepointonline.SharepointonlineTriggerOperations")),
            });
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "sharepointonline", OperationName = "OnNewItem")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<object>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert — CSDK203 fires because the operation has no mapped payload type
        Diagnostic? unmapped = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadOperationUnmapped, StringComparison.Ordinal));
        Assert.IsNotNull(unmapped, message: "Expected CSDK203 for unmapped operation even with weak type.");
        Assert.AreEqual(DiagnosticSeverity.Warning, unmapped.Severity);
    }

    // ---------------------------------------------------------------
    // CSDK200: Deserialize<T> type mismatch
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_WrongPayloadType_EmitsCSdk200()
    {
        // Arrange
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<TeamsOnNewChannelMessageTriggerPayload>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? mismatch = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadTypeMismatch, StringComparison.Ordinal));
        Assert.IsNotNull(mismatch, message: "Expected CSDK200 for wrong payload type.");
        Assert.AreEqual(DiagnosticSeverity.Error, mismatch.Severity);
        Assert.IsTrue(mismatch.Message.Contains("Office365OnNewEmailTriggerPayload", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------
    // CSDK201: Deserialize<T> uses weak type
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_ObjectType_EmitsCSdk201()
    {
        // Arrange
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<object>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? weakType = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadWeakType, StringComparison.Ordinal));
        Assert.IsNotNull(weakType, message: "Expected CSDK201 for weak type 'object'.");
        Assert.AreEqual(DiagnosticSeverity.Warning, weakType.Severity);
        Assert.IsTrue(weakType.Message.Contains("Office365OnNewEmailTriggerPayload", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_DynamicType_EmitsCSdk201()
    {
        // Arrange
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<dynamic>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? weakType = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadWeakType, StringComparison.Ordinal));
        Assert.IsNotNull(weakType, message: "Expected CSDK201 for weak type 'dynamic'.");
        Assert.AreEqual(DiagnosticSeverity.Warning, weakType.Severity);
    }

    [TestMethod]
    public async Task ValidateAsync_JsonElementType_EmitsCSdk201()
    {
        // Arrange
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<JsonElement>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? weakType = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadWeakType, StringComparison.Ordinal));
        Assert.IsNotNull(weakType, message: "Expected CSDK201 for weak type 'JsonElement'.");
        Assert.AreEqual(DiagnosticSeverity.Warning, weakType.Severity);
    }

    // ---------------------------------------------------------------
    // CSDK202: Generic argument type not found
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_UnknownType_EmitsCSdk202()
    {
        // Arrange
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<CompletelyFakeType>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? notFound = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadTypeNotFound, StringComparison.Ordinal));
        Assert.IsNotNull(notFound, message: "Expected CSDK202 for unknown type.");
        Assert.AreEqual(DiagnosticSeverity.Error, notFound.Severity);
        Assert.IsTrue(notFound.Message.Contains("CompletelyFakeType", StringComparison.Ordinal));
        Assert.IsTrue(notFound.Message.Contains("Office365OnNewEmailTriggerPayload", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------
    // CSDK203: Operation name doesn't map to payload type
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_OperationWithNoPayloadMapping_EmitsCSdk203()
    {
        // Arrange — create an index where the operation exists but no matching type name
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = SdkIndex.CreateForTesting(
            connectorNames: new[]
            {
                new SdkConstant("Office365", "office365", "ConnectorNames", "Microsoft.Azure.Connectors.Sdk.ConnectorNames"),
            },
            triggerOperations: new Dictionary<string, ImmutableArray<SdkConstant>>
            {
                ["office365"] = ImmutableArray.Create(
                    new SdkConstant("OnNewEmail", "OnNewEmail", "Office365TriggerOperations", "Microsoft.Azure.Connectors.DirectClient.Office365.Office365TriggerOperations")),
            },
            typeNames: new[]
            {
                // No payload type for OnNewEmail — simulates a missing TriggerPayload class
                "Microsoft.Azure.Connectors.DirectClient.Office365.GraphClientReceiveMessage",
            });
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<GraphClientReceiveMessage>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? unmapped = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadOperationUnmapped, StringComparison.Ordinal));
        Assert.IsNotNull(unmapped, message: "Expected CSDK203 when operation has no payload mapping.");
        Assert.AreEqual(DiagnosticSeverity.Warning, unmapped.Severity);
        Assert.IsTrue(unmapped.Message.Contains("OnNewEmail", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------
    // CSDK204: Type name does not follow the *TriggerPayload naming convention
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_NonPayloadType_EmitsCSdk204()
    {
        // Arrange — GraphClientReceiveMessage exists but is not a *TriggerPayload
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<GraphClientReceiveMessage>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? notPayload = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadNotPayloadType, StringComparison.Ordinal));
        Assert.IsNotNull(notPayload, message: "Expected CSDK204 for non-payload type.");
        Assert.AreEqual(DiagnosticSeverity.Warning, notPayload.Severity);
        Assert.IsTrue(notPayload.Message.Contains("GraphClientReceiveMessage", StringComparison.Ordinal));
        Assert.IsTrue(notPayload.Message.Contains("naming convention", StringComparison.Ordinal));
        Assert.IsTrue(notPayload.Message.Contains("Office365OnNewEmailTriggerPayload", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------
    // Edge cases
    // ---------------------------------------------------------------
    [TestMethod]
    public async Task ValidateAsync_ConnectorNameAsConstant_ResolvesCorrectly()
    {
        // Arrange — uses ConnectorNames.Office365 member access syntax
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = ConnectorNames.Office365, OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<Office365OnNewEmailTriggerPayload>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            public static class ConnectorNames { public const string Office365 = "office365"; }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count, message: "No diagnostics expected when connector name constant resolves correctly.");
    }

    [TestMethod]
    public async Task ValidateAsync_DeserializeAsyncMethod_ValidatesPayload()
    {
        // Arrange — DeserializeAsync should also be checked
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = await JsonSerializer.DeserializeAsync<object>(stream);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? weakType = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadWeakType, StringComparison.Ordinal));
        Assert.IsNotNull(weakType, message: "Expected CSDK201 for DeserializeAsync with weak type.");
    }

    [TestMethod]
    public async Task ValidateAsync_NullSdkIndex_NoDiagnostic()
    {
        // Arrange
        var validator = new TriggerPayloadValidator();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<object>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
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
        Assert.AreEqual(0, diagnostics.Count, message: "No diagnostics expected when SDK index is null.");
    }

    [TestMethod]
    public async Task ValidateAsync_EmptyDocument_NoDiagnostic()
    {
        // Arrange
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = string.Empty;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count, message: "No diagnostics expected for empty document.");
    }

    [TestMethod]
    public async Task ValidateAsync_DiagnosticRangeOnTypeArgument_PreciseRange()
    {
        // Arrange
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<object>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(1, diagnostics.Count, message: "Expected exactly one diagnostic.");
        Diagnostic diagnostic = diagnostics[0];

        // The diagnostic range should be on "object" inside Deserialize<object>
        Assert.IsTrue(
            diagnostic.Range.Start.Line == diagnostic.Range.End.Line,
            message: "Diagnostic should be on a single line.");
        Assert.IsTrue(
            diagnostic.Range.End.Character > diagnostic.Range.Start.Character,
            message: "Diagnostic should have non-zero width covering the type argument.");
    }

    [TestMethod]
    public async Task ValidateAsync_MultipleDeserializeCalls_EmitsMultipleDiagnostics()
    {
        // Arrange
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var a = JsonSerializer.Deserialize<object>(body1);
                    var b = JsonSerializer.Deserialize<dynamic>(body2);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(2, diagnostics.Count, message: "Expected two diagnostics for two weak-type Deserialize calls.");
        Assert.IsTrue(diagnostics.All(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadWeakType, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ValidateAsync_NonDeserializeGenericMethod_NoDiagnostic()
    {
        // Arrange — generic method that is NOT a deserialization call
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var list = new List<object>();
                    var result = Activator.CreateInstance<object>();
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count, message: "No diagnostics expected for non-deserialization generic methods.");
    }

    [TestMethod]
    public async Task ValidateAsync_OperationNameDifferentCasing_ResolvesCorrectly()
    {
        // Arrange — operation name uses different casing than the canonical FieldName.
        // The validator should normalize it and still find the expected payload type.
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "onnewemail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<Office365OnNewEmailTriggerPayload>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert — no CSDK203 because casing should be normalized to canonical form
        Assert.AreEqual(0, diagnostics.Count, message: "No diagnostics expected when operation name casing differs but resolves correctly.");
    }

    [TestMethod]
    public async Task ValidateAsync_FullyQualifiedCorrectType_NoDiagnostic()
    {
        // Arrange — fully-qualified type argument should match the expected payload type
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<Microsoft.Azure.Connectors.DirectClient.Office365.Office365OnNewEmailTriggerPayload>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count, message: "No diagnostic expected for fully-qualified correct payload type.");
    }

    [TestMethod]
    public async Task ValidateAsync_FullyQualifiedWeakType_EmitsCSdk201()
    {
        // Arrange — fully-qualified weak type (System.Text.Json.JsonElement)
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? weakType = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadWeakType, StringComparison.Ordinal));
        Assert.IsNotNull(weakType, message: "Expected CSDK201 for fully-qualified JsonElement.");
    }

    [TestMethod]
    public async Task ValidateAsync_ConditionalAccessDeserialize_NoDiagnostic()
    {
        // Arrange — conditional access deserialization is skipped entirely because
        // known serializer types (JsonSerializer, JsonConvert) are static classes and
        // cannot be used with ?. in valid C#. Any conditional access receiver is
        // therefore a user-defined type or variable, not an SDK serializer.
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class JsonSerializer { public T Deserialize<T>(string s) => default; }
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var JsonSerializer = new JsonSerializer();
                    var payload = JsonSerializer?.Deserialize<object>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert — no diagnostics because conditional access is not flagged
        Assert.AreEqual(0, diagnostics.Count, message: "Conditional access on a non-static receiver should not emit diagnostics.");
    }

    [TestMethod]
    public async Task ValidateAsync_LocalTypeShadowsSerializer_NoDiagnostic()
    {
        // Arrange — file declares a class named "JsonSerializer" that shadows the imported type.
        // The simple-name receiver should not be treated as a known serializer.
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class JsonSerializer
            {
                public static T Deserialize<T>(string s) => default;
            }
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<object>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert — no diagnostics because local type shadows the imported serializer
        Assert.AreEqual(0, diagnostics.Count, message: "Local type declaration shadows the imported serializer; should not emit diagnostics.");
    }

    [TestMethod]
    public async Task ValidateAsync_AliasUsingDirective_NoDiagnostic()
    {
        // Arrange — alias directive "using STJ = System.Text.Json;" does not bring
        // JsonSerializer into scope by simple name. The validator should not treat
        // JsonSerializer.Deserialize<T> as a known serializer in this case.
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using STJ = System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<object>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert — no diagnostics because alias using doesn't bring types into simple-name scope
        Assert.AreEqual(0, diagnostics.Count, message: "Alias using directive should not enable serializer detection by simple name.");
    }

    [TestMethod]
    public async Task ValidateAsync_SimpleNameUsingStatic_EmitsCSdk201()
    {
        // Arrange — "using System.Text.Json; using static JsonSerializer;" is valid C#
        // because the namespace import makes JsonSerializer resolvable.
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            using static JsonSerializer;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = Deserialize<object>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? weakType = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadWeakType, StringComparison.Ordinal));
        Assert.IsNotNull(weakType, message: "Expected CSDK201 for bare Deserialize<object> with simple-name using static.");
    }

    [TestMethod]
    public async Task ValidateAsync_UsingStaticDirectCall_EmitsCSdk201()
    {
        // Arrange — using static System.Text.Json.JsonSerializer enables bare Deserialize<T>() calls
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using static System.Text.Json.JsonSerializer;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = Deserialize<object>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Diagnostic? weakType = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadWeakType, StringComparison.Ordinal));
        Assert.IsNotNull(weakType, message: "Expected CSDK201 for bare Deserialize<object> with using static.");
    }

    [TestMethod]
    public async Task ValidateAsync_GlobalQualifiedCorrectType_NoDiagnostic()
    {
        // Arrange — global:: prefixed fully-qualified type should match correctly
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<global::Microsoft.Azure.Connectors.DirectClient.Office365.Office365OnNewEmailTriggerPayload>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count, message: "No diagnostic expected for global:: prefixed correct payload type.");
    }

    [TestMethod]
    public async Task ValidateAsync_GlobalQualifiedUsingNamespace_EmitsCSdk201()
    {
        // Arrange — using global::System.Text.Json; should be normalized to System.Text.Json
        // so that JsonSerializer is recognized as a known serializer receiver.
        var validator = new TriggerPayloadValidator();
        SdkIndex sdkIndex = TriggerPayloadValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using global::System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<object>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert — should recognize the serializer despite global:: prefix on the using directive
        Diagnostic? weakType = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadWeakType, StringComparison.Ordinal));
        Assert.IsNotNull(weakType, message: "Expected CSDK201 when using global::System.Text.Json namespace import.");
    }

    [TestMethod]
    public async Task ValidateAsync_NestedSdkType_EmitsCSdk200()
    {
        // Arrange — SDK type names use '+' for nested types (e.g., "Namespace.Outer+Inner"),
        // but C# source code uses '.' (e.g., "Outer.Inner"). BuildTypeNameLookup extracts
        // the rightmost segment after '+' into the lookup set, so "InnerTriggerPayload" is
        // found as an existing type, but it doesn't match the expected payload for the operation.
        var validator = new TriggerPayloadValidator();
        SdkIndex nestedSdkIndex = SdkIndex.CreateForTesting(
            connectorNames: new[]
            {
                new SdkConstant("Office365", "office365", "ConnectorNames", "Microsoft.Azure.Connectors.Sdk.ConnectorNames"),
            },
            triggerOperations: new Dictionary<string, ImmutableArray<SdkConstant>>
            {
                ["office365"] = ImmutableArray.Create(
                    new SdkConstant("OnNewEmail", "OnNewEmail", "Office365TriggerOperations", "Microsoft.Azure.Connectors.DirectClient.Office365.Office365TriggerOperations")),
            },
            typeNames: new[]
            {
                "Microsoft.Azure.Connectors.DirectClient.Office365.Office365OnNewEmailTriggerPayload",
                "Microsoft.Azure.Connectors.DirectClient.Office365.Outer+WrongTriggerPayload",
            });
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Text.Json;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod()
                {
                    var payload = JsonSerializer.Deserialize<WrongTriggerPayload>(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, nestedSdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert — WrongTriggerPayload is found in TypeNameLookup (extracted from
        // "Outer+WrongTriggerPayload" via the '+' separator), but doesn't match
        // the expected Office365OnNewEmailTriggerPayload, so CSDK200 fires.
        Diagnostic? mismatch = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerPayloadTypeMismatch, StringComparison.Ordinal));
        Assert.IsNotNull(mismatch, message: "Expected CSDK200 for nested type that exists in index but doesn't match the expected payload.");
    }
}
