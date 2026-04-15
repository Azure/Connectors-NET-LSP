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
    /// Known serializer type names (simple identifiers) that host deserialization methods.
    /// Used to reduce false positives from user-defined methods named Deserialize.
    /// </summary>
    private static readonly HashSet<string> KnownSerializerTypeNames = new(StringComparer.Ordinal)
    {
        "JsonSerializer",
        "JsonConvert",
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

        // Cache resolved TriggerMetadataInfo per method to avoid re-scanning
        // attributes and SDK operation lists for each Deserialize<T> call.
        var triggerMetadataCache = new Dictionary<MethodDeclarationSyntax, TriggerMetadataInfo?>();

        // Check if the file contains a using static directive for a known serializer type
        // (e.g., using static System.Text.Json.JsonSerializer). If so, bare Deserialize<T>()
        // calls without a receiver are valid and should be checked.
        bool hasStaticSerializerImport = root.Usings.Any(usingDirective =>
            usingDirective.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) &&
            usingDirective.Name is not null &&
            TriggerPayloadValidator.KnownSerializerTypeNames.Contains(
                TriggerPayloadValidator.GetSimpleTypeName(usingDirective.Name)));

        foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.ValidateDeserializeInvocation(invocation, sourceText, sdkIndex, hasStaticSerializerImport, triggerMetadataCache, diagnostics);
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
        bool hasStaticSerializerImport,
        Dictionary<MethodDeclarationSyntax, TriggerMetadataInfo?> triggerMetadataCache,
        List<LspDiagnostic> diagnostics)
    {
        GenericNameSyntax? genericName = TriggerPayloadValidator.ExtractDeserializeGenericName(invocation, hasStaticSerializerImport);
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

        // Unwrap NullableTypeSyntax (e.g., "Namespace.Type?") to get the inner type
        // for qualification checks, since the nullable wrapper hides the qualified form.
        TypeSyntax unwrappedType = typeArgument is NullableTypeSyntax nullable
            ? nullable.ElementType
            : typeArgument;

        // Use the full syntax text when the type argument is qualified (e.g., "Namespace.Type"),
        // otherwise fall back to the simple name for unqualified identifiers.
        // Normalize alias-qualified names (e.g., "global::Namespace.Type") by stripping
        // the alias prefix so the key matches SDK index entries.
        bool isQualifiedType = unwrappedType is QualifiedNameSyntax or AliasQualifiedNameSyntax;
        string typeKey = unwrappedType switch
        {
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.ToString(),
            QualifiedNameSyntax qualified => TriggerPayloadValidator.NormalizeQualifiedName(qualified),
            _ => simpleTypeName,
        };

        // Find the enclosing method and its [ConnectorTriggerMetadata] attribute
        MethodDeclarationSyntax? enclosingMethod = invocation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (enclosingMethod is null)
        {
            return;
        }

        // Use cached trigger metadata to avoid re-scanning attributes per invocation
        if (!triggerMetadataCache.TryGetValue(enclosingMethod, out TriggerMetadataInfo? triggerInfo))
        {
            triggerInfo = TriggerPayloadValidator.ExtractTriggerMetadata(enclosingMethod, sdkIndex);
            triggerMetadataCache[enclosingMethod] = triggerInfo;
        }

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

        this.EmitPayloadDiagnostic(typeArgument, simpleTypeName, typeKey, isQualifiedType, expectedPayloadType, triggerInfo, sourceText, sdkIndex, diagnostics);
    }

    /// <summary>
    /// Applies the diagnostic priority chain and emits the appropriate diagnostic for the type argument.
    /// </summary>
    private void EmitPayloadDiagnostic(
        TypeSyntax typeArgument,
        string simpleTypeName,
        string typeKey,
        bool isQualifiedType,
        string? expectedPayloadType,
        TriggerMetadataInfo triggerInfo,
        SourceText sourceText,
        SdkIndex sdkIndex,
        List<LspDiagnostic> diagnostics)
    {
        LspRange typeRange = ValidatorHelpers.ToLspRange(typeArgument.Span, sourceText);

        // Use the original syntax text for diagnostic messages (e.g., "System.Text.Json.JsonElement")
        // instead of the simplified name, to be clear about what the user wrote.
        string typeArgumentText = typeArgument.ToString();

        // CSDK203: Operation does not map to a known payload type.
        // Check this before the weak-type check so that unmapped operations
        // are always surfaced, even when T is a weak type like 'object'.
        if (expectedPayloadType is null)
        {
            diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
                typeRange,
                LspDiagnosticSeverity.Warning,
                DiagnosticCodes.TriggerPayloadOperationUnmapped,
                $"Operation '{triggerInfo.OperationName}' on connector '{triggerInfo.ConnectorNameValue}' does not map to a known trigger payload type. Cannot verify Deserialize<{typeArgumentText}>."));
            return;
        }

        // CSDK201: Weak type when a typed payload exists
        if (TriggerPayloadValidator.WeakTypeNames.Contains(simpleTypeName))
        {
            string expectedSimpleName = TriggerPayloadValidator.ExtractSimpleNameFromFullName(expectedPayloadType);
            diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
                typeRange,
                LspDiagnosticSeverity.Warning,
                DiagnosticCodes.TriggerPayloadWeakType,
                $"Deserialize<{typeArgumentText}> uses a weak type. Use the typed payload '{expectedSimpleName}' for operation '{triggerInfo.OperationName}' on connector '{triggerInfo.ConnectorNameValue}'."));
            return;
        }

        // CSDK202: Type not found in SDK type list (uses SdkIndex.TypeNameLookup).
        // For qualified types, only check the fully-qualified key to avoid false negatives
        // where a different namespace has the same simple name.
        // For unqualified types, check the simple name once (typeKey == simpleTypeName).
        bool typeExists = isQualifiedType
            ? sdkIndex.TypeNameLookup.Contains(typeKey)
            : sdkIndex.TypeNameLookup.Contains(simpleTypeName);
        if (!typeExists)
        {
            string suggestion = $" Expected type: '{TriggerPayloadValidator.ExtractSimpleNameFromFullName(expectedPayloadType)}'.";
            diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
                typeRange,
                LspDiagnosticSeverity.Error,
                DiagnosticCodes.TriggerPayloadTypeNotFound,
                $"Type '{typeArgumentText}' is not found in the SDK type list.{suggestion}"));
            return;
        }

        // Check if T matches the expected payload type.
        // For qualified types, require a fully-qualified match to avoid false suppression
        // of CSDK200 when a different namespace has the same simple type name.
        // For unqualified types, compare simple names.
        string expectedSimple = TriggerPayloadValidator.ExtractSimpleNameFromFullName(expectedPayloadType);
        bool isMatch = isQualifiedType
            ? string.Equals(typeKey, expectedPayloadType, StringComparison.Ordinal)
            : string.Equals(simpleTypeName, expectedSimple, StringComparison.Ordinal);
        if (isMatch)
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
                $"Type '{typeArgumentText}' does not follow the expected trigger payload naming convention (name should end with 'TriggerPayload'). Use '{expectedSimple}' for operation '{triggerInfo.OperationName}'."));
            return;
        }

        // CSDK200: Type mismatch — wrong payload type for this operation
        diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
            typeRange,
            LspDiagnosticSeverity.Error,
            DiagnosticCodes.TriggerPayloadTypeMismatch,
            $"Deserialize<{typeArgumentText}> does not match the expected payload type '{expectedSimple}' for operation '{triggerInfo.OperationName}' on connector '{triggerInfo.ConnectorNameValue}'."));
    }

    /// <summary>
    /// Extracts the <see cref="GenericNameSyntax"/> from an invocation if it is a deserialization call
    /// on a known serializer type (e.g., <c>JsonSerializer</c>, <c>JsonConvert</c>).
    /// Handles direct calls, member access, and conditional access (e.g., <c>serializer?.Deserialize&lt;T&gt;()</c>).
    /// Returns null if the invocation is not a recognized deserialization method or the receiver
    /// is not a known serializer type.
    /// </summary>
    private static GenericNameSyntax? ExtractDeserializeGenericName(InvocationExpressionSyntax invocation, bool hasStaticSerializerImport)
    {
        GenericNameSyntax? genericName = null;
        string? receiverName = null;

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            genericName = memberAccess.Name as GenericNameSyntax;
            receiverName = TriggerPayloadValidator.GetReceiverSimpleName(memberAccess.Expression);
        }
        else if (invocation.Expression is GenericNameSyntax directGeneric)
        {
            // Direct call like Deserialize<T>(...) with no receiver.
            // Only allow when the file has a using static directive for a known serializer.
            if (!hasStaticSerializerImport)
            {
                return null;
            }

            genericName = directGeneric;
            receiverName = null;
        }
        else if (invocation.Expression is MemberBindingExpressionSyntax memberBinding)
        {
            // Handles conditional access: serializer?.Deserialize<T>(body)
            genericName = memberBinding.Name as GenericNameSyntax;

            // Recover the receiver from the enclosing conditional access expression
            // so we can apply the same known-serializer check as for normal member access.
            if (invocation.Parent is ConditionalAccessExpressionSyntax conditionalAccess)
            {
                receiverName = TriggerPayloadValidator.GetReceiverSimpleName(conditionalAccess.Expression);
            }
        }

        if (genericName is null)
        {
            return null;
        }

        if (!TriggerPayloadValidator.DeserializationMethodNames.Contains(genericName.Identifier.Text))
        {
            return null;
        }

        // Require a known serializer receiver for member access and conditional access calls.
        // Direct calls (receiverName is null) are allowed when hasStaticSerializerImport is true.
        if (receiverName is not null && !TriggerPayloadValidator.KnownSerializerTypeNames.Contains(receiverName))
        {
            return null;
        }

        return genericName;
    }

    /// <summary>
    /// Extracts the simple (rightmost) identifier name from an expression used as a
    /// method receiver. For <c>System.Text.Json.JsonSerializer</c> returns <c>"JsonSerializer"</c>.
    /// For <c>serializer</c> (a variable) returns <c>"serializer"</c>.
    /// </summary>
    private static string? GetReceiverSimpleName(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.Text,
            _ => null,
        };
    }

    /// <summary>
    /// Normalizes a <see cref="QualifiedNameSyntax"/> by stripping any <c>global::</c>
    /// alias prefix from its leftmost part. For <c>global::Namespace.Type</c> returns
    /// <c>"Namespace.Type"</c>. For normal qualified names, returns <c>ToString()</c>.
    /// </summary>
    private static string NormalizeQualifiedName(QualifiedNameSyntax qualified)
    {
        // If the leftmost part is an AliasQualifiedNameSyntax (global::...), strip it
        if (qualified.Left is AliasQualifiedNameSyntax aliasLeft)
        {
            return aliasLeft.Name + "." + qualified.Right;
        }

        return qualified.ToString();
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
    /// Extracts the simple name from a fully qualified or nested type name.
    /// For <c>Microsoft.Azure.Connectors.DirectClient.Office365.Office365OnNewEmailTriggerPayload</c>
    /// returns <c>"Office365OnNewEmailTriggerPayload"</c>.
    /// For <c>Namespace.Outer+Inner</c> returns <c>"Inner"</c>.
    /// </summary>
    private static string ExtractSimpleNameFromFullName(string fullTypeName)
    {
        int lastSeparator = Math.Max(fullTypeName.LastIndexOf('.'), fullTypeName.LastIndexOf('+'));
        return lastSeparator >= 0 ? fullTypeName.Substring(lastSeparator + 1) : fullTypeName;
    }

    /// <summary>
    /// Holds the resolved connector name value and operation name from a
    /// <c>[ConnectorTriggerMetadata]</c> attribute.
    /// </summary>
    private sealed record TriggerMetadataInfo(string ConnectorNameValue, string OperationName);
}
