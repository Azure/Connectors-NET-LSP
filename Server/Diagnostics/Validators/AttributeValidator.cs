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

        // CSDK004: Missing ConnectorName
        if (connectorNameArgument is null)
        {
            diagnostics.Add(AttributeValidator.CreateDiagnostic(
                AttributeValidator.GetAttributeNameRange(attribute, sourceText),
                LspDiagnosticSeverity.Error,
                DiagnosticCodes.TriggerMetadataMissingConnectorName,
                "[ConnectorTriggerMetadata] is missing required 'ConnectorName' argument."));
        }

        // CSDK005: Missing OperationName
        if (operationNameArgument is null)
        {
            diagnostics.Add(AttributeValidator.CreateDiagnostic(
                AttributeValidator.GetAttributeNameRange(attribute, sourceText),
                LspDiagnosticSeverity.Error,
                DiagnosticCodes.TriggerMetadataMissingOperationName,
                "[ConnectorTriggerMetadata] is missing required 'OperationName' argument."));
        }

        // Validate ConnectorName value (CSDK001, CSDK002, CSDK003)
        string? connectorNameValue = null;
        if (connectorNameArgument is not null)
        {
            connectorNameValue = AttributeValidator.ExtractStringValue(connectorNameArgument);
            if (connectorNameValue is not null)
            {
                this.ValidateConnectorName(
                    connectorNameValue,
                    connectorNameArgument,
                    sourceText,
                    sdkIndex,
                    diagnostics);
            }
        }

        // Validate OperationName value (CSDK007, CSDK008)
        if (operationNameArgument is not null && connectorNameValue is not null)
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
        this.ValidateTriggerMethodSignature(attribute, method, sourceText, diagnostics);
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
    /// </summary>
    private void ValidateConnectorName(
        string connectorNameValue,
        AttributeArgumentSyntax argument,
        SourceText sourceText,
        SdkIndex sdkIndex,
        List<LspDiagnostic> diagnostics)
    {
        ImmutableArray<SdkConstant> knownConnectors = sdkIndex.ConnectorNameConstants;

        // Exact match — value is valid
        if (knownConnectors.Any(connector =>
            string.Equals(connector.Value, connectorNameValue, StringComparison.Ordinal) ||
            string.Equals(connector.FieldName, connectorNameValue, StringComparison.Ordinal)))
        {
            return;
        }

        // CSDK003: Case-insensitive match (wrong casing)
        SdkConstant? casingMatch = knownConnectors.FirstOrDefault(connector =>
            string.Equals(connector.Value, connectorNameValue, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(connector.FieldName, connectorNameValue, StringComparison.OrdinalIgnoreCase));

        if (casingMatch is not null)
        {
            diagnostics.Add(AttributeValidator.CreateDiagnostic(
                AttributeValidator.GetArgumentValueRange(argument, sourceText),
                LspDiagnosticSeverity.Warning,
                DiagnosticCodes.ConnectorNameCasing,
                $"ConnectorName has wrong casing. Expected '{casingMatch.Value}' (or use ConnectorNames.{casingMatch.FieldName})."));
            return;
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
            return;
        }

        // CSDK001: Completely unknown
        diagnostics.Add(AttributeValidator.CreateDiagnostic(
            AttributeValidator.GetArgumentValueRange(argument, sourceText),
            LspDiagnosticSeverity.Error,
            DiagnosticCodes.UnknownConnectorName,
            $"ConnectorName '{connectorNameValue}' does not match any known connector in the SDK."));
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
            string availableOperations = string.Join(", ", operations.Select(operation => operation.FieldName));
            diagnostics.Add(AttributeValidator.CreateDiagnostic(
                AttributeValidator.GetArgumentValueRange(argument, sourceText),
                LspDiagnosticSeverity.Error,
                DiagnosticCodes.UnknownOperationName,
                $"OperationName '{operationNameValue}' is not a known trigger operation for connector '{connectorNameValue}'. Available: {availableOperations}."));
        }
    }

    /// <summary>
    /// Validates that the method signature follows the trigger callback pattern.
    /// Emits CSDK006 if the method does not return Task or does not have an HttpTrigger parameter.
    /// </summary>
    private void ValidateTriggerMethodSignature(
        AttributeSyntax attribute,
        MethodDeclarationSyntax method,
        SourceText sourceText,
        List<LspDiagnostic> diagnostics)
    {
        string returnType = method.ReturnType.ToString();

        // Check for Task-based return type
        bool hasAsyncReturn = returnType.StartsWith("Task", StringComparison.Ordinal) ||
                              returnType.StartsWith("async", StringComparison.Ordinal) ||
                              returnType.StartsWith("ValueTask", StringComparison.Ordinal);

        if (!hasAsyncReturn)
        {
            diagnostics.Add(AttributeValidator.CreateDiagnostic(
                AttributeValidator.GetAttributeNameRange(attribute, sourceText),
                LspDiagnosticSeverity.Warning,
                DiagnosticCodes.TriggerMetadataSignatureMismatch,
                "[ConnectorTriggerMetadata] is on a non-async method. Trigger callbacks should return Task or Task<T>."));
        }
    }

    /// <summary>
    /// Determines whether the attribute name matches [ConnectorTriggerMetadata] or [ConnectorTrigger].
    /// </summary>
    private static bool IsTriggerMetadataAttribute(string attributeName)
    {
        return attributeName.Contains("ConnectorTriggerMetadata", StringComparison.Ordinal) ||
               (attributeName.Contains("ConnectorTrigger", StringComparison.Ordinal) &&
                !attributeName.Contains("ConnectorTriggerMetadata", StringComparison.Ordinal));
    }

    /// <summary>
    /// Determines whether the attribute name matches [ConnectorOperation].
    /// </summary>
    private static bool IsConnectorOperationAttribute(string attributeName)
    {
        return attributeName.Contains("ConnectorOperation", StringComparison.Ordinal) &&
               !attributeName.Contains("ConnectorTrigger", StringComparison.Ordinal);
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
