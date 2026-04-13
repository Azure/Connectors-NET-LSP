//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using OmniSharp.Extensions.LanguageServer.Protocol;

using SdkLspServer;
using SdkLspServer.Services.Connections;

using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SdkLspServer.Diagnostics.Validators;

/// <summary>
/// Validates connection parameter usage against the <see cref="ConnectionsService"/> state.
/// Emits diagnostics CSDK100–CSDK105.
/// </summary>
internal sealed class ConnectionConfigValidator : IDiagnosticValidator
{
    private readonly ConnectionsService connectionsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionConfigValidator"/> class.
    /// </summary>
    /// <param name="connectionsService">The connections service providing runtime connection state.</param>
    public ConnectionConfigValidator(ConnectionsService connectionsService)
    {
        this.connectionsService = connectionsService ?? throw new ArgumentNullException(nameof(connectionsService));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<LspDiagnostic>> ValidateAsync(
        DocumentUri documentUri,
        string documentText,
        SdkIndex? sdkIndex,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<LspDiagnostic>();

        if (string.IsNullOrWhiteSpace(documentText))
        {
            return diagnostics;
        }

        ConnectionsConfig? connections = this.connectionsService.GetConnections();

        // Suppress connection diagnostics when connections haven't been loaded yet.
        // This avoids false positives when the server just started and hasn't received
        // the custom/updateConnections notification.
        if (connections is null)
        {
            return diagnostics;
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(documentText, cancellationToken: cancellationToken);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot(cancellationToken);
        SourceText sourceText = await tree
            .GetTextAsync(cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Track connector types referenced by this document for CSDK102 analysis.
        // Maps connector type -> first usage location for meaningful diagnostic placement.
        var referencedConnectorTypes = new Dictionary<string, TextSpan>(StringComparer.OrdinalIgnoreCase);

        foreach (SyntaxNode node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (node is InvocationExpressionSyntax invocation)
            {
                this.ValidateInvocationArguments(invocation, sourceText, connections, referencedConnectorTypes, diagnostics, sdkIndex);
            }
            else if (node is AttributeSyntax attribute)
            {
                this.ValidateAttributeConnectionArguments(attribute, sourceText, connections, referencedConnectorTypes, diagnostics);
            }
        }

        // CSDK102: No connection configured for referenced connectors.
        foreach (KeyValuePair<string, TextSpan> entry in referencedConnectorTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<string> connectionNames = ConnectionsHelper.GetConnectionNamesForConnector(connections, entry.Key);
            if (!connectionNames.Any())
            {
                diagnostics.Add(ConnectionConfigValidator.CreateDiagnostic(
                    ConnectionConfigValidator.ToLspRange(entry.Value, sourceText),
                    LspDiagnosticSeverity.Error,
                    DiagnosticCodes.NoConnectionConfigured,
                    $"No connection configured for connector '{entry.Key}'. Add a connection in local.settings.json or connections.json."));
            }
        }

        return diagnostics;
    }

    /// <summary>
    /// Validates connection-related arguments in method invocations.
    /// Detects CSDK100 (invalid value), CSDK101 (hard-coded), CSDK104 (ambiguous),
    /// and CSDK105 (type mismatch).
    /// </summary>
    private void ValidateInvocationArguments(
        InvocationExpressionSyntax invocation,
        SourceText sourceText,
        ConnectionsConfig connections,
        Dictionary<string, TextSpan> referencedConnectorTypes,
        List<LspDiagnostic> diagnostics,
        SdkIndex? sdkIndex)
    {
        // Infer the connector type once per invocation (used for CSDK102 and CSDK104).
        // Filter against known connectors (sdkIndex) or configured connection types to avoid
        // false positives from arbitrary *Client types (e.g., HttpClient -> "http").
        string? invokedConnectorType = ConnectionConfigValidator.InferConnectorTypeFromInvocation(invocation);
        if (invokedConnectorType is not null &&
            !ConnectionConfigValidator.IsKnownConnectorType(invokedConnectorType, sdkIndex, connections))
        {
            invokedConnectorType = null;
        }

        if (invokedConnectorType is not null)
        {
            referencedConnectorTypes.TryAdd(invokedConnectorType, invocation.Expression.Span);
        }

        // Look for connection parameters by name convention.
        foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
        {
            string? parameterName = ConnectionConfigValidator.GetArgumentParameterName(argument);
            if (parameterName is null || !ConnectionConfigValidator.IsConnectionParameterName(parameterName))
            {
                continue;
            }

            // Only validate string literal arguments — variables, method calls, etc. are not statically analyzable.
            if (argument.Expression is not LiteralExpressionSyntax literal ||
                !literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                continue;
            }

            string connectionValue = literal.Token.ValueText;
            LspRange valueRange = ConnectionConfigValidator.ToLspRange(literal.Span, sourceText);

            // CSDK101: Hard-coded connection string value.
            if (ConnectionConfigValidator.LooksLikeHardCodedConnectionString(connectionValue))
            {
                diagnostics.Add(ConnectionConfigValidator.CreateDiagnostic(
                    valueRange,
                    LspDiagnosticSeverity.Information,
                    DiagnosticCodes.ConnectionParameterHardCoded,
                    $"Connection parameter uses a hard-coded value '{connectionValue}'. Consider using a configuration setting or environment variable."));
                continue;
            }

            // CSDK100: Connection parameter value doesn't match any configured connection.
            if (!string.IsNullOrEmpty(connectionValue) &&
                !ConnectionsHelper.ContainsConnection(connections, connectionValue))
            {
                diagnostics.Add(ConnectionConfigValidator.CreateDiagnostic(
                    valueRange,
                    LspDiagnosticSeverity.Warning,
                    DiagnosticCodes.ConnectionParameterValueInvalid,
                    $"Connection '{connectionValue}' is not found in the connections configuration."));
                continue;
            }

            // CSDK105: Connection type mismatch.
            if (invokedConnectorType is not null && !string.IsNullOrEmpty(connectionValue))
            {
                this.ValidateConnectionTypeMismatch(connectionValue, invokedConnectorType, valueRange, connections, diagnostics);
            }
        }

        // CSDK104: Multiple connections match the connector type.
        if (invokedConnectorType is not null)
        {
            List<string> matchingConnections = ConnectionsHelper
                .GetConnectionNamesForConnector(connections, invokedConnectorType)
                .ToList();

            if (matchingConnections.Count > 1)
            {
                // Only emit when there's no explicitly provided connection parameter
                // (auto-resolution would be ambiguous).
                bool hasExplicitConnectionArg = invocation.ArgumentList.Arguments.Any(argument =>
                {
                    // Named connection argument.
                    string? argumentParameterName = ConnectionConfigValidator.GetArgumentParameterName(argument);
                    if (argumentParameterName is not null &&
                        ConnectionConfigValidator.IsConnectionParameterName(argumentParameterName))
                    {
                        return true;
                    }

                    // Positional string literal matching a configured connection name.
                    if (argument.Expression is LiteralExpressionSyntax positionalLiteral &&
                        positionalLiteral.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        string positionalValue = positionalLiteral.Token.ValueText;
                        return matchingConnections.Any(connectionName =>
                            string.Equals(connectionName, positionalValue, StringComparison.OrdinalIgnoreCase));
                    }

                    return false;
                });

                if (!hasExplicitConnectionArg)
                {
                    LspRange invocationRange = ConnectionConfigValidator.ToLspRange(invocation.Expression.Span, sourceText);
                    diagnostics.Add(ConnectionConfigValidator.CreateDiagnostic(
                        invocationRange,
                        LspDiagnosticSeverity.Warning,
                        DiagnosticCodes.MultipleConnectionsAmbiguous,
                        $"Multiple connections match connector '{invokedConnectorType}': {string.Join(", ", matchingConnections)}. Specify the connection explicitly."));
                }
            }
        }
    }

    /// <summary>
    /// Validates connection-related arguments in SDK attributes such as
    /// <c>[ConnectorTriggerMetadata]</c> and <c>[ConnectorOperation]</c>.
    /// </summary>
    private void ValidateAttributeConnectionArguments(
        AttributeSyntax attribute,
        SourceText sourceText,
        ConnectionsConfig connections,
        Dictionary<string, TextSpan> referencedConnectorTypes,
        List<LspDiagnostic> diagnostics)
    {
        string attributeName = ConnectionConfigValidator.ExtractRightmostIdentifier(attribute.Name.ToString());

        if (!ConnectionConfigValidator.IsSdkAttribute(attributeName))
        {
            return;
        }

        // Extract ConnectorName to determine the connector type.
        string? connectorType = ConnectionConfigValidator.ExtractNamedArgumentValue(attribute, "ConnectorName");
        if (connectorType is not null)
        {
            AttributeArgumentSyntax? connectorNameArg = ConnectionConfigValidator.FindNamedArgument(attribute, "ConnectorName");
            TextSpan span = connectorNameArg?.Expression.Span ?? attribute.Name.Span;
            referencedConnectorTypes.TryAdd(connectorType, span);

            // CSDK104: Multiple connections for this connector type.
            // Suppress when ConnectionName is explicitly provided (it disambiguates).
            bool hasExplicitConnectionName = ConnectionConfigValidator.FindNamedArgument(attribute, "ConnectionName") is not null;

            if (!hasExplicitConnectionName)
            {
                List<string> matchingConnections = ConnectionsHelper
                    .GetConnectionNamesForConnector(connections, connectorType)
                    .ToList();

                if (matchingConnections.Count > 1 && connectorNameArg is not null)
                {
                    diagnostics.Add(ConnectionConfigValidator.CreateDiagnostic(
                        ConnectionConfigValidator.ToLspRange(connectorNameArg.Expression.Span, sourceText),
                        LspDiagnosticSeverity.Warning,
                        DiagnosticCodes.MultipleConnectionsAmbiguous,
                        $"Multiple connections match connector '{connectorType}': {string.Join(", ", matchingConnections)}. Auto-resolution may be ambiguous."));
                }
            }
        }

        // Check for explicit ConnectionName argument.
        string? connectionName = ConnectionConfigValidator.ExtractNamedArgumentValue(attribute, "ConnectionName");
        if (connectionName is not null)
        {
            AttributeArgumentSyntax? connectionNameArg = ConnectionConfigValidator.FindNamedArgument(attribute, "ConnectionName");
            if (connectionNameArg is not null)
            {
                LspRange valueRange = ConnectionConfigValidator.ToLspRange(connectionNameArg.Expression.Span, sourceText);

                // CSDK103: Connection name missing from config.
                if (!ConnectionsHelper.ContainsConnection(connections, connectionName))
                {
                    diagnostics.Add(ConnectionConfigValidator.CreateDiagnostic(
                        valueRange,
                        LspDiagnosticSeverity.Warning,
                        DiagnosticCodes.ConnectionMissing,
                        $"Connection '{connectionName}' is missing from the connections configuration."));
                }

                // CSDK105: Connection type doesn't match connector type.
                if (connectorType is not null)
                {
                    this.ValidateConnectionTypeMismatch(connectionName, connectorType, valueRange, connections, diagnostics);
                }
            }
        }
    }

    /// <summary>
    /// Checks whether a connection's type matches the expected connector type.
    /// Emits CSDK105 if there is a mismatch.
    /// </summary>
    private void ValidateConnectionTypeMismatch(
        string connectionName,
        string expectedConnectorType,
        LspRange valueRange,
        ConnectionsConfig connections,
        List<LspDiagnostic> diagnostics)
    {
        string? actualConnectorType = ConnectionConfigValidator.GetConnectionConnectorType(connections, connectionName);

        // Skip mismatch check when the actual type is unknown or empty (malformed config).
        if (string.IsNullOrEmpty(actualConnectorType) ||
            string.Equals(actualConnectorType, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(actualConnectorType, expectedConnectorType, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(ConnectionConfigValidator.CreateDiagnostic(
                valueRange,
                LspDiagnosticSeverity.Warning,
                DiagnosticCodes.ConnectionTypeMismatch,
                $"Connection '{connectionName}' is of type '{actualConnectorType}' but the code expects connector type '{expectedConnectorType}'."));
        }
    }

    /// <summary>
    /// Gets the connector type for a configured connection name.
    /// </summary>
    private static string? GetConnectionConnectorType(ConnectionsConfig connections, string connectionName)
    {
        if (connections.ManagedApiConnections?.TryGetValue(connectionName, out ManagedApiConnection? managed) == true)
        {
            return ConnectionsHelper.ExtractConnectorType(managed.Api.Id);
        }

        if (connections.DirectClientConnections?.TryGetValue(connectionName, out DirectClientConnection? directClient) == true)
        {
            return directClient.ConnectorType;
        }

        return null;
    }

    /// <summary>
    /// Determines whether a value looks like a hard-coded connection string or URL
    /// rather than a connection configuration name.
    /// </summary>
    private static bool LooksLikeHardCodedConnectionString(string value)
    {
        return value.Contains("://", StringComparison.Ordinal) ||
               value.Contains("/subscriptions/", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("apim/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks whether an inferred connector type is a known connector.
    /// Validates against the SDK index (when available) and configured connection types.
    /// When the SDK index is unavailable, only accepts types that have configured connections
    /// to avoid false positives from arbitrary receiver types (e.g., HttpClient -> "http").
    /// </summary>
    private static bool IsKnownConnectorType(string connectorType, SdkIndex? sdkIndex, ConnectionsConfig connections)
    {
        // Always accept types that have configured connections.
        IEnumerable<string> configuredTypes = ConnectionsHelper.GetConnectorTypes(connections);
        if (configuredTypes.Any(type =>
            string.Equals(type, connectorType, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Check against SDK index (most reliable filter for false positives).
        if (sdkIndex is not null)
        {
            return sdkIndex.ConnectorNameConstants.Any(connector =>
                string.Equals(connector.Value, connectorType, StringComparison.OrdinalIgnoreCase));
        }

        // Without SDK index and no matching configured connection, reject to avoid false positives.
        return false;
    }

    /// <summary>
    /// Determines whether a parameter name suggests it is a connection parameter.
    /// Matches patterns like "connectionName", "connection", or names containing "connection".
    /// </summary>
    private static bool IsConnectionParameterName(string parameterName)
    {
        return parameterName.Contains("connection", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Infers the connector type from a method invocation by examining the member-access receiver expression.
    /// For example: <c>office365Client.GetEmailAsync(...)</c> uses <c>office365Client</c> and
    /// <c>new TeamsClient().SendMessage()</c> uses <c>TeamsClient</c> to infer the connector type.
    /// </summary>
    private static string? InferConnectorTypeFromInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            string? receiverIdentifier = ConnectionConfigValidator.TryGetReceiverIdentifier(memberAccess.Expression);
            if (!string.IsNullOrEmpty(receiverIdentifier))
            {
                return DynamicValuesHelper.InferConnectorFromContainingType(receiverIdentifier);
            }
        }

        return null;
    }

    /// <summary>
    /// Tries to extract the most useful identifier text from a receiver expression for connector inference.
    /// Handles simple identifiers, member access, object creation, parenthesized, and conditional access.
    /// </summary>
    private static string? TryGetReceiverIdentifier(ExpressionSyntax expression)
    {
        return expression switch
        {
            MemberAccessExpressionSyntax nestedAccess => nestedAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            ObjectCreationExpressionSyntax objectCreation => ConnectionConfigValidator.TryGetTypeIdentifier(objectCreation.Type),
            ParenthesizedExpressionSyntax parenthesized => ConnectionConfigValidator.TryGetReceiverIdentifier(parenthesized.Expression),
            ConditionalAccessExpressionSyntax conditionalAccess => ConnectionConfigValidator.TryGetReceiverIdentifier(conditionalAccess.Expression),
            _ => null,
        };
    }

    /// <summary>
    /// Tries to extract the rightmost type identifier from a type syntax node.
    /// </summary>
    private static string? TryGetTypeIdentifier(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax genericName => genericName.Identifier.Text,
            QualifiedNameSyntax qualifiedName => ConnectionConfigValidator.TryGetTypeIdentifier(qualifiedName.Right),
            AliasQualifiedNameSyntax aliasQualifiedName => aliasQualifiedName.Name.Identifier.Text,
            NullableTypeSyntax nullableType => ConnectionConfigValidator.TryGetTypeIdentifier(nullableType.ElementType),
            _ => typeSyntax.ToString(),
        };
    }

    /// <summary>
    /// Gets the parameter name for a named argument.
    /// Returns null for positional arguments since resolving parameter names by position
    /// would require a semantic model, which is not available in this validator.
    /// </summary>
    private static string? GetArgumentParameterName(ArgumentSyntax argument)
    {
        // Named argument: the name is explicit.
        if (argument.NameColon is not null)
        {
            return argument.NameColon.Name.Identifier.Text;
        }

        // Positional arguments are not analyzed — without a semantic model
        // we cannot resolve parameter names from position.
        return null;
    }

    /// <summary>
    /// Determines whether an attribute name matches SDK attributes that accept connection parameters.
    /// </summary>
    private static bool IsSdkAttribute(string attributeName)
    {
        return string.Equals(attributeName, "ConnectorTriggerMetadata", StringComparison.Ordinal) ||
               string.Equals(attributeName, "ConnectorTriggerMetadataAttribute", StringComparison.Ordinal) ||
               string.Equals(attributeName, "ConnectorTrigger", StringComparison.Ordinal) ||
               string.Equals(attributeName, "ConnectorTriggerAttribute", StringComparison.Ordinal) ||
               string.Equals(attributeName, "ConnectorOperation", StringComparison.Ordinal) ||
               string.Equals(attributeName, "ConnectorOperationAttribute", StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts the value of a named argument from an attribute.
    /// </summary>
    private static string? ExtractNamedArgumentValue(AttributeSyntax attribute, string parameterName)
    {
        AttributeArgumentSyntax? argument = ConnectionConfigValidator.FindNamedArgument(attribute, parameterName);
        if (argument is null)
        {
            return null;
        }

        // Only analyze string literals to avoid false positives when constants or
        // member access expressions are used (e.g., ConnectorNames.Office365).
        // Without a semantic model, we cannot resolve constant values.
        if (argument.Expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        return null;
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
    /// Extracts the rightmost identifier from a potentially qualified attribute name.
    /// </summary>
    private static string ExtractRightmostIdentifier(string attributeName)
    {
        int lastDot = attributeName.LastIndexOf('.');
        int lastAliasQualifier = attributeName.LastIndexOf("::", StringComparison.Ordinal);

        int afterDot = lastDot >= 0 ? lastDot + 1 : 0;
        int afterAlias = lastAliasQualifier >= 0 ? lastAliasQualifier + 2 : 0;
        int identifierStartIndex = Math.Max(afterDot, afterAlias);

        return identifierStartIndex > 0
            ? attributeName.Substring(identifierStartIndex)
            : attributeName;
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
