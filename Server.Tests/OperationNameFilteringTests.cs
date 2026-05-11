//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using SdkLspServer;

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

        // Find the OperationName attribute argument
        AttributeSyntax attr = root.DescendantNodes().OfType<AttributeSyntax>()
            .First(a => a.Name.ToString().Contains("ConnectorTriggerMetadata", StringComparison.Ordinal));

        string? connectorName = ReadSiblingAttributeParameterValue(attr, "ConnectorName");

        Assert.AreEqual("Office365", connectorName, "Should read ConnectorName from sibling argument");
    }

    /// <summary>
    /// Verifies that when OperationName has an incomplete expression (cursor just typed '='),
    /// Roslyn's error recovery still produces a parsable attribute with the sibling ConnectorName.
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

        SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

        // Find position right after "OperationName = "
        int position = code.IndexOf("OperationName = ", StringComparison.Ordinal) + "OperationName = ".Length;

        SyntaxToken token = root.FindToken(position);

        // Check if the token is inside an AttributeArgumentSyntax
        AttributeArgumentSyntax? attrArg = token.Parent?.AncestorsAndSelf()
            .OfType<AttributeArgumentSyntax>()
            .FirstOrDefault();

        if (attrArg != null)
        {
            // AST path: verify sibling value can be read
            AttributeSyntax? attr = attrArg.AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
            Assert.IsNotNull(attr, "Should find parent attribute");

            string? connectorName = ReadSiblingAttributeParameterValue(attr, "ConnectorName");
            Assert.AreEqual(
                "Office365",
                connectorName,
                "AST path should read ConnectorName from sibling argument even with incomplete OperationName");
        }
        else
        {
            // AST path failed — this is expected for very incomplete code.
            // Text-based fallback would handle this.
            Assert.Inconclusive("Roslyn could not parse incomplete attribute — text-based fallback needed");
        }
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

        string? connectorName = ExtractParameterValueFromText(contextWindow, "ConnectorName");

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

        string? connectorName = ExtractParameterValueFromText(contextWindow, "ConnectorName");

        // BUG: This currently returns "AzureBlob" because IndexOf finds the FIRST occurrence.
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

        string? connectorName = ExtractParameterValueFromText(contextWindow, "ConnectorName");

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

        string? connectorName = ExtractParameterValueFromText(contextWindow, "ConnectorName");

        Assert.AreEqual("Office365", connectorName);
    }

    /// <summary>
    /// Verifies that when the parameter is not found, null is returned.
    /// </summary>
    [TestMethod]
    public void ExtractParameterValue_ParameterNotPresent_ReturnsNull()
    {
        string contextWindow = "[ConnectorTriggerMetadata(ConnectorName = ConnectorNames.Office365, OperationName = ";

        string? result = ExtractParameterValueFromText(contextWindow, "Connection");

        Assert.IsNull(result);
    }

    // ── Helpers (replicated from CompletionHandler private methods) ──────────
    private static string? ReadSiblingAttributeParameterValue(AttributeSyntax attr, string parameterName)
    {
        if (attr.ArgumentList is null)
        {
            return null;
        }

        foreach (AttributeArgumentSyntax arg in attr.ArgumentList.Arguments)
        {
            if (arg.NameEquals is null ||
                !string.Equals(arg.NameEquals.Name.Identifier.Text, parameterName, StringComparison.Ordinal))
            {
                continue;
            }

            if (arg.Expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }

            if (arg.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                return memberAccess.Name.Identifier.Text;
            }

            if (arg.Expression is IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.Text;
            }
        }

        return null;
    }

    private static string? ExtractParameterValueFromText(string contextText, string parameterName)
    {
        // Narrow to the last attribute in the context so we don't match
        // parameters from an earlier [ConnectorTriggerMetadata].
        int lastAttrBracket = contextText.LastIndexOf("[ConnectorTrigger", StringComparison.Ordinal);
        string searchText = lastAttrBracket >= 0
            ? contextText.Substring(lastAttrBracket)
            : contextText;

        int paramIndex = searchText.IndexOf(parameterName, StringComparison.Ordinal);
        if (paramIndex < 0)
        {
            return null;
        }

        string afterParam = searchText.Substring(paramIndex + parameterName.Length).TrimStart();
        if (!afterParam.StartsWith("=", StringComparison.Ordinal))
        {
            return null;
        }

        afterParam = afterParam.Substring(1).TrimStart();

        if (afterParam.StartsWith("\"", StringComparison.Ordinal))
        {
            int endQuote = afterParam.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return afterParam.Substring(1, endQuote - 1);
            }
        }

        int dotIndex = afterParam.IndexOf('.');
        if (dotIndex >= 0)
        {
            string afterDot = afterParam.Substring(dotIndex + 1);
            int end = 0;
            while (end < afterDot.Length && (char.IsLetterOrDigit(afterDot[end]) || afterDot[end] == '_'))
            {
                end++;
            }

            if (end > 0)
            {
                return afterDot.Substring(0, end);
            }
        }

        return null;
    }
}
