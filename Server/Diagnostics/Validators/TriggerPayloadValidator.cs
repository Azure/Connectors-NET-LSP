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
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SdkLspServer.Diagnostics.Validators;

/// <summary>
/// Validates trigger payload type usage — catches mismatches between
/// <c>[ConnectorTriggerMetadata]</c> operations and <c>Deserialize&lt;T&gt;</c>
/// generic arguments.
/// Emits diagnostics CSDK200–CSDK204.
/// </summary>
internal sealed class TriggerPayloadValidator : IDiagnosticValidator
{
    /// <summary>
    /// Names of deserialization methods whose generic type arguments are validated.
    /// </summary>
    private static readonly HashSet<string> DeserializationMethodNames = new(StringComparer.Ordinal)
    {
        "Deserialize",
        "DeserializeAsync",
        "DeserializeObject",
        "DeserializeObjectAsync",
    };

    /// <summary>
    /// Weak type names that should use a typed payload when one exists.
    /// </summary>
    private static readonly HashSet<string> WeakTypeNames = new(StringComparer.Ordinal)
    {
        "object",
        "Object",
        "dynamic",
        "JsonElement",
        "JObject",
        "JToken",
    };

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

        foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.ValidateDeserializeInvocation(invocation, sourceText, sdkIndex, diagnostics);
        }

        return diagnostics;
    }

    /// <summary>
    /// Validates a single invocation expression, checking if it is a <c>Deserialize&lt;T&gt;</c>
    /// call inside a method decorated with <c>[ConnectorTriggerMetadata]</c>.
    /// </summary>
    private void ValidateDeserializeInvocation(
        InvocationExpressionSyntax invocation,
        SourceText sourceText,
        SdkIndex sdkIndex,
        List<LspDiagnostic> diagnostics)
    {
        GenericNameSyntax? genericName = TriggerPayloadValidator.ExtractDeserializeGenericName(invocation);
        if (genericName is null)
        {
            return;
        }

        if (genericName.TypeArgumentList.Arguments.Count != 1)
        {
            return;
        }

        TypeSyntax typeArgument = genericName.TypeArgumentList.Arguments[0];
        string simpleTypeName = TriggerPayloadValidator.GetSimpleTypeName(typeArgument);

        // Find the enclosing method and its [ConnectorTriggerMetadata] attribute
        MethodDeclarationSyntax? enclosingMethod = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (enclosingMethod is null)
        {
            return;
        }

        TriggerMetadataInfo? triggerInfo = TriggerPayloadValidator.ExtractTriggerMetadata(enclosingMethod, sdkIndex);
        if (triggerInfo is null)
        {
            return;
        }

        // Normalize operation name to canonical form (case-insensitive match)
        // to avoid false CSDK203 when operation name has valid but different casing.
        string canonicalOperationName = TriggerPayloadValidator.ResolveCanonicalOperationName(
            triggerInfo.OperationName,
            triggerInfo.ConnectorNameValue,
            sdkIndex) ?? triggerInfo.OperationName;

        string? expectedPayloadType = sdkIndex.GetPayloadTypeForOperation(triggerInfo.ConnectorNameValue, canonicalOperationName);

        this.EmitPayloadDiagnostic(typeArgument, simpleTypeName, expectedPayloadType, triggerInfo, sourceText, sdkIndex, diagnostics);
    }

    /// <summary>
    /// Applies the diagnostic priority chain and emits the appropriate diagnostic for the type argument.
    /// </summary>
    private void EmitPayloadDiagnostic(
        TypeSyntax typeArgument,
        string simpleTypeName,
        string? expectedPayloadType,
        TriggerMetadataInfo triggerInfo,
        SourceText sourceText,
        SdkIndex sdkIndex,
        List<LspDiagnostic> diagnostics)
    {
        LspRange typeRange = ValidatorHelpers.ToLspRange(typeArgument.Span, sourceText);

        // CSDK201: Weak type when a typed payload exists
        if (TriggerPayloadValidator.WeakTypeNames.Contains(simpleTypeName))
        {
            if (expectedPayloadType is not null)
            {
                string expectedSimpleName = TriggerPayloadValidator.ExtractSimpleNameFromFullName(expectedPayloadType);
                diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
                    typeRange,
                    LspDiagnosticSeverity.Warning,
                    DiagnosticCodes.TriggerPayloadWeakType,
                    $"Deserialize<{simpleTypeName}> uses a weak type. Use the typed payload '{expectedSimpleName}' for operation '{triggerInfo.OperationName}' on connector '{triggerInfo.ConnectorNameValue}'."));
            }

            return;
        }

        // CSDK202: Type not found in SDK type list
        bool typeExists = TriggerPayloadValidator.TypeExistsInIndex(simpleTypeName, sdkIndex);
        if (!typeExists)
        {
            string suggestion = expectedPayloadType is not null
                ? $" Expected type: '{TriggerPayloadValidator.ExtractSimpleNameFromFullName(expectedPayloadType)}'."
                : string.Empty;
            diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
                typeRange,
                LspDiagnosticSeverity.Error,
                DiagnosticCodes.TriggerPayloadTypeNotFound,
                $"Type '{simpleTypeName}' is not found in the SDK type list.{suggestion}"));
            return;
        }

        // CSDK203: Operation does not map to a known payload type
        if (expectedPayloadType is null)
        {
            diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
                typeRange,
                LspDiagnosticSeverity.Warning,
                DiagnosticCodes.TriggerPayloadOperationUnmapped,
                $"Operation '{triggerInfo.OperationName}' on connector '{triggerInfo.ConnectorNameValue}' does not map to a known trigger payload type. Cannot verify Deserialize<{simpleTypeName}>."));
            return;
        }

        // Check if T matches the expected payload type
        string expectedSimple = TriggerPayloadValidator.ExtractSimpleNameFromFullName(expectedPayloadType);
        if (string.Equals(simpleTypeName, expectedSimple, StringComparison.Ordinal))
        {
            return;
        }

        // CSDK204: Type exists but does not follow the expected trigger payload naming convention
        if (!simpleTypeName.EndsWith("TriggerPayload", StringComparison.Ordinal))
        {
            diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
                typeRange,
                LspDiagnosticSeverity.Warning,
                DiagnosticCodes.TriggerPayloadNotPayloadType,
                $"Type '{simpleTypeName}' does not follow the expected trigger payload naming convention (name should end with 'TriggerPayload'). Use '{expectedSimple}' for operation '{triggerInfo.OperationName}'."));
            return;
        }

        // CSDK200: Type mismatch — wrong payload type for this operation
        diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
            typeRange,
            LspDiagnosticSeverity.Error,
            DiagnosticCodes.TriggerPayloadTypeMismatch,
            $"Deserialize<{simpleTypeName}> does not match the expected payload type '{expectedSimple}' for operation '{triggerInfo.OperationName}' on connector '{triggerInfo.ConnectorNameValue}'."));
    }

    /// <summary>
    /// Extracts the <see cref="GenericNameSyntax"/> from an invocation if it is a deserialization call.
    /// Returns null if the invocation is not a recognized deserialization method.
    /// </summary>
    private static GenericNameSyntax? ExtractDeserializeGenericName(InvocationExpressionSyntax invocation)
    {
        GenericNameSyntax? genericName = null;

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            genericName = memberAccess.Name as GenericNameSyntax;
        }
        else if (invocation.Expression is GenericNameSyntax directGeneric)
        {
            genericName = directGeneric;
        }

        if (genericName is null)
        {
            return null;
        }

        if (!TriggerPayloadValidator.DeserializationMethodNames.Contains(genericName.Identifier.Text))
        {
            return null;
        }

        return genericName;
    }

    /// <summary>
    /// Extracts the simple (unqualified) type name from a <see cref="TypeSyntax"/> node.
    /// For <c>System.Text.Json.JsonElement</c> returns <c>"JsonElement"</c>.
    /// For <c>object</c> returns <c>"object"</c>.
    /// </summary>
    private static string GetSimpleTypeName(TypeSyntax typeArgument)
    {
        return typeArgument switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            QualifiedNameSyntax qualified => TriggerPayloadValidator.GetSimpleTypeName(qualified.Right),
            AliasQualifiedNameSyntax aliasQualified => TriggerPayloadValidator.GetSimpleTypeName(aliasQualified.Name),
            GenericNameSyntax generic => generic.Identifier.Text,
            NullableTypeSyntax nullable => TriggerPayloadValidator.GetSimpleTypeName(nullable.ElementType),
            PredefinedTypeSyntax predefined => predefined.Keyword.Text,
            _ => typeArgument.ToString(),
        };
    }

    /// <summary>
    /// Extracts the trigger metadata from the enclosing method's <c>[ConnectorTriggerMetadata]</c> attribute.
    /// Returns null if the method does not have the attribute or if ConnectorName/OperationName cannot be resolved.
    /// </summary>
    private static TriggerMetadataInfo? ExtractTriggerMetadata(MethodDeclarationSyntax method, SdkIndex sdkIndex)
    {
        foreach (AttributeListSyntax attributeList in method.AttributeLists)
        {
            foreach (AttributeSyntax attribute in attributeList.Attributes)
            {
                string attributeName = attribute.Name.ToString();
                if (!ValidatorHelpers.IsTriggerMetadataAttribute(attributeName))
                {
                    continue;
                }

                AttributeArgumentSyntax? connectorNameArgument = ValidatorHelpers.FindNamedArgument(attribute, "ConnectorName");
                AttributeArgumentSyntax? operationNameArgument = ValidatorHelpers.FindNamedArgument(attribute, "OperationName");

                if (connectorNameArgument is null || operationNameArgument is null)
                {
                    return null;
                }

                string? connectorNameText = ValidatorHelpers.ExtractStringValue(connectorNameArgument);
                string? operationNameText = ValidatorHelpers.ExtractStringValue(operationNameArgument);

                if (connectorNameText is null || operationNameText is null)
                {
                    return null;
                }

                string? connectorNameValue = TriggerPayloadValidator.ResolveConnectorNameValue(connectorNameText, sdkIndex);
                if (connectorNameValue is null)
                {
                    return null;
                }

                return new TriggerMetadataInfo(connectorNameValue, operationNameText);
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a connector name text (either a value like "office365" or a field name like "Office365")
    /// to the canonical connector name value using the SDK index.
    /// </summary>
    private static string? ResolveConnectorNameValue(string connectorNameText, SdkIndex sdkIndex)
    {
        // Check for direct value match first
        SdkConstant? directMatch = sdkIndex.ConnectorNameConstants.FirstOrDefault(connector =>
            string.Equals(connector.Value, connectorNameText, StringComparison.OrdinalIgnoreCase));

        if (directMatch is not null)
        {
            return directMatch.Value;
        }

        // Check for field name match (from member access like ConnectorNames.Office365)
        SdkConstant? fieldMatch = sdkIndex.ConnectorNameConstants.FirstOrDefault(connector =>
            string.Equals(connector.FieldName, connectorNameText, StringComparison.OrdinalIgnoreCase));

        if (fieldMatch is not null)
        {
            return fieldMatch.Value;
        }

        return null;
    }

    /// <summary>
    /// Resolves an operation name to its canonical form (FieldName) using a case-insensitive
    /// lookup against the SDK index. This ensures that <c>GetPayloadTypeForOperation</c>
    /// (which is case-sensitive) receives the correct casing.
    /// </summary>
    private static string? ResolveCanonicalOperationName(string operationNameText, string connectorNameValue, SdkIndex sdkIndex)
    {
        ImmutableArray<SdkConstant> operations = sdkIndex.GetTriggerOperations(connectorNameValue);

        SdkConstant? match = operations.FirstOrDefault(operation =>
            string.Equals(operation.FieldName, operationNameText, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(operation.Value, operationNameText, StringComparison.OrdinalIgnoreCase));

        return match?.FieldName;
    }

    /// <summary>
    /// Checks whether a simple type name exists in the SDK index type list.
    /// </summary>
    private static bool TypeExistsInIndex(string simpleTypeName, SdkIndex sdkIndex)
    {
        return sdkIndex.TypeNames.Any(fullTypeName =>
            string.Equals(fullTypeName, simpleTypeName, StringComparison.Ordinal) ||
            fullTypeName.EndsWith("." + simpleTypeName, StringComparison.Ordinal));
    }

    /// <summary>
    /// Extracts the simple name from a fully qualified type name.
    /// For <c>Microsoft.Azure.Connectors.DirectClient.Office365.Office365OnNewEmailTriggerPayload</c>
    /// returns <c>"Office365OnNewEmailTriggerPayload"</c>.
    /// </summary>
    private static string ExtractSimpleNameFromFullName(string fullTypeName)
    {
        int lastDot = fullTypeName.LastIndexOf('.');
        return lastDot >= 0 ? fullTypeName.Substring(lastDot + 1) : fullTypeName;
    }

    /// <summary>
    /// Holds the resolved connector name value and operation name from a
    /// <c>[ConnectorTriggerMetadata]</c> attribute.
    /// </summary>
    private sealed record TriggerMetadataInfo(string ConnectorNameValue, string OperationName);
}
