//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using System.Collections.Immutable;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using OmniSharp.Extensions.LanguageServer.Protocol;

using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SdkLspServer.Diagnostics.Validators;

/// <summary>
/// Validates <c>[ConnectorTriggerMetadata]</c>, <c>[ConnectorTrigger]</c>, and
/// <c>[ConnectorOperation]</c> attribute arguments against the SDK index.
/// Emits diagnostics CSDK001–CSDK009.
/// </summary>
internal sealed class AttributeValidator : IDiagnosticValidator
{
    /// <summary>
    /// Maximum Levenshtein distance for a match to be considered a typo.
    /// </summary>
    private const int MaxTypoDistance = 2;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LspDiagnostic>> ValidateAsync(
        DocumentUri documentUri,
        string documentText,
        SdkIndex? sdkIndex,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<LspDiagnostic>();

        if (string.IsNullOrWhiteSpace(documentText) || sdkIndex is null)
        {
            return diagnostics;
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(documentText, cancellationToken: cancellationToken);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot(cancellationToken);
        SourceText sourceText = await tree
            .GetTextAsync(cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        foreach (MethodDeclarationSyntax method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.ValidateMethodAttributes(method, sourceText, sdkIndex, diagnostics);
        }

        return diagnostics;
    }

    /// <summary>
    /// Validates all relevant attributes on a single method declaration.
    /// </summary>
    private void ValidateMethodAttributes(
        MethodDeclarationSyntax method,
        SourceText sourceText,
        SdkIndex sdkIndex,
        List<LspDiagnostic> diagnostics)
    {
        foreach (AttributeListSyntax attributeList in method.AttributeLists)
        {
            foreach (AttributeSyntax attribute in attributeList.Attributes)
            {
                string attributeName = attribute.Name.ToString();

                if (AttributeValidator.IsTriggerMetadataAttribute(attributeName))
                {
                    this.ValidateTriggerMetadataAttribute(attribute, method, sourceText, sdkIndex, diagnostics);
                }
                else if (AttributeValidator.IsConnectorOperationAttribute(attributeName))
                {
                    this.ValidateConnectorOperationAttribute(attribute, sourceText, sdkIndex, diagnostics);
                }
            }
        }
    }

    /// <summary>
    /// Validates a [ConnectorTriggerMetadata] or [ConnectorTrigger] attribute.
    /// Emits CSDK001–CSDK008.
    /// </summary>
    private void ValidateTriggerMetadataAttribute(
        AttributeSyntax attribute,
        MethodDeclarationSyntax method,
        SourceText sourceText,
        SdkIndex sdkIndex,
        List<LspDiagnostic> diagnostics)
    {
        AttributeArgumentSyntax? connectorNameArgument = AttributeValidator.FindNamedArgument(attribute, "ConnectorName");
        AttributeArgumentSyntax? operationNameArgument = AttributeValidator.FindNamedArgument(attribute, "OperationName");

        string displayName = $"[{attribute.Name}]";

        // CSDK004: Missing ConnectorName
        if (connectorNameArgument is null)
        {
            diagnostics.Add(AttributeValidator.CreateDiagnostic(
                AttributeValidator.GetAttributeNameRange(attribute, sourceText),
                LspDiagnosticSeverity.Error,
                DiagnosticCodes.TriggerMetadataMissingConnectorName,
                $"{displayName} is missing required 'ConnectorName' argument."));
        }

        // CSDK005: Missing OperationName
        if (operationNameArgument is null)
        {
            diagnostics.Add(AttributeValidator.CreateDiagnostic(
                AttributeValidator.GetAttributeNameRange(attribute, sourceText),
                LspDiagnosticSeverity.Error,
                DiagnosticCodes.TriggerMetadataMissingOperationName,
                $"{displayName} is missing required 'OperationName' argument."));
        }

        // Validate ConnectorName value (CSDK001, CSDK002, CSDK003)
        string? connectorNameValue = null;
        bool connectorNameResolved = false;
        if (connectorNameArgument is not null)
        {
            connectorNameValue = AttributeValidator.ExtractStringValue(connectorNameArgument);
            if (connectorNameValue is not null)
            {
                connectorNameResolved = this.ValidateConnectorName(
                    connectorNameValue,
                    connectorNameArgument,
                    sourceText,
                    sdkIndex,
                    diagnostics);
            }
        }

        // Validate OperationName value (CSDK007, CSDK008) only when ConnectorName resolves
        if (operationNameArgument is not null && connectorNameValue is not null && connectorNameResolved)
        {
            string? operationNameValue = AttributeValidator.ExtractStringValue(operationNameArgument);
            if (operationNameValue is not null)
            {
                this.ValidateOperationName(
                    operationNameValue,
                    connectorNameValue,
                    operationNameArgument,
                    sourceText,
                    sdkIndex,
                    diagnostics);
            }
        }

        // CSDK006: Signature mismatch
        this.ValidateTriggerMethodSignature(attribute, method, sourceText, displayName, diagnostics);
    }

    /// <summary>
    /// Validates a [ConnectorOperation] attribute.
    /// Emits CSDK009.
    /// </summary>
    private void ValidateConnectorOperationAttribute(
        AttributeSyntax attribute,
        SourceText sourceText,
        SdkIndex sdkIndex,
        List<LspDiagnostic> diagnostics)
    {
        AttributeArgumentSyntax? operationNameArgument = AttributeValidator.FindNamedArgument(attribute, "OperationName");
        AttributeArgumentSyntax? connectorNameArgument = AttributeValidator.FindNamedArgument(attribute, "ConnectorName");

        if (operationNameArgument is null || connectorNameArgument is null)
        {
            return;
        }

        string? connectorNameValue = AttributeValidator.ExtractStringValue(connectorNameArgument);
        string? operationNameValue = AttributeValidator.ExtractStringValue(operationNameArgument);

        if (connectorNameValue is null || operationNameValue is null)
        {
            return;
        }

        // Look up all trigger operations for this connector
        ImmutableArray<SdkConstant> operations = sdkIndex.GetTriggerOperations(connectorNameValue);
        IEnumerable<SdkConstant> allOperations = sdkIndex.GetAllTriggerOperations();

        bool foundInConnector = operations.Any(operation =>
            string.Equals(operation.Value, operationNameValue, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(operation.FieldName, operationNameValue, StringComparison.OrdinalIgnoreCase));

        if (!foundInConnector)
        {
            bool foundInAny = allOperations.Any(operation =>
                string.Equals(operation.Value, operationNameValue, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(operation.FieldName, operationNameValue, StringComparison.OrdinalIgnoreCase));

            string message = foundInAny
                ? $"[ConnectorOperation] operation '{operationNameValue}' exists but does not belong to connector '{connectorNameValue}'."
                : $"[ConnectorOperation] operation '{operationNameValue}' not found in SDK index for connector '{connectorNameValue}'.";

            diagnostics.Add(AttributeValidator.CreateDiagnostic(
                AttributeValidator.GetArgumentValueRange(operationNameArgument, sourceText),
                LspDiagnosticSeverity.Warning,
                DiagnosticCodes.ConnectorOperationUnknown,
                message));
        }
    }

    /// <summary>
    /// Validates a ConnectorName value against the SDK index.
    /// Emits CSDK001 (unknown), CSDK002 (typo), or CSDK003 (casing).
    /// Returns true if the connector name resolves to a known connector (even with casing issues).
    /// </summary>
    private bool ValidateConnectorName(
        string connectorNameValue,
        AttributeArgumentSyntax argument,
        SourceText sourceText,
        SdkIndex sdkIndex,
        List<LspDiagnostic> diagnostics)
    {
        ImmutableArray<SdkConstant> knownConnectors = sdkIndex.ConnectorNameConstants;

        // Check if the argument is a constant reference (member access / identifier) vs a string literal.
        // For string literals, only match against Value (the canonical form); FieldName matching
        // would hide casing issues (e.g., literal "Office365" matching FieldName when Value is "office365").
        bool isConstantReference = argument.Expression is MemberAccessExpressionSyntax or IdentifierNameSyntax;

        // Exact match — value is valid
        bool exactMatch = isConstantReference
            ? knownConnectors.Any(connector =>
                string.Equals(connector.Value, connectorNameValue, StringComparison.Ordinal) ||
                string.Equals(connector.FieldName, connectorNameValue, StringComparison.Ordinal))
            : knownConnectors.Any(connector =>
                string.Equals(connector.Value, connectorNameValue, StringComparison.Ordinal));

        if (exactMatch)
        {
            return true;
        }

        // CSDK003: Case-insensitive match (wrong casing)
        SdkConstant? casingMatch = isConstantReference
            ? knownConnectors.FirstOrDefault(connector =>
                string.Equals(connector.Value, connectorNameValue, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(connector.FieldName, connectorNameValue, StringComparison.OrdinalIgnoreCase))
            : knownConnectors.FirstOrDefault(connector =>
                string.Equals(connector.Value, connectorNameValue, StringComparison.OrdinalIgnoreCase));

        if (casingMatch is not null)
        {
            diagnostics.Add(AttributeValidator.CreateDiagnostic(
                AttributeValidator.GetArgumentValueRange(argument, sourceText),
                LspDiagnosticSeverity.Warning,
                DiagnosticCodes.ConnectorNameCasing,
                $"ConnectorName has wrong casing. Expected '{casingMatch.Value}' (or use ConnectorNames.{casingMatch.FieldName})."));
            return true;
        }

        // CSDK002: Typo detection (Levenshtein distance <= 2)
        SdkConstant? closestMatch = null;
        int closestDistance = int.MaxValue;

        foreach (SdkConstant connector in knownConnectors)
        {
            int distance = LevenshteinDistance.Compute(connectorNameValue, connector.Value);
            if (distance <= AttributeValidator.MaxTypoDistance && distance < closestDistance)
            {
                closestDistance = distance;
                closestMatch = connector;
            }
        }

        if (closestMatch is not null)
        {
            diagnostics.Add(AttributeValidator.CreateDiagnostic(
                AttributeValidator.GetArgumentValueRange(argument, sourceText),
                LspDiagnosticSeverity.Warning,
                DiagnosticCodes.ConnectorNameTypo,
                $"Possible typo in ConnectorName '{connectorNameValue}'. Did you mean '{closestMatch.Value}' (ConnectorNames.{closestMatch.FieldName})?"));
            return false;
        }

        // CSDK001: Completely unknown
        diagnostics.Add(AttributeValidator.CreateDiagnostic(
            AttributeValidator.GetArgumentValueRange(argument, sourceText),
            LspDiagnosticSeverity.Error,
            DiagnosticCodes.UnknownConnectorName,
            $"ConnectorName '{connectorNameValue}' does not match any known connector in the SDK."));
        return false;
    }

    /// <summary>
    /// Validates an OperationName value against the SDK index for a given connector.
    /// Emits CSDK007 (unknown operation) or CSDK008 (no triggers for connector).
    /// </summary>
    private void ValidateOperationName(
        string operationNameValue,
        string connectorNameValue,
        AttributeArgumentSyntax argument,
        SourceText sourceText,
        SdkIndex sdkIndex,
        List<LspDiagnostic> diagnostics)
    {
        ImmutableArray<SdkConstant> operations = sdkIndex.GetTriggerOperations(connectorNameValue);

        // CSDK008: Connector has no trigger operations
        if (operations.IsEmpty)
        {
            diagnostics.Add(AttributeValidator.CreateDiagnostic(
                AttributeValidator.GetArgumentValueRange(argument, sourceText),
                LspDiagnosticSeverity.Warning,
                DiagnosticCodes.OperationNameNoTriggers,
                $"Connector '{connectorNameValue}' has no trigger operations in the SDK index."));
            return;
        }

        // CSDK007: Operation not found
        bool found = operations.Any(operation =>
            string.Equals(operation.Value, operationNameValue, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(operation.FieldName, operationNameValue, StringComparison.OrdinalIgnoreCase));

        if (!found)
        {
            const int maxDisplayedOperations = 5;
            IEnumerable<string> operationNames = operations.Select(operation => operation.FieldName);
            string availableOperations = operations.Length <= maxDisplayedOperations
                ? string.Join(", ", operationNames)
                : string.Join(", ", operationNames.Take(maxDisplayedOperations)) + $", ... ({operations.Length} total)";
            diagnostics.Add(AttributeValidator.CreateDiagnostic(
                AttributeValidator.GetArgumentValueRange(argument, sourceText),
                LspDiagnosticSeverity.Error,
                DiagnosticCodes.UnknownOperationName,
                $"OperationName '{operationNameValue}' is not a known trigger operation for connector '{connectorNameValue}'. Available: {availableOperations}."));
        }
    }

    /// <summary>
    /// Validates that the method signature uses an async-compatible return type for the trigger callback pattern.
    /// Emits CSDK006 if the method does not return Task, Task&lt;T&gt;, ValueTask, or ValueTask&lt;T&gt;.
    /// </summary>
    private void ValidateTriggerMethodSignature(
        AttributeSyntax attribute,
        MethodDeclarationSyntax method,
        SourceText sourceText,
        string displayName,
        List<LspDiagnostic> diagnostics)
    {
        // Extract the rightmost identifier from the return type to handle
        // both simple (Task) and fully-qualified (System.Threading.Tasks.Task) forms.
        string returnTypeIdentifier = AttributeValidator.GetReturnTypeIdentifier(method.ReturnType);

        bool hasAsyncReturn = string.Equals(returnTypeIdentifier, "Task", StringComparison.Ordinal) ||
                              string.Equals(returnTypeIdentifier, "ValueTask", StringComparison.Ordinal);

        if (!hasAsyncReturn)
        {
            diagnostics.Add(AttributeValidator.CreateDiagnostic(
                AttributeValidator.GetAttributeNameRange(attribute, sourceText),
                LspDiagnosticSeverity.Warning,
                DiagnosticCodes.TriggerMetadataSignatureMismatch,
                $"{displayName} is on a method that does not return Task, Task<T>, ValueTask, or ValueTask<T>. Trigger callbacks should use an async-compatible return type."));
        }
    }

    /// <summary>
    /// Extracts the simple identifier from a return type syntax node.
    /// For <c>System.Threading.Tasks.Task&lt;int&gt;</c> returns <c>"Task"</c>.
    /// For <c>Task</c> returns <c>"Task"</c>.
    /// For <c>global::System.Threading.Tasks.Task</c> returns <c>"Task"</c>.
    /// For <c>Task?</c> returns <c>"Task"</c>.
    /// </summary>
    private static string GetReturnTypeIdentifier(TypeSyntax returnType)
    {
        return returnType switch
        {
            GenericNameSyntax generic => generic.Identifier.Text,
            QualifiedNameSyntax qualified => AttributeValidator.GetReturnTypeIdentifier(qualified.Right),
            AliasQualifiedNameSyntax aliasQualified => AttributeValidator.GetReturnTypeIdentifier(aliasQualified.Name),
            NullableTypeSyntax nullable => AttributeValidator.GetReturnTypeIdentifier(nullable.ElementType),
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => returnType.ToString(),
        };
    }

    /// <summary>
    /// Determines whether the attribute name matches [ConnectorTriggerMetadata] or [ConnectorTrigger].
    /// </summary>
    private static bool IsTriggerMetadataAttribute(string attributeName)
    {
        string identifier = AttributeValidator.ExtractRightmostIdentifier(attributeName);
        return string.Equals(identifier, "ConnectorTriggerMetadata", StringComparison.Ordinal) ||
               string.Equals(identifier, "ConnectorTriggerMetadataAttribute", StringComparison.Ordinal) ||
               string.Equals(identifier, "ConnectorTrigger", StringComparison.Ordinal) ||
               string.Equals(identifier, "ConnectorTriggerAttribute", StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether the attribute name matches [ConnectorOperation].
    /// </summary>
    private static bool IsConnectorOperationAttribute(string attributeName)
    {
        string identifier = AttributeValidator.ExtractRightmostIdentifier(attributeName);
        return string.Equals(identifier, "ConnectorOperation", StringComparison.Ordinal) ||
               string.Equals(identifier, "ConnectorOperationAttribute", StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts the rightmost identifier from a potentially qualified attribute name.
    /// For example, "MyNamespace.ConnectorTriggerMetadata" and
    /// "global::ConnectorTriggerMetadata" both return "ConnectorTriggerMetadata".
    /// </summary>
    private static string ExtractRightmostIdentifier(string attributeName)
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
    /// Finds a named argument in an attribute by parameter name.
    /// </summary>
    private static AttributeArgumentSyntax? FindNamedArgument(AttributeSyntax attribute, string parameterName)
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
    /// Extracts the string value from an attribute argument expression.
    /// Supports string literals (returns the literal value) and member access expressions
    /// (returns the member name, e.g., "Office365" from ConnectorNames.Office365).
    /// </summary>
    private static string? ExtractStringValue(AttributeArgumentSyntax argument)
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
    /// Gets the LSP range covering the attribute name for diagnostic placement.
    /// </summary>
    private static LspRange GetAttributeNameRange(AttributeSyntax attribute, SourceText sourceText)
    {
        TextSpan span = attribute.Name.Span;
        return AttributeValidator.ToLspRange(span, sourceText);
    }

    /// <summary>
    /// Gets the LSP range covering the argument value expression for diagnostic placement.
    /// </summary>
    private static LspRange GetArgumentValueRange(AttributeArgumentSyntax argument, SourceText sourceText)
    {
        TextSpan span = argument.Expression.Span;
        return AttributeValidator.ToLspRange(span, sourceText);
    }

    /// <summary>
    /// Converts a Roslyn <see cref="TextSpan"/> to an LSP <see cref="LspRange"/>.
    /// </summary>
    private static LspRange ToLspRange(TextSpan span, SourceText sourceText)
    {
        LinePosition start = sourceText.Lines.GetLinePosition(span.Start);
        LinePosition end = sourceText.Lines.GetLinePosition(span.End);

        return new LspRange(
            new LspPosition(start.Line, start.Character),
            new LspPosition(end.Line, end.Character));
    }

    /// <summary>
    /// Creates an LSP diagnostic with standard source.
    /// </summary>
    private static LspDiagnostic CreateDiagnostic(
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
