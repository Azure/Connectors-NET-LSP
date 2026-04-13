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
public class AttributeValidatorTests
{
    private static SdkIndex CreateMockSdkIndex()
    {
        return AttributeValidatorTests.CreateMockSdkIndex(
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
            });
    }

    private static SdkIndex CreateMockSdkIndex(
        SdkConstant[] connectorNames,
        Dictionary<string, ImmutableArray<SdkConstant>> triggerOperations)
    {
        return SdkIndex.CreateForTesting(
            connectorNames: connectorNames,
            triggerOperations: triggerOperations);
    }

    [TestMethod]
    public async Task ValidateAsync_UnknownConnectorName_EmitsCSdk001()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "nonexistent", OperationName = "Op")]
                public async Task MyMethod() { }
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
        Diagnostic? unknown = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.UnknownConnectorName, StringComparison.Ordinal));
        Assert.IsNotNull(unknown, message: "Expected CSDK001 for unknown connector name.");
        Assert.AreEqual(DiagnosticSeverity.Error, unknown.Severity);
        Assert.IsTrue(unknown.Message.Contains("nonexistent", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_TypoInConnectorName_EmitsCSdk002()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "ofice365", OperationName = "OnNewEmail")]
                public async Task MyMethod() { }
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
        Diagnostic? typo = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectorNameTypo, StringComparison.Ordinal));
        Assert.IsNotNull(typo, message: "Expected CSDK002 for typo in connector name.");
        Assert.AreEqual(DiagnosticSeverity.Warning, typo.Severity);
        Assert.IsTrue(typo.Message.Contains("office365", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_WrongCasingConnectorName_EmitsCSdk003()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "OFFICE365", OperationName = "OnNewEmail")]
                public async Task MyMethod() { }
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
        Diagnostic? casing = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectorNameCasing, StringComparison.Ordinal));
        Assert.IsNotNull(casing, message: "Expected CSDK003 for wrong casing.");
        Assert.AreEqual(DiagnosticSeverity.Warning, casing.Severity);
        Assert.IsTrue(casing.Message.Contains("office365", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_MissingConnectorName_EmitsCSdk004()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(OperationName = "OnNewEmail")]
                public async Task MyMethod() { }
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
        Diagnostic? missing = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerMetadataMissingConnectorName, StringComparison.Ordinal));
        Assert.IsNotNull(missing, message: "Expected CSDK004 for missing ConnectorName.");
        Assert.AreEqual(DiagnosticSeverity.Error, missing.Severity);
    }

    [TestMethod]
    public async Task ValidateAsync_MissingOperationName_EmitsCSdk005()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365")]
                public async Task MyMethod() { }
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
        Diagnostic? missing = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerMetadataMissingOperationName, StringComparison.Ordinal));
        Assert.IsNotNull(missing, message: "Expected CSDK005 for missing OperationName.");
        Assert.AreEqual(DiagnosticSeverity.Error, missing.Severity);
    }

    [TestMethod]
    public async Task ValidateAsync_NonAsyncMethod_EmitsCSdk006()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public void MyMethod() { }
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
        Diagnostic? signatureMismatch = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerMetadataSignatureMismatch, StringComparison.Ordinal));
        Assert.IsNotNull(signatureMismatch, message: "Expected CSDK006 for non-async method.");
        Assert.AreEqual(DiagnosticSeverity.Warning, signatureMismatch.Severity);
    }

    [TestMethod]
    public async Task ValidateAsync_AsyncTaskMethod_NoCSdk006()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Threading.Tasks;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task<string> MyMethod() { return ""; }
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
        Diagnostic? signatureMismatch = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerMetadataSignatureMismatch, StringComparison.Ordinal));
        Assert.IsNull(signatureMismatch, message: "Should not emit CSDK006 for async Task method.");
    }

    [TestMethod]
    public async Task ValidateAsync_AsyncVoidMethod_EmitsCSdk006()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            using System.Threading.Tasks;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async void MyMethod() { await Task.Yield(); }
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
        Diagnostic? signatureMismatch = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.TriggerMetadataSignatureMismatch, StringComparison.Ordinal));
        Assert.IsNotNull(signatureMismatch, message: "Expected CSDK006 for async void trigger method.");
    }

    [TestMethod]
    public async Task ValidateAsync_UnknownOperationName_EmitsCSdk007()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewSomething")]
                public async Task MyMethod() { }
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
        Diagnostic? unknown = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.UnknownOperationName, StringComparison.Ordinal));
        Assert.IsNotNull(unknown, message: "Expected CSDK007 for unknown operation name.");
        Assert.AreEqual(DiagnosticSeverity.Error, unknown.Severity);
        Assert.IsTrue(unknown.Message.Contains("OnNewSomething", StringComparison.Ordinal));
        Assert.IsTrue(unknown.Message.Contains("OnNewEmail", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ValidateAsync_ConnectorWithNoTriggers_EmitsCSdk008()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "sharepointonline", OperationName = "OnNewItem")]
                public async Task MyMethod() { }
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
        Diagnostic? noTriggers = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.OperationNameNoTriggers, StringComparison.Ordinal));
        Assert.IsNotNull(noTriggers, message: "Expected CSDK008 when connector has no trigger operations.");
        Assert.AreEqual(DiagnosticSeverity.Warning, noTriggers.Severity);
    }

    [TestMethod]
    public async Task ValidateAsync_ConnectorOperationUnknown_EmitsCSdk009()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                [ConnectorOperation(ConnectorName = "office365", OperationName = "NonExistentOp")]
                public async Task MyMethod() { }
            }
            public sealed class ConnectorOperationAttribute : Attribute
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
        Diagnostic? unknown = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.ConnectorOperationUnknown, StringComparison.Ordinal));
        Assert.IsNotNull(unknown, message: "Expected CSDK009 for unknown connector operation.");
        Assert.AreEqual(DiagnosticSeverity.Warning, unknown.Severity);
    }

    [TestMethod]
    public async Task ValidateAsync_ValidConnectorAndOperation_NoDiagnostics()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod() { }
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

        // Assert — should only have no attribute-validation diagnostics (no CSDK001-009)
        var attributeDiagnostics = diagnostics.Where(diagnostic =>
            diagnostic.Code?.String?.StartsWith("CSDK0", StringComparison.Ordinal) == true &&
            int.TryParse(diagnostic.Code?.String?.Substring(4), out int code) &&
            code <= 9).ToList();
        Assert.AreEqual(0, attributeDiagnostics.Count, message: "Expected no attribute validation diagnostics for valid code.");
    }

    [TestMethod]
    public async Task ValidateAsync_ConstantReference_ValidConnector_NoDiagnosticOnConnectorName()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = ConnectorNames.Office365, OperationName = "OnNewEmail")]
                public async Task MyMethod() { }
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

        // Assert — ConnectorName "Office365" should match by field name, no CSDK001/002/003
        Diagnostic? connectorDiag = diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Code?.String is "CSDK001" or "CSDK002" or "CSDK003");
        Assert.IsNull(connectorDiag, message: "Constant reference Office365 should match by field name.");
    }

    [TestMethod]
    public async Task ValidateAsync_NullSdkIndex_NoDiagnostics()
    {
        // Arrange
        var validator = new AttributeValidator();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365", OperationName = "OnNewEmail")]
                public async Task MyMethod() { }
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
        Assert.AreEqual(0, diagnostics.Count, message: "Should not validate when SDK index is null.");
    }

    [TestMethod]
    public async Task ValidateAsync_EmptyDocument_NoDiagnostics()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, string.Empty, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_NoAttributes_NoDiagnostics()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            class Test
            {
                public void MyMethod() { }
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert
        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_ConnectorTriggerAttribute_AlsoRecognized()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                [ConnectorTrigger(ConnectorName = "nonexistent", OperationName = "Op")]
                public async Task MyMethod() { }
            }
            public sealed class ConnectorTriggerAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            """;

        // Act
        IReadOnlyList<Diagnostic> diagnostics = await validator
            .ValidateAsync(uri, code, sdkIndex, CancellationToken.None)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Assert — should emit CSDK001 for unknown connector even with ConnectorTrigger name
        Diagnostic? unknown = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.UnknownConnectorName, StringComparison.Ordinal));
        Assert.IsNotNull(unknown, message: "ConnectorTrigger (without Metadata) should also be validated.");
    }

    [TestMethod]
    public async Task ValidateAsync_DiagnosticRangesPointToArgumentValues()
    {
        // Arrange
        var validator = new AttributeValidator();
        var sdkIndex = AttributeValidatorTests.CreateMockSdkIndex();
        var uri = DocumentUri.From("file:///test.cs");
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "nonexistent", OperationName = "Op")]
                public async Task MyMethod() { }
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

        // Assert — CSDK001 range should cover "nonexistent" (with quotes) on line 3
        Diagnostic? unknown = diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Code?.String, DiagnosticCodes.UnknownConnectorName, StringComparison.Ordinal));
        Assert.IsNotNull(unknown);
        Assert.AreEqual(3, unknown.Range.Start.Line);
        Assert.IsTrue(unknown.Range.Start.Character > 0);
    }

    [TestMethod]
    public void LevenshteinDistance_IdenticalStrings_ReturnsZero()
    {
        Assert.AreEqual(0, LevenshteinDistance.Compute("office365", "office365"));
    }

    [TestMethod]
    public void LevenshteinDistance_SingleCharDifference_ReturnsOne()
    {
        Assert.AreEqual(1, LevenshteinDistance.Compute("ofice365", "office365"));
    }

    [TestMethod]
    public void LevenshteinDistance_TwoCharDifference_ReturnsTwo()
    {
        Assert.AreEqual(2, LevenshteinDistance.Compute("ofce365", "office365"));
    }

    [TestMethod]
    public void LevenshteinDistance_EmptySource_ReturnsTargetLength()
    {
        Assert.AreEqual(5, LevenshteinDistance.Compute(string.Empty, "teams"));
    }

    [TestMethod]
    public void LevenshteinDistance_EmptyTarget_ReturnsSourceLength()
    {
        Assert.AreEqual(5, LevenshteinDistance.Compute("teams", string.Empty));
    }

    [TestMethod]
    public void LevenshteinDistance_BothEmpty_ReturnsZero()
    {
        Assert.AreEqual(0, LevenshteinDistance.Compute(string.Empty, string.Empty));
    }

    [TestMethod]
    public void LevenshteinDistance_CaseInsensitive_ReturnsZero()
    {
        Assert.AreEqual(0, LevenshteinDistance.Compute("Office365", "office365"));
    }

    [TestMethod]
    public void LevenshteinDistance_CompletelyDifferent_ReturnsMaxLength()
    {
        int result = LevenshteinDistance.Compute("abc", "xyz");
        Assert.AreEqual(3, result);
    }
}
