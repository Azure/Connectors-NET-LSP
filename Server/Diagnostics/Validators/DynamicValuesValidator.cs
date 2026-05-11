//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using OmniSharp.Extensions.LanguageServer.Protocol;

using SdkLspServer.Services.Connections;
using SdkLspServer.Store;
using SdkLspServer.Store.DynamicData;

using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;

namespace SdkLspServer.Diagnostics.Validators;

/// <summary>
/// Validates call sites that pass string literal arguments to SDK connector client
/// methods whose parameters are annotated with <c>[DynamicValues]</c>.
/// <para>
/// When a string literal is passed to such a parameter and the dynamic values have
/// been fetched (e.g., by hover or completion), the validator checks whether the
/// literal is among the cached values. If not, it emits CSDK300.
/// </para>
/// <para>
/// The validator does <b>not</b> make network calls itself — it reads from the
/// <see cref="LSPStore.DynamicData"/> cache, which is populated by hover and
/// completion handlers. This keeps diagnostic validation fast and offline.
/// </para>
/// <para>
/// <b>Limitation:</b> In multi-connection scenarios the cache lookup only finds
/// values when exactly one connection matches the connector type (auto-resolution).
/// Explicit connection extraction from the invocation is not implemented.
/// </para>
/// Emits diagnostic CSDK300.
/// </summary>
internal sealed class DynamicValuesValidator : IDiagnosticValidator
{
    private const string DynamicValuesAttributeName = "DynamicValuesAttribute";
    private const string DynamicValuesShortName = "DynamicValues";

