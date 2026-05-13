//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SdkLspServer.Diagnostics.Validators;

/// <summary>
/// Shared helper methods for diagnostic validators that analyze Roslyn syntax trees.
/// Provides attribute parsing, type extraction, and LSP range conversion utilities.
/// </summary>
internal static class ValidatorHelpers
{
    /// <summary>
    /// Finds a named argument in an attribute by parameter name.
    /// </summary>
    /// <returns>The matching attribute argument, or <see langword="null"/> if not found.</returns>
    public static AttributeArgumentSyntax? FindNamedArgument(AttributeSyntax attribute, string parameterName)
    {
        if (attribute.ArgumentList is null)
        {
            return null;
        }

        return attribute.ArgumentList.Arguments.FirstOrDefault(argument =>
            argument.NameEquals is not null &&
            string.Equals(argument.NameEquals.Name.Identifier.Text, parameterName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Extracts a syntactic text value from an attribute argument expression.
    /// For string literals, returns the literal value. For member access expressions,
    /// returns the rightmost identifier text (e.g., "Office365" from ConnectorNames.Office365).
    /// For bare identifiers, returns the identifier text. This is a purely syntactic
    /// extraction — no semantic constant evaluation is performed.
    /// </summary>
    /// <returns>The extracted string value, or <see langword="null"/> if the expression is not a recognized form.</returns>
    public static string? ExtractStringValue(AttributeArgumentSyntax argument)
    {
        if (argument.Expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        if (argument.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text;
        }

        if (argument.Expression is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.Text;
        }

        return null;
    }

    /// <summary>
    /// Extracts the rightmost identifier from a potentially qualified attribute name.
    /// For example, "MyNamespace.ConnectorTriggerMetadata" and
    /// "global::ConnectorTriggerMetadata" both return "ConnectorTriggerMetadata".
    /// </summary>
    /// <returns>The rightmost identifier portion of the attribute name.</returns>
    public static string ExtractRightmostIdentifier(string attributeName)
    {
        int lastDot = attributeName.LastIndexOf('.');
        int lastAliasQualifier = attributeName.LastIndexOf("::", StringComparison.Ordinal);

        // Calculate the character position after each separator type.
        // When not found (-1), use 0 so it doesn't affect the Max calculation.
        int afterDot = lastDot >= 0 ? lastDot + 1 : 0;
        int afterAlias = lastAliasQualifier >= 0 ? lastAliasQualifier + 2 : 0;
        int identifierStartIndex = Math.Max(afterDot, afterAlias);

        return identifierStartIndex > 0
            ? attributeName.Substring(identifierStartIndex)
            : attributeName;
    }

    /// <summary>
    /// Determines whether the attribute name matches [ConnectorTriggerMetadata] or [ConnectorTrigger].
    /// </summary>
    /// <returns><see langword="true"/> if the attribute name matches a trigger metadata attribute; otherwise, <see langword="false"/>.</returns>
    public static bool IsTriggerMetadataAttribute(string attributeName)
    {
        string identifier = ValidatorHelpers.ExtractRightmostIdentifier(attributeName);
        return string.Equals(identifier, "ConnectorTriggerMetadata", StringComparison.Ordinal) ||
               string.Equals(identifier, "ConnectorTriggerMetadataAttribute", StringComparison.Ordinal) ||
               string.Equals(identifier, "ConnectorTrigger", StringComparison.Ordinal) ||
               string.Equals(identifier, "ConnectorTriggerAttribute", StringComparison.Ordinal);
    }

    /// <summary>
    /// Converts a Roslyn <see cref="TextSpan"/> to an LSP <see cref="LspRange"/>.
    /// </summary>
    /// <returns>The equivalent LSP range.</returns>
    public static LspRange ToLspRange(TextSpan span, SourceText sourceText)
    {
        LinePosition start = sourceText.Lines.GetLinePosition(span.Start);
        LinePosition end = sourceText.Lines.GetLinePosition(span.End);

        return new LspRange(
            new LspPosition(start.Line, start.Character),
            new LspPosition(end.Line, end.Character));
    }

    /// <summary>
    /// Gets the LSP range covering the attribute name for diagnostic placement.
    /// </summary>
    /// <returns>The LSP range covering the attribute name.</returns>
    public static LspRange GetAttributeNameRange(AttributeSyntax attribute, SourceText sourceText)
    {
        TextSpan span = attribute.Name.Span;
        return ValidatorHelpers.ToLspRange(span, sourceText);
    }

    /// <summary>
    /// Gets the LSP range covering the argument value expression for diagnostic placement.
    /// </summary>
    /// <returns>The LSP range covering the argument value expression.</returns>
    public static LspRange GetArgumentValueRange(AttributeArgumentSyntax argument, SourceText sourceText)
    {
        TextSpan span = argument.Expression.Span;
        return ValidatorHelpers.ToLspRange(span, sourceText);
    }

    /// <summary>
    /// Creates an LSP diagnostic with standard source.
    /// </summary>
    /// <returns>A new LSP diagnostic with the specified properties.</returns>
    public static LspDiagnostic CreateDiagnostic(
        LspRange range,
        LspDiagnosticSeverity severity,
        string code,
        string message)
    {
        return new LspDiagnostic
        {
            Range = range,
            Severity = severity,
            Code = code,
            Source = DiagnosticCodes.Source,
            Message = message,
        };
    }
}
