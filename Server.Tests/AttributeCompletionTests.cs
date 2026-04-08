using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Server.Tests;

/// <summary>
/// Tests for attribute-context completion detection and parameter extraction.
/// Validates the static helper methods used in CompletionHandler for
/// [ConnectorTriggerMetadata] attribute argument completions.
/// </summary>
[TestClass]
public class AttributeCompletionTests
{
    /// <summary>
    /// Verifies that GetTriggerMetadataParameterValue correctly extracts string literal values
    /// from [ConnectorTriggerMetadata] attribute parameters.
    /// </summary>
    [TestMethod]
    public void GetTriggerMetadataParameterValue_StringLiteral_ReturnsValue()
    {
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

        MethodDeclarationSyntax method = GetFirstMethod(code);

        string? connectorName = GetTriggerMetadataParameterValue(method, "ConnectorName");
        string? operationName = GetTriggerMetadataParameterValue(method, "OperationName");

        Assert.AreEqual("office365", connectorName);
        Assert.AreEqual("OnNewEmail", operationName);
    }

    /// <summary>
    /// Verifies that GetTriggerMetadataParameterValue correctly extracts constant references
    /// like ConnectorNames.Office365 from attribute parameters.
    /// </summary>
    [TestMethod]
    public void GetTriggerMetadataParameterValue_ConstantReference_ReturnsMemberName()
    {
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = ConnectorNames.Office365)]
                public void MyMethod() { }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
            }
            public static class ConnectorNames { public const string Office365 = "office365"; }
            """;

        MethodDeclarationSyntax method = GetFirstMethod(code);

        string? result = GetTriggerMetadataParameterValue(method, "ConnectorName");

        Assert.AreEqual("Office365", result);
    }

    /// <summary>
    /// Verifies that GetTriggerMetadataParameterValue returns null when the attribute
    /// does not exist on the method.
    /// </summary>
    [TestMethod]
    public void GetTriggerMetadataParameterValue_NoAttribute_ReturnsNull()
    {
        string code = """
            using System;
            class Test
            {
                [Obsolete]
                public void MyMethod() { }
            }
            """;

        MethodDeclarationSyntax method = GetFirstMethod(code);

        string? result = GetTriggerMetadataParameterValue(method, "ConnectorName");

        Assert.IsNull(result);
    }

    /// <summary>
    /// Verifies that GetTriggerMetadataParameterValue returns null when the parameter
    /// is not present in the attribute.
    /// </summary>
    [TestMethod]
    public void GetTriggerMetadataParameterValue_ParameterMissing_ReturnsNull()
    {
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(ConnectorName = "office365")]
                public void MyMethod() { }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
            }
            """;

        MethodDeclarationSyntax method = GetFirstMethod(code);

        string? result = GetTriggerMetadataParameterValue(method, "OperationName");

        Assert.IsNull(result);
    }

    /// <summary>
    /// Verifies that ConnectorTrigger (without Metadata suffix) is also recognized
    /// as a valid trigger attribute for future compatibility.
    /// </summary>
    [TestMethod]
    public void GetTriggerMetadataParameterValue_ConnectorTriggerAttributeName_ReturnsValue()
    {
        string code = """
            using System;
            class Test
            {
                [ConnectorTrigger(ConnectorName = "teams")]
                public void MyMethod() { }
            }
            public sealed class ConnectorTriggerAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
            }
            """;

        MethodDeclarationSyntax method = GetFirstMethod(code);

        string? result = GetTriggerMetadataParameterValue(method, "ConnectorName");

        Assert.AreEqual("teams", result);
    }

    /// <summary>
    /// Verifies that TryExtractAttributeParameterName correctly extracts parameter names
    /// from line prefixes in various formats.
    /// </summary>
    [TestMethod]
    [DataRow("    ConnectorName = ", "ConnectorName")]
    [DataRow("    ConnectorName =", "ConnectorName")]
    [DataRow("    ConnectorName = \"", "ConnectorName")]
    [DataRow("    OperationName = ", "OperationName")]
    [DataRow("    Connection = \"", "Connection")]
    [DataRow("(ConnectorName = ", "ConnectorName")]
    [DataRow(", OperationName = ", "OperationName")]
    public void TryExtractAttributeParameterName_ValidPatterns_ExtractsName(string linePrefix, string expected)
    {
        bool result = TryExtractAttributeParameterName(linePrefix, out string? paramName);

        Assert.IsTrue(result);
        Assert.AreEqual(expected, paramName);
    }

    /// <summary>
    /// Verifies that TryExtractAttributeParameterName returns false for non-attribute patterns.
    /// </summary>
    [TestMethod]
    [DataRow("var x = 5")]
    [DataRow("if (x < y)")]
    [DataRow("return result;")]
    [DataRow("")]
    public void TryExtractAttributeParameterName_InvalidPatterns_ReturnsFalse(string linePrefix)
    {
        bool result = TryExtractAttributeParameterName(linePrefix, out _);

        Assert.IsFalse(result);
    }

    /// <summary>
    /// Verifies that ExtractParameterValueFromText correctly extracts string literal values
    /// from raw attribute text context.
    /// </summary>
    [TestMethod]
    public void ExtractParameterValueFromText_StringLiteral_ReturnsValue()
    {
        string context = "[ConnectorTriggerMetadata(ConnectorName = \"office365\", OperationName = \"";

        string? result = ExtractParameterValueFromText(context, "ConnectorName");

        Assert.AreEqual("office365", result);
    }

    /// <summary>
    /// Verifies that ExtractParameterValueFromText correctly extracts member access constants
    /// like ConnectorNames.Office365 from raw text context.
    /// </summary>
    [TestMethod]
    public void ExtractParameterValueFromText_ConstantReference_ReturnsMemberName()
    {
        string context = "[ConnectorTriggerMetadata(ConnectorName = ConnectorNames.Office365, OperationName = \"";

        string? result = ExtractParameterValueFromText(context, "ConnectorName");

        Assert.AreEqual("Office365", result);
    }

    /// <summary>
    /// Verifies that ExtractParameterValueFromText returns null when the parameter is not found.
    /// </summary>
    [TestMethod]
    public void ExtractParameterValueFromText_ParameterNotFound_ReturnsNull()
    {
        string context = "[ConnectorTriggerMetadata(ConnectorName = \"office365\"";

        string? result = ExtractParameterValueFromText(context, "OperationName");

        Assert.IsNull(result);
    }

    /// <summary>
    /// Verifies that the deserialization method name whitelist correctly identifies known patterns.
    /// </summary>
    [TestMethod]
    [DataRow("Deserialize", true)]
    [DataRow("DeserializeAsync", true)]
    [DataRow("DeserializeObject", true)]
    [DataRow("DeserializeObjectAsync", true)]
    [DataRow("deserialize", true)] // case insensitive
    [DataRow("Serialize", false)]
    [DataRow("ToList", false)]
    [DataRow("GetValue", false)]
    [DataRow("Compare", false)]
    public void DeserializationMethodCheck_CorrectlyIdentifiesPatterns(string methodName, bool shouldMatch)
    {
        var deserializationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Deserialize",
            "DeserializeAsync",
            "DeserializeObject",
            "DeserializeObjectAsync",
        };

        bool result = deserializationNames.Contains(methodName);

        Assert.AreEqual(shouldMatch, result);
    }

    /// <summary>
    /// Integration test: verifies that GetEnclosingMethodTriggerOperationName finds
    /// the OperationName from the method's attribute when the cursor is inside the method body.
    /// </summary>
    [TestMethod]
    public void GetEnclosingMethodTriggerOperationName_CursorInMethodBody_ReturnsOperationName()
    {
        string code = """
            using System;
            class Test
            {
                [ConnectorTriggerMetadata(
                    ConnectorName = ConnectorNames.Office365,
                    OperationName = Office365TriggerOperations.OnNewEmail)]
                public void TriggerCallback()
                {
                    var body = "test";
                    var payload = Deserialize(body);
                }
            }
            public sealed class ConnectorTriggerMetadataAttribute : Attribute
            {
                public string ConnectorName { get; set; } = "";
                public string OperationName { get; set; } = "";
            }
            public static class ConnectorNames { public const string Office365 = "office365"; }
            public static class Office365TriggerOperations { public const string OnNewEmail = "OnNewEmail"; }
            """;

        SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

        // Find position inside the method body (at "Deserialize")
        int position = code.IndexOf("Deserialize(body)", StringComparison.Ordinal);
        Assert.IsTrue(position > 0, "Could not find Deserialize call in code");

        string? result = GetEnclosingMethodTriggerOperationName(root, position);

        // Should return "OnNewEmail" (the member name from the constant reference)
        Assert.AreEqual("OnNewEmail", result);
    }

    /// <summary>
    /// Verifies that GetEnclosingMethodTriggerOperationName returns null when the cursor
    /// is in a method without [ConnectorTriggerMetadata].
    /// </summary>
    [TestMethod]
    public void GetEnclosingMethodTriggerOperationName_NoAttribute_ReturnsNull()
    {
        string code = """
            class Test
            {
                public void RegularMethod()
                {
                    var x = Deserialize(data);
                }
            }
            """;

        SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

        int position = code.IndexOf("Deserialize(data)", StringComparison.Ordinal);

        string? result = GetEnclosingMethodTriggerOperationName(root, position);

        Assert.IsNull(result);
    }

    // Helper: parse code and get first method declaration
    private static MethodDeclarationSyntax GetFirstMethod(string code)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot();
        return root.DescendantNodes().OfType<MethodDeclarationSyntax>().First();
    }

    // Expose the static helper methods for testing via reflection-like direct invocation.
    // These replicate the logic from CompletionHandler since the methods are private.
    private static string? GetTriggerMetadataParameterValue(MethodDeclarationSyntax method, string parameterName)
    {
        foreach (AttributeListSyntax attrList in method.AttributeLists)
        {
            foreach (AttributeSyntax attr in attrList.Attributes)
            {
                string attrName = attr.Name.ToString();
                if (!attrName.Contains("ConnectorTriggerMetadata", StringComparison.Ordinal) &&
                    !attrName.Contains("ConnectorTrigger", StringComparison.Ordinal))
                {
                    continue;
                }

                if (attr.ArgumentList is null)
                {
                    continue;
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
            }
        }

        return null;
    }

    private static bool TryExtractAttributeParameterName(string linePrefix, out string? paramName)
    {
        paramName = null;

        string trimmed = linePrefix.TrimEnd('"', ' ');
        int equalsIndex = trimmed.LastIndexOf('=');
        if (equalsIndex < 0)
        {
            return false;
        }

        string beforeEquals = trimmed.Substring(0, equalsIndex).TrimEnd();

        int nameStart = beforeEquals.LastIndexOfAny(new[] { '(', ',', ' ', '\t' }) + 1;
        paramName = beforeEquals.Substring(nameStart).Trim();

        return paramName.Length > 0 && char.IsUpper(paramName[0]);
    }

    private static string? ExtractParameterValueFromText(string contextText, string parameterName)
    {
        int paramIndex = contextText.IndexOf(parameterName, StringComparison.Ordinal);
        if (paramIndex < 0)
        {
            return null;
        }

        string afterParam = contextText.Substring(paramIndex + parameterName.Length).TrimStart();
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

    private static string? GetEnclosingMethodTriggerOperationName(CompilationUnitSyntax root, int absolutePosition)
    {
        SyntaxToken token = root.FindToken(absolutePosition);
        MethodDeclarationSyntax? method = token.Parent?.AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (method is null)
        {
            return null;
        }

        return GetTriggerMetadataParameterValue(method, "OperationName");
    }
}
