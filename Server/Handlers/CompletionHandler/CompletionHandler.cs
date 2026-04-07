using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using SdkLspServer.Handlers.HoverHandler;
using SdkLspServer.Services.Api;
using SdkLspServer.Services.Connections;
using SdkLspServer.Services.Telemetry;
using SdkLspServer.Store;
using SdkLspServer.Store.DynamicData;

namespace SdkLspServer.Handlers.CompletionHandler;

/// <summary>
/// Provides completion items for SDK usage. In particular, when the user types
/// "GetManagedConnectors()." this handler suggests available connector names
/// (e.g., Msnweather) based on the SDK assemblies loaded via <see cref="SdkIndex"/>.
/// Can also make dynamic API calls to fetch additional completion suggestions.
/// </summary>
public class CompletionHandler(SdkIndex? sdkIndex, BufferManager bufferManager, ConnectionsService connectionsService, ApiService apiService, LSPStore lspStore, ITelemetryService telemetryService) : CompletionHandlerBase
{
    private readonly SdkIndex? sdkIndex = sdkIndex;
    private readonly BufferManager bufferManager = bufferManager;
    private readonly ConnectionsService connectionsService = connectionsService;
    private readonly ApiService apiService = apiService;
    private readonly LSPStore lspStore = lspStore;
    private readonly ITelemetryService telemetry = telemetryService;
    private int completionRequestCount = 0;

    public TextDocumentSelector DocumentSelector { get; } = new(
        new TextDocumentFilter { Pattern = "**/*.cs" });

    public static TextDocumentAttributes GetTextDocumentAttributes(Uri uri)
    {
        return new TextDocumentAttributes(uri, "csharp");
    }

    [System.Diagnostics.CodeAnalysis.RequiresAssemblyFiles]
    public override async Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Sample completion requests at 25%
        bool shouldTrack = (++completionRequestCount % 4) == 0;

        if (shouldTrack)
        {
            telemetry.TrackEvent("Completion_Request");
        }

        var items = new List<CompletionItem>();

