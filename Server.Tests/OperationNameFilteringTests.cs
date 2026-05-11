//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SdkLspServer.Handlers.CompletionHandler;

namespace Server.Tests;

/// <summary>
/// Tests that OperationName completion in [ConnectorTriggerMetadata] attributes
/// is correctly filtered by the sibling ConnectorName value.
/// Regression tests for issue #74.
/// </summary>
[TestClass]
public class OperationNameFilteringTests
{
    /// <summary>
    /// Verifies that ReadSiblingAttributeParameterValue correctly reads ConnectorName
    /// from a complete [ConnectorTriggerMetadata] attribute when cursor is at OperationName.
    /// </summary>
    [TestMethod]
    public void ReadSiblingValue_CompleteAttribute_ReturnsConnectorName()
    {
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = ConnectorNames.Office365, OperationName = "OnNewEmail")]
                public void MyMethod() { }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            public static class ConnectorNames { public const string Office365 = "office365"; }
            """;

        SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

        // Find the ConnectorTriggerMetadata attribute node
        AttributeSyntax attribute = root.DescendantNodes().OfType<AttributeSyntax>()
            .First(attributeNode => attributeNode.Name.ToString().Contains("ConnectorTriggerMetadata", StringComparison.Ordinal));

        string? connectorName = CompletionHandler.ReadSiblingAttributeParameterValue(attribute, "ConnectorName");

        Assert.AreEqual("Office365", connectorName, "Should read ConnectorName from sibling argument");
    }

    /// <summary>
    /// Verifies that when OperationName has an incomplete expression (cursor just typed '='),
    /// both the AST path and the text-based fallback can read the sibling ConnectorName.
    /// </summary>
    [TestMethod]
    public void ReadSiblingValue_IncompleteOperationName_ReturnsConnectorName()
    {
        // Simulates the state when user just typed '=' after OperationName
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = ConnectorNames.Office365, OperationName = )]
                public void MyMethod() { }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            public static class ConnectorNames { public const string Office365 = "office365"; }
            """;

        // Text-based fallback: always deterministic regardless of Roslyn error recovery
        string contextWindow = "[ConnectorTriggerMetadata(ConnectorName = ConnectorNames.Office365, OperationName = ";
        string? textResult = CompletionHandler.ExtractParameterValueFromText(contextWindow, "ConnectorName");
        Assert.AreEqual(
            "Office365",
            textResult,
            "Text-based fallback should read ConnectorName from sibling argument");

        // AST path: Roslyn error recovery parses the incomplete attribute successfully
        SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        AttributeSyntax? attribute = root.DescendantNodes().OfType<AttributeSyntax>()
            .FirstOrDefault(attributeNode => attributeNode.Name.ToString().Contains("ConnectorTriggerMetadata", StringComparison.Ordinal));

        Assert.IsNotNull(attribute, "Roslyn should parse the incomplete attribute via error recovery");
        string? astResult = CompletionHandler.ReadSiblingAttributeParameterValue(attribute, "ConnectorName");
        Assert.AreEqual(
            "Office365",
            astResult,
            "AST path should also read ConnectorName from the parsed attribute");
    }

    /// <summary>
    /// Verifies that ExtractParameterValueFromText correctly handles the case
    /// where ConnectorName appears as both a parameter name and inside a constant reference.
    /// The text-based path should extract the correct value.
    /// </summary>
    [TestMethod]
    public void ExtractConnectorName_FromContextWindow_CorrectValue()
    {
        string contextWindow = "[ConnectorTriggerMetadata(ConnectorName = ConnectorNames.Office365, OperationName = ";

        string? connectorName = CompletionHandler.ExtractParameterValueFromText(contextWindow, "ConnectorName");

        Assert.AreEqual(
            "Office365",
            connectorName,
            "Should extract 'Office365' from ConnectorNames.Office365 constant reference");
    }

    /// <summary>
    /// Verifies that when two [ConnectorTriggerMetadata] attributes are present on the same line,
    /// the text-based path does NOT pick up the ConnectorName from the wrong attribute.
    /// This is the root cause of issue #74.
    /// </summary>
    [TestMethod]
    public void ExtractConnectorName_TwoAttributesSameLine_PicksCorrectOne()
    {
        // If two attributes appear in the context window (e.g., stacked or same-line),
        // the text-based extraction must pick the LAST ConnectorName, not the first.
        string contextWindow =
            "[ConnectorTriggerMetadata(ConnectorName = ConnectorNames.AzureBlob, OperationName = AzureBlobTriggerOperations.OnUpdatedFiles)]" +
            "[ConnectorTriggerMetadata(ConnectorName = ConnectorNames.Office365, OperationName = ";

        string? connectorName = CompletionHandler.ExtractParameterValueFromText(contextWindow, "ConnectorName");

        // Regression: before issue #74 fix, IndexOf found the FIRST ConnectorName occurrence
        // and returned "AzureBlob" instead of "Office365".
        Assert.AreEqual(
            "Office365",
            connectorName,
            "Should extract ConnectorName from the attribute closest to the cursor, not the first one");
    }

    /// <summary>
    /// Verifies the extraction with string literal ConnectorName values.
    /// </summary>
    [TestMethod]
    public void ExtractConnectorName_StringLiteral_ReturnsValue()
    {
        string contextWindow = "[ConnectorTriggerMetadata(ConnectorName = \"office365\", OperationName = ";

        string? connectorName = CompletionHandler.ExtractParameterValueFromText(contextWindow, "ConnectorName");

        Assert.AreEqual("office365", connectorName);
    }

    /// <summary>
    /// Verifies the extraction works when the attribute spans multiple lines.
    /// </summary>
    [TestMethod]
    public void ExtractConnectorName_MultiLineAttribute_ReturnsValue()
    {
        string contextWindow =
            "[ConnectorTriggerMetadata(\n" +
            "    ConnectorName = ConnectorNames.Office365,\n" +
            "    OperationName = ";

        string? connectorName = CompletionHandler.ExtractParameterValueFromText(contextWindow, "ConnectorName");

        Assert.AreEqual("Office365", connectorName);
    }

    /// <summary>
    /// Verifies that when the parameter is not found, null is returned.
    /// </summary>
    [TestMethod]
    public void ExtractParameterValue_ParameterNotPresent_ReturnsNull()
    {
        string contextWindow = "[ConnectorTriggerMetadata(ConnectorName = ConnectorNames.Office365, OperationName = ";

        string? result = CompletionHandler.ExtractParameterValueFromText(contextWindow, "Connection");

        Assert.IsNull(result);
    }

    /// <summary>
    /// Verifies that an unterminated string literal does not fall through to the
    /// constant reference parsing path, which could misinterpret a dot from a later
    /// parameter as a ConnectorName value.
    /// </summary>
    [TestMethod]
    public void ExtractConnectorName_UnterminatedStringLiteral_ReturnsNull()
    {
        // ConnectorName starts with a quote but is not closed — cursor is mid-edit.
        // OperationName has a dot that should NOT be mistaken for the ConnectorName value.
        string contextWindow = "[ConnectorTriggerMetadata(ConnectorName = \"office, OperationName = SomeOps.SomeValue";

        string? connectorName = CompletionHandler.ExtractParameterValueFromText(contextWindow, "ConnectorName");

        Assert.IsNull(
            connectorName,
            "Should return null for unterminated string literal, not fall through to dot parsing");
    }
}