    private readonly Services.CompilationService compilationService;
    private readonly LSPStore lspStore;
    private readonly ConnectionsService connectionsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicValuesValidator"/> class.
    /// </summary>
    /// <param name="compilationService">The compilation service for semantic analysis.</param>
    /// <param name="lspStore">The LSP store providing cached dynamic values.</param>
    /// <param name="connectionsService">The connections service for connector/connection resolution.</param>
    public DynamicValuesValidator(
        Services.CompilationService compilationService,
        LSPStore lspStore,
        ConnectionsService connectionsService)
    {
        this.compilationService = compilationService ?? throw new ArgumentNullException(nameof(compilationService));
        this.lspStore = lspStore ?? throw new ArgumentNullException(nameof(lspStore));
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

        if (string.IsNullOrWhiteSpace(documentText) || sdkIndex is null)
        {
            return diagnostics;
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(documentText, cancellationToken: cancellationToken);
        CompilationUnitSyntax root = tree.GetCompilationUnitRoot(cancellationToken);
        SourceText sourceText = await tree
            .GetTextAsync(cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        // Resolve file path for NuGet reference discovery.
        string? filePath = string.Equals(documentUri.Scheme, "file", StringComparison.OrdinalIgnoreCase)
            ? documentUri.GetFileSystemPath()
            : null;

        (_, SemanticModel semanticModel) = this.compilationService
            .GetCompilation(documentUri.ToUri(), tree, filePath);

        // Memoize cached value lookups per (connector, operation) to avoid
        // redundant connection resolution and cache hits on each invocation.
        var cachedValuesLookup = new Dictionary<(string Connector, string Operation), List<DynamicValueItem>?>();

        foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.ValidateInvocation(invocation, semanticModel, sourceText, cachedValuesLookup, cancellationToken, diagnostics);
        }

        return diagnostics;
    }

    /// <summary>
    /// Validates a single invocation expression by resolving its method symbol
    /// and checking whether any arguments correspond to <c>[DynamicValues]</c>-annotated parameters.
    /// When cached dynamic values exist, validates that string literals match a known value.
    /// </summary>
    private void ValidateInvocation(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        SourceText sourceText,
        Dictionary<(string Connector, string Operation), List<DynamicValueItem>?> cachedValuesLookup,
        CancellationToken cancellationToken,
        List<LspDiagnostic> diagnostics)
    {
        SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        IMethodSymbol? methodSymbol = symbolInfo.Symbol as IMethodSymbol;

        // Fall back to candidate symbols only when there is exactly one candidate
        // to avoid selecting an arbitrary overload from ambiguous resolution.
        if (methodSymbol is null &&
            symbolInfo.CandidateSymbols.Length == 1 &&
            symbolInfo.CandidateSymbols[0] is IMethodSymbol singleCandidate)
        {
            methodSymbol = singleCandidate;
        }

        if (methodSymbol is null)
        {
            return;
        }

        // Only inspect methods on types from the Connectors SDK.
        // Check both namespace and assembly name. Source-defined types under
        // Azure.Connectors.Sdk.* are intentionally included because the test
        // infrastructure defines mock SDK types inline for unit testing.
        string? containingNamespace = methodSymbol.ContainingType?.ContainingNamespace?.ToDisplayString();
        string? containingAssembly = methodSymbol.ContainingAssembly?.Name;
        if (containingNamespace is null ||
            !containingNamespace.StartsWith("Azure.Connectors.Sdk", StringComparison.Ordinal))
        {
            return;
        }

        // When the symbol comes from source (same compilation), the assembly name
        // matches the compilation's own assembly. Only filter on assembly when
        // the symbol is from a metadata reference (i.e., a real SDK DLL).
        string? compilationAssembly = semanticModel.Compilation.AssemblyName;
        if (containingAssembly is not null &&
            !string.Equals(containingAssembly, compilationAssembly, StringComparison.Ordinal) &&
            !containingAssembly.StartsWith("Azure.Connectors.Sdk", StringComparison.Ordinal))
        {
            return;
        }

        // Infer the connector name from the containing type (e.g., SharePointOnlineClient → sharepointonline).
        string? connectorName = DynamicValuesHelper.InferConnectorFromContainingType(
            methodSymbol.ContainingType?.Name);

        // Short-circuit when connector inference fails — cache lookup requires a connector name.
        if (string.IsNullOrEmpty(connectorName))
        {
            return;
        }

        SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;

        foreach (IParameterSymbol parameter in methodSymbol.Parameters)
        {
            (bool hasDynamicValues, string? operationId) = DynamicValuesValidator.GetDynamicValuesAttribute(parameter);
            if (!hasDynamicValues || string.IsNullOrEmpty(operationId))
            {
                continue;
            }

            // Find the argument that maps to this parameter (positional or named).
            ArgumentSyntax? argument = DynamicValuesValidator.FindArgumentForParameter(
                arguments, parameter);

            if (argument is null)
            {
                continue;
            }

            // Only validate string literal arguments — variables, method calls, etc.
            // are not statically analyzable.
            if (argument.Expression is not LiteralExpressionSyntax literal ||
                !literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                continue;
            }

            // CSDK300: String literal does not match any cached dynamic value.
            // Only emit when we have cached values to check against.
            string literalValue = literal.Token.ValueText;
            var cacheKey = (connectorName, operationId);
            if (!cachedValuesLookup.TryGetValue(cacheKey, out List<DynamicValueItem>? cachedValues))
            {
                cachedValues = this.TryGetCachedValues(connectorName, operationId);
                cachedValuesLookup[cacheKey] = cachedValues;
            }

            if (cachedValues is null || cachedValues.Count == 0)
            {
                // No cached values — skip. The hover or completion handler will
                // populate the cache when the user interacts with the parameter.
                continue;
            }

            bool isValid = cachedValues.Any(item =>
                DynamicValuesValidator.ValuesMatch(item.Value, literalValue));

            if (!isValid)
            {
                diagnostics.Add(ValidatorHelpers.CreateDiagnostic(
                    ValidatorHelpers.ToLspRange(literal.Span, sourceText),
                    LspDiagnosticSeverity.Warning,
                    DiagnosticCodes.DynamicValuesInvalidValue,
                    $"'{literalValue}' is not a valid value for '{methodSymbol.Name}' parameter '{parameter.Name}' (operation: '{operationId}'). Use IntelliSense to select from available values."));
            }
        }
    }

    /// <summary>
    /// Attempts to retrieve cached dynamic values for the given connector and operation.
    /// Tries auto-resolved connection name via <see cref="DynamicValuesHelper.ResolveConnectionByConnectorType"/>.
    /// <para>
    /// <b>Limitation:</b> Only auto-resolves when exactly one connection matches the
    /// connector type. Multi-connection scenarios (where the call site explicitly passes
    /// a connection name) are not handled — the validator will not find cached values
    /// and will skip validation (no false positives, but also no detection).
    /// </para>
    /// </summary>
    private List<DynamicValueItem>? TryGetCachedValues(string? connectorName, string operationId)
    {
        if (string.IsNullOrEmpty(connectorName))
        {
            return null;
        }

        // Resolve the connection name the same way hover does — auto-resolve when
        // exactly one connection matches the connector type.
        string? connectionName = DynamicValuesHelper.ResolveConnectionByConnectorType(
            this.connectionsService, connectorName);

        if (string.IsNullOrEmpty(connectionName))
        {
            return null;
        }

        return this.lspStore.DynamicData.Get(connectorName, operationId, connectionName);
    }

    /// <summary>
    /// Compares a cached value against a literal value, stripping surrounding quotes
    /// from the cached value if present. The hover handler stores values with literal
    /// quote characters for code insertion (e.g., <c>"\"siteUrl\""</c>), but
    /// <see cref="Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax"/>
    /// <c>Token.ValueText</c> returns the unquoted content.
    /// </summary>
    private static bool ValuesMatch(string cachedValue, string literalValue)
    {
        if (string.IsNullOrEmpty(cachedValue))
        {
            return false;
        }

        // Strip surrounding quotes if the cached value is wrapped (e.g., "\"value\"" → "value").
        // NOTE: Dynamic values from the API are plain strings (site URLs, list names, etc.)
        // wrapped in literal quote characters for code insertion. They do not contain C#
        // escape sequences (\\, \\\", etc.) so simple quote stripping is sufficient.
        string normalizedCached = cachedValue.Length >= 2 &&
            cachedValue[0] == '"' &&
            cachedValue[cachedValue.Length - 1] == '"'
                ? cachedValue.Substring(1, cachedValue.Length - 2)
                : cachedValue;

        return string.Equals(normalizedCached, literalValue, StringComparison.Ordinal);
    }

    /// <summary>
    /// Checks whether a parameter has the <c>[DynamicValues]</c> attribute and extracts the operation ID.
    /// <para>
    /// Matches by short name (<c>DynamicValuesAttribute</c> / <c>DynamicValues</c>) rather than
    /// fully-qualified name. This is safe because the containing method is already filtered to
    /// <c>Azure.Connectors.Sdk.*</c> namespace + assembly, making a same-named attribute from
    /// another namespace extremely unlikely on these parameters.
    /// </para>
    /// </summary>
    private static (bool HasDynamicValues, string? OperationId) GetDynamicValuesAttribute(IParameterSymbol parameter)
    {
        foreach (AttributeData attribute in parameter.GetAttributes())
        {
            string attrName = attribute.AttributeClass?.Name ?? string.Empty;

            if (string.Equals(attrName, DynamicValuesValidator.DynamicValuesAttributeName, StringComparison.Ordinal) ||
                string.Equals(attrName, DynamicValuesValidator.DynamicValuesShortName, StringComparison.Ordinal))
            {
                string? operationId = attribute.ConstructorArguments.Length > 0
                    ? attribute.ConstructorArguments[0].Value?.ToString()
                    : null;

                return (true, operationId);
            }
        }

        return (false, null);
    }

    /// <summary>
    /// Finds the argument syntax node that maps to the given parameter,
    /// handling both positional and named argument styles.
    /// </summary>
    private static ArgumentSyntax? FindArgumentForParameter(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        IParameterSymbol parameter)
    {
        // Check for a named argument first.
        foreach (ArgumentSyntax argument in arguments)
        {
            if (argument.NameColon is not null &&
                string.Equals(argument.NameColon.Name.Identifier.ValueText, parameter.Name, StringComparison.Ordinal))
            {
                return argument;
            }
        }

        // Fall back to positional matching.
        int ordinal = parameter.Ordinal;
        if (ordinal >= 0 && ordinal < arguments.Count)
        {
            // Only use positional match if the argument at this position is not a named argument
            // for a different parameter.
            ArgumentSyntax candidate = arguments[ordinal];
            if (candidate.NameColon is null)
            {
                return candidate;
            }
        }

        return null;
    }
}
