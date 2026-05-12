//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using OmniSharp.Extensions.LanguageServer.Protocol;

using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;

namespace SdkLspServer.Diagnostics.Validators;

/// <summary>
/// Detects common anti-patterns in SDK usage code.
/// Emits diagnostics CSDK401–CSDK405.
/// <list type="bullet">
/// <item>CSDK401 — <c>[ConnectorOperation]</c> attribute value doesn't match any known operation.</item>
/// <item>CSDK402 — <c>*Input</c> type used where <c>*Output</c> is expected (or vice versa).</item>
/// <item>CSDK403 — Catching <c>ConnectorException</c> without checking <c>StatusCode</c>.</item>
/// <item>CSDK404 — Async connector method called without <c>await</c>.</item>
/// <item>CSDK405 — <c>CancellationToken</c> available but not passed to connector API call.</item>
/// </list>
/// </summary>
internal sealed class SdkAntiPatternValidator : IDiagnosticValidator
{
    private readonly Services.CompilationService compilationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SdkAntiPatternValidator"/> class.
    /// </summary>
    /// <param name="compilationService">The compilation service for semantic analysis.</param>
    public SdkAntiPatternValidator(Services.CompilationService compilationService)
    {
        this.compilationService = compilationService ?? throw new ArgumentNullException(nameof(compilationService));
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

        SyntaxTree tree = CSharpSyntaxTree.ParseText(documentText, cancellationToken: cancellationToken);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot(cancellationToken);
        SourceText sourceText = await tree
            .GetTextAsync(cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Syntax-only checks (no compilation needed)
        SdkAntiPatternValidator.CheckConnectorOperationValues(root, sourceText, sdkIndex, cancellationToken, diagnostics);
        SdkAntiPatternValidator.CheckPayloadTypeDirection(root, sourceText, sdkIndex, cancellationToken, diagnostics);
        SdkAntiPatternValidator.CheckConnectorExceptionHandling(root, sourceText, cancellationToken, diagnostics);

        // Semantic checks (require compilation)
        string? filePath = string.Equals(documentUri.Scheme, "file", StringComparison.OrdinalIgnoreCase)
            ? documentUri.GetFileSystemPath()
            : null;

        (_, SemanticModel semanticModel) = this.compilationService
            .GetCompilation(documentUri.ToUri(), tree, filePath);

        SdkAntiPatternValidator.CheckAsyncWithoutAwait(root, sourceText, semanticModel, cancellationToken, diagnostics);
        SdkAntiPatternValidator.CheckCancellationTokenNotPassed(root, sourceText, semanticModel, cancellationToken, diagnostics);

        return diagnostics;
    }

    /// <summary>
    /// CSDK401: Checks <c>[ConnectorOperation]</c> attribute operation values against
    /// known operations in the SDK index. When a ConnectorName is present on the
    /// attribute, validates against that connector's operations first; falls back
    /// to all operations only when ConnectorName is absent or unresolvable.
    /// Also handles positional arguments with precise diagnostic range placement.
    /// </summary>
    private static void CheckConnectorOperationValues(
        CompilationUnitSyntax root,
        SourceText sourceText,
        SdkIndex? sdkIndex,
        CancellationToken cancellationToken,
        List<LspDiagnostic> diagnostics)
    {
        if (sdkIndex is null)
        {
            return;
        }

        foreach (MethodDeclarationSyntax method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            foreach (AttributeListSyntax attributeList in method.AttributeLists)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (AttributeSyntax attribute in attributeList.Attributes)
                {
                    string attributeName = attribute.Name.ToString();
                    string identifier = ValidatorHelpers.ExtractRightmostIdentifier(attributeName);

                    if (!string.Equals(identifier, "ConnectorOperation", StringComparison.Ordinal) &&
                        !string.Equals(identifier, "ConnectorOperationAttribute", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    (string? operationName, AttributeArgumentSyntax? operationArgument) =
                        SdkAntiPatternValidator.GetOperationNameAndArgumentFromAttribute(attribute);

                    if (operationName is null)
                    {
                        continue;
                    }

                    // Try connector-scoped validation first when ConnectorName is present.
                    string? connectorName = SdkAntiPatternValidator.GetConnectorNameFromAttribute(attribute);

                    // Resolve constant-style ConnectorName (e.g., "Office365" from
                    // ConnectorNames.Office365) to the canonical value ("office365")
                    // using the SDK index's ConnectorNameConstants.
                    if (connectorName is not null)
                    {
                        SdkConstant? matchedConnector = sdkIndex.ConnectorNameConstants
                            .FirstOrDefault(connector =>
                                string.Equals(connector.FieldName, connectorName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(connector.Value, connectorName, StringComparison.OrdinalIgnoreCase));

                        if (matchedConnector is not null)
                        {
                            connectorName = matchedConnector.Value;
                        }
                    }

                    bool found;
                    string message;

                    if (connectorName is not null)
                    {
                        var connectorOperations = sdkIndex.GetTriggerOperations(connectorName);
                        found = connectorOperations.Any(operation =>
                            string.Equals(operation.Value, operationName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(operation.FieldName, operationName, StringComparison.OrdinalIgnoreCase));

                        if (!found)
                        {
                            // Check if it exists in a different connector for a more helpful message.
                            bool foundInOther = sdkIndex.GetAllTriggerOperations().Any(operation =>
                                string.Equals(operation.Value, operationName, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(operation.FieldName, operationName, StringComparison.OrdinalIgnoreCase));

                            message = foundInOther
                                ? $"Operation '{operationName}' exists in the SDK but does not belong to connector '{connectorName}'."
                                : $"Operation '{operationName}' does not match any known connector operation in the SDK index.";
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        found = sdkIndex.GetAllTriggerOperations().Any(operation =>
                            string.Equals(operation.Value, operationName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(operation.FieldName, operationName, StringComparison.OrdinalIgnoreCase));

                        if (found)
                        {
                            continue;
                        }

                        message = $"Operation '{operationName}' does not match any known connector operation in the SDK index.";
                    }

                    var range = operationArgument is not null
                        ? ValidatorHelpers.GetArgumentValueRange(operationArgument, sourceText)
                        : ValidatorHelpers.GetAttributeNameRange(attribute, sourceText);

                    diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
                        range,
                        LspDiagnosticSeverity.Warning,
                        DiagnosticCodes.ConnectorOperationValueUnknown,
                        message));
                }
            }
        }
    }

    /// <summary>
    /// CSDK402: Detects input/output type direction mismatches.
    /// Warns when an <c>*Input</c> type is used to receive an <c>await</c> result
    /// (which typically returns an output type), if a corresponding <c>*Output</c>
    /// type exists in the SDK index.
    /// </summary>
    private static void CheckPayloadTypeDirection(
        CompilationUnitSyntax root,
        SourceText sourceText,
        SdkIndex? sdkIndex,
        CancellationToken cancellationToken,
        List<LspDiagnostic> diagnostics)
    {
        if (sdkIndex is null)
        {
            return;
        }

        foreach (LocalDeclarationStatementSyntax localDecl in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            TypeSyntax typeSyntax = localDecl.Declaration.Type;

            // Skip 'var' declarations — can't determine type name from syntax alone.
            if (typeSyntax is IdentifierNameSyntax identifierName &&
                string.Equals(identifierName.Identifier.Text, "var", StringComparison.Ordinal))
            {
                continue;
            }

            // Unwrap nullable, qualified, and alias-qualified type syntax nodes
            // to extract the simple type name (handles T?, Ns.T, global::T).
            string simpleTypeName = SdkAntiPatternValidator.GetSimpleTypeName(typeSyntax);

            if (!simpleTypeName.EndsWith("Input", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (VariableDeclaratorSyntax variable in localDecl.Declaration.Variables)
            {
                if (variable.Initializer?.Value is AwaitExpressionSyntax)
                {
                    string suggestedType = simpleTypeName.Substring(0, simpleTypeName.Length - "Input".Length) + "Output";

                    if (sdkIndex.TypeNameLookup.Contains(suggestedType))
                    {
                        diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
                            ValidatorHelpers.ToLspRange(typeSyntax.Span, sourceText),
                            LspDiagnosticSeverity.Information,
                            DiagnosticCodes.WrongPayloadTypeDirection,
                            $"Type '{simpleTypeName}' appears to be an input type but is used to receive an async result. Did you mean '{suggestedType}'?"));
                    }

                    // One diagnostic per declaration is sufficient — the type span
                    // is shared across all variables in the declaration.
                    break;
                }
            }
        }
    }

    /// <summary>
    /// CSDK403: Detects <c>catch (ConnectorException)</c> blocks that do not reference
    /// <c>StatusCode</c> on the exception variable.
    /// </summary>
    private static void CheckConnectorExceptionHandling(
        CompilationUnitSyntax root,
        SourceText sourceText,
        CancellationToken cancellationToken,
        List<LspDiagnostic> diagnostics)
    {
        foreach (CatchClauseSyntax catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (catchClause.Declaration is null)
            {
                continue;
            }

            string typeName = catchClause.Declaration.Type.ToString();

            // Extract the simple type name using the shared helper that handles
            // qualified names (Ns.Type), alias-qualified (global::Type), and
            // double-colon forms.
            string simpleTypeName = ValidatorHelpers.ExtractRightmostIdentifier(typeName);

            if (!string.Equals(simpleTypeName, "ConnectorException", StringComparison.Ordinal))
            {
                continue;
            }

            string exceptionVariableName = catchClause.Declaration.Identifier.ValueText;

            if (string.IsNullOrEmpty(exceptionVariableName))
            {
                continue;
            }

            // Check both regular member access (ex.StatusCode) and conditional
            // access (ex?.StatusCode) so that null-safe patterns are recognized.
            // Also check the catch filter expression (catch ... when (ex.StatusCode == ...)).
            bool referencesStatusCode = SdkAntiPatternValidator.BlockReferencesStatusCode(
                catchClause.Block, exceptionVariableName);

            if (!referencesStatusCode && catchClause.Filter?.FilterExpression is not null)
            {
                referencesStatusCode = SdkAntiPatternValidator.ExpressionReferencesStatusCode(
                    catchClause.Filter.FilterExpression, exceptionVariableName);
            }

            if (!referencesStatusCode)
            {
                diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
                    ValidatorHelpers.ToLspRange(catchClause.Declaration.Span, sourceText),
                    LspDiagnosticSeverity.Warning,
                    DiagnosticCodes.ConnectorExceptionWithoutStatusCode,
                    $"Catching ConnectorException without checking StatusCode. Consider inspecting '{exceptionVariableName}.StatusCode' for error-specific handling."));
            }
        }
    }

    /// <summary>
    /// CSDK404: Detects async connector methods called without <c>await</c>.
    /// Only catches fire-and-forget expression statements (not assignments to variables).
    /// Uses semantic analysis to verify the method returns <c>Task</c> or <c>ValueTask</c>.
    /// </summary>
    private static void CheckAsyncWithoutAwait(
        CompilationUnitSyntax root,
        SourceText sourceText,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        List<LspDiagnostic> diagnostics)
    {
        foreach (ExpressionStatementSyntax expressionStatement in root.DescendantNodes().OfType<ExpressionStatementSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // If the expression is an await, the invocation is properly awaited.
            if (expressionStatement.Expression is AwaitExpressionSyntax)
            {
                continue;
            }

            if (expressionStatement.Expression is not InvocationExpressionSyntax invocation)
            {
                continue;
            }

            // Unwrap chained invocations like
            // client.SendEmailAsync(...).ConfigureAwait(continueOnCapturedContext: false)
            // to find the underlying connector SDK method call.
            InvocationExpressionSyntax connectorInvocation = SdkAntiPatternValidator.UnwrapChainedInvocation(
                invocation);

            IMethodSymbol? methodSymbol = SdkAntiPatternValidator.ResolveMethodSymbol(
                connectorInvocation, semanticModel, cancellationToken);

            if (methodSymbol is null)
            {
                continue;
            }

            if (!SdkAntiPatternValidator.IsConnectorSdkMethod(methodSymbol, semanticModel))
            {
                continue;
            }

            string returnTypeName = methodSymbol.ReturnType.Name;

            if (!string.Equals(returnTypeName, "Task", StringComparison.Ordinal) &&
                !string.Equals(returnTypeName, "ValueTask", StringComparison.Ordinal))
            {
                continue;
            }

            diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
                ValidatorHelpers.ToLspRange(invocation.Span, sourceText),
                LspDiagnosticSeverity.Warning,
                DiagnosticCodes.AsyncConnectorCallWithoutAwait,
                $"Async connector method '{methodSymbol.Name}' called without 'await'. The result will be discarded and errors may go unobserved."));
        }
    }

    /// <summary>
    /// CSDK405: Detects connector API calls within methods that have a
    /// <c>CancellationToken</c> parameter, where the token is not forwarded.
    /// </summary>
    private static void CheckCancellationTokenNotPassed(
        CompilationUnitSyntax root,
        SourceText sourceText,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        List<LspDiagnostic> diagnostics)
    {
        foreach (MethodDeclarationSyntax method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Find CancellationToken parameters using the semantic model for
            // reliable resolution (handles aliases, global:: qualification, etc.).
            string? cancellationTokenParamName = SdkAntiPatternValidator.FindCancellationTokenParameterName(
                method, semanticModel, cancellationToken);

            if (cancellationTokenParamName is null)
            {
                continue;
            }

            // Collect invocations from body or expression body.
            IEnumerable<SyntaxNode> bodyNodes = method.Body?.DescendantNodes()
                ?? method.ExpressionBody?.DescendantNodes()
                ?? Enumerable.Empty<SyntaxNode>();

            foreach (InvocationExpressionSyntax invocation in bodyNodes.OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                IMethodSymbol? methodSymbol = SdkAntiPatternValidator.ResolveMethodSymbol(
                    invocation, semanticModel, cancellationToken);

                if (methodSymbol is null)
                {
                    continue;
                }

                if (!SdkAntiPatternValidator.IsConnectorSdkMethod(methodSymbol, semanticModel))
                {
                    continue;
                }

                // Check if the SDK method has a CancellationToken parameter.
                bool methodAcceptsCancellationToken = methodSymbol.Parameters.Any(parameter =>
                    string.Equals(parameter.Type.Name, "CancellationToken", StringComparison.Ordinal) &&
                    string.Equals(
                        parameter.Type.ContainingNamespace?.ToDisplayString(),
                        "System.Threading",
                        StringComparison.Ordinal));

                if (!methodAcceptsCancellationToken)
                {
                    continue;
                }

                // Check if any argument passes the cancellation token.
                bool passesCancellationToken = invocation.ArgumentList.Arguments.Any(argument =>
                    argument.Expression is IdentifierNameSyntax identifier &&
                    string.Equals(identifier.Identifier.Text, cancellationTokenParamName, StringComparison.Ordinal));

                if (!passesCancellationToken)
                {
                    diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
                        ValidatorHelpers.ToLspRange(invocation.Span, sourceText),
                        LspDiagnosticSeverity.Warning,
                        DiagnosticCodes.CancellationTokenNotPassed,
                        $"Connector method '{methodSymbol.Name}' accepts a CancellationToken but none was passed. Consider forwarding '{cancellationTokenParamName}'."));
                }
            }
        }
    }

    /// <summary>
    /// Extracts the operation name and corresponding argument syntax from a
    /// <c>[ConnectorOperation]</c> attribute, checking the named <c>OperationName</c>
    /// argument first, then falling back to the first positional argument.
    /// Returns both the value and the argument node for precise diagnostic placement.
    /// </summary>
    private static (string? OperationName, AttributeArgumentSyntax? Argument) GetOperationNameAndArgumentFromAttribute(
        AttributeSyntax attribute)
    {
        AttributeArgumentSyntax? namedArgument = ValidatorHelpers.FindNamedArgument(attribute, "OperationName");

        if (namedArgument is not null)
        {
            return (ValidatorHelpers.ExtractStringValue(namedArgument), namedArgument);
        }

        // Fall back to first positional argument.
        if (attribute.ArgumentList is not null &&
            attribute.ArgumentList.Arguments.Count > 0)
        {
            AttributeArgumentSyntax first = attribute.ArgumentList.Arguments[0];

            if (first.NameEquals is null && first.NameColon is null)
            {
                return (ValidatorHelpers.ExtractStringValue(first), first);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Extracts the ConnectorName value from a <c>[ConnectorOperation]</c> attribute
    /// when present as a named argument.
    /// </summary>
    private static string? GetConnectorNameFromAttribute(AttributeSyntax attribute)
    {
        AttributeArgumentSyntax? connectorNameArgument = ValidatorHelpers.FindNamedArgument(attribute, "ConnectorName");

        if (connectorNameArgument is null)
        {
            return null;
        }

        return ValidatorHelpers.ExtractStringValue(connectorNameArgument);
    }

    /// <summary>
    /// Unwraps chained invocations to find the innermost invocation expression.
    /// For example, <c>client.SendEmailAsync(...).ConfigureAwait(continueOnCapturedContext: false)</c> resolves
    /// to the <c>client.SendEmailAsync(...)</c> invocation so the SDK method symbol
    /// can be resolved instead of <c>ConfigureAwait</c>.
    /// </summary>
    private static InvocationExpressionSyntax UnwrapChainedInvocation(InvocationExpressionSyntax invocation)
    {
        InvocationExpressionSyntax current = invocation;

        // Walk down member-access chains: outer.Method() where outer is itself
        // an invocation (i.e., inner.SdkMethod().ConfigureAwait(...)).
        while (current.Expression is MemberAccessExpressionSyntax memberAccess &&
               memberAccess.Expression is InvocationExpressionSyntax innerInvocation)
        {
            current = innerInvocation;
        }

        return current;
    }

    /// <summary>
    /// Extracts the simple (unqualified, non-nullable) type name from a type syntax node.
    /// Handles <c>Nullable&lt;T&gt;</c> (<c>T?</c>), qualified names (<c>Ns.T</c>),
    /// alias-qualified names (<c>global::T</c>), and generic names (<c>T&lt;U&gt;</c>).
    /// </summary>
    private static string GetSimpleTypeName(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            NullableTypeSyntax nullable => SdkAntiPatternValidator.GetSimpleTypeName(nullable.ElementType),
            QualifiedNameSyntax qualified => SdkAntiPatternValidator.GetSimpleTypeName(qualified.Right),
            AliasQualifiedNameSyntax aliasQualified => SdkAntiPatternValidator.GetSimpleTypeName(aliasQualified.Name),
            GenericNameSyntax generic => generic.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => typeSyntax.ToString(),
        };
    }

    /// <summary>
    /// Checks whether a catch block references <c>StatusCode</c> on the exception variable,
    /// handling both regular member access (<c>ex.StatusCode</c>) and conditional access
    /// (<c>ex?.StatusCode</c>).
    /// </summary>
    private static bool BlockReferencesStatusCode(BlockSyntax block, string exceptionVariableName)
    {
        foreach (SyntaxNode node in block.DescendantNodes())
        {
            // Regular: ex.StatusCode
            if (node is MemberAccessExpressionSyntax memberAccess &&
                string.Equals(memberAccess.Name.Identifier.Text, "StatusCode", StringComparison.Ordinal) &&
                memberAccess.Expression is IdentifierNameSyntax memberIdentifier &&
                string.Equals(memberIdentifier.Identifier.Text, exceptionVariableName, StringComparison.Ordinal))
            {
                return true;
            }

            // Conditional: ex?.StatusCode
            if (node is ConditionalAccessExpressionSyntax conditionalAccess &&
                conditionalAccess.Expression is IdentifierNameSyntax conditionalIdentifier &&
                string.Equals(conditionalIdentifier.Identifier.Text, exceptionVariableName, StringComparison.Ordinal) &&
                conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding &&
                string.Equals(memberBinding.Name.Identifier.Text, "StatusCode", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether an expression syntax node references <c>StatusCode</c> on the
    /// exception variable. Used to scan catch filter expressions
    /// (<c>catch (ConnectorException ex) when (ex.StatusCode == ...)</c>).
    /// </summary>
    private static bool ExpressionReferencesStatusCode(ExpressionSyntax expression, string exceptionVariableName)
    {
        foreach (SyntaxNode node in expression.DescendantNodesAndSelf())
        {
            if (node is MemberAccessExpressionSyntax memberAccess &&
                string.Equals(memberAccess.Name.Identifier.Text, "StatusCode", StringComparison.Ordinal) &&
                memberAccess.Expression is IdentifierNameSyntax identifier &&
                string.Equals(identifier.Identifier.Text, exceptionVariableName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Uses the semantic model to find a <c>CancellationToken</c> parameter in the
    /// given method declaration. More reliable than syntax-string checks because it
    /// handles aliases, <c>global::</c> qualification, and fully-qualified type names.
    /// </summary>
    private static string? FindCancellationTokenParameterName(
        MethodDeclarationSyntax method,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        IMethodSymbol? methodSymbol = semanticModel.GetDeclaredSymbol(method, cancellationToken);

        if (methodSymbol is null)
        {
            return null;
        }

        foreach (IParameterSymbol parameter in methodSymbol.Parameters)
        {
            if (string.Equals(parameter.Type.Name, "CancellationToken", StringComparison.Ordinal) &&
                string.Equals(
                    parameter.Type.ContainingNamespace?.ToDisplayString(),
                    "System.Threading",
                    StringComparison.Ordinal))
            {
                return parameter.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the <see cref="IMethodSymbol"/> from an invocation expression,
    /// falling back to a single candidate symbol when exact resolution fails.
    /// </summary>
    private static IMethodSymbol? ResolveMethodSymbol(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);

        if (symbolInfo.Symbol is IMethodSymbol methodSymbol)
        {
            return methodSymbol;
        }

        if (symbolInfo.CandidateSymbols.Length == 1 &&
            symbolInfo.CandidateSymbols[0] is IMethodSymbol singleCandidate)
        {
            return singleCandidate;
        }

        return null;
    }

    /// <summary>
    /// Determines whether the given method symbol belongs to a connector SDK type.
    /// Checks the containing type's namespace and, for metadata references, the assembly name.
    /// </summary>
    private static bool IsConnectorSdkMethod(IMethodSymbol methodSymbol, SemanticModel semanticModel)
    {
        string? containingNamespace = methodSymbol.ContainingType?.ContainingNamespace?.ToDisplayString();

        if (containingNamespace is null ||
            !containingNamespace.StartsWith("Azure.Connectors.Sdk", StringComparison.Ordinal))
        {
            return false;
        }

        string? containingAssembly = methodSymbol.ContainingAssembly?.Name;
        string? compilationAssembly = semanticModel.Compilation.AssemblyName;

        if (containingAssembly is not null &&
            !string.Equals(containingAssembly, compilationAssembly, StringComparison.Ordinal) &&
            !containingAssembly.StartsWith("Azure.Connectors.Sdk", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }
}
