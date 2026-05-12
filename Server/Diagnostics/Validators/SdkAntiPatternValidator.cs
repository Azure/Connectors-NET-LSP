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
        SdkAntiPatternValidator.CheckConnectorOperationValues(root, sourceText, sdkIndex, diagnostics);
        SdkAntiPatternValidator.CheckPayloadTypeDirection(root, sourceText, sdkIndex, diagnostics);
        SdkAntiPatternValidator.CheckConnectorExceptionHandling(root, sourceText, diagnostics);

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
    /// known operations in the SDK index. Unlike CSDK009 in <see cref="AttributeValidator"/>,
    /// this also handles positional arguments and checks independently of connector name
    /// resolution.
    /// </summary>
    private static void CheckConnectorOperationValues(
        CompilationUnitSyntax root,
        SourceText sourceText,
        SdkIndex? sdkIndex,
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
                foreach (AttributeSyntax attribute in attributeList.Attributes)
                {
                    string attributeName = attribute.Name.ToString();
                    string identifier = ValidatorHelpers.ExtractRightmostIdentifier(attributeName);

                    if (!string.Equals(identifier, "ConnectorOperation", StringComparison.Ordinal) &&
                        !string.Equals(identifier, "ConnectorOperationAttribute", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string? operationName = SdkAntiPatternValidator.GetOperationNameFromAttribute(attribute);

                    if (operationName is null)
                    {
                        continue;
                    }

                    bool found = sdkIndex.GetAllTriggerOperations().Any(operation =>
                        string.Equals(operation.Value, operationName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(operation.FieldName, operationName, StringComparison.OrdinalIgnoreCase));

                    if (!found)
                    {
                        AttributeArgumentSyntax? operationArgument = ValidatorHelpers.FindNamedArgument(attribute, "OperationName");
                        var range = operationArgument is not null
                            ? ValidatorHelpers.GetArgumentValueRange(operationArgument, sourceText)
                            : ValidatorHelpers.GetAttributeNameRange(attribute, sourceText);

                        diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
                            range,
                            LspDiagnosticSeverity.Warning,
                            DiagnosticCodes.ConnectorOperationValueUnknown,
                            $"Operation '{operationName}' does not match any known connector operation in the SDK index."));
                    }
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
        List<LspDiagnostic> diagnostics)
    {
        if (sdkIndex is null)
        {
            return;
        }

        foreach (LocalDeclarationStatementSyntax localDecl in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            TypeSyntax typeSyntax = localDecl.Declaration.Type;

            // Skip 'var' declarations — can't determine type name from syntax alone.
            if (typeSyntax is IdentifierNameSyntax identifierName &&
                string.Equals(identifierName.Identifier.Text, "var", StringComparison.Ordinal))
            {
                continue;
            }

            string typeName = typeSyntax.ToString();

            // Get simple type name (handle qualified names)
            int lastDot = typeName.LastIndexOf('.');
            string simpleTypeName = lastDot >= 0 ? typeName.Substring(lastDot + 1) : typeName;

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
        List<LspDiagnostic> diagnostics)
    {
        foreach (CatchClauseSyntax catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
        {
            if (catchClause.Declaration is null)
            {
                continue;
            }

            string typeName = catchClause.Declaration.Type.ToString();

            // Extract the simple type name from potentially qualified names
            int lastDot = typeName.LastIndexOf('.');
            string simpleTypeName = lastDot >= 0 ? typeName.Substring(lastDot + 1) : typeName;

            if (!string.Equals(simpleTypeName, "ConnectorException", StringComparison.Ordinal))
            {
                continue;
            }

            string exceptionVariableName = catchClause.Declaration.Identifier.ValueText;

            if (string.IsNullOrEmpty(exceptionVariableName))
            {
                continue;
            }

            bool referencesStatusCode = catchClause.Block.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Any(access =>
                    string.Equals(access.Name.Identifier.Text, "StatusCode", StringComparison.Ordinal) &&
                    access.Expression is IdentifierNameSyntax identifier &&
                    string.Equals(identifier.Identifier.Text, exceptionVariableName, StringComparison.Ordinal));

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

            // Find CancellationToken parameters in this method.
            string? cancellationTokenParamName = method.ParameterList.Parameters
                .Where(parameter => SdkAntiPatternValidator.IsCancellationTokenType(parameter.Type))
                .Select(parameter => parameter.Identifier.Text)
                .FirstOrDefault();

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
                    string.Equals(parameter.Type.Name, "CancellationToken", StringComparison.Ordinal));

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
    /// Extracts the operation name from a <c>[ConnectorOperation]</c> attribute,
    /// checking the named <c>OperationName</c> argument first, then falling back
    /// to the first positional argument.
    /// </summary>
    private static string? GetOperationNameFromAttribute(AttributeSyntax attribute)
    {
        AttributeArgumentSyntax? namedArgument = ValidatorHelpers.FindNamedArgument(attribute, "OperationName");

        if (namedArgument is not null)
        {
            return ValidatorHelpers.ExtractStringValue(namedArgument);
        }

        // Fall back to first positional argument.
        if (attribute.ArgumentList is not null &&
            attribute.ArgumentList.Arguments.Count > 0)
        {
            AttributeArgumentSyntax first = attribute.ArgumentList.Arguments[0];

            if (first.NameEquals is null && first.NameColon is null)
            {
                return ValidatorHelpers.ExtractStringValue(first);
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

    /// <summary>
    /// Determines whether a type syntax node represents <c>CancellationToken</c>.
    /// </summary>
    private static bool IsCancellationTokenType(TypeSyntax? type)
    {
        if (type is null)
        {
            return false;
        }

        string typeName = type.ToString();

        return string.Equals(typeName, "CancellationToken", StringComparison.Ordinal) ||
               typeName.EndsWith(".CancellationToken", StringComparison.Ordinal);
    }
}