        try
        {
            string documentPath = request.TextDocument.Uri.ToString();
            string? documentText = bufferManager.GetBuffer(documentPath);
            if (string.IsNullOrEmpty(documentText))
            {
                if (request.TextDocument.Uri.Scheme == "file")
                {
                    try
                    {
                        string filePath = request.TextDocument.Uri.GetFileSystemPath();
                        documentText = await File.ReadAllTextAsync(filePath, cancellationToken);
                    }
                    catch
                    {
                        return new CompletionList(items);
                    }
                }
                else
                {
                    return new CompletionList(items);
                }
            }

            // Parse document
            SyntaxTree tree = CSharpSyntaxTree.ParseText(documentText, cancellationToken: cancellationToken);
            CompilationUnitSyntax root = tree.GetCompilationUnitRoot(cancellationToken);
            Microsoft.CodeAnalysis.Text.SourceText text = await tree.GetTextAsync(cancellationToken);

            if (request.Position.Line >= text.Lines.Count)
            {
                return new CompletionList(items);
            }

            TextLine line = text.Lines[request.Position.Line];
            int absolutePosition = Math.Min(line.Start + request.Position.Character, text.Length);

            // Find the token at cursor position
            SyntaxToken token = root.FindToken(absolutePosition);

            // Fast-exit for '=' trigger character: only proceed if we're in an attribute context.
            // This avoids processing every assignment in C# files.
            if (request.Context?.TriggerKind == CompletionTriggerKind.TriggerCharacter &&
                request.Context?.TriggerCharacter == "=")
            {
                List<CompletionItem>? eqAttrCompletions = HandleAttributeArgumentCompletion(root, absolutePosition, text, request.Position);
                return new CompletionList(eqAttrCompletions ?? new List<CompletionItem>());
            }

            // Fast-exit for '(' and ',' triggers: only proceed if at a [DynamicValues] argument position.
            // This avoids processing every method call and comma in C# files.
            if (request.Context?.TriggerKind == CompletionTriggerKind.TriggerCharacter &&
                (request.Context?.TriggerCharacter == "(" || request.Context?.TriggerCharacter == ","))
            {
                List<CompletionItem>? argCompletions = await HandleArgumentPositionDynamicValuesAsync(root, absolutePosition, tree, cancellationToken);
                return new CompletionList(argCompletions ?? new List<CompletionItem>());
            }

            // PRIORITY 0: Check if we're inside an attribute argument (e.g., [ConnectorTriggerMetadata(ConnectorName = |)])
            // This must run BEFORE string literal detection because attribute values like ConnectorName = "office365"
            // are simultaneously string literals and attribute arguments — we want attribute-specific completions.
            List<CompletionItem>? attrCompletions = HandleAttributeArgumentCompletion(root, absolutePosition, text, request.Position);
            if (attrCompletions?.Count > 0)
            {
                return new CompletionList(attrCompletions);
            }

            // PRIORITY 1: Check if we're inside a string literal (for dynamic values completion)
            // Also check the token before the cursor position in case we're right after the opening quote
            SyntaxToken tokenBefore = root.FindToken(Math.Max(0, absolutePosition - 1));

            // Check both current token and previous token for string literals
            bool isInString = token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralToken) ||
                             tokenBefore.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralToken);

            if (isInString)
            {
                await Console.Error.WriteLineAsync("[CompletionHandler] Detected string literal context");
                SyntaxToken stringToken = token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralToken) ? token : tokenBefore;

                // Check if this is a connection parameter first
                List<CompletionItem>? connectionCompletions = await HandleConnectionCompletionAsync(stringToken, tree, cancellationToken);
                if (connectionCompletions?.Any() == true)
                {
                    return new CompletionList(connectionCompletions);
                }

                // If not connection, try dynamic values completion
                List<CompletionItem>? dynamicCompletions = await HandleDynamicValuesCompletionAsync(stringToken, tree, cancellationToken);
                if (dynamicCompletions?.Any() == true)
                {
                    return new CompletionList(dynamicCompletions);
                }
                else
                {
                    await Console.Error.WriteLineAsync("[CompletionHandler] No dynamic completions found");
                }
            }

            // PRIORITY 1b: Check if cursor is at an argument position in a method call with [DynamicValues].
            // This handles the case where the user hasn't typed a quote yet (empty argument position).
            // Cheap syntax check first: only proceed if cursor is inside an ArgumentListSyntax
            // to avoid building a Roslyn compilation for every completion request.
            bool isInArgumentList = token.Parent?.AncestorsAndSelf().OfType<ArgumentListSyntax>().Any() == true;
            if (!isInArgumentList)
            {
                SyntaxToken prevForArgCheck = root.FindToken(Math.Max(0, absolutePosition - 1));
                isInArgumentList = (prevForArgCheck.IsKind(SyntaxKind.OpenParenToken) || prevForArgCheck.IsKind(SyntaxKind.CommaToken))
                    && prevForArgCheck.Parent is ArgumentListSyntax;
            }

            if (isInArgumentList)
            {
                List<CompletionItem>? argDynamicCompletions = await HandleArgumentPositionDynamicValuesAsync(root, absolutePosition, tree, cancellationToken);
                if (argDynamicCompletions?.Count > 0)
                {
                    return new CompletionList(argDynamicCompletions);
                }
            }

            // PRIORITY 2: Check if we're inside a generic type argument (e.g., Deserialize<|> or TriggerCallbackPayload<|>)
            // and suggest trigger payload types from the SDK. Enhanced: filters by enclosing method's attribute.
            List<CompletionItem>? triggerCompletions = HandleTriggerPayloadTypeCompletion(root, absolutePosition, text, request.Position);
            if (triggerCompletions?.Count > 0)
            {
                return new CompletionList(triggerCompletions);
            }

            // THIRD: Original logic for GetManagedConnectors() completion
            // Find the token immediately left of the cursor (common for dot-triggered completion)
            token = root.FindToken(Math.Max(0, absolutePosition - 1));

            // Walk up to the nearest MemberAccessExpression like: <expr> . <name>
            MemberAccessExpressionSyntax? memberAccess = token.Parent?.AncestorsAndSelf().OfType<MemberAccessExpressionSyntax>().LastOrDefault();
            if (memberAccess is null)
            {
                return new CompletionList(items);
            }

            // Identify the target expression for which we are completing members (left of the dot)
            ExpressionSyntax targetExpr = memberAccess.Expression;

            // Only provide completions if this member access chain includes GetManagedConnectors()
            if (!ContainsGetManagedConnectors(targetExpr))
            {
                return new CompletionList(items);
            }

            // Create a small compilation so Roslyn can tell us the return type of GetManagedConnectors()
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(string).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            };

            if (sdkIndex != null)
            {
                foreach (string assemblyPath in sdkIndex.AssemblyPaths)
                {
                    try
                    {
                        if (File.Exists(assemblyPath))
                        {
                            references.Add(MetadataReference.CreateFromFile(assemblyPath));
                        }
                    }
                    catch
                    {
                        // Ignore assembly loading issues.
                    }
                }
            }

            var compilation = CSharpCompilation.Create(
                assemblyName: "CompletionAnalysis",
                syntaxTrees: new[] { tree },
                references: references);

            SemanticModel semanticModel = compilation.GetSemanticModel(tree);

            // Get the type of the target expression (could be the result of GetManagedConnectors(), or a connector like Outlook)
            TypeInfo typeInfo = semanticModel.GetTypeInfo(targetExpr, cancellationToken);
            ITypeSymbol? type = typeInfo.Type ?? typeInfo.ConvertedType;

            if (type is null)
            {
                SymbolInfo targetSymbolInfo = semanticModel.GetSymbolInfo(targetExpr, cancellationToken);
                type = (targetSymbolInfo.Symbol as IMethodSymbol)?.ReturnType
                       ?? (targetSymbolInfo.Symbol as IPropertySymbol)?.Type
                       ?? targetSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault()?.ReturnType
                       ?? targetSymbolInfo.CandidateSymbols.OfType<IPropertySymbol>().FirstOrDefault()?.Type;
            }

            if (type is null)
            {
                return new CompletionList(items);
            }

            // If completing directly after GetManagedConnectors(), suggest connector properties.
            if (targetExpr is InvocationExpressionSyntax inv && string.Equals(GetInvokedSimpleName(inv.Expression), "GetManagedConnectors", StringComparison.Ordinal))
            {
                foreach (ISymbol member in type.GetMembers().Where(m => m.Kind == Microsoft.CodeAnalysis.SymbolKind.Property))
                {
                    if (member is IPropertySymbol prop && prop.DeclaredAccessibility == Accessibility.Public && !prop.IsStatic)
                    {
                        items.Add(new CompletionItem
                        {
                            Label = prop.Name,
                            Kind = CompletionItemKind.Property,
                            Detail = prop.Type?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? string.Empty,
                            InsertText = prop.Name,
                        });
                    }
                }
            }
            else
            {
                // Otherwise, we're after a connector property, suggest its operations (methods)
                // Prefer methods annotated with [ConnectorOperation(...)] if any, else fall back to all public instance methods
                IEnumerable<IMethodSymbol> methods = type
                    .GetMembers()
                    .OfType<IMethodSymbol>()
                    .Where(m => m.MethodKind == MethodKind.Ordinary && m.DeclaredAccessibility == Accessibility.Public && !m.IsStatic);

                // Try to filter to ConnectorOperation methods if present
                var opMethods = methods
                    .Where(m => m.GetAttributes().Any(a => string.Equals(a.AttributeClass?.Name, "ConnectorOperationAttribute", StringComparison.Ordinal)
                                                        || string.Equals(a.AttributeClass?.Name, "ConnectorOperation", StringComparison.Ordinal)))
                    .ToList();

                // Avoid property accessors or event methods etc.
                foreach (IMethodSymbol m in opMethods.Count > 0 ? opMethods : methods)
                {
                    // Skip common noise
                    if (m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || m.Name.StartsWith("add_") || m.Name.StartsWith("remove_"))
                    {
                        continue;
                    }

                    items.Add(new CompletionItem
                    {
                        Label = m.Name,
                        Kind = CompletionItemKind.Method,
                        Detail = m.ReturnType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? string.Empty,
                        InsertText = m.Name,
                    });
                }
            }

            // If nothing was found, still return an empty list to satisfy LSP.
            stopwatch.Stop();

            if (shouldTrack)
            {
                telemetry.TrackMetric("Completion_ResponseTime_Ms", stopwatch.ElapsedMilliseconds);
                telemetry.TrackMetric("Completion_ItemCount", items.Count);
            }

            return new CompletionList(items);
        }
        catch (Exception ex)
        {
            telemetry.TrackException(ex, new Dictionary<string, string>
            {
                { "Handler", "Completion" },
            });

            return new CompletionList(items);
        }
    }

    /// <summary>
    /// Known method names that accept a generic type argument for deserialization.
    /// Only these patterns trigger payload type suggestions when &lt; is typed.
    /// </summary>
    private static readonly HashSet<string> DeserializationMethodNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Deserialize",
        "DeserializeAsync",
        "DeserializeObject",
        "DeserializeObjectAsync",
    };

    /// <summary>
    /// Handles completion when the cursor is inside a generic type argument (e.g., after &lt;).
    /// Suggests trigger payload types from the SDK index.
    /// Enhanced: only fires for known deserialization patterns and filters by enclosing method attribute.
    /// </summary>
    private List<CompletionItem>? HandleTriggerPayloadTypeCompletion(CompilationUnitSyntax root, int absolutePosition, Microsoft.CodeAnalysis.Text.SourceText sourceText, Position requestPosition)
    {
        if (sdkIndex is null)
        {
            return null;
        }

        bool isDeserializationContext = false;

        // NOTE(daviburg): Text-based detection using line/column from the request.
        // When the code is incomplete (no closing >), Roslyn cannot produce a TypeArgumentListSyntax.
        // Only fire for known deserialization patterns, not arbitrary < usage.
        if (requestPosition.Line < sourceText.Lines.Count)
        {
            string currentLine = sourceText.Lines[requestPosition.Line].ToString();
            int col = Math.Min(requestPosition.Character, currentLine.Length);
            string linePrefix = currentLine.Substring(0, col).TrimEnd();

            if (linePrefix.EndsWith("<", StringComparison.Ordinal))
            {
                string beforeAngle = linePrefix.Substring(0, linePrefix.Length - 1).TrimEnd();
                int nameStart = beforeAngle.LastIndexOfAny(new[] { '.', ' ', '\t', '(' }) + 1;
                string methodName = beforeAngle.Substring(nameStart);

                if (DeserializationMethodNames.Contains(methodName))
                {
                    isDeserializationContext = true;
                }
            }
        }

        // AST-based fallback for when the code IS complete (e.g., user already has Deserialize<|>)
        if (!isDeserializationContext)
        {
            SyntaxToken token = root.FindToken(Math.Max(0, absolutePosition - 1));

            TypeArgumentListSyntax? typeArgList = token.Parent?.AncestorsAndSelf()
                .OfType<TypeArgumentListSyntax>()
                .FirstOrDefault();

            if (typeArgList is null && token.IsKind(SyntaxKind.LessThanToken))
            {
                typeArgList = token.Parent as TypeArgumentListSyntax;
            }

            // Only treat as deserialization context if the type argument list
            // belongs to a generic method invocation whose name is in DeserializationMethodNames.
            if (typeArgList is not null &&
                absolutePosition >= typeArgList.SpanStart &&
                absolutePosition < typeArgList.Span.End &&
                typeArgList.Parent is GenericNameSyntax genericName &&
                DeserializationMethodNames.Contains(genericName.Identifier.Text))
            {
                isDeserializationContext = true;
            }
        }

        if (!isDeserializationContext)
        {
            return null;
        }

        // Try to filter payload types based on enclosing method's [ConnectorTriggerMetadata] attribute
        string? operationName = GetEnclosingMethodTriggerOperationName(root, absolutePosition);
        string? connectorName = operationName != null
            ? GetEnclosingMethodTriggerConnectorName(root, absolutePosition)
            : null;

        Console.Error.WriteLine($"[CompletionHandler] TriggerPayload filter: operationName='{operationName}', connectorName='{connectorName}'");

        if (operationName != null)
        {
            string? resolvedConnector = ResolveConnectorName(connectorName);
            if (resolvedConnector != null)
            {
                string? payloadType = sdkIndex.GetPayloadTypeForOperation(resolvedConnector, operationName);
                if (payloadType != null)
                {
                    Console.Error.WriteLine($"[CompletionHandler] TriggerPayload: filtered to '{payloadType}'");
                    return BuildFilteredPayloadCompletionItems(payloadType);
                }

                Console.Error.WriteLine($"[CompletionHandler] TriggerPayload: no payload type for '{resolvedConnector}:{operationName}'");
            }

            // Connector has [ConnectorTriggerMetadata] but no matching TriggerPayload type.
            // Don't show unrelated payload types from other connectors — offer the generic envelope instead.
            Console.Error.WriteLine("[CompletionHandler] TriggerPayload: connector has no typed payload — suggesting generic TriggerCallbackPayload<T>");
            return BuildGenericPayloadCompletionItems();
        }

        Console.Error.WriteLine("[CompletionHandler] TriggerPayload: no [ConnectorTriggerMetadata] on enclosing method — showing all payload types");

        // Fallback: show all trigger payload types
        return GetTriggerPayloadCompletionItems();
    }

    /// <summary>
    /// Builds a completion list containing a single, context-filtered trigger payload type.
    /// </summary>
    private static List<CompletionItem> BuildFilteredPayloadCompletionItems(string fullTypeName)
    {
        string shortName = fullTypeName.Contains('.')
            ? fullTypeName.Substring(fullTypeName.LastIndexOf('.') + 1)
            : fullTypeName;

        int namespaceDotIndex = fullTypeName.LastIndexOf('.');
        string namespacePart = namespaceDotIndex >= 0
            ? fullTypeName.Substring(0, namespaceDotIndex)
            : string.Empty;

        return new List<CompletionItem>
        {
            new CompletionItem
            {
                Label = shortName,
                Kind = CompletionItemKind.Class,
                Detail = $"Trigger payload type — {fullTypeName}",
                InsertText = shortName,
                SortText = $"0_{shortName}",
                Preselect = true,
                Documentation = new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = $"**{shortName}**\n\n" +
                            "Typed trigger payload matched from `[ConnectorTriggerMetadata]` attribute.\n\n" +
                            (namespacePart.Length > 0 ? $"Namespace: `{namespacePart}`\n\n" : string.Empty) +
                            "Use this type with `JsonSerializer.Deserialize<T>()` to deserialize incoming trigger callback payloads.",
                },
            },
        };
    }

    /// <summary>
    /// Builds a completion list with the generic TriggerCallbackPayload envelope type.
    /// Used when the connector's trigger operation has no typed payload (e.g., Teams triggers use dynamic schema).
    /// </summary>
    private static List<CompletionItem> BuildGenericPayloadCompletionItems()
    {
        return new List<CompletionItem>
        {
            new CompletionItem
            {
                Label = "TriggerCallbackPayload<JsonElement>",
                Kind = CompletionItemKind.Class,
                Detail = "Generic trigger payload envelope (no typed payload for this connector)",
                InsertText = "TriggerCallbackPayload<JsonElement>",
                SortText = "0_TriggerCallbackPayload",
                Preselect = true,
                Documentation = new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = "**TriggerCallbackPayload\\<JsonElement\\>**\n\n" +
                            "This connector's trigger operation does not have a typed payload class in the SDK " +
                            "(the response schema is dynamic). Use `JsonElement` to deserialize the raw trigger body " +
                            "and access properties dynamically.\n\n" +
                            "Example: `payload.Body.Value[0].GetProperty(\"messageBody\").GetString()`",
                },
            },
        };
    }

    /// <summary>
    /// Extracts the OperationName value from the [ConnectorTriggerMetadata] attribute on the enclosing method.
    /// Returns null if the cursor is not inside a method with this attribute.
    /// </summary>
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

    /// <summary>
    /// Extracts the ConnectorName value from the [ConnectorTriggerMetadata] attribute on the enclosing method.
    /// Returns null if the cursor is not inside a method with this attribute.
    /// </summary>
    private static string? GetEnclosingMethodTriggerConnectorName(CompilationUnitSyntax root, int absolutePosition)
    {
        SyntaxToken token = root.FindToken(absolutePosition);
        MethodDeclarationSyntax? method = token.Parent?.AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (method is null)
        {
            return null;
        }

        return GetTriggerMetadataParameterValue(method, "ConnectorName");
    }

    /// <summary>
    /// Reads a named parameter value from a [ConnectorTriggerMetadata] or [ConnectorTrigger] attribute on a method.
    /// Handles both string literals ("office365") and constant references (ConnectorNames.Office365).
    /// </summary>
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

                    // String literal: ConnectorName = "office365"
                    if (arg.Expression is LiteralExpressionSyntax literal &&
                        literal.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        return literal.Token.ValueText;
                    }

                    // Constant reference: ConnectorName = ConnectorNames.Office365
                    if (arg.Expression is MemberAccessExpressionSyntax memberAccess)
                    {
                        // Return the member name (e.g., "Office365") — the caller resolves the value
                        return memberAccess.Name.Identifier.Text;
                    }

                    // Direct identifier: ConnectorName = SomeConstant
                    if (arg.Expression is IdentifierNameSyntax identifier)
                    {
                        return identifier.Identifier.Text;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Handles completion when the cursor is inside an attribute argument.
    /// Detects [ConnectorTriggerMetadata(ConnectorName = |)] and similar patterns.
    /// Uses both AST walking and text-based fallback for incomplete code.
    /// </summary>
    private List<CompletionItem>? HandleAttributeArgumentCompletion(CompilationUnitSyntax root, int absolutePosition, Microsoft.CodeAnalysis.Text.SourceText sourceText, Position requestPosition)
    {
        if (sdkIndex is null)
        {
            return null;
        }

        // Try AST-based detection first
        SyntaxToken token = root.FindToken(absolutePosition);
        AttributeArgumentSyntax? attrArg = token.Parent?.AncestorsAndSelf()
            .OfType<AttributeArgumentSyntax>()
            .FirstOrDefault();

        if (attrArg != null)
        {
            AttributeSyntax? attr = attrArg.AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
            string? attrName = attr?.Name?.ToString();

            if (attrName != null &&
                (attrName.Contains("ConnectorTriggerMetadata", StringComparison.Ordinal) ||
                 attrName.Contains("ConnectorTrigger", StringComparison.Ordinal)))
            {
                string? paramName = attrArg.NameEquals?.Name.Identifier.Text;
                return HandleConnectorTriggerAttributeCompletion(paramName, attr!, root);
            }
        }

        // Text-based fallback for incomplete attribute code
        return HandleAttributeArgumentCompletionTextBased(sourceText, requestPosition);
    }

    /// <summary>
    /// Text-based fallback for attribute argument completion when AST is incomplete.
    /// Detects patterns like: [ConnectorTriggerMetadata(ConnectorName = | or ConnectorName = "|.
    /// </summary>
    private List<CompletionItem>? HandleAttributeArgumentCompletionTextBased(Microsoft.CodeAnalysis.Text.SourceText sourceText, Position requestPosition)
    {
        if (sdkIndex is null)
        {
            return null;
        }

        int lineNum = requestPosition.Line;
        if (lineNum >= sourceText.Lines.Count)
        {
            return null;
        }

        string currentLine = sourceText.Lines[lineNum].ToString();
        int col = Math.Min(requestPosition.Character, currentLine.Length);
        string linePrefix = currentLine.Substring(0, col);

        // Look for attribute context: scan backwards from cursor to find [ConnectorTriggerMetadata(
        // This handles multi-line attributes by scanning previous lines too
        string contextWindow = linePrefix;
        for (int scanLine = lineNum - 1; scanLine >= Math.Max(0, lineNum - 5); scanLine--)
        {
            contextWindow = sourceText.Lines[scanLine].ToString() + "\n" + contextWindow;
            if (contextWindow.Contains('['))
            {
                break;
            }
        }

        // Check if we're inside a ConnectorTriggerMetadata or ConnectorTrigger attribute
        if (!contextWindow.Contains("ConnectorTriggerMetadata", StringComparison.Ordinal) &&
            !contextWindow.Contains("ConnectorTrigger", StringComparison.Ordinal))
        {
            return null;
        }

        // Determine which parameter we're editing by examining what's just before the cursor
        string trimmedPrefix = linePrefix.TrimEnd();

        // Pattern: ParameterName = " or ParameterName =
        if (TryExtractAttributeParameterName(trimmedPrefix, out string? paramName))
        {
            return HandleConnectorTriggerAttributeParameterCompletion(paramName, contextWindow);
        }

        return null;
    }

    /// <summary>
    /// Extracts the attribute parameter name from text like 'ConnectorName = "' or 'ConnectorName ='.
    /// </summary>
    private static bool TryExtractAttributeParameterName(string linePrefix, out string? paramName)
    {
        paramName = null;

        // Match patterns: 'ParamName = "' or 'ParamName = ' or 'ParamName ="'
        string trimmed = linePrefix.TrimEnd('"', ' ');
        int equalsIndex = trimmed.LastIndexOf('=');
        if (equalsIndex < 0)
        {
            return false;
        }

        string beforeEquals = trimmed.Substring(0, equalsIndex).TrimEnd();

        // Extract the identifier before =
        int nameStart = beforeEquals.LastIndexOfAny(new[] { '(', ',', ' ', '\t' }) + 1;
        paramName = beforeEquals.Substring(nameStart).Trim();

        return paramName.Length > 0 && char.IsUpper(paramName[0]);
    }

    /// <summary>
    /// Provides completions for attribute parameters on [ConnectorTriggerMetadata] or [ConnectorTrigger].
    /// Routes to ConnectorName, OperationName, or Connection completion based on the parameter name.
    /// </summary>
    private List<CompletionItem>? HandleConnectorTriggerAttributeCompletion(string? paramName, AttributeSyntax attr, CompilationUnitSyntax root)
    {
        if (string.IsNullOrEmpty(paramName))
        {
            return null;
        }

        // Detect if cursor is inside a string literal by checking the current argument expression
        bool isInString = false;
        if (attr.ArgumentList != null)
        {
            AttributeArgumentSyntax? currentArg = attr.ArgumentList.Arguments
                .FirstOrDefault(a => a.NameEquals != null &&
                    string.Equals(a.NameEquals.Name.Identifier.Text, paramName, StringComparison.Ordinal));
            isInString = currentArg?.Expression is LiteralExpressionSyntax literal &&
                         literal.IsKind(SyntaxKind.StringLiteralExpression);
        }

        if (string.Equals(paramName, "ConnectorName", StringComparison.Ordinal))
        {
            return GetConnectorNameCompletions(isInString);
        }

        if (string.Equals(paramName, "OperationName", StringComparison.Ordinal))
        {
            // Read sibling ConnectorName value to filter operations
            string? connectorName = ReadSiblingAttributeParameterValue(attr, "ConnectorName");
            return GetOperationNameCompletions(connectorName, isInString);
        }

        if (string.Equals(paramName, "Connection", StringComparison.Ordinal))
        {
            // Read sibling ConnectorName value to filter connections
            string? connectorName = ReadSiblingAttributeParameterValue(attr, "ConnectorName");
            return GetConnectionCompletionsForAttribute(connectorName);
        }

        return null;
    }

    /// <summary>
    /// Provides completions for attribute parameters using text-based context (fallback when AST is incomplete).
    /// </summary>
    private List<CompletionItem>? HandleConnectorTriggerAttributeParameterCompletion(string? paramName, string contextWindow)
    {
        if (string.IsNullOrEmpty(paramName))
        {
            return null;
        }

        // In text-based fallback, if the context ends with " the cursor is inside a string literal
        bool isInString = contextWindow.TrimEnd().EndsWith("\"", StringComparison.Ordinal);

        if (string.Equals(paramName, "ConnectorName", StringComparison.Ordinal))
        {
            return GetConnectorNameCompletions(isInString);
        }

        if (string.Equals(paramName, "OperationName", StringComparison.Ordinal))
        {
            // Try to extract ConnectorName from the context window text
            string? connectorName = ExtractParameterValueFromText(contextWindow, "ConnectorName");
            return GetOperationNameCompletions(connectorName, isInString);
        }

        if (string.Equals(paramName, "Connection", StringComparison.Ordinal))
        {
            string? connectorName = ExtractParameterValueFromText(contextWindow, "ConnectorName");
            return GetConnectionCompletionsForAttribute(connectorName);
        }

        return null;
    }

    /// <summary>
    /// Returns completion items for ConnectorName — lists all ConnectorNames.* constants.
    /// When cursor is inside a string literal, inserts the raw value; otherwise inserts the constant reference.
    /// </summary>
    private List<CompletionItem>? GetConnectorNameCompletions(bool isInsideStringLiteral = false)
    {
        if (sdkIndex is null || sdkIndex.ConnectorNameConstants.IsEmpty)
        {
            return null;
        }

        var items = new List<CompletionItem>();
        foreach (SdkConstant constant in sdkIndex.ConnectorNameConstants)
        {
            string insertText = isInsideStringLiteral
                ? constant.Value
                : $" ConnectorNames.{constant.FieldName}";

            items.Add(new CompletionItem
            {
                Label = $"ConnectorNames.{constant.FieldName}",
                Kind = CompletionItemKind.Constant,
                Detail = $"\"{constant.Value}\"",
                InsertText = insertText,
                FilterText = $"{constant.FieldName} {constant.Value} ConnectorNames",
                SortText = $"0_{constant.FieldName}",
                Documentation = new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = $"**{constant.ClassName}.{constant.FieldName}**\n\n" +
                            $"Value: `\"{constant.Value}\"`\n\n" +
                            $"Connector constant from `{constant.FullClassName}`.",
                },
            });
        }

        return items;
    }

    /// <summary>
    /// Returns completion items for OperationName — lists *TriggerOperations constants,
    /// optionally filtered by connector name.
    /// When cursor is inside a string literal, inserts the raw value; otherwise inserts the constant reference.
    /// </summary>
    private List<CompletionItem>? GetOperationNameCompletions(string? connectorName, bool isInsideStringLiteral = false)
    {
        if (sdkIndex is null)
        {
            return null;
        }

        var items = new List<CompletionItem>();

        // Resolve connector name: if it's a constant field name like "Office365", map to "office365"
        string? resolvedConnector = ResolveConnectorName(connectorName);

        IEnumerable<SdkConstant> operations;
        if (resolvedConnector != null)
        {
            operations = sdkIndex.GetTriggerOperations(resolvedConnector);
        }
        else
        {
            // No connector set — show all operations with connector prefix
            operations = sdkIndex.GetAllTriggerOperations();
        }

        foreach (SdkConstant op in operations)
        {
            string displayLabel = $"{op.ClassName}.{op.FieldName}";
            string insertText = isInsideStringLiteral
                ? op.Value
                : $" {displayLabel}";

            items.Add(new CompletionItem
            {
                Label = displayLabel,
                Kind = CompletionItemKind.Constant,
                Detail = $"\"{op.Value}\"",
                InsertText = insertText,
                FilterText = $"{op.FieldName} {op.Value} {op.ClassName}",
                SortText = $"0_{op.FieldName}",
                Documentation = new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = $"**{op.ClassName}.{op.FieldName}**\n\n" +
                            $"Value: `\"{op.Value}\"`\n\n" +
                            $"Trigger operation constant from `{op.FullClassName}`.",
                },
            });
        }

        return items.Count > 0 ? items : null;
    }

    /// <summary>
    /// Returns completion items for Connection parameter in attribute context.
    /// Filters by connector type if ConnectorName is set.
    /// </summary>
    private List<CompletionItem>? GetConnectionCompletionsForAttribute(string? connectorName)
    {
        ConnectionsConfig? connections = connectionsService.GetConnections();
        if (connections is null)
        {
            return null;
        }

        string? resolvedConnector = ResolveConnectorName(connectorName);

        IEnumerable<(string Key, string ConnectorType, string Detail)> connectionEntries;
        if (resolvedConnector != null)
        {
            connectionEntries = ConnectionsHelper.GetConnectionNamesForConnector(connections, resolvedConnector)
                .Select(name => (name, resolvedConnector, $"Connection for {resolvedConnector}"));
        }
        else
        {
            connectionEntries = ConnectionsHelper.GetAllConnections(connections);
        }

        var items = connectionEntries.Select(entry => new CompletionItem
        {
            Label = entry.Key,
            Kind = CompletionItemKind.Value,
            Detail = $"Connection — {entry.ConnectorType}",
            InsertText = entry.Key,
            FilterText = entry.Key,
            SortText = entry.Key,
        }).ToList();

        return items.Count > 0 ? items : null;
    }

    /// <summary>
    /// Resolves a connector name that might be a constant field name (e.g., "Office365") to its value ("office365").
    /// Also handles direct string values.
    /// </summary>
    private string? ResolveConnectorName(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        if (sdkIndex is null)
        {
            return input;
        }

        // Check if it's a field name from ConnectorNames (e.g., "Office365" → "office365")
        SdkConstant? match = sdkIndex.ConnectorNameConstants
            .FirstOrDefault(c => string.Equals(c.FieldName, input, StringComparison.OrdinalIgnoreCase));

        if (match != null)
        {
            return match.Value;
        }

        // It might already be the string value (e.g., "office365")
        return input;
    }

    /// <summary>
    /// Reads a sibling parameter value from an AttributeSyntax.
    /// For example, reading "ConnectorName" when the cursor is at "OperationName".
    /// </summary>
    private static string? ReadSiblingAttributeParameterValue(AttributeSyntax attr, string parameterName)
    {
        if (attr.ArgumentList is null)
        {
            return null;
        }

        foreach (AttributeArgumentSyntax arg in attr.ArgumentList.Arguments)
        {
            if (arg.NameEquals is null ||
                !string.Equals(arg.NameEquals.Name.Identifier.Text, parameterName, StringComparison.Ordinal))
            {
                continue;
            }

            // String literal: "office365"
            if (arg.Expression is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }

            // Member access: ConnectorNames.Office365 → return "Office365"
            if (arg.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                return memberAccess.Name.Identifier.Text;
            }

            // Identifier: SomeConstant
            if (arg.Expression is IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.Text;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts a parameter value from raw text context. Handles both:
    /// - String literals: ConnectorName = "office365"
    /// - Constant references: ConnectorName = ConnectorNames.Office365.
    /// </summary>
    private static string? ExtractParameterValueFromText(string contextText, string parameterName)
    {
        // Find "ParameterName = " or "ParameterName ="
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

        // String literal: "value"
        if (afterParam.StartsWith("\"", StringComparison.Ordinal))
        {
            int endQuote = afterParam.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return afterParam.Substring(1, endQuote - 1);
            }
        }

        // Constant reference: ConnectorNames.Office365
        // Extract the member name after the dot
        int dotIndex = afterParam.IndexOf('.');
        if (dotIndex >= 0)
        {
            string afterDot = afterParam.Substring(dotIndex + 1);

            // Read identifier characters
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

    /// <summary>
    /// Returns cached trigger payload completion items built from the SDK index.
    /// Lazily creates and caches the list on first call.
    /// </summary>
    private List<CompletionItem>? triggerPayloadCompletionCache;

    private List<CompletionItem>? GetTriggerPayloadCompletionItems()
    {
        if (triggerPayloadCompletionCache is not null)
        {
            return triggerPayloadCompletionCache;
        }

        if (sdkIndex is null)
        {
            return null;
        }

        var triggerPayloadTypes = sdkIndex.TypeNames
            .Where(typeName => typeName.EndsWith("TriggerPayload", StringComparison.Ordinal))
            .ToList();

        if (triggerPayloadTypes.Count == 0)
        {
            return null;
        }

        var items = new List<CompletionItem>();
        foreach (string fullTypeName in triggerPayloadTypes)
        {
            string shortName = fullTypeName.Contains('.')
                ? fullTypeName.Substring(fullTypeName.LastIndexOf('.') + 1)
                : fullTypeName;

            int namespaceDotIndex = fullTypeName.LastIndexOf('.');
            string namespacePart = namespaceDotIndex >= 0
                ? fullTypeName.Substring(0, namespaceDotIndex)
                : string.Empty;

            items.Add(new CompletionItem
            {
                Label = shortName,
                Kind = CompletionItemKind.Class,
                Detail = $"Trigger payload type — {fullTypeName}",
                InsertText = shortName,
                SortText = $"0_{shortName}",
                Documentation = new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = $"**{shortName}**\n\nTyped trigger payload for AI Gateway callbacks.\n\n" +
                            (namespacePart.Length > 0 ? $"Namespace: `{namespacePart}`\n\n" : string.Empty) +
                            "Use this type with `JsonSerializer.Deserialize<T>()` to deserialize incoming trigger callback payloads.",
                },
            });
        }

        triggerPayloadCompletionCache = items;
        return items;
    }

    /// <summary>
    /// Handles completion when the cursor is inside a string literal that represents a connection parameter.
    /// Provides a list of available connections from connections.json.
    /// </summary>
    private async Task<List<CompletionItem>?> HandleConnectionCompletionAsync(SyntaxToken stringToken, SyntaxTree tree, CancellationToken cancellationToken)
    {
        try
        {
            // Find the argument that contains this string literal
            ArgumentSyntax? argument = stringToken.Parent?.AncestorsAndSelf().OfType<ArgumentSyntax>().FirstOrDefault();
            if (argument == null)
            {
                return null;
            }

            // Find the invocation expression (the method call)
            InvocationExpressionSyntax? invocation = argument.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (invocation == null)
            {
                return null;
            }

            // Get all arguments from the invocation
            ArgumentListSyntax argumentList = invocation.ArgumentList;
            int argumentIndex = argumentList.Arguments.IndexOf(argument);

            await Console.Error.WriteLineAsync($"[CompletionHandler] Found argument at index: {argumentIndex}");

            // Check parameter name from named argument
            string? parameterName = argument.NameColon?.Name.Identifier.Text;
            await Console.Error.WriteLineAsync($"[CompletionHandler] Parameter name: {parameterName ?? "NULL"}");

            // Check if this is a connection parameter based on:
            // 1. Parameter name contains "connection"
            // 2. Method name contains connector name (e.g., "Microsoftforms")
            bool isConnectionByName = parameterName?.Contains("connection", StringComparison.OrdinalIgnoreCase) == true;

            // Check method name to see if it's a connector method
            string? methodName = null;
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                methodName = memberAccess.Name.Identifier.Text;
            }
            else if (invocation.Expression is IdentifierNameSyntax identifier)
            {
                methodName = identifier.Identifier.Text;
            }

            await Console.Error.WriteLineAsync($"[CompletionHandler] Method name: {methodName ?? "NULL"}");

            // Check if method name is a known connector method (starts with capital letter and not "Get")
            bool isConnectorMethod = methodName != null &&
                                    char.IsUpper(methodName[0]) &&
                                    !methodName.StartsWith("Get", StringComparison.Ordinal) &&
                                    argumentIndex == 0; // Connection is typically first parameter

            await Console.Error.WriteLineAsync($"[CompletionHandler] isConnectionByName={isConnectionByName}, isConnectorMethod={isConnectorMethod}");

            if (!isConnectionByName && !isConnectorMethod)
            {
                await Console.Error.WriteLineAsync("[CompletionHandler] Not a connection parameter");
                return null;
            }

            // Get connections from ConnectionsService
            ConnectionsConfig? connections = connectionsService.GetConnections();
            var allConnections = ConnectionsHelper.GetAllConnections(connections).ToList();
            if (allConnections.Count == 0)
            {
                return null;
            }

            // Create completion items for each connection
            var items = new List<CompletionItem>();
            foreach ((string connectionKey, string connectorType, string detail) in allConnections)
            {
                items.Add(new CompletionItem
                {
                    Label = connectionKey,
                    Kind = CompletionItemKind.Value,
                    Detail = $"Connection - {connectorType}",
                    Documentation = detail,
                    InsertText = connectionKey,  // Insert just the key without quotes
                    FilterText = connectionKey,
                    SortText = connectionKey,
                    Preselect = false,
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[CompletionHandler] Error in HandleConnectionCompletionAsync: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Handles completion when the cursor is inside a string literal that's part of a parameter with [DynamicValues].
    /// Supports both lambda-wrapped parameters and direct string arguments in method calls.
    /// </summary>
    private async Task<List<CompletionItem>?> HandleDynamicValuesCompletionAsync(SyntaxToken stringToken, SyntaxTree tree, CancellationToken cancellationToken)
    {
        try
        {
            // Try two paths to find the containing argument and invocation:
            // Path 1: Lambda-wrapped parameter (e.g., connector.Method(formId: x => "value"))
            // Path 2: Direct string argument (e.g., connector.Method("siteAddress"))
            ArgumentSyntax? argument = null;
            InvocationExpressionSyntax? invocation = null;

            // Path 1: Lambda-wrapped
            LambdaExpressionSyntax? lambda = stringToken.Parent?.AncestorsAndSelf().OfType<LambdaExpressionSyntax>().FirstOrDefault();
            if (lambda != null)
            {
                argument = lambda.AncestorsAndSelf().OfType<ArgumentSyntax>().FirstOrDefault();
            }

            // Path 2: Direct string argument (no lambda wrapper)
            if (argument == null)
            {
                argument = stringToken.Parent?.AncestorsAndSelf().OfType<ArgumentSyntax>().FirstOrDefault();
            }

            if (argument == null)
            {
                return null;
            }

            invocation = argument.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (invocation == null)
            {
                return null;
            }

            // Get all arguments from the invocation
            ArgumentListSyntax argumentList = invocation.ArgumentList;
            int argumentIndex = argumentList.Arguments.IndexOf(argument);

            // Build a semantic model
            List<MetadataReference> references = BuildMetadataReferences();
            var compilation = CSharpCompilation.Create(
                assemblyName: "DynamicValuesCompletion",
                syntaxTrees: new[] { tree },
                references: references);

            SemanticModel semanticModel = compilation.GetSemanticModel(tree);

            // Get the method symbol being invoked
            SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken: cancellationToken);

            // FALLBACK: If method symbol is null (due to compilation errors), try to infer from syntax
            if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            {
                // Extract method name from syntax
                string? methodName = null;
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    methodName = memberAccess.Name.Identifier.Text;
                }

                if (string.IsNullOrEmpty(methodName))
                {
                    return null;
                }

                // Use fallback: infer operation name from parameter name pattern
                string? parameterName = null;
                if (argument.NameColon != null)
                {
                    parameterName = argument.NameColon.Name.Identifier.Text;
                }

                if (string.IsNullOrEmpty(parameterName))
                {
                    return null;
                }

                // Infer operation based on parameter name (formId -> ListForms, responseId -> ListResponses, etc.)
                string? inferredOperationName = InferOperationFromParameter(parameterName);
                if (string.IsNullOrEmpty(inferredOperationName))
                {
                    return null;
                }

                // Fetch dynamic values using fallback approach
                return await FetchDynamicValueCompletionsAsync(
                    inferredOperationName,
                    invocation,
                    null, // No method symbol available
                    argument,
                    semanticModel,
                    cancellationToken);
            }

            // Get the parameter for this argument
            if (argumentIndex >= methodSymbol.Parameters.Length)
            {
                return null;
            }

            IParameterSymbol parameter = methodSymbol.Parameters[argumentIndex];

            // Check for [DynamicValues] attribute
            AttributeData? dynamicValuesAttr = parameter.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "DynamicValuesAttribute" || a.AttributeClass?.Name == "DynamicValues");

            if (dynamicValuesAttr == null)
            {
                return null;
            }

            // Extract operation name from attribute
            string? operationName = null;
            if (dynamicValuesAttr.ConstructorArguments.Length > 0)
            {
                operationName = dynamicValuesAttr.ConstructorArguments[0].Value?.ToString();
            }

            if (string.IsNullOrEmpty(operationName))
            {
                return null;
            }

            // Fetch the dynamic values using the same logic as HoverHandler
            return await FetchDynamicValueCompletionsAsync(
                operationName,
                invocation,
                methodSymbol,
                argument,
                semanticModel,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[CompletionHandler] Error in HandleDynamicValuesCompletionAsync: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Handles completion when the cursor is at an argument position in a method call,
    /// even if no string literal has been typed yet. Detects [DynamicValues] on the
    /// corresponding parameter and fetches values from the connector API.
    /// </summary>
    private async Task<List<CompletionItem>?> HandleArgumentPositionDynamicValuesAsync(
        CompilationUnitSyntax root,
        int absolutePosition,
        SyntaxTree tree,
        CancellationToken cancellationToken)
    {
        try
        {
            // Find if cursor is inside an argument list
            SyntaxToken token = root.FindToken(absolutePosition);
            ArgumentListSyntax? argumentList = token.Parent?.AncestorsAndSelf()
                .OfType<ArgumentListSyntax>()
                .FirstOrDefault();

            // Also check if cursor is right after ( or , in an argument list
            if (argumentList == null)
            {
                SyntaxToken prevToken = root.FindToken(Math.Max(0, absolutePosition - 1));
                if (prevToken.IsKind(SyntaxKind.OpenParenToken) || prevToken.IsKind(SyntaxKind.CommaToken))
                {
                    argumentList = prevToken.Parent as ArgumentListSyntax;
                }
            }

            if (argumentList == null)
            {
                return null;
            }

            InvocationExpressionSyntax? invocation = argumentList.Parent as InvocationExpressionSyntax;
            if (invocation == null)
            {
                return null;
            }

            // Determine which argument position the cursor is at
            int argumentIndex = 0;
            ArgumentSyntax? currentArgument = null;

            for (int index = 0; index < argumentList.Arguments.Count; index++)
            {
                ArgumentSyntax arg = argumentList.Arguments[index];
                if (absolutePosition >= arg.SpanStart && absolutePosition <= arg.Span.End)
                {
                    argumentIndex = index;
                    currentArgument = arg;
                    break;
                }
            }

            // If cursor isn't inside any argument, determine position from commas
            if (currentArgument == null)
            {
                argumentIndex = 0;
                foreach (SyntaxToken separator in argumentList.Arguments.GetSeparators())
                {
                    if (absolutePosition > separator.SpanStart)
                    {
                        argumentIndex++;
                    }
                }
            }

            // Build compilation to resolve the method symbol
            List<MetadataReference> references = BuildMetadataReferences();
            var compilation = CSharpCompilation.Create(
                assemblyName: "ArgPositionCompletion",
                syntaxTrees: new[] { tree },
                references: references);

            SemanticModel semanticModel = compilation.GetSemanticModel(tree);
            SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken: cancellationToken);
            IMethodSymbol? methodSymbol = symbolInfo.Symbol as IMethodSymbol
                ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

            if (methodSymbol == null)
            {
                await Console.Error.WriteLineAsync("[CompletionHandler] Could not resolve method symbol for argument position");
                return null;
            }

            // Check if the parameter at this position has [DynamicValues]
            if (argumentIndex >= methodSymbol.Parameters.Length)
            {
                return null;
            }

            IParameterSymbol parameter = methodSymbol.Parameters[argumentIndex];
            AttributeData? dynamicValuesAttr = parameter.GetAttributes()
                .FirstOrDefault(a => string.Equals(a.AttributeClass?.Name, "DynamicValuesAttribute", StringComparison.Ordinal)
                    || string.Equals(a.AttributeClass?.Name, "DynamicValues", StringComparison.Ordinal));

            if (dynamicValuesAttr == null)
            {
                return null;
            }

            string? operationName = dynamicValuesAttr.ConstructorArguments.Length > 0
                ? dynamicValuesAttr.ConstructorArguments[0].Value?.ToString()
                : null;

            if (string.IsNullOrEmpty(operationName))
            {
                return null;
            }

            await Console.Error.WriteLineAsync($"[CompletionHandler] Found [DynamicValues(\"{operationName}\")] on parameter '{parameter.Name}' at index {argumentIndex}");

            // Create a synthetic argument if we don't have one (cursor at empty position)
            ArgumentSyntax argForFetch = currentArgument
                ?? SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(string.Empty)));

            return await FetchDynamicValueCompletionsAsync(
                operationName,
                invocation,
                methodSymbol,
                argForFetch,
                semanticModel,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[CompletionHandler] Error in HandleArgumentPositionDynamicValuesAsync: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Fetches dynamic value completions for a specific operation.
    /// This integrates with the same Azure API calls used in HoverHandler.
    /// </summary>
    private async Task<List<CompletionItem>> FetchDynamicValueCompletionsAsync(
        string operationName,
        InvocationExpressionSyntax invocation,
        IMethodSymbol? methodSymbol,
        ArgumentSyntax argument,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var items = new List<CompletionItem>();

        try
        {
            // 1. Infer connector name from operation or method name
            string? connectorName = InferConnectorName(operationName, methodSymbol);

            if (string.IsNullOrEmpty(connectorName))
            {
                await Console.Error.WriteLineAsync($"[CompletionHandler] FetchDynamic: Could not infer connector name for operation '{operationName}', method '{methodSymbol?.Name}'");
                return items;
            }

            // 2. Extract connection name from method call arguments
            string? connectionName = ExtractConnectionName(invocation, methodSymbol);

            // Fallback for DirectClient pattern: connection is injected via DI, not passed as argument.
            // Look up connections matching the connector type from the connections config.
            // NOTE(daviburg): This is a best-effort heuristic. When multiple connections of the same
            // connector type exist, we cannot determine which connection THIS specific client instance
            // uses without tracing the DI registration. See issue #21.
            if (string.IsNullOrEmpty(connectionName) && !string.IsNullOrEmpty(connectorName))
            {
                connectionName = DynamicValuesHelper.ResolveConnectionByConnectorType(connectionsService, connectorName);
                if (!string.IsNullOrEmpty(connectionName))
                {
                    await Console.Error.WriteLineAsync($"[CompletionHandler] FetchDynamic: Resolved connection '{connectionName}' by connector type '{connectorName}' (single DirectClient match)");
                }
                else
                {
                    int matchCount = DynamicValuesHelper.GetConnectionCountForConnector(connectionsService, connectorName);
                    if (matchCount > 1)
                    {
                        await Console.Error.WriteLineAsync($"[CompletionHandler] FetchDynamic: {matchCount} connections match connector '{connectorName}' — cannot determine which client instance uses. Skipping.");
                    }
                }
            }

            if (string.IsNullOrEmpty(connectionName))
            {
                await Console.Error.WriteLineAsync($"[CompletionHandler] FetchDynamic: Could not extract connection name from invocation for connector '{connectorName}'");
                return items;
            }

            await Console.Error.WriteLineAsync($"[CompletionHandler] FetchDynamic: connector='{connectorName}', connection='{connectionName}', operation='{operationName}'");

            // 3. Check if values are already in the shared store
            List<DynamicValueItem>? cachedValues = lspStore.DynamicData.Get(connectorName, operationName, connectionName);
            if (cachedValues != null)
            {
                return cachedValues.ConvertAll(v => new CompletionItem
                {
                    Label = v.Description,
                    Kind = CompletionItemKind.Value,
                    Detail = $"{connectorName} - {operationName}",
                    Documentation = $"ID: {v.Value}",
                    InsertText = v.Value.Trim('"'),
                    FilterText = v.Description,
                    SortText = v.Description,
                    Preselect = false,
                });
            }

            // 5. Determine API path: DirectClient (runtime URL + operation path) vs ARM (/dynamicInvoke)
            DynamicOperationMetadata? metadata = DynamicOperationsRegistry.GetOperationMetadata(connectorName, operationName);
            if (metadata == null)
            {
                await Console.Error.WriteLineAsync($"[CompletionHandler] FetchDynamic: No metadata for '{connectorName}:{operationName}' in DynamicOperationsRegistry");
                return items;
            }

            ConnectionsConfig? connections = connectionsService.GetConnections();
            string? runtimeUrl = ConnectionsHelper.GetDirectClientRuntimeUrl(connections, connectionName);
            bool isDirectClient = !string.IsNullOrEmpty(runtimeUrl);

            IEnumerable<DynamicValueItem>? dynamicValues;

            if (isDirectClient)
            {
                // DirectClient: call the operation path directly on the runtime URL
                string directUrl = $"{runtimeUrl!.TrimEnd('/')}{metadata.Path}";
                await Console.Error.WriteLineAsync($"[CompletionHandler] FetchDynamic: DirectClient {metadata.Method.ToUpperInvariant()} {LogSanitizer.SanitizeUrl(directUrl)}");
                dynamicValues = await FetchFromDirectApiAsync(connectorName, directUrl, metadata, cancellationToken);
            }
            else
            {
                // ARM management: use /dynamicInvoke POST wrapper
                string? apiUrl = BuildDynamicApiUrl(connectionName);
                if (string.IsNullOrEmpty(apiUrl))
                {
                    await Console.Error.WriteLineAsync($"[CompletionHandler] FetchDynamic: Could not build API URL for connection '{connectionName}'");
                    return items;
                }

                HoverHandlerTypes.DynamicInvokePayload? payload = BuildDynamicApiPayload(connectorName, operationName);
                if (payload == null)
                {
                    await Console.Error.WriteLineAsync($"[CompletionHandler] FetchDynamic: Could not build payload for '{connectorName}:{operationName}'");
                    return items;
                }

                await Console.Error.WriteLineAsync($"[CompletionHandler] FetchDynamic: ARM POST {LogSanitizer.SanitizeUrl(apiUrl)}");
                dynamicValues = await FetchFromDynamicApiAsync(connectorName, apiUrl, payload, cancellationToken);
            }

            // 6. Check results
            if (dynamicValues?.Any() != true)
            {
                await Console.Error.WriteLineAsync($"[CompletionHandler] FetchDynamic: API returned no values for '{connectorName}:{operationName}'");
                return items;
            }

            await Console.Error.WriteLineAsync($"[CompletionHandler] FetchDynamic: Got {dynamicValues.Count()} values from API");

            // 7. Store in shared store for future use
            var storeItems = dynamicValues.Select(v => new DynamicValueItem(v.Value, v.Description)).ToList();
            lspStore.DynamicData.Set(connectorName, operationName, connectionName, storeItems);
            await Console.Error.WriteLineAsync($"[CompletionHandler] Stored {storeItems.Count} items in LSPStore.DynamicData");

            // 7. Convert to completion items
            items = dynamicValues.Select(v => new CompletionItem
            {
                Label = v.Description,
                Kind = CompletionItemKind.Value,
                Detail = $"{connectorName} - {operationName}",
                Documentation = $"ID: {v.Value}",
                InsertText = v.Value.Trim('"'),
                FilterText = v.Description,
                SortText = v.Description,
                Preselect = false,
            }).ToList();
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[CompletionHandler] Error fetching dynamic values: {ex.Message}");
        }

        return items;
    }

    private List<MetadataReference> BuildMetadataReferences()
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(string).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        };

        if (sdkIndex != null)
        {
            foreach (string assemblyPath in sdkIndex.AssemblyPaths)
            {
                try
                {
                    if (File.Exists(assemblyPath))
                    {
                        references.Add(MetadataReference.CreateFromFile(assemblyPath));
                    }
                }
                catch
                {
                    // Ignore
                }
            }
        }

        return references;
    }

    private static string? InferConnectorName(string operationName, IMethodSymbol? methodSymbol)
    {
        // Check method attributes first (if available)
        if (methodSymbol != null)
        {
            AttributeData? connectorAttr = methodSymbol.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "ConnectorOperationAttribute" || a.AttributeClass?.Name == "ConnectorOperation");

            if (connectorAttr?.ConstructorArguments.Length > 1)
            {
                return connectorAttr.ConstructorArguments[1].Value?.ToString();
            }
        }

        // Infer from method's containing type name using shared helper.
        if (methodSymbol != null)
        {
            string? fromType = DynamicValuesHelper.InferConnectorFromContainingType(methodSymbol.ContainingType?.Name);
            if (!string.IsNullOrEmpty(fromType))
            {
                return fromType;
            }
        }

        // Fallback: Infer from operation name patterns
        if (operationName.Contains("Form", StringComparison.OrdinalIgnoreCase))
        {
            return "microsoftforms";
        }

        if (operationName.Contains("Team", StringComparison.OrdinalIgnoreCase))
        {
            return "teams";
        }

        if (operationName.Contains("Mail", StringComparison.OrdinalIgnoreCase) ||
            operationName.Contains("Outlook", StringComparison.OrdinalIgnoreCase))
        {
            return "outlook";
        }

        return null;
    }

    /// <summary>
    /// Infers the operation name from a parameter name pattern.
    /// For example: formId -> ListForms, responseId -> ListResponses.
    /// </summary>
    private static string? InferOperationFromParameter(string parameterName)
    {
        // Common parameter name patterns and their corresponding operations
        var patterns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "formId", "ListForms" },
            { "responseId", "ListResponses" },
            { "teamId", "ListTeams" },
            { "channelId", "ListChannels" },
            { "messageId", "ListMessages" },
            { "folderId", "ListFolders" },
            { "mailId", "ListMails" },
        };

        if (patterns.TryGetValue(parameterName, out string? operation))
        {
            return operation;
        }

        // Try to infer from parameter name patterns (e.g., "itemId" -> "ListItems")
        if (parameterName.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
        {
            string baseName = parameterName[..^2];
            return $"List{baseName}s";  // Convert to plural
        }

        return null;
    }

    /// <summary>
    /// Extracts the connection name from the invocation chain.
    /// Walks up the method chain to find connection parameters in parent invocations.
    /// For example: .Microsoftforms("conn").GetFormResponseById(...) finds "conn" in parent.
    /// </summary>
    private static string? ExtractConnectionName(InvocationExpressionSyntax invocation, IMethodSymbol? methodSymbol)
    {
        // First try to extract from the current invocation
        string? connectionName = ExtractConnectionFromSingleInvocation(invocation, methodSymbol);
        if (!string.IsNullOrEmpty(connectionName))
        {
            return connectionName;
        }

        // For method chains like .Microsoftforms("conn").GetFormResponseById(...)
        // Walk up the member access chain to find the connection parameter
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            // The expression on the left might be another invocation
            if (memberAccess.Expression is InvocationExpressionSyntax parentInvocation)
            {
                // Look for string literal in parent invocation (e.g., Microsoftforms("conn"))
                return ExtractFirstStringLiteral(parentInvocation);
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts connection from a single invocation (no chain traversal).
    /// </summary>
    private static string? ExtractConnectionFromSingleInvocation(InvocationExpressionSyntax invocation, IMethodSymbol? methodSymbol)
    {
        ArgumentListSyntax argumentList = invocation.ArgumentList;
        if (argumentList.Arguments.Count == 0)
        {
            return null;
        }

        // Find the parameter with [ConnectionName] attribute (if methodSymbol available)
        if (methodSymbol != null)
        {
            for (int i = 0; i < methodSymbol.Parameters.Length; i++)
            {
                IParameterSymbol param = methodSymbol.Parameters[i];
                bool hasConnectionNameAttr = param.GetAttributes().Any(a =>
                    a.AttributeClass?.Name == "ConnectionNameAttribute" ||
                    a.AttributeClass?.Name == "ConnectionName");

                if (hasConnectionNameAttr && i < argumentList.Arguments.Count)
                {
                    ArgumentSyntax arg = argumentList.Arguments[i];
                    if (arg.Expression is LiteralExpressionSyntax literal &&
                        literal.Token.Value is string connectionName)
                    {
                        return connectionName;
                    }
                }
            }
        }

        // Fallback: try first string literal argument
        return ExtractFirstStringLiteral(invocation);
    }

    /// <summary>
    /// Extracts the first string literal from an invocation's arguments.
    /// </summary>
    private static string? ExtractFirstStringLiteral(InvocationExpressionSyntax invocation)
    {
        foreach (ArgumentSyntax arg in invocation.ArgumentList.Arguments)
        {
            if (arg.Expression is LiteralExpressionSyntax literal &&
                literal.Token.Value is string value)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the API URL for fetching dynamic values based on connector and connection name.
    /// Resolves the ARM connection resource name for both managed API and DirectClient connections.
    /// </summary>
    private string? BuildDynamicApiUrl(string connectionName)
    {
        if (string.IsNullOrEmpty(connectionName))
        {
            return null;
        }

        // Resolve the ARM connection resource name (may differ from logical name for DirectClient)
        ConnectionsConfig? connections = connectionsService.GetConnections();
        string? armConnectionName = ConnectionsHelper.ResolveArmConnectionName(connections, connectionName);

        if (string.IsNullOrEmpty(armConnectionName))
        {
            return null;
        }

        ApiServiceConfig apiConfiguration = apiService.Config;

        return $"{apiConfiguration.BaseUrl}/subscriptions/{apiConfiguration.SubscriptionId}/resourceGroups/{apiConfiguration.ResourceGroup}/providers/Microsoft.Web/connections/{armConnectionName}/dynamicInvoke?api-version={apiConfiguration.ApiVersion}";
    }

    private static HoverHandlerTypes.DynamicInvokePayload? BuildDynamicApiPayload(string connectorName, string operationName)
    {
        // Use the DynamicOperationsRegistry from HoverHandler
        DynamicOperationMetadata? metadata = DynamicOperationsRegistry.GetOperationMetadata(connectorName, operationName);
        return metadata == null
            ? null
            : new HoverHandlerTypes.DynamicInvokePayload
            {
                Request = new HoverHandlerTypes.DynamicInvokeRequest
                {
                    Method = metadata.Method,
                    Path = metadata.Path,
                    Queries = metadata.QueryParameters,
                },
            };
    }

    private async Task<IEnumerable<DynamicValueItem>?> FetchFromDynamicApiAsync(
        string connectorName,
        string apiUrl,
        HoverHandlerTypes.DynamicInvokePayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            // Call Azure dynamicInvoke API
            HoverHandlerTypes.DynamicInvokeResponse<List<HoverHandlerTypes.FormItem>>? dynamicResponse = await apiService.PostJsonAsync<HoverHandlerTypes.DynamicInvokeResponse<List<HoverHandlerTypes.FormItem>>>(
                apiUrl,
                payload,
                cancellationToken);

            List<HoverHandlerTypes.FormItem>? forms = dynamicResponse?.Response?.Body;
            return forms == null || forms.Count == 0
                ? (IEnumerable<DynamicValueItem>?)null
                : forms.Select(form => new DynamicValueItem(
                $"\"{form.Id}\"",
                form.Title ?? "Untitled Form"));
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[CompletionHandler] Error calling dynamic API: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Fetches dynamic values from a DirectClient connector's runtime URL.
    /// Calls the operation path directly (e.g., GET /datasets) instead of using the ARM /dynamicInvoke wrapper.
    /// </summary>
    private async Task<IEnumerable<DynamicValueItem>?> FetchFromDirectApiAsync(
        string connectorName,
        string directUrl,
        DynamicOperationMetadata metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            HoverHandlerTypes.DynamicValuesListResponse? listResponse;

            if (string.Equals(metadata.Method, "get", StringComparison.OrdinalIgnoreCase))
            {
                listResponse = await apiService.GetJsonAsync<HoverHandlerTypes.DynamicValuesListResponse>(directUrl, cancellationToken);
            }
            else
            {
                object payload = metadata.QueryParameters.Count > 0 ? metadata.QueryParameters : new { };
                listResponse = await apiService.PostJsonAsync<HoverHandlerTypes.DynamicValuesListResponse>(directUrl, payload, cancellationToken);
            }

            if (listResponse?.Value == null || listResponse.Value.Count == 0)
            {
                await Console.Error.WriteLineAsync("[CompletionHandler] DirectClient API returned no values");
                return null;
            }

            await Console.Error.WriteLineAsync($"[CompletionHandler] DirectClient API returned {listResponse.Value.Count} values");

            return listResponse.Value.Select(item => new DynamicValueItem(
                $"\"{item.Name}\"",
                item.DisplayName ?? item.Name ?? "Unknown"));
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[CompletionHandler] Error calling DirectClient API for '{connectorName}': {ex.Message}");
            return null;
        }
    }

    // Resolve is not used (ResolveProvider = false), but the abstract base requires this.
    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        return Task.FromResult(request);
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions(CompletionCapability capability, OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities.ClientCapabilities clientCapabilities)
    {
        return new CompletionRegistrationOptions
        {
            DocumentSelector = DocumentSelector,
            TriggerCharacters = new Container<string>(".", "\"", "<", "=", "(", ","),  // Trigger on dot, quotes, angle bracket, equals (attribute args), open paren and comma (method arg positions)
            ResolveProvider = false,
        };
    }

    private static string GetInvokedSimpleName(ExpressionSyntax expr)
    {
        // Handles cases like IdentifierName("GetManagedConnectors") and MemberAccessExpression("x.GetManagedConnectors")
        return expr switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax ma when ma.Name is IdentifierNameSyntax id2 => id2.Identifier.ValueText,
            _ => string.Empty,
        };
    }

    private static bool ContainsGetManagedConnectors(ExpressionSyntax expression)
    {
        foreach (InvocationExpressionSyntax inv in expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (string.Equals(GetInvokedSimpleName(inv.Expression), "GetManagedConnectors", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
